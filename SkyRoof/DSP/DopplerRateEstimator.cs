using System;

namespace SkyRoof
{
  /// <summary>
  /// Estimates the rate of change of the doppler factor from successive observations, for the
  /// Slicer's continuous (ramped) doppler correction.
  /// </summary>
  /// <remarks>
  /// Differencing the doppler factor over the measured time between observations gives a slope the
  /// mixer can extrapolate along between the few-per-second observation updates. The rate is forced
  /// to zero whenever there is nothing to extrapolate — the first sample, a gap with no observation,
  /// or a change of source (a different satellite) — so a discontinuity snaps cleanly instead of
  /// ramping a bogus slope across the gap.
  /// </remarks>
  public class DopplerRateEstimator
  {
    private double FactorRatePerSec;
    private double PrevFactor;
    private DateTime PrevUtc;
    private object? PrevSource;
    private bool HaveSample;

    // change in the doppler factor per second; 0 when the latest update was not continuous
    public double FactorRate => FactorRatePerSec;

    /// <summary>
    /// Feed one observation. <paramref name="hasObservation"/> is false when the propagator returned
    /// nothing (the factor was forced to 0); <paramref name="source"/> identifies the satellite by
    /// reference so that switching satellites resets the estimate.
    /// </summary>
    public void Update(double factor, bool hasObservation, object? source, DateTime nowUtc)
    {
      double dt = (nowUtc - PrevUtc).TotalSeconds;

      bool continuous =
        HaveSample
        && hasObservation
        && ReferenceEquals(source, PrevSource)
        && dt > 0;

      FactorRatePerSec = continuous ? (factor - PrevFactor) / dt : 0;

      PrevFactor = factor;
      PrevUtc = nowUtc;
      PrevSource = source;
      HaveSample = true;
    }
  }
}
