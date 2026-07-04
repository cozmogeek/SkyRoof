namespace SkyRoof
{
  // simple modal text prompt, used instead of native in-place label editing of TreeView/ListView,
  // which access-violates in UIAutomationCore.dll when a UIA/accessibility client is active.
  public partial class TextInputForm : Form
  {
    public TextInputForm()
    {
      InitializeComponent();
    }

    // shows the prompt modally; returns the entered (trimmed) text, or null if cancelled
    public static string? PromptForText(IWin32Window? owner, string title, string prompt, string initial = "")
    {
      using var form = new TextInputForm();
      form.Text = title;
      form.promptLabel.Text = prompt;
      form.textBox.Text = initial;

      return form.ShowDialog(owner) == DialogResult.OK ? form.textBox.Text.Trim() : null;
    }

    private void TextInputForm_Shown(object sender, EventArgs e)
    {
      textBox.SelectAll();
      textBox.Focus();
    }
  }
}
