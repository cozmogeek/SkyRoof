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
      SatNameLabel = new Label();
      toolTip1 = new ToolTip(components);
      StatusLabel = new Label();
      treeView1 = new TreeView();
      richTextBox1 = new RichTextBox();
      SuspendLayout();
      // 
      // SatNameLabel
      // 
      SatNameLabel.Dock = DockStyle.Top;
      SatNameLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
      SatNameLabel.Location = new Point(0, 0);
      SatNameLabel.Name = "SatNameLabel";
      SatNameLabel.Size = new Size(858, 23);
      SatNameLabel.TabIndex = 1;
      SatNameLabel.Text = "___";
      SatNameLabel.TextAlign = ContentAlignment.MiddleCenter;
      // 
      // StatusLabel
      // 
      StatusLabel.Dock = DockStyle.Top;
      StatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
      StatusLabel.Location = new Point(0, 23);
      StatusLabel.Name = "StatusLabel";
      StatusLabel.Size = new Size(858, 23);
      StatusLabel.TabIndex = 2;
      StatusLabel.Text = "___";
      StatusLabel.TextAlign = ContentAlignment.MiddleCenter;
      // 
      // treeView1
      // 
      treeView1.Dock = DockStyle.Left;
      treeView1.FullRowSelect = true;
      treeView1.Location = new Point(0, 46);
      treeView1.Name = "treeView1";
      treeView1.ShowRootLines = false;
      treeView1.Size = new Size(339, 538);
      treeView1.TabIndex = 3;
      // 
      // richTextBox1
      // 
      richTextBox1.Dock = DockStyle.Fill;
      richTextBox1.Font = new Font("Courier New", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
      richTextBox1.Location = new Point(339, 46);
      richTextBox1.Name = "richTextBox1";
      richTextBox1.Size = new Size(519, 538);
      richTextBox1.TabIndex = 4;
      richTextBox1.Text = "";
      // 
      // TelemetryPanel
      // 
      AutoScaleDimensions = new SizeF(7F, 15F);
      AutoScaleMode = AutoScaleMode.Font;
      ClientSize = new Size(858, 584);
      Controls.Add(richTextBox1);
      Controls.Add(treeView1);
      Controls.Add(StatusLabel);
      Controls.Add(SatNameLabel);
      Name = "TelemetryPanel";
      StartPosition = FormStartPosition.CenterParent;
      Text = "Telemetry";
      FormClosing += TelemetryPanel_FormClosing;
      Shown += TelemetryPanel_Shown;
      ResumeLayout(false);
    }

    #endregion
    public Label SatNameLabel;
    private ToolTip toolTip1;
    public Label StatusLabel;
    private TreeView treeView1;
    private RichTextBox richTextBox1;
  }
}
