using MathNet.Numerics;
using MathNet.Numerics.Optimization.TrustRegion;
using Newtonsoft.Json;
using Serilog;
using SGPdotNET.Observation;
using SkyRoof.Satellites;
using VE3NEA;
using VE3NEA.Tlm.Core;
using VE3NEA.Tlm.Deframing;
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
    private TreeNode? CurrentTxPass;

    internal class TxPassInfo
    {
      internal DateTime StartTime = DateTime.Now;
      internal SatnogsDbTransmitter Transmitter;
      internal int Orbit;
      internal SignalParams? SignalParams;
      internal int BurstCount = 0;
      internal int FrameCount = 0;

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
          $"Orbit: {Orbit}\n" +
          $"Bursts: {BurstCount}\n" +
          $"Frames: {FrameCount}\n" +
          $"---\n{paramsStr}";
      }
    }


    //----------------------------------------------------------------------------------------------
    //                                         system
    //----------------------------------------------------------------------------------------------
    public TelemetryPanel()
    {
      InitializeComponent();
    }

    public TelemetryPanel(Context ctx)
    {
      Log.Information("Creating TelemetryPanel");
      this.ctx = ctx;

      InitializeComponent();

      ctx.TelemetryPanel = this;
      ctx.MainForm.TelemetryMNU.Checked = true;

      SetTransmitter();
    }

    private void TelemetryPanel_FormClosing(object sender, FormClosingEventArgs e)
    {
      Log.Information("Closing TelemetryPanel");
      ctx.TelemetryPanel = null;
      ctx.MainForm.TelemetryMNU.Checked = false;
    }

    private void TelemetryPanel_Shown(object sender, EventArgs e)
    {

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
        toolTip1.Hide(SatNameLabel);
        toolTip1.Hide(StatusLabel);
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
      bool needPipeline = !Terrestrial && SignalParamsDecodable() && SatAboveHorizon;
      if ((Decoder != null) == needPipeline) return;

      if (Decoder != null) Decoder.Pipeline.FrameDecoded -= FrameDecodedHandler;

      var old = Decoder;
      Decoder = null;
      old?.Dispose();

      if (needPipeline)
      {
        Decoder = new(SignalParams!);
        Decoder.Pipeline.FrameDecoded += FrameDecodedHandler;
        Decoder.Pipeline.BurstDecoded += BurstDecodedHandler;

      }
    }

    private readonly static Modulation[] SupportedModulations = { 
      Modulation.Fsk, 
      Modulation.Gfsk, 
      Modulation.Gmsk, 
      Modulation.Bpsk 
    };

    private bool SignalParamsDecodable()
    {
      if (SignalParams == null) return false;
      if (SignalParams.Framing == Framing.Unknown) return false;
      return SupportedModulations.Contains(SignalParams.Modulation);
    }

    private void BurstDecodedHandler(StreamingBurstReport report)
    {
      BeginInvoke(() =>
        {
          StatusLabel.Text = "decoding...";
          var txPassInfo = (TxPassInfo?)CurrentTxPass?.Tag;
          if (txPassInfo != null) txPassInfo.BurstCount++;
        }
       );
    }

    private void FrameDecodedHandler(Frame frame)
    {
      BeginInvoke(() => AddFrame(frame));
    }




    //----------------------------------------------------------------------------------------------
    //                                       treeview
    //----------------------------------------------------------------------------------------------
    private void AddFrame(Frame frame)
    {
      int orbit = ctx.SdrPasses.GetNextPass(Satellite)?.OrbitNumber ?? -1;
      var txPassInfo = (TxPassInfo?)CurrentTxPass?.Tag;

      if (CurrentTxPass == null || !(txPassInfo!.IsSame(Transmitter, orbit)))
      {
        CurrentTxPass = new TreeNode($"{DateTime.Now:yyyy-MM-dd HH:mm} {Transmitter.Satellite.name}  {Transmitter.description}");
        txPassInfo = new TxPassInfo(Transmitter, orbit);
        txPassInfo.SignalParams = SignalParams;
        CurrentTxPass.Tag = txPassInfo;
        treeView1.Nodes.Add(CurrentTxPass);
      }

      var node = new TreeNode($"{DateTime.Now:HH:mm:ss}  {frame.Length} b  {Ax25Address.Describe(frame.Bytes)}");
      string frameText = BuildFrameText(frame);
      node.Tag = frameText;
      CurrentTxPass.Nodes.Add(node);
      txPassInfo.FrameCount++;
      //richTextBox1.Text = JsonConvert.SerializeObject(frame, Formatting.Indented, SerializerParams);
      richTextBox1.Text = frameText;

      CurrentTxPass.Expand();
    }

    private string BuildFrameText(Frame frame)
    {

      string asc = "ASCII\n" +
        // frame.Ascii: wrapped every 30 chars, indented by 2 spaces
        "\n";

      string hex = "HEX\n" +
        // frame.Bytes: 2 spaces, 3 hex digits (offset), 8 hex bytes: "  AAA  BB BB BB BB BB BB BB BB"
        "\n";

      string meta = "META\n" +
        $"  Time: {frame.TimeSeconds:F3}s\n" +
        $"  CFO: {frame.CfoHz:F1}Hz\n" +
        $"  SNR: {frame.SnrDb:F1}dB\n" +
        $"  Burst: #{frame.BurstIndex}\n" +
        $"  CRC: {(frame.CrcValid ? "OK" : "FAIL")}\n" +
        $"  Corrections: {frame.CorrectedBits}\n" +
        $"  Erasures: {frame.ErasedBytes}\n";



      return tlm + asc + hex + meta;
    }

    internal void UpdateTxStatus()
    {
      SatAboveHorizon = ctx.SdrPasses.GetNextPass(Satellite)?.IsAboveHorizon() ?? false;
      CreatDestroyPipeline();

      if (Terrestrial) StatusLabel.Text = "not decoded";
      else if (Decoder == null) StatusLabel.Text = "format not supported";
      else if (!SatAboveHorizon) StatusLabel.Text = "satellite below horizon";
      else StatusLabel.Text = "ready to decode";
    }

    internal void ProcessSamples(DataEventArgs<Complex32> e)
    {
      Decoder?.StartProcessing(e);
    }

    private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
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
  }
}