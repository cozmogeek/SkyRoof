using System.ComponentModel;

namespace SkyRoof
{
  public class SatnogsUploaderSettings
  {
    [Description("Upload decoded telemetry frames to the SatNOGS DB (db.satnogs.org). Requires a SatNOGS-DB API key")]
    [DefaultValue(false)]
    public bool Enabled { get; set; } = false;

    [DisplayName("API Key")]
    [Description("Your SatNOGS DB API key (profile -> Settings -> API Key on the SatNOGS server)")]
    public string ApiToken { get; set; } = "";


    public override string ToString() { return string.Empty; }
  }
}
