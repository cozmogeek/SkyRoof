using System.ComponentModel;

namespace SkyRoof
{
  public class RecordingSettings
  {
    public const double DefaultBasebandIqMsps = 2.4;

    [DisplayName("Baseband IQ Rate, Msps")]
    [Description("Sample rate for wideband I/Q recordings (LRPT/HRPT). The SDR stream is downsampled to this rate. Use 0 to record at the SDR native rate.")]
    [DefaultValue(DefaultBasebandIqMsps)]
    public double BasebandIqMsps { get; set; } = DefaultBasebandIqMsps;

    public int GetWidebandIqRate(int sdrSampleRate)
    {
      if (sdrSampleRate <= 0) return sdrSampleRate;
      if (BasebandIqMsps <= 0) return sdrSampleRate;

      int want = (int)Math.Round(BasebandIqMsps * 1_000_000.0);
      want = Math.Clamp(want, SdrConst.AUDIO_SAMPLING_RATE, 20_000_000);
      return Math.Min(want, sdrSampleRate);
    }

    public override string ToString() { return string.Empty; }
  }
}
