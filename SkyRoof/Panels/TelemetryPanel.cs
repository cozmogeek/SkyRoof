using Serilog;
using WeifenLuo.WinFormsUI.Docking;

namespace SkyRoof
{
  public partial class TelemetryPanel : DockContent
  {
    private readonly Context ctx;
    private SatnogsDbSatellite? Satellite;

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

      SetSatellite();
    }

    public void SetSatellite(SatnogsDbSatellite? sat = null)
    {
      sat ??= ctx.SatelliteSelector.SelectedSatellite;
      Satellite = sat;
      SatNameLabel.Text = sat.name;
    }

    private void TelemetryPanel_FormClosing(object sender, FormClosingEventArgs e)
    {
      Log.Information("Closing TelemetryPanel");
      ctx.TelemetryPanel = null;
      ctx.MainForm.TelemetryMNU.Checked = false;
    }
  }
}
