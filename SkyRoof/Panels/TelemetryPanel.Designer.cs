namespace SkyRoof
{
  partial class TelemetryPanel
  {
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null))
      {
        components.Dispose();
      }
      base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
      components = new System.ComponentModel.Container();
      TelemetryView = new ListView();
      SatNameLabel = new Label();
      toolTip1 = new ToolTip(components);
      SuspendLayout();
      //
      // TelemetryView
      //
      TelemetryView.Dock = DockStyle.Fill;
      TelemetryView.FullRowSelect = true;
      TelemetryView.LabelWrap = false;
      TelemetryView.Location = new Point(0, 23);
      TelemetryView.MultiSelect = false;
      TelemetryView.Name = "TelemetryView";
      TelemetryView.ShowItemToolTips = true;
      TelemetryView.Size = new Size(356, 561);
      TelemetryView.TabIndex = 0;
      TelemetryView.UseCompatibleStateImageBehavior = false;
      TelemetryView.View = View.Details;
      //
      // SatNameLabel
      //
      SatNameLabel.Dock = DockStyle.Top;
      SatNameLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
      SatNameLabel.Location = new Point(0, 0);
      SatNameLabel.Name = "SatNameLabel";
      SatNameLabel.Size = new Size(356, 23);
      SatNameLabel.TabIndex = 1;
      SatNameLabel.Text = "___";
      SatNameLabel.TextAlign = ContentAlignment.MiddleCenter;
      //
      // TelemetryPanel
      //
      AutoScaleDimensions = new SizeF(7F, 15F);
      AutoScaleMode = AutoScaleMode.Font;
      ClientSize = new Size(356, 584);
      Controls.Add(TelemetryView);
      Controls.Add(SatNameLabel);
      Name = "TelemetryPanel";
      StartPosition = FormStartPosition.CenterParent;
      Text = "Telemetry";
      FormClosing += TelemetryPanel_FormClosing;
      ResumeLayout(false);
    }

    #endregion

    public ListView TelemetryView;
    public Label SatNameLabel;
    private ToolTip toolTip1;
  }
}
