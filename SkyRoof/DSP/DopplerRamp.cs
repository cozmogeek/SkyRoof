using System;

namespace SkyRoof
{
  /// <summary>
  /// Pure, native-free schedule for a continuously-swept doppler-correction frequency.
  /// </summary>
  /// <remarks>
  /// The downlink doppler offset is recomputed only a few times per second, which would make a
  /// mixer NCO step its frequency in an audible staircase. Instead of stepping, the mixer snaps to
  /// the freshly measured offset and then ramps linearly at the measured doppler rate, so by the
  /// time the next correction arrives the NCO is already approximately right (open-loop
  /// extrapolation). This class holds only the arithmetic of that ramp so it can be unit-tested
  /// without loading liquid-dsp; the actual mixing lives in the <see cref="Slicer"/>.
  /// </remarks>
  public static class DopplerRamp
  {
    /// <summary>
    /// Doppler-corrected frequency, in Hz, at <paramref name="elapsedSec"/> seconds after the last
    /// snap. The ramp is capped at <paramref name="maxRampSec"/> so a stale rate cannot sweep the
    /// frequency away if updates stop arriving (runaway guard).
    /// </summary>
    public static double FrequencyHz(double elapsedSec, double offsetHz, double rateHzPerSec, double maxRampSec)
    {
      double t = Math.Min(elapsedSec, maxRampSec);
      if (t < 0) t = 0;
      return offsetHz + rateHzPerSec * t;
    }

    /// <summary>
    /// NCO angular frequency, in radians per sample, for the chunk that starts
    /// <paramref name="samplesSinceSnap"/> samples after the last snap.
    /// </summary>
    public static double FrequencyRadPerSample(long samplesSinceSnap, double offsetHz,
      double rateHzPerSec, double inputRate, double maxRampSec)
    {
      double hz = FrequencyHz(samplesSinceSnap / inputRate, offsetHz, rateHzPerSec, maxRampSec);
      return Math.Tau * hz / inputRate;
    }

    /// <summary>
    /// Number of input samples to mix at a constant frequency before re-evaluating the ramp.
    /// Chosen for a ~1 ms step regardless of sample rate; at worst-case doppler rates the residual
    /// per-chunk frequency step is about 1 Hz, well below audibility. Never less than one sample.
    /// </summary>
    public static int ChunkSize(double inputRate) => Math.Max(1, (int)(inputRate / 1000.0));
  }
}
