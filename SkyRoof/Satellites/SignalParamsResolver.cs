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

      // TODO: return result even if baud is missing
      if (baud is not double bd) return null;

      string modeString = $"{grSats?.modulation} {tx.description} {tx.mode} {tx.DownlinkMode}";
      Modulation mod = ExtractyModulation(modeString);

      string framingString = $"{grSats?.framing} {tx.description} {tx.mode} {tx.DownlinkMode}";
      Framing framing = ExtractFraming(framingString);

      double? devOverride = mod == Modulation.GMSK ? null : grSats?.deviation;

      // Differential (PSK only): taken from the gr-satellites satyaml precoding — unlike the DB's unreliable
      // BPSK/DBPSK labels, gr-satellites states it explicitly (e.g. ASRTU-1 "precoding: differential"). Default
      // to coherent: every off-air PSK signal in the corpus (TEVEL2-1, Eaglet-1) is coherent, so we resolve
      // coherent unless precoding explicitly says "differential".
      bool? differential = null;
      if (mod == Modulation.BPSK || mod == Modulation.QPSK)
        differential = grSats?.precoding is string pc && pc.Contains("differential", StringComparison.OrdinalIgnoreCase);

      var sp = new SignalParams(bd, mod, framing, AudioSampleRate, devOverride)
      {
        Manchester = IsManchester(grSats?.modulation, tx.description),
        Differential = differential,
        RsBasis = grSats?.rs_basis,
        FrameSize = grSats?.frame_size
      };
      return framing == Framing.CCSDS ? ApplyCcsdsOptions(sp, grSats, grSats?.framing) : sp;
    }


    /// <summary>Apply CCSDS-specific carry-through facts (block variant + satyaml overrides) to the
    /// already-classified <see cref="SignalParams"/>. Mirrors <c>ParamResolver.ApplyCcsdsOptions</c>.</summary>
    private static SignalParams ApplyCcsdsOptions(SignalParams sp, GrSatsInfo? g, string? framingText)
    {
      string ft = framingText ?? "";
      bool uncoded = ft.Contains("Uncoded", StringComparison.OrdinalIgnoreCase);
      bool concatenated = ft.Contains("Concatenated", StringComparison.OrdinalIgnoreCase);
      bool? scrambler = g?.scrambler is string sc
        ? !sc.Equals("none", StringComparison.OrdinalIgnoreCase)
        : (bool?)null;
      bool? precoding = g?.precoding is string pc
        ? pc.Contains("differential", StringComparison.OrdinalIgnoreCase)
        : (bool?)null;
      return sp with
      {
        RsEnabled = !uncoded,
        Convolutional = concatenated ? g?.convolutional ?? "CCSDS" : null,
        RsInterleaving = g?.rs_interleaving,
        Scrambler = scrambler,
        Differential = precoding ?? sp.Differential
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
          ? Framing.AX100RS : Framing.AX100ASM;

      if (s.Contains("CCSDS", StringComparison.OrdinalIgnoreCase))
        return Framing.CCSDS;

      if (s.Contains("USP", StringComparison.OrdinalIgnoreCase))
        return Framing.USP;

      if (s.Contains("AX.25", StringComparison.OrdinalIgnoreCase) ||
          s.Contains("G3RUH", StringComparison.OrdinalIgnoreCase))
        return Framing.AX25G3RUH;

      // AMSAT-EA GENESIS family (HADES-SA SpinnyONE et al.): SatNOGS labels these "GENESIS FSK"/HADES.
      if (s.Contains("GENESIS", StringComparison.OrdinalIgnoreCase) ||
          s.Contains("HADES", StringComparison.OrdinalIgnoreCase))
        return Framing.HADES;

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
      if (s.Contains("GMSK")) return Modulation.GMSK;
      if (s.Contains("GFSK")) return Modulation.GFSK;
      if (s.Contains("AFSK")) return Modulation.FSK;
      if (s.Contains("MSK")) return Modulation.FSK;
      if (s.Contains("FSK")) return Modulation.FSK;

      // PSK constellation order only: any QPSK flavor (incl. OQPSK / DQPSK) → Qpsk, everything else PSK → Bpsk.
      // The differential-vs-coherent distinction is deliberately NOT taken from the label — the DB's
      // "BPSK"/"DBPSK" tags proved unreliable — it comes from the gr_sats precoding field instead
      // (see Resolve), defaulting to coherent.
      if (s.Contains("QPSK")) return Modulation.QPSK;
      if (s.Contains("BPSK")) return Modulation.BPSK;
      if (s.Contains("PSK")) return Modulation.BPSK;

      if (s.Contains("SSTV")) return Modulation.SSTV;
      if (s.Contains("CW")) return Modulation.CW;
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
