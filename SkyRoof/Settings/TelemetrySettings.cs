using System.ComponentModel;
using VE3NEA;

namespace SkyRoof
{
  // the denoising filter applied to an SSTV image before it is auto-saved
  public enum SstvSaveFilter
  {
    [Description("None")]
    None,
    [Description("Wiener")]
    Wiener,
    [Description("Non-local means")]
    Nlm,
  }

  public class TelemetrySettings
  {
    [DisplayName("Background Decode")]
    [Description("Decode telemetry from other satellites that are currently in the SDR passband, not only the selected one. Each in-band downlink gets its own Doppler-tracked channel; frames appear under that satellite in the Telemetry panel. The selected satellite is unchanged. Uses extra CPU.")]
    [DefaultValue(true)]
    public bool BackgroundDecode { get; set; } = true;

    [DisplayName("Save to File")]
    [Description("Save decoded frames to a file")]
    [DefaultValue(false)]
    public bool ArchiveToFile { get; set; }

    [DisplayName("SSTV Auto-Save Filter")]
    [Description("Denoising filter applied to SSTV images when they are saved automatically")]
    [DefaultValue(SstvSaveFilter.Wiener)]
    [TypeConverter(typeof(EnumDescriptionConverter))]
    public SstvSaveFilter SstvSaveFilter { get; set; } = SstvSaveFilter.Wiener;

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

    // height of the text sub-panel below the image. This is the fixed panel of ImageSplitContainer, so it,
    // and not the splitter distance, is the quantity that survives a resize of the panel
    [Browsable(false)]
    [DefaultValue(106)]
    public int ImageTextHeight { get; set; } = 106;


    public override string ToString() { return string.Empty; }
  }
}
