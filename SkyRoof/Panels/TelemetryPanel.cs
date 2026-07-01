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
    private DecodeSnapshot? CurrentDecode;

    // Identity of the transmitter a decoder was built for, captured when the pipeline is created and bound to
    // that pipeline's event handlers. Frames surface on the decode worker thread, possibly after the user has
    // switched to a different transmitter; carrying the snapshot with the frame keeps it attributed to the
    // transmitter that actually produced it instead of to whatever is selected when the frame arrives.
    private sealed class DecodeSnapshot
    {
      internal readonly SatnogsDbSatellite? Satellite;
      internal readonly SatnogsDbTransmitter Transmitter;
      internal readonly SignalParams SignalParams;

      internal DecodeSnapshot(SatnogsDbSatellite? satellite, SatnogsDbTransmitter transmitter, SignalParams signalParams)
      {
        Satellite = satellite;
        Transmitter = transmitter;
        SignalParams = signalParams;
      }
    }

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
          $"Sat: {Transmitter?.Satellite?.name ?? "Unknown"}\n" +
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

      // stop and free the decode pipeline (joins its worker thread and releases native FFTW memory)
      Decoder?.Dispose();
      Decoder = null;
      CurrentDecode = null;

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
      UpdateParamsTooltip();
    }

    /// <summary>Refresh the params tooltip on both status labels: the resolved <see cref="SignalParams"/> (which
    /// picks up the actual deviation the pipeline resolves for a blind FSK burst) plus the name of the telemetry
    /// field-decoder definition its frames will be parsed with. Re-called when a frame arrives so the actual
    /// deviation replaces the initial one once the pipeline locks it.</summary>
    private void UpdateParamsTooltip()
    {
      string tooltip;
      if (SignalParams == null)
        tooltip = "Parameters unknown";
      else
      {
        string paramsStr = JsonConvert.SerializeObject(SignalParams, Formatting.Indented, SerializerParams);
        string telemetry = TelemetryRegistry?.ForNorad(Satellite?.norad_cat_id)?.Id ?? "none";
        tooltip = $"{paramsStr}\nTelemetry: {telemetry}";
      }
      toolTip1.SetToolTip(SatNameLabel, tooltip);
      toolTip1.SetToolTip(StatusLabel, tooltip);
    }

    private void CreatDestroyPipeline()
    {
      bool needPipeline = !Terrestrial && IsDecodable() && SatAboveHorizon;

      // keep the existing decoder only if it was built for the currently selected transmitter. a transmitter
      // change must rebuild the pipeline: otherwise it keeps decoding with the previous transmitter's params
      // and its frames get attributed to the newly selected transmitter (wrong sat/norad/telemetry parser).
      bool matches = Decoder != null && CurrentDecode != null && CurrentDecode.Transmitter.uuid == Transmitter.uuid;

      if (needPipeline && matches) return;
      if (!needPipeline && Decoder == null) return;

      // destroy the existing decoder: purge its queued IQ (recorded for the old transmitter / tuning) so the
      // backlog is discarded rather than decoded and mis-attributed, then dispose it
      if (Decoder != null)
      {
        Decoder.Purge();
        var old = Decoder;
        Decoder = null;
        CurrentDecode = null;
        old.Dispose();
      }

      // create the new decoder, binding the current selection to its handlers so every frame it emits is
      // attributed to this transmitter even if the selection changes while the frame is in flight
      if (needPipeline)
      {
        var snapshot = new DecodeSnapshot(Satellite, Transmitter, SignalParams!);
        CurrentDecode = snapshot;
        Decoder = new(SignalParams!);
        Decoder.Pipeline.FrameDecoded += frame => FrameDecodedHandler(frame, snapshot);
        Decoder.Pipeline.BurstDecoded += report => BurstDecodedHandler(report, snapshot);
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

    private void BurstDecodedHandler(StreamingBurstReport report, DecodeSnapshot snapshot)
    {
      BeginInvoke(() =>
        {
          StatusLabel.Text = "decoding...";
          // create the pass entry on the first burst (grayed until a valid frame arrives), not on the first frame
          var txPassInfo = EnsureCurrentPassNode(snapshot);
          txPassInfo.BurstCount++;
          // refresh the right panel if this pass entry is the one currently selected
          if (treeView1.SelectedNode == CurrentPassNode) richTextBox1.Text = txPassInfo.Describe();
        }
       );
    }

    private void FrameDecodedHandler(Frame frame, DecodeSnapshot snapshot)
    {
      ctx.KissServer.SendToAll(frame);
      if (snapshot.Satellite?.norad_cat_id is int norad) SatnogsUploader?.Submit(frame, norad);
      BeginInvoke(() => AddFrame(frame, snapshot));
    }




    //----------------------------------------------------------------------------------------------
    //                                       treeview
    //----------------------------------------------------------------------------------------------
    private void AddFrame(Frame frame, DecodeSnapshot snapshot)
    {
      var txPassInfo = EnsureCurrentPassNode(snapshot);

      // un-gray the pass entry once the first valid frame of the pass is decoded
      if (!txPassInfo.HasValidFrame)
      {
        txPassInfo.HasValidFrame = true;
        CurrentPassNode!.ForeColor = Color.Empty;
      }

      bool mustScroll = LastFrameNode == null || treeView1.SelectedNode == LastFrameNode;

      string addr = (snapshot.SignalParams.Framing == Framing.AX25G3RUH ? Ax25Address.Describe(frame.Bytes) : "") ?? "";
      string nodeText = $"{DateTime.Now:HH:mm:ss}  {frame.Length} bytes  {addr}";
      LastFrameNode = new TreeNode(nodeText);
      string frameText = BuildFrameText(frame, snapshot);
      LastFrameNode.Tag = frameText;
      CurrentPassNode.Nodes.Add(LastFrameNode);
      txPassInfo.FrameCount++;

      SaveFrameToFile(frame, addr, frameText, snapshot);

      // the pipeline may have locked a blind FSK burst's actual deviation while decoding this frame — refresh the
      // tooltip so it shows the deviation actually used instead of the initial (unknown) one. only do this for the
      // currently selected transmitter's decoder, so a late frame from a previous transmitter can't overwrite it.
      if (ReferenceEquals(snapshot, CurrentDecode)) UpdateParamsTooltip();

      CurrentPassNode.Expand();

      if (mustScroll) treeView1.SelectedNode = LastFrameNode;
      else if (treeView1.SelectedNode == CurrentPassNode) richTextBox1.Text = txPassInfo.Describe();
    }

    /// <summary>Returns the current pass node's info, creating the pass node (grayed until the first valid
    /// frame) when this is the first burst or frame of a new transmitter+orbit pass.</summary>
    private TxPassInfo EnsureCurrentPassNode(DecodeSnapshot snapshot)
    {
      int orbit = ctx.SdrPasses.GetNextPass(snapshot.Satellite)?.OrbitNumber ?? -1;
      var txPassInfo = (TxPassInfo?)CurrentPassNode?.Tag;

      if (CurrentPassNode == null || !(txPassInfo!.IsSame(snapshot.Transmitter, orbit)))
      {
        CurrentPassNode = new TreeNode($"{DateTime.Now:yyyy-MM-dd HH:mm} {snapshot.Transmitter.Satellite.name}  {snapshot.Transmitter.description}");
        CurrentPassNode.ForeColor = Color.Gray;
        txPassInfo = new TxPassInfo(snapshot.Transmitter, orbit);
        txPassInfo.SignalParams = snapshot.SignalParams;
        CurrentPassNode.Tag = txPassInfo;
        treeView1.Nodes.Add(CurrentPassNode);
      }

      return txPassInfo!;
    }

    private string BuildFrameText(Frame frame, DecodeSnapshot snapshot)
    {
      string tlm = "";
      var def = TelemetryRegistry?.ForNorad(snapshot.Satellite?.norad_cat_id);
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
    private void SaveFrameToFile(Frame frame, string addr, string frameText, DecodeSnapshot snapshot)
    {
      if (!ctx.Settings.Telemetry.ArchiveToFile) return;

      FrameLogger ??= CreateFrameLogger();

      string header = $"Sat: {snapshot.Transmitter.Satellite.name}  Tx: \"{snapshot.Transmitter.description}\"  Uuid: {snapshot.Transmitter.uuid}  Frame: {frame.Length} bytes" +
        (addr.Length > 0 ? $"  Addr: {addr}" : "");
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