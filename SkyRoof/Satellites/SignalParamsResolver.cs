using System.Text.RegularExpressions;
using VE3NEA.Tlm.Core;

namespace SkyRoof.Satellites
{
  internal class SignalParamsResolver
  {
    // sample rate of the audio stream fed to the StreamingPipeline; the demodulator resamples internally
    private const double AudioSampleRate = 48000;

    public static SignalParams? Resolve(SatnogsDbTransmitter tx)
    {
      if (tx == null) return null;
      var grSats = tx.gr_sats;

      // Baud: prefer the curated gr-satellites baudrate, then the SatNOGS DB field, then a token parsed
      // from the description (e.g. "GMSK 9k6"). No baud ⇒ format not supported.
      double? baud = grSats?.baudrate ?? tx.baud ?? ParseBaud(tx.description);
      if (baud is not double bd) return null;

      // Modulation: trust the gr-satellites enrichment first (curated), then the description, then the mode.
      Modulation mod = ExtractyModulation(grSats?.modulation);
      if (mod == Modulation.Unknown) mod = ExtractyModulation(tx.description);
      if (mod == Modulation.Unknown) mod = ExtractyModulation(tx.mode);
      if (mod == Modulation.Unknown) mod = ExtractyModulation(tx.DownlinkMode);

      // Framing: take it from the gr-satellites enrichment first (e.g. "AX100 ASM+Golay"), then from the
      // DB mode, then the description
      Framing framing = ExtractFraming(grSats?.framing);
      if (framing == Framing.Unknown) framing = ExtractFraming(tx.description);

      // Deviation: GMSK ⇒ baud/4 (derived in SignalParams); else the gr-satellites enrichment value (which
      // carries the hand-curated transmitter overrides folded in by SkyRoof, e.g. SATURN/HADES-SA FSK deviation).
      double? devOverride = mod == Modulation.Gmsk ? null : grSats?.deviation;

      // Differential (PSK only): taken from the gr-satellites satyaml precoding — unlike the DB's unreliable
      // BPSK/DBPSK labels, gr-satellites states it explicitly (e.g. ASRTU-1 "precoding: differential"). Default
      // to coherent: every off-air PSK signal in the corpus (TEVEL2-1, Eaglet-1) is coherent, so we resolve
      // coherent unless precoding explicitly says "differential".
      bool? differential = null;
      if (mod == Modulation.Bpsk || mod == Modulation.Qpsk)
        differential = grSats?.precoding is string pc && pc.Contains("differential", StringComparison.OrdinalIgnoreCase);

      return new SignalParams(bd, mod, framing, AudioSampleRate, devOverride)
      {
        Manchester = IsManchester(grSats?.modulation, tx.description),
        Differential = differential,
        RsBasis = grSats?.rs_basis,
        FrameSize = grSats?.frame_size
      };
    }


    /// <summary>Parse a baud token: <c>9k6→9600</c>, <c>2k4→2400</c>, plain <c>800/600/300</c>.</summary>
    private static readonly Regex BaudTokenRegex = new(
  @"(?<a>\d{1,2})k(?<b>\d)|(?<plain>\d{3,5})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static double? ParseBaud(string? text)
    {
      if (string.IsNullOrEmpty(text)) return null;
      foreach (Match m in BaudTokenRegex.Matches(text))
      {
        if (m.Groups["a"].Success)
          return int.Parse(m.Groups["a"].Value) * 1000 + int.Parse(m.Groups["b"].Value) * 100;
        if (m.Groups["plain"].Success)
        {
          int v = int.Parse(m.Groups["plain"].Value);
          if (v is 300 or 600 or 800 or 1200 or 2400 or 4800 or 9600 or 19200) return v;
        }
      }
      return null;
    }


    /// <summary>Map one DB <c>mode</c> / <c>description</c> / gr-satellites string to a deframing flavor (FR-5).</summary>
    public static Framing ExtractFraming(string? text)
    {
      string s = text ?? "";

      // GOMspace AX100: gr-satellites framing strings "AX100 ASM+Golay" / "AX100 Reed Solomon", or DB
      // descriptions like "GMSK 4k8 AX.100 Mode 5". Mode 5 = ASM+Golay, Mode 6 = the RS framing; plain
      // "AX100" defaults to ASM+Golay (the overwhelmingly common flavor in the wild).
      if (s.Contains("AX100", StringComparison.OrdinalIgnoreCase) ||
          s.Contains("AX.100", StringComparison.OrdinalIgnoreCase))
        return s.Contains("Reed", StringComparison.OrdinalIgnoreCase) ||
               s.Contains("Mode 6", StringComparison.OrdinalIgnoreCase)
          ? Framing.Ax100Rs : Framing.Ax100Asm;

      if (s.Contains("USP", StringComparison.OrdinalIgnoreCase))
        return Framing.Usp;

      if (s.Contains("AX.25", StringComparison.OrdinalIgnoreCase) ||
          s.Contains("G3RUH", StringComparison.OrdinalIgnoreCase))
        return Framing.Ax25G3ruh;

      // AMSAT-EA GENESIS family (HADES-SA SpinnyONE et al.): SatNOGS labels these "GENESIS FSK"/HADES.
      if (s.Contains("GENESIS", StringComparison.OrdinalIgnoreCase) ||
          s.Contains("HADES", StringComparison.OrdinalIgnoreCase))
        return Framing.Hades;

      return Framing.Unknown;
    }

    /// <summary>
    /// Classify modulation from one DB <c>mode</c> / <c>description</c> string. Order matters:
    /// GMSK/GFSK are checked before the generic "FSK" substring they contain. AFSK is treated as
    /// plain FSK (the demodulator handles it on the FSK path).
    /// </summary>
    public static Modulation ExtractyModulation(string? text)
    {
      string s = (text ?? "").ToUpperInvariant();
      if (s.Contains("GMSK")) return Modulation.Gmsk;
      if (s.Contains("GFSK")) return Modulation.Gfsk;
      if (s.Contains("AFSK")) return Modulation.Fsk;
      if (s.Contains("MSK")) return Modulation.Fsk;
      if (s.Contains("FSK")) return Modulation.Fsk;

      // PSK constellation order only: any QPSK flavor (incl. OQPSK / DQPSK) → Qpsk, everything else PSK → Bpsk.
      // The differential-vs-coherent distinction is deliberately NOT taken from the label — the DB's
      // "BPSK"/"DBPSK" tags proved unreliable — it comes from the gr_sats precoding field instead
      // (see Resolve), defaulting to coherent.
      if (s.Contains("QPSK")) return Modulation.Qpsk;
      if (s.Contains("BPSK")) return Modulation.Bpsk;
      if (s.Contains("PSK")) return Modulation.Bpsk;

      if (s.Contains("SSTV")) return Modulation.Sstv;
      if (s.Contains("CW")) return Modulation.Cw;
      return Modulation.Unknown;
    }

    /// <summary>
    /// True if any of the supplied mode/description/modulation strings declares Manchester (bi-phase-L) line
    /// coding — the <c>DBPSK Manchester</c> AMSAT/FUNcube case. Drives <see cref="SignalParams.Manchester"/> so
    /// the demodulator combines chip pairs into data symbols.
    /// </summary>
    public static bool? IsManchester(params string?[] texts)
    {
      foreach (var t in texts)
        if (!string.IsNullOrEmpty(t) && t.Contains("MANCHESTER", StringComparison.OrdinalIgnoreCase)) return true;
      return null;
    }
  }
}
