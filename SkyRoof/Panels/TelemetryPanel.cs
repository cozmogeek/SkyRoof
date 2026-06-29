using MathNet.Numerics;
using Newtonsoft.Json;
using Serilog;
using SkyRoof.Satellites;
using VE3NEA;
using VE3NEA.Tlm.Core;
using VE3NEA.Tlm.Deframing;
using VE3NEA.Tlm.Telemetry;
using WeifenLuo.WinFormsUI.Docking;

namespace SkyRoof
{
  public partial class TelemetryPanel : DockContent
  {
    private static readonly JsonSerializerSettings SerializerParams =
      new() { NullValueHandling = NullValueHandling.Ignore };

    private readonly Context ctx;
    private SatnogsDbSatellite? Satellite;
    private SatnogsDbTransmitter Transmitter;
    private bool Terrestrial;
    private bool SatAboveHorizon = false;
    private SignalParams? SignalParams;
    private TelemetryDecocder? Decoder;
    private SatnogsUploader? SatnogsUploader;
    private TreeNode? CurrentPassNode;
    private TelemetryRegistry? TelemetryRegistry;
    private TreeNode LastFrameNode;
    private ILogger? FrameLogger;

    internal class TxPassInfo
    {
      internal DateTime StartTime = DateTime.Now;
      internal SatnogsDbTransmitter Transmitter;
      internal int Orbit;
      internal SignalParams? SignalParams;
      internal int BurstCount = 0;
      internal int FrameCount = 0;
      internal bool HasValidFrame = false;

      internal TxPassInfo(SatnogsDbTransmitter transmitter, int orbit)
      {
        Transmitter = transmitter;
        Orbit = orbit;
      }

      internal bool IsSame(SatnogsDbTransmitter transmitter, int orbit)
      {
        return Transmitter.uuid == transmitter.uuid && Orbit == orbit;
      }

      internal string Describe()
      {
        string paramsStr = JsonConvert.SerializeObject(SignalParams, Formatting.Indented, SerializerParams);

        return
          $"Start: {StartTime:yyyy-MM-dd HH:mm:ss}\n" +
          $"Sat: {Transmitter.Satellite.name}\n" +
          $"Tx: {Transmitter.description}\n" +
          $"Orbit: {Orbit}\n\n" +
          $"Bursts: {BurstCount}\n" +
          $"Frames: {FrameCount}\n\n" +
          $"Params:{paramsStr}";
      }
    }


    //----------------------------------------------------------------------------------------------
    //                                         system
    //----------------------------------------------------------------------------------------------
    // only for designer
    public TelemetryPanel()
    {
      InitializeComponent();
    }

    public TelemetryPanel(Context ctx)
    {
      Log.Information("Creating TelemetryPanel");
      this.ctx = ctx;

      InitializeComponent();

      string path = Path.Combine(Utils.GetUserDataFolder(), "TelemetryRegistry");
      TelemetryRegistry = new TelemetryRegistry(path);

      ctx.TelemetryPanel = this;
      ctx.MainForm.TelemetryMNU.Checked = true;

      SatnogsUploader = new SatnogsUploader(ctx);

      SetTransmitter();
    }

    private void TelemetryPanel_FormClosing(object sender, FormClosingEventArgs e)
    {
      Log.Information("Closing TelemetryPanel");
      ctx.TelemetryPanel = null;
      ctx.MainForm.TelemetryMNU.Checked = false;

      SatnogsUploader?.Dispose();
      SatnogsUploader = null;

      (FrameLogger as IDisposable)?.Dispose();
      FrameLogger = null;
    }




    //----------------------------------------------------------------------------------------------
    //                                     pipeline
    //----------------------------------------------------------------------------------------------
    internal void SetTransmitter()
    {
      Satellite = ctx.SatelliteSelector.SelectedSatellite;
      Transmitter = ctx.SatelliteSelector.SelectedTransmitter;
      Terrestrial = ctx.FrequencyControl.RadioLink.IsTerrestrial;

      if (Terrestrial) SatNameLabel.Text = "Terrestrial";
      else SatNameLabel.Text = $"{Satellite.name}  {Transmitter.description}";

      ResolveSignalParams();
      UpdateTxStatus();
      CreatDestroyPipeline();
    }

    private void ResolveSignalParams()
    {
      if (Terrestrial)
      {
        SignalParams = null;
        return;
      }

      SignalParams = SignalParamsResolver.Resolve(Transmitter);

      if (SignalParams == null)
      {
        toolTip1.SetToolTip(SatNameLabel, "Parameters unknown");
        toolTip1.SetToolTip(StatusLabel, "Parameters unknown");
      }
      else
      {
        var tooltip = JsonConvert.SerializeObject(SignalParams, Formatting.Indented, SerializerParams);
        toolTip1.SetToolTip(SatNameLabel, tooltip);
        toolTip1.SetToolTip(StatusLabel, tooltip);
      }
    }

    private void CreatDestroyPipeline()
    {
      bool needPipeline = !Terrestrial && IsDecodable() && SatAboveHorizon;
      if ((Decoder != null) == needPipeline) return;

      // destroy decoder
      if (Decoder != null)
      {
        Decoder.Pipeline.FrameDecoded -= FrameDecodedHandler;
        Decoder.Pipeline.BurstDecoded -= BurstDecodedHandler;
      }

      var old = Decoder;
      Decoder = null;
      old?.Dispose();

      // create new decoder
      if (needPipeline)
      {
        Decoder = new(SignalParams!);
        Decoder.Pipeline.FrameDecoded += FrameDecodedHandler;
        Decoder.Pipeline.BurstDecoded += BurstDecodedHandler;
      }
    }

    private readonly static Modulation[] SupportedModulations = {
      Modulation.FSK,
      Modulation.GFSK,
      Modulation.GMSK,
      Modulation.BPSK
    };

    private bool IsDecodable()
    {
      if (SignalParams == null) return false;
      if (SignalParams.Framing == Framing.Unknown || SignalParams.Modulation == Modulation.Unknown || SignalParams.Baud == 0) return false;
      return SupportedModulations.Contains(SignalParams.Modulation);
    }

    private void BurstDecodedHandler(StreamingBurstReport report)
    {
      BeginInvoke(() =>
        {
          StatusLabel.Text = "decoding...";
          // create the pass entry on the first burst (grayed until a valid frame arrives), not on the first frame
          var txPassInfo = EnsureCurrentPassNode();
          txPassInfo.BurstCount++;
          // refresh the right panel if this pass entry is the one currently selected
          if (treeView1.SelectedNode == CurrentPassNode) richTextBox1.Text = txPassInfo.Describe();
        }
       );
    }

    private void FrameDecodedHandler(Frame frame)
    {
      ctx.KissServer.SendToAll(frame);
      if (Satellite?.norad_cat_id is int norad) SatnogsUploader?.Submit(frame, norad);
      BeginInvoke(() => AddFrame(frame));
    }




    //----------------------------------------------------------------------------------------------
    //                                       treeview
    //----------------------------------------------------------------------------------------------
    private void AddFrame(Frame frame)
    {
      var txPassInfo = EnsureCurrentPassNode();

      // un-gray the pass entry once the first valid frame of the pass is decoded
      if (!txPassInfo.HasValidFrame)
      {
        txPassInfo.HasValidFrame = true;
        CurrentPassNode!.ForeColor = Color.Empty;
      }

      bool mustScroll = LastFrameNode == null || treeView1.SelectedNode == LastFrameNode;

      string addr = SignalParams.Framing == Framing.AX25G3RUH ? Ax25Address.Describe(frame.Bytes) : "";
      string nodeText = $"{DateTime.Now:HH:mm:ss}  {frame.Length} bytes  {addr}";
      LastFrameNode = new TreeNode(nodeText);
      string frameText = BuildFrameText(frame);
      LastFrameNode.Tag = frameText;
      CurrentPassNode.Nodes.Add(LastFrameNode);
      txPassInfo.FrameCount++;

      SaveFrameToFile(frame, addr, frameText);

      CurrentPassNode.Expand();

      if (mustScroll) treeView1.SelectedNode = LastFrameNode;
      else if (treeView1.SelectedNode == CurrentPassNode) richTextBox1.Text = txPassInfo.Describe();
    }

    /// <summary>Returns the current pass node's info, creating the pass node (grayed until the first valid
    /// frame) when this is the first burst or frame of a new transmitter+orbit pass.</summary>
    private TxPassInfo EnsureCurrentPassNode()
    {
      int orbit = ctx.SdrPasses.GetNextPass(Satellite)?.OrbitNumber ?? -1;
      var txPassInfo = (TxPassInfo?)CurrentPassNode?.Tag;

      if (CurrentPassNode == null || !(txPassInfo!.IsSame(Transmitter, orbit)))
      {
        CurrentPassNode = new TreeNode($"{DateTime.Now:yyyy-MM-dd HH:mm} {Transmitter.Satellite.name}  {Transmitter.description}");
        CurrentPassNode.ForeColor = Color.Gray;
        txPassInfo = new TxPassInfo(Transmitter, orbit);
        txPassInfo.SignalParams = SignalParams;
        CurrentPassNode.Tag = txPassInfo;
        treeView1.Nodes.Add(CurrentPassNode);
      }

      return txPassInfo!;
    }

    private string BuildFrameText(Frame frame)
    {
      string tlm = "";
      var def = TelemetryRegistry?.ForNorad(Satellite?.norad_cat_id);
      if (def != null)
      {
        var record = TelemetryParser.Parse(def, frame.Bytes);
        if (record != null)
          tlm = "TELEMETRY:\n" +
            string.Join("", record.Fields.Select(f => $"  {f.Name}: {f.Value}{(f.Units.Length > 0 ? " " + f.Units : "")}\n")) +
            "\n";
      }

      var chars = frame.Ascii;
      string asc = "";
      for (int i = 0; i < chars.Length; i += 28)
        asc += "  " + chars.Substring(i, Math.Min(28, chars.Length - i)) + "\n";
      asc = "ASCII:\n" + asc + "\n";

      var bytes = frame.Bytes;
      string hex = "";
      for (int i = 0; i < bytes.Length; i += 8)
        hex += $"  {i:X3}  " + string.Join(" ", bytes.Skip(i).Take(8).Select(b => b.ToString("X2"))) + "\n";
      hex = "HEX:\n" + hex + "\n";

      string meta = "META:\n" +
        $"  CFO: {frame.CfoHz:F1} Hz\n" +
        $"  SNR: {frame.SnrDb:F1} dB\n" +
        $"  CRC: {frame.CrcValid switch { true => "OK", false => "FAIL", null => "n/a" }}\n" +
        $"  Corrections: {frame.CorrectedBits}\n" +
        $"  Erasures: {frame.ErasedBytes}\n";

      return tlm + asc + hex + meta;
    }




    //----------------------------------------------------------------------------------------------
    //                                       save to file
    //----------------------------------------------------------------------------------------------
    // mirror everything shown in the tree to the file: the date and time, satellite, transmitter
    // and frame length in the header, the frame address (if any), then the frame detail shown in the
    // right pane
    private void SaveFrameToFile(Frame frame, string addr, string frameText)
    {
      if (!ctx.Settings.Telemetry.ArchiveToFile) return;

      FrameLogger ??= CreateFrameLogger();

      string header = $"Sat: {Transmitter.Satellite.name}  Tx: \"{Transmitter.description}\"  Frame: {frame.Length} bytes" +
        (addr.Length > 0 ? $"  {addr}" : "");
      FrameLogger.Information("{Header}\n{Body}", header, frameText);
    }

    private static ILogger CreateFrameLogger()
    {
      string fileName = Path.Combine(Utils.GetUserDataFolder(), "TelemetryDecodes", "frames_.txt");
      return new LoggerConfiguration()
        .WriteTo.File(fileName,
          rollingInterval: RollingInterval.Day,
          outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss}  {Message:lj}{NewLine}",
          shared: true)
        .CreateLogger();
    }

    internal void UpdateTxStatus()
    {
      SatAboveHorizon = ctx.SdrPasses.GetNextPass(Satellite)?.IsAboveHorizon() ?? false;
      CreatDestroyPipeline();

      if (Terrestrial) StatusLabel.Text = "not decoded";
      else if (!IsDecodable()) StatusLabel.Text = "format not supported";
      else if (!SatAboveHorizon) StatusLabel.Text = "satellite below horizon";
      else StatusLabel.Text = "ready to decode";
    }

    internal void ProcessSamples(DataEventArgs<Complex32> e)
    {
      Decoder?.StartProcessing(e);
    }

    private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
    {
      var node = e.Node;
      if (node == null) return;

      if (node.Level == 0)
      {
        var info = node.Tag as TxPassInfo;
        richTextBox1.Text = info!.Describe();
      }
      else
        richTextBox1.Text = (string)node!.Tag!;
    }

    private void ClearAllMNU_Click(object sender, EventArgs e)
    {
      LastFrameNode = null;
      CurrentPassNode = null;
      richTextBox1.Clear();
      treeView1.Nodes.Clear();
    }
  }
}