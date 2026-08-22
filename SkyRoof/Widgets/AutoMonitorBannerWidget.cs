namespace SkyRoof
{
  public class AutoMonitorBannerWidget : UserControl
  {
    public Context ctx;

    private readonly Label label = new();
    private readonly Button stopBtn = new();
    private readonly TableLayoutPanel layout = new();

    public AutoMonitorBannerWidget()
    {
      BorderStyle = BorderStyle.FixedSingle;

      layout.Dock = DockStyle.Fill;
      layout.ColumnCount = 2;
      layout.RowCount = 1;
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
      Controls.Add(layout);

      label.Dock = DockStyle.Fill;
      label.TextAlign = ContentAlignment.MiddleLeft;
      label.Padding = new Padding(10, 0, 10, 0);
      label.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
      label.Text = "AUTO TUNING ENABLED — SkyRoof may switch satellites/transmitters during monitored passes";
      label.BackColor = Color.Transparent;

      stopBtn.AutoSize = true;
      stopBtn.Margin = new Padding(0, 18, 10, 18);
      stopBtn.Text = "Stop";
      stopBtn.BackColor = Color.IndianRed;
      stopBtn.ForeColor = Color.Black;
      stopBtn.FlatStyle = FlatStyle.Flat;
      stopBtn.FlatAppearance.BorderColor = Color.Black;
      stopBtn.Click += (s, e) =>
      {
        if (ctx == null) return;
        ctx.MainForm?.SetAutoMonitorEnabled(false);
      };

      layout.Controls.Add(label, 0, 0);
      layout.Controls.Add(stopBtn, 1, 0);

      ApplyThemeColors();
    }

    private void ApplyThemeColors()
    {
      Color back = Theme.IsDark ? Theme.UhfTint : Color.Gold;
      Color text = Theme.IsDark ? Color.White : Color.Black;

      BackColor = back;
      layout.BackColor = back;
      ForeColor = text;
      label.ForeColor = text;
    }

    public void SyncFromSettings()
    {
      bool enabled = ctx?.Settings?.Satellites?.AutoMonitorEnabled == true;
      Visible = enabled;
    }
  }
}

