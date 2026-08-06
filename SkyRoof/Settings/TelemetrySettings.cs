using System.ComponentModel;

namespace SkyRoof
{
  public class TelemetrySettings
  {
    [DisplayName("Save to File")]
    [Description("Save decoded frames to a file")]
    [DefaultValue(false)]
    public bool ArchiveToFile { get; set; }

    [DisplayName("KISS Server")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public KissServerSettings KissServer { get; set; } = new();

    [DisplayName("SatNOGS Upload")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public SatnogsUploaderSettings SatnogsUploader { get; set; } = new();

    [DisplayName("Report to AMSAT")]
    [Description("Automatically offer to report reception to the AMSAT Satellite Status Page after decoding SSTV or logging a QSO. The Waterfall satellite menu still allows manual reports when this is off.")]
    [DefaultValue(true)]
    public bool ReportToAmsat { get; set; } = true;

    [Browsable(false)]
    [DefaultValue(247)]
    public int SplitterDistance { get; set; } = 247;

    [Browsable(false)]
    [DefaultValue(416)]
    public int ImageSplitterDistance { get; set; } = 416;


    public override string ToString() { return string.Empty; }
  }
}
