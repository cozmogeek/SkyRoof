using System;
using MathNet.Numerics;
using VE3NEA;

namespace SkyRoof
{
  /// <summary>
  /// A tunable complex down-converter whose frequency is swept continuously along a doppler ramp.
  /// </summary>
  /// <remarks>
  /// Owns a liquid-dsp NCO and the thread-safe handoff between the UI thread (which calls
  /// <see cref="SetTarget"/> a few times per second) and the audio worker (which calls
  /// <see cref="MixDown"/> per block). The worker snaps the NCO to the freshly requested target and
  /// then ramps its frequency at the requested rate, so the correction varies smoothly instead of
  /// stepping. The ramp arithmetic lives in <see cref="DopplerRamp"/>; this class is the actuator.
  /// </remarks>
  public unsafe class RampedMixer : IDisposable
  {
    private const double MAX_RAMP_SEC = 1.0; // runaway guard: freeze the ramp if updates stop
    private readonly double InputRate;
    private NativeLiquidDsp.nco_crcf* Nco;

    private readonly object Lock = new();
    private double PendingOffsetHz, PendingRateHz;
    private bool Pending;
    private double RampOffsetHz, RampRateHz; // worker-thread ramp state
    private long SamplesSinceSnap;

    public RampedMixer(double inputRate)
    {
      InputRate = inputRate;
      Nco = NativeLiquidDsp.nco_crcf_create(NativeLiquidDsp.LiquidNcoType.LIQUID_NCO);
    }

    /// <summary>
    /// Set the target frequency (Hz) and the rate (Hz/s) to ramp at until the next call.
    /// <paramref name="rateHzPerSec"/> = 0 applies a single clean step.
    /// </summary>
    public void SetTarget(double offsetHz, double rateHzPerSec)
    {
      lock (Lock)
      {
        PendingOffsetHz = offsetHz;
        PendingRateHz = rateHzPerSec;
        Pending = true;
      }
    }

    // mix a block down to baseband in place, sweeping the frequency in fine sub-blocks for
    // continuous correction. phase stays continuous across chunks because nco_crcf_set_frequency
    // leaves the accumulated phase untouched.
    public void MixDown(Complex32* data, int count)
    {
      // adopt a pending target: snap the mixer and restart the ramp from this block
      lock (Lock)
        if (Pending)
        {
          RampOffsetHz = PendingOffsetHz;
          RampRateHz = PendingRateHz;
          SamplesSinceSnap = 0;
          Pending = false;
        }

      int chunk = DopplerRamp.ChunkSize(InputRate);
      for (int i = 0; i < count; i += chunk)
      {
        int n = Math.Min(chunk, count - i);
        float w = (float)DopplerRamp.FrequencyRadPerSample(
          SamplesSinceSnap, RampOffsetHz, RampRateHz, InputRate, MAX_RAMP_SEC);
        NativeLiquidDsp.nco_crcf_set_frequency(Nco, w);
        NativeLiquidDsp.nco_crcf_mix_block_down(Nco, data + i, data + i, (uint)n);
        SamplesSinceSnap += n;
      }
    }

    public void Dispose()
    {
      if (Nco != null) NativeLiquidDsp.nco_crcf_destroy(Nco);
      Nco = null;
    }
  }
}
