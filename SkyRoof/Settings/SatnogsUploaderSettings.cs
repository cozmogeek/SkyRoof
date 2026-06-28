using System.ComponentModel;

namespace SkyRoof
{
  public class SatnogsUploaderSettings
  {
    [Description("Upload decoded telemetry frames to the SatNOGS DB (db.satnogs.org). Requires a SatNOGS-DB API key")]
    public bool Enabled { get; set; } = false;

    [DisplayName("API Key")]
    [Description("Your SatNOGS DB API key (profile -> Settings -> API Key on the SatNOGS server)")]
    public string ApiToken { get; set; } = "";

    [DisplayName("URL")]
    [Description("SatNOGS DB telemetry endpoint (SiDS protocol). Defaults to the testing server; switch to https://db.satnogs.org/api/telemetry/ to submit to the production database")]
    [DefaultValue("https://db-dev.satnogs.org/api/telemetry/")]
    public string Url { get; set; } = "https://db-dev.satnogs.org/api/telemetry/";


    public override string ToString() { return string.Empty; }
  }
}
