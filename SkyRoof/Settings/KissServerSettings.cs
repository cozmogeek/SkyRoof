using System.ComponentModel;

namespace SkyRoof
{
  public class KissServerSettings
  {
    [Description("Share decoded telemetry frames over a KISS-over-TCP server (compatible with UZ7HO soundmodem and Dire Wolf KISS clients)")]
    public bool Enabled { get; set; } = false;

    [DisplayName("TCP Port")]
    [Description("KISS server TCP port (8100 = UZ7HO soundmodem, 8001 = Dire Wolf)")]
    [DefaultValue((ushort)8100)]
    public ushort Port { get; set; } = 8100;


    public override string ToString() { return string.Empty; }
  }
}
