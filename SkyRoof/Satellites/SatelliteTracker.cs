using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using SGPdotNET.CoordinateSystem;
using SGPdotNET.Observation;
using SGPdotNET.TLE;
using static SkyRoof.SatellitePass;

namespace SkyRoof
{
  public class SatelliteTracker
  {
    private Satellite? Satellite;
    public Tle? Tle;
    public bool Enabled { get; private set; }

    public SatelliteTracker(SatnogsDbTle? tle)
    {
      if (tle == null) return;

      Tle = CreateTle(tle);
      if (Tle == null) return;

      Satellite = new(Tle);
      Enabled = true;
    }

    public Tle? CreateTle(SatnogsDbTle tle) => CreateSgpTle(tle);

    // Space-Track catalog numbers above 99999 use Alpha-5 in the 5-character TLE field
    // (A1101 = 101101). SGP.NET 1.4 only accepts digits there, so the letter is mapped to
    // norad % 100000 before parse. SGP4 does not use the catalog number for propagation.
    internal static Tle? CreateSgpTle(SatnogsDbTle tle)
    {
      if (tle == null) return null;
      try
      {
        return new Tle(tle.tle0, NumericCatalogLine(tle.tle1), NumericCatalogLine(tle.tle2));
      }
      catch (Exception ex)
      {
        Log.Error(ex, $"Error creating TLE object: |{tle.tle0}|{tle.tle1}|{tle.tle2}|.");
        return null;
      }
    }

    private static string NumericCatalogLine(string line)
    {
      if (string.IsNullOrEmpty(line) || line.Length < 7) return line;

      string field = line.Substring(2, 5);
      if (field.All(char.IsDigit)) return line;
      if (!TryDecodeAlpha5(field, out int norad)) return line;

      var chars = (line.Length >= 69 ? line : line.PadRight(69)).ToCharArray();
      (norad % 100000).ToString("00000").CopyTo(0, chars, 2, 5);
      chars[68] = (char)('0' + TleChecksum(chars));
      return new string(chars, 0, 69);
    }

    // Celestrak Alpha-5: first character A=10 … Z=33, skipping I and O
    internal static bool TryDecodeAlpha5(string field, out int norad)
    {
      norad = 0;
      if (string.IsNullOrWhiteSpace(field) || field.Length != 5) return false;

      char c = char.ToUpperInvariant(field[0]);
      int high;
      if (c is >= '0' and <= '9') high = c - '0';
      else if (c is >= 'A' and <= 'H') high = c - 'A' + 10;
      else if (c is >= 'J' and <= 'N') high = c - 'J' + 18;
      else if (c is >= 'P' and <= 'Z') high = c - 'P' + 23;
      else return false;

      if (!int.TryParse(field.AsSpan(1, 4), out int rest)) return false;
      norad = high * 10000 + rest;
      return true;
    }

    private static int TleChecksum(char[] line)
    {
      int sum = 0;
      for (int i = 0; i < 68 && i < line.Length; i++)
      {
        char c = line[i];
        if (c is >= '0' and <= '9') sum += c - '0';
        else if (c == '-') sum += 1;
      }
      return sum % 10;
    }

    public GeodeticCoordinate? Predict(DateTime? utc = null)
    {
      if (!Enabled) return null;

      try
      {
        return Satellite!.Predict(utc ?? DateTime.UtcNow).ToGeodetic();
      }
      catch (Exception ex)
      {
        // preduction for current time failed, disable further tracking
        Enabled = false;
        Log.Error(ex, $"Predict failed for {Tle!.Name} ({Tle.NoradNumber}).");
        return null;
      }
    }

    internal TopocentricObservation? Observe(GroundStation groundStation, DateTime utc)
    {
      if (!Enabled) return null;

      try
      {
        return groundStation.Observe(Satellite, utc);
      }
      catch (Exception ex)
      {
        Enabled = false;
        Log.Error(ex, $"Observe failed for {Tle!.Name} ({Tle.NoradNumber}).");
        return null;
      }
    }

    internal List<SatelliteVisibilityPeriod> ComputePasses(GroundStation groundStation, DateTime startTime, DateTime endTime)
    {
      if (!Enabled) return new();

      try
      {
        return groundStation.Observe(Satellite, startTime, endTime, TimeSpan.FromSeconds(15), clipToStartTime: false);
      }
      catch (Exception ex)
      {
        Enabled = false;
        Log.Error(ex, $"ComputePasses failed for {Tle!.Name} ({Tle.NoradNumber}).");
        return new();
      }
    }

    internal bool IsGeoStationary()
    {
      if (!Enabled) return false;
      return Math.Abs(Tle!.MeanMotionRevPerDay - 1) < 0.1f;
    }

    internal List<SatelliteVisibilityPeriod> ComputeGeostationaryPasses(GroundStation groundStation)
    {
      var observation = Observe(groundStation, DateTime.UtcNow);
      if (observation == null || observation.Elevation < 0) return new();

      return new()
      {
        new SatelliteVisibilityPeriod(
          null,
          DateTime.UtcNow,
          DateTime.UtcNow + TimeSpan.FromDays(1),
          observation.Elevation,
          DateTime.UtcNow + TimeSpan.FromHours(12)
          )
      };
    }
  }
}
