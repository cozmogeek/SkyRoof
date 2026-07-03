using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SkyRoof
{
  public partial class SatelliteDetailsForm : Form
  {
    private Context ctx;

    public SatelliteDetailsForm()
    {
      InitializeComponent();
    }

    internal static void ShowSatellite(SatnogsDbSatellite? satellite, Form parent, Context ctx)
    {
      var dlg = new SatelliteDetailsForm();
      dlg.ctx = ctx;
      dlg.satelliteDetailsControl1.ShowSatellite(satellite);
      dlg.ShowDialog(parent);
    }

    private void SatelliteDetailsForm_Shown(object? sender, EventArgs e)
    {
      var sett = ctx.Settings.Ui.SatelliteDetailsForm;
      if (sett.Size.Width > 0) Size = sett.Size;
      satelliteDetailsControl1.RestoreLayout(ctx.Settings.Ui);
    }

    private void SatelliteDetailsForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
      ctx.Settings.Ui.SatelliteDetailsForm.Size = Size;
      satelliteDetailsControl1.SaveLayout(ctx.Settings.Ui);
    }
  }
}
