namespace SkyRoof
{
  partial class EarthViewPanel
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
      openglControl1 = new SharpGL.OpenGLControl();
      label1 = new Label();
      ModePanel = new FlowLayoutPanel();
      RealTimeRadioBtn = new RadioButton();
      PassRadioBtn = new RadioButton();
      ((System.ComponentModel.ISupportInitialize)openglControl1).BeginInit();
      ModePanel.SuspendLayout();
      SuspendLayout();
      //
      // openglControl1
      //
      openglControl1.Dock = DockStyle.Fill;
      openglControl1.DrawFPS = false;
      openglControl1.Location = new Point(0, 0);
      openglControl1.Margin = new Padding(4, 3, 4, 3);
      openglControl1.Name = "openglControl1";
      openglControl1.OpenGLVersion = SharpGL.Version.OpenGLVersion.OpenGL3_3;
      openglControl1.RenderContextType = SharpGL.RenderContextType.NativeWindow;
      // Manual (not TimerBased): SharpGL's autonomous render timer keeps drawing through the
      // handle destroy/recreate that docking causes, corrupting the GL context. Render on
      // demand via openglControl1.Invalidate() instead, matching WaterfallControl.
      openglControl1.RenderTrigger = SharpGL.RenderTrigger.Manual;
      openglControl1.Size = new Size(400, 379);
      openglControl1.TabIndex = 1;
      openglControl1.OpenGLInitialized += openglControl1_OpenGLInitialized;
      openglControl1.OpenGLDraw += openglControl1_OpenGLDraw;
      openglControl1.Resize += openglControl1_Resize;
      //
      // label1
      //
      label1.AutoSize = true;
      label1.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
      label1.Location = new Point(4, 30);
      label1.Name = "label1";
      label1.Size = new Size(27, 19);
      label1.TabIndex = 2;
      label1.Text = "___";
      //
      // ModePanel
      //
      ModePanel.AutoSize = true;
      ModePanel.Controls.Add(RealTimeRadioBtn);
      ModePanel.Controls.Add(PassRadioBtn);
      ModePanel.Dock = DockStyle.Top;
      ModePanel.Location = new Point(0, 0);
      ModePanel.MaximumSize = new Size(0, 100);
      ModePanel.Name = "ModePanel";
      ModePanel.Size = new Size(400, 25);
      ModePanel.TabIndex = 3;
      //
      // RealTimeRadioBtn
      //
      RealTimeRadioBtn.AutoSize = true;
      RealTimeRadioBtn.Checked = true;
      RealTimeRadioBtn.Location = new Point(3, 3);
      RealTimeRadioBtn.Name = "RealTimeRadioBtn";
      RealTimeRadioBtn.Size = new Size(76, 19);
      RealTimeRadioBtn.TabIndex = 0;
      RealTimeRadioBtn.TabStop = true;
      RealTimeRadioBtn.Text = "Real Time";
      RealTimeRadioBtn.UseVisualStyleBackColor = true;
      RealTimeRadioBtn.CheckedChanged += radioButton_CheckedChanged;
      //
      // PassRadioBtn
      //
      PassRadioBtn.AutoSize = true;
      PassRadioBtn.Enabled = false;
      PassRadioBtn.Location = new Point(85, 3);
      PassRadioBtn.Name = "PassRadioBtn";
      PassRadioBtn.Size = new Size(99, 19);
      PassRadioBtn.TabIndex = 1;
      PassRadioBtn.Text = "Selected Pass";
      PassRadioBtn.UseVisualStyleBackColor = true;
      PassRadioBtn.CheckedChanged += radioButton_CheckedChanged;
      //
      // EarthViewPanel
      //
      AutoScaleDimensions = new SizeF(7F, 15F);
      AutoScaleMode = AutoScaleMode.Font;
      ClientSize = new Size(400, 379);
      Controls.Add(label1);
      Controls.Add(openglControl1);
      Controls.Add(ModePanel);
      Name = "EarthViewPanel";
      Text = "Earth View";
      FormClosing += EarthViewPanel_FormClosing;
      ((System.ComponentModel.ISupportInitialize)openglControl1).EndInit();
      ModePanel.ResumeLayout(false);
      ModePanel.PerformLayout();
      ResumeLayout(false);
      PerformLayout();
    }

    #endregion

    private SharpGL.OpenGLControl openglControl1;
    private Label label1;
    private FlowLayoutPanel ModePanel;
    private RadioButton RealTimeRadioBtn;
    private RadioButton PassRadioBtn;
  }
}