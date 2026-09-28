
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Rebar;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace SkyRoof
{
  public partial class LoqFt4QsoDialog : Form
  {
    private Context ctx;
    private QsoInfo qso;

    public LoqFt4QsoDialog(Context ctx, QsoInfo qso)
    {
      InitializeComponent();

      this.ctx = ctx;
      this.qso = qso;
    }

    internal static void PopUp(Context ctx, QsoInfo qso)
    {
      var dialog = new LoqFt4QsoDialog(ctx, qso);
      dialog.label1.Text = $"Save FT4 QSO with {qso.Call}?";
      dialog.Show(ctx.MainForm);
    }

    protected override void OnLoad(EventArgs e)
    {
      base.OnLoad(e);
      CenterOnMainForm();
    }

    private void CenterOnMainForm()
    {
      var owner = ctx.MainForm;
      Rectangle r = owner != null && owner.IsHandleCreated
        ? owner.Bounds
        : Screen.PrimaryScreen!.WorkingArea;

      var wa = Screen.FromRectangle(r).WorkingArea;
      var rand = new Random();
      int x = r.Left + (r.Width - Width) / 2 + rand.Next(-50, 50);
      int y = r.Top + (r.Height - Height) / 2 + rand.Next(-50, 50);
      x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
      y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height));
      Location = new Point(x, y);
    }

    private void SaveBtn_Click(object sender, EventArgs e)
    {
      ctx.Ft4ConsolePanel?.WsjtxUdpSender?.SendLogQsoMessage(qso);
      ctx.LoggerInterface.SaveQso(qso);
      Hide();
    }

    private void EditBtn_Click(object sender, EventArgs e)
    {
      if (ctx.QsoEntryPanel == null)
      {
        ctx.MainForm.ShowFloatingPanel(new QsoEntryPanel(ctx));
        ctx.QsoEntryPanel!.ShouldClose = true;
      }
      Hide();
      ctx.QsoEntryPanel!.SetQsoInfo(qso);
      ctx.QsoEntryPanel!.Focus();
    }
    private void LoqFt4QsoDialog_FormClosing(object sender, FormClosingEventArgs e)
    {
      if (e.CloseReason == CloseReason.UserClosing)
      {
        e.Cancel = true;
        Hide();
      }
    }

    private void CancelBtn_Click(object sender, EventArgs e)
    {
      Hide();
    }
  }
}
