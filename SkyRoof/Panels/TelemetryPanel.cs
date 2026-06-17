using MathNet.Numerics;
using Newtonsoft.Json;
using Serilog;
using SkyRoof.Satellites;
using VE3NEA;
using VE3NEA.Tlm.Core;
using WeifenLuo.WinFormsUI.Docking;

namespace SkyRoof
{
  public partial class TelemetryPanel : DockContent
  {
    private readonly Context ctx;
    private SatnogsDbSatellite? Satellite;
    private SatnogsDbTransmitter Transmitter;
    private bool Terrestrial;
    private bool SatAboveHorizon= false;
    private VE3NEA.Tlm.Core.SignalParams? SignalParams;
    private TelemetryDecocder? Decoder;


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
      treeView1.Nodes.Add("Child 1");
      treeView1.Nodes.Add("Child 2");
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
      if (SignalParams == null) toolTip1.Hide(SatNameLabel);
      else toolTip1.SetToolTip(SatNameLabel, JsonConvert.SerializeObject(SignalParams, Formatting.Indented));
    }

    private void CreatDestroyPipeline()
    {
      bool needPipeline = !Terrestrial && SignalParams != null && SatAboveHorizon;
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

    private void BurstDecodedHandler(StreamingBurstReport report)
    {
      BeginInvoke(() => StatusLabel.Text = "decoding...");
    }

    private void FrameDecodedHandler(VE3NEA.Tlm.Core.Frame frame)
    {
      BeginInvoke(() => AddFrame(frame));  
    }

    private void AddFrame(VE3NEA.Tlm.Core.Frame frame)
    {
      treeView1.Nodes.Add($"CRC {frame.CrcValid}");
      richTextBox1.Text = JsonConvert.SerializeObject(frame, Formatting.Indented);
    }

    internal void UpdateTxStatus()
    {
      SatAboveHorizon = ctx.SdrPasses.IsAboveHorizon(Satellite);
     
      CreatDestroyPipeline();

      if (Terrestrial) StatusLabel.Text = "not decoded";
      else if (SignalParams == null) StatusLabel.Text = "format not supported";
      else if (!SatAboveHorizon) StatusLabel.Text = "satellite below horizon";
      else StatusLabel.Text = "ready to decode";
    }

    internal void ProcessSamples(DataEventArgs<Complex32> e)
    {
     Decoder?.StartProcessing(e);
    }
  }
}
