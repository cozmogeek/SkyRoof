using System.ComponentModel;

namespace SkyRoof
{
  public class SatnogsUploaderSettings
  {
    [Description("Upload decoded telemetry frames to the SatNOGS DB (db.satnogs.org). Requires your callsign and grid square (set under User) and a SatNOGS DB API key")]
    public bool Enabled { get; set; } = false;

    [DisplayName("API Key")]
    [Description("Your SatNOGS DB API key (profile -> Settings -> API Key on the server set in URL); a permanent key, regenerate it there if compromised. The dev and production servers have separate accounts and keys")]
    public string ApiToken { get; set; } = "";

    [DisplayName("URL")]
    [Description("SatNOGS DB telemetry endpoint (SiDS protocol). Defaults to the dev/staging server (db-dev.satnogs.org) for testing; switch to https://db.satnogs.org/api/telemetry/ to submit to the production database")]
    [DefaultValue("https://db-dev.satnogs.org/api/telemetry/")]
    public string Url { get; set; } = "https://db-dev.satnogs.org/api/telemetry/";


    public override string ToString() { return string.Empty; }
  }
}
