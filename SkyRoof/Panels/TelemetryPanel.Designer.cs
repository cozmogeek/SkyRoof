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
      MenuStrip = new ContextMenuStrip(components);
      ClearAllMNU = new ToolStripMenuItem();
      richTextBox1 = new RichTextBox();
      splitContainer1 = new SplitContainer();
      ImagePanel = new Panel();
      ImageBox = new PictureBox();
      ImageMetaBox = new TextBox();
      ImageMenu = new ContextMenuStrip(components);
      SaveImageMNU = new ToolStripMenuItem();
      CopyImageMNU = new ToolStripMenuItem();
      MenuStrip.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
      splitContainer1.Panel1.SuspendLayout();
      splitContainer1.Panel2.SuspendLayout();
      splitContainer1.SuspendLayout();
      ImagePanel.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)ImageBox).BeginInit();
      ImageMenu.SuspendLayout();
      SuspendLayout();
      // 
      // SatNameLabel
      // 
      SatNameLabel.Dock = DockStyle.Top;
      SatNameLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
      SatNameLabel.Location = new Point(0, 0);
      SatNameLabel.Name = "SatNameLabel";
      SatNameLabel.Size = new Size(669, 23);
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
      StatusLabel.Size = new Size(669, 23);
      StatusLabel.TabIndex = 2;
      StatusLabel.Text = "___";
      StatusLabel.TextAlign = ContentAlignment.MiddleCenter;
      // 
      // treeView1
      // 
      treeView1.ContextMenuStrip = MenuStrip;
      treeView1.Dock = DockStyle.Fill;
      treeView1.FullRowSelect = true;
      treeView1.HideSelection = false;
      treeView1.Location = new Point(0, 0);
      treeView1.Name = "treeView1";
      treeView1.ShowNodeToolTips = true;
      treeView1.Size = new Size(247, 526);
      treeView1.TabIndex = 3;
      treeView1.AfterSelect += treeView1_AfterSelect;
      // 
      // MenuStrip
      // 
      MenuStrip.Items.AddRange(new ToolStripItem[] { ClearAllMNU });
      MenuStrip.Name = "ClearAllMNU";
      MenuStrip.Size = new Size(119, 26);
      // 
      // ClearAllMNU
      // 
      ClearAllMNU.Name = "ClearAllMNU";
      ClearAllMNU.Size = new Size(118, 22);
      ClearAllMNU.Text = "Clear All";
      ClearAllMNU.Click += ClearAllMNU_Click;
      // 
      // richTextBox1
      // 
      richTextBox1.BackColor = SystemColors.Window;
      richTextBox1.Dock = DockStyle.Fill;
      richTextBox1.Font = new Font("Courier New", 11.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
      richTextBox1.Location = new Point(0, 0);
      richTextBox1.Name = "richTextBox1";
      richTextBox1.ReadOnly = true;
      richTextBox1.Size = new Size(418, 526);
      richTextBox1.TabIndex = 4;
      richTextBox1.Text = "";
      //
      // splitContainer1
      //
      splitContainer1.Dock = DockStyle.Fill;
      splitContainer1.Location = new Point(0, 46);
      splitContainer1.Name = "splitContainer1";
      //
      // splitContainer1.Panel1
      //
      splitContainer1.Panel1.Controls.Add(treeView1);
      //
      // splitContainer1.Panel2
      //
      splitContainer1.Panel2.Controls.Add(ImagePanel);
      splitContainer1.Panel2.Controls.Add(richTextBox1);
      splitContainer1.Size = new Size(669, 526);
      splitContainer1.SplitterDistance = 247;
      splitContainer1.TabIndex = 5;
      //
      // ImagePanel
      //
      ImagePanel.Controls.Add(ImageBox);
      ImagePanel.Controls.Add(ImageMetaBox);
      ImagePanel.Dock = DockStyle.Fill;
      ImagePanel.Location = new Point(0, 0);
      ImagePanel.Name = "ImagePanel";
      ImagePanel.Size = new Size(418, 526);
      ImagePanel.TabIndex = 6;
      ImagePanel.Visible = false;
      //
      // ImageBox
      //
      ImageBox.BackColor = Color.Black;
      ImageBox.ContextMenuStrip = ImageMenu;
      ImageBox.Dock = DockStyle.Fill;
      ImageBox.Location = new Point(0, 0);
      ImageBox.Name = "ImageBox";
      ImageBox.Size = new Size(418, 416);
      ImageBox.SizeMode = PictureBoxSizeMode.Zoom;
      ImageBox.TabIndex = 0;
      ImageBox.TabStop = false;
      //
      // ImageMetaBox
      //
      ImageMetaBox.BackColor = SystemColors.Window;
      ImageMetaBox.Dock = DockStyle.Bottom;
      ImageMetaBox.Font = new Font("Courier New", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
      ImageMetaBox.Location = new Point(0, 416);
      ImageMetaBox.Multiline = true;
      ImageMetaBox.Name = "ImageMetaBox";
      ImageMetaBox.ReadOnly = true;
      ImageMetaBox.ScrollBars = ScrollBars.Vertical;
      ImageMetaBox.Size = new Size(418, 110);
      ImageMetaBox.TabIndex = 1;
      //
      // ImageMenu
      //
      ImageMenu.Items.AddRange(new ToolStripItem[] { SaveImageMNU, CopyImageMNU });
      ImageMenu.Name = "ImageMenu";
      ImageMenu.Size = new Size(129, 48);
      //
      // SaveImageMNU
      //
      SaveImageMNU.Name = "SaveImageMNU";
      SaveImageMNU.Size = new Size(128, 22);
      SaveImageMNU.Text = "Save As...";
      SaveImageMNU.Click += SaveImageMNU_Click;
      //
      // CopyImageMNU
      //
      CopyImageMNU.Name = "CopyImageMNU";
      CopyImageMNU.Size = new Size(128, 22);
      CopyImageMNU.Text = "Copy";
      CopyImageMNU.Click += CopyImageMNU_Click;
      // 
      // TelemetryPanel
      // 
      AutoScaleDimensions = new SizeF(7F, 15F);
      AutoScaleMode = AutoScaleMode.Font;
      ClientSize = new Size(669, 572);
      Controls.Add(splitContainer1);
      Controls.Add(StatusLabel);
      Controls.Add(SatNameLabel);
      Name = "TelemetryPanel";
      StartPosition = FormStartPosition.CenterParent;
      Text = "Telemetry";
      FormClosing += TelemetryPanel_FormClosing;
      Shown += TelemetryPanel_Shown;
      MenuStrip.ResumeLayout(false);
      splitContainer1.Panel1.ResumeLayout(false);
      splitContainer1.Panel2.ResumeLayout(false);
      ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
      splitContainer1.ResumeLayout(false);
      ImagePanel.ResumeLayout(false);
      ImagePanel.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)ImageBox).EndInit();
      ImageMenu.ResumeLayout(false);
      ResumeLayout(false);
    }

    #endregion
    public Label SatNameLabel;
    private ToolTip toolTip1;
    public Label StatusLabel;
    private TreeView treeView1;
    private RichTextBox richTextBox1;
    private SplitContainer splitContainer1;
    private ContextMenuStrip MenuStrip;
    private ToolStripMenuItem ClearAllMNU;
    private Panel ImagePanel;
    private PictureBox ImageBox;
    private TextBox ImageMetaBox;
    private ContextMenuStrip ImageMenu;
    private ToolStripMenuItem SaveImageMNU;
    private ToolStripMenuItem CopyImageMNU;
  }
}
