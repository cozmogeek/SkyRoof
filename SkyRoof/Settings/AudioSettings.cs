using System.ComponentModel;
using CSCore.CoreAudioAPI;
using VE3NEA;

namespace SkyRoof
{
  public class AudioSettings
  {
    // non-browsable
    public int SoundcardVolume = -25;
    public bool SpeakerEnabled = true;

    [DisplayName("Speaker Audio Device")]
    [Description("Soundcard for audio output")]
    [TypeConverter(typeof(OutputSoundcardNameConverter))]
    public string? SpeakerSoundcard { get; set; } = Soundcard.GetDefaultSoundcardId(DataFlow.Render);

    [DisplayName("FM Squelch")]
    [Description("Enable Squelch in the FM mode")]
    [DefaultValue(true)]
    public bool Squelch { get; set; } = true;

    [DisplayName("Mode Volume")]
    [Description("Relative speaker volume for each demodulation mode (dB). Use to balance loud FM vs quiet SSB/CW.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public ModeVolumeSettings ModeVolume { get; set; } = new();

    public override string ToString() { return string.Empty; }
  }

  /// <summary>
  /// Relative AF gain per <see cref="Slicer.Mode"/>, applied in the slicer after demodulation.
  /// 0 dB leaves the existing mode levels unchanged.
  /// </summary>
  public class ModeVolumeSettings
  {
    private const int ModeCount = 7;

    [DisplayName("USB")]
    [Description("Relative volume for USB (dB)")]
    [DefaultValue(0)]
    public int Usb { get; set; } = 0;

    [DisplayName("LSB")]
    [Description("Relative volume for LSB (dB)")]
    [DefaultValue(0)]
    public int Lsb { get; set; } = 0;

    [DisplayName("USB Data")]
    [Description("Relative volume for USB_D (dB)")]
    [DefaultValue(0)]
    public int UsbData { get; set; } = 0;

    [DisplayName("LSB Data")]
    [Description("Relative volume for LSB_D (dB)")]
    [DefaultValue(0)]
    public int LsbData { get; set; } = 0;

    [DisplayName("CW")]
    [Description("Relative volume for CW (dB). Applied on top of the built-in CW boost.")]
    [DefaultValue(0)]
    public int Cw { get; set; } = 0;

    [DisplayName("FM")]
    [Description("Relative volume for FM (dB)")]
    [DefaultValue(0)]
    public int Fm { get; set; } = 0;

    [DisplayName("FM Data")]
    [Description("Relative volume for FM_D (dB)")]
    [DefaultValue(0)]
    public int FmData { get; set; } = 0;

    public int GetDb(Slicer.Mode mode) => mode switch
    {
      Slicer.Mode.USB => Usb,
      Slicer.Mode.LSB => Lsb,
      Slicer.Mode.USB_D => UsbData,
      Slicer.Mode.LSB_D => LsbData,
      Slicer.Mode.CW => Cw,
      Slicer.Mode.FM => Fm,
      Slicer.Mode.FM_D => FmData,
      _ => 0,
    };

    /// <summary>Write linear (amplitude) gains for all modes into <paramref name="dest"/> (length ≥ 7).</summary>
    public void CopyLinearGainsTo(float[] dest)
    {
      for (int i = 0; i < ModeCount; i++)
        dest[i] = Dsp.FromDb2(GetDb((Slicer.Mode)i));
    }

    public override string ToString() { return string.Empty; }
  }
}
