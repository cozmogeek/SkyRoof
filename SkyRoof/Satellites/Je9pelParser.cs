using System.Globalization;
using System.Text.RegularExpressions;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof.Satellites
{
  // Mines modulation / framing / baud from the free-text JE9PEL "Mode" field for the transmitter's
  // downlink, contributing a resolution layer below gr_sats (see SignalParamsResolver). JE9PEL carries
  // nothing finer (no deviation / precoding / RS), so only those three fields are filled, and a field is
  // left null where its token is absent, so the layer only fills gaps a higher layer left open.
  internal static class Je9pelParser
  {
    // a JE9PEL Downlink must fall within this of tx.downlink_low to be mined for this transmitter
    private const long FrequencyToleranceHz = 5000;

    // decimal MHz tokens in a JE9PEL Downlink field (e.g. "435.670-435.610" -> 435.670, 435.610)
    private static readonly Regex MHzTokenRegex = new(@"\d+\.\d+", RegexOptions.Compiled);

    // build the JE9PEL resolution layer for tx, or null when no JE9PEL row matches the downlink
    public static GrSatsInfo? BuildJe9pelLayer(SatnogsDbTransmitter tx)
    {
      var rows = tx.Satellite?.JE9PELtransmitters;
      if (rows == null || rows.Count == 0 || tx.downlink_low == null) return null;

      JE9PELtransmitter? row = NearestRow(rows, tx.downlink_low.Value);
      if (row == null) return null;

      // normalize the token separators JE9PEL uses ("2k4 GMSK_USP", "2k4*/9k6/19k2 GFSK CW") to spaces so
      // the shared extractors see whitespace-delimited tokens; then reuse them exactly as other layers do
      string text = (row.Mode ?? "").Replace('_', ' ').Replace('/', ' ');

      Modulation mod = SignalParamsResolver.ExtractyModulation(text);
      Framing framing = SignalParamsResolver.ExtractFraming(text);

      return new GrSatsInfo
      {
        baudrate = SignalParamsResolver.ParseBaud(text),
        modulation = mod != Modulation.Unknown ? mod.ToString() : null,
        framing = framing != Framing.Unknown ? framing.ToString() : null
      };
    }

    // the JE9PEL row whose Downlink is nearest downlinkLow, or null if the nearest is still out of tolerance
    private static JE9PELtransmitter? NearestRow(List<JE9PELtransmitter> rows, long downlinkLow)
    {
      JE9PELtransmitter? best = null;
      long bestDistance = long.MaxValue;

      foreach (var row in rows)
      {
        long? hz = NearestFrequencyHz(row.Downlink, downlinkLow);
        if (hz == null) continue;
        long distance = Math.Abs(hz.Value - downlinkLow);
        if (distance < bestDistance) { bestDistance = distance; best = row; }
      }

      return bestDistance <= FrequencyToleranceHz ? best : null;
    }

    // nearest of the MHz tokens in a JE9PEL Downlink field to targetHz, in Hz; null if the field has none.
    // a range like "435.670-435.610" contributes both endpoints and the closer one wins.
    private static long? NearestFrequencyHz(string? downlink, long targetHz)
    {
      long? best = null;
      long bestDistance = long.MaxValue;

      foreach (Match m in MHzTokenRegex.Matches(downlink ?? ""))
        if (double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double mhz))
        {
          long hz = (long)Math.Round(mhz * 1e6);
          long distance = Math.Abs(hz - targetHz);
          if (distance < bestDistance) { bestDistance = distance; best = hz; }
        }

      return best;
    }
  }
}
