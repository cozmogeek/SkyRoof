using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VE3NEA
{
  public partial class WaitBox : Form
  {
    public WaitBox()
    {
      InitializeComponent();
    }

    /// <summary>
    /// Shows a wait box, runs <paramref name="action"/>, then closes the box.
    /// Use around slow operations such as FFTW plan creation. Call on the UI thread.
    /// </summary>
    public static void Run(Action action)
    {
      var box = new WaitBox();
      box.Show();
      Application.DoEvents();
      try
      {
        action();
      }
      finally
      {
        box.Close();
        box.Dispose();
      }
    }
  }
}
