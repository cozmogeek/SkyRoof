using MathNet.Numerics;
using VE3NEA;

namespace SkyRoof
{
  /// <summary>
  /// Downsamples complex IQ from the SDR rate to a recording rate using the same
  /// liquid-dsp octave + rational path as <see cref="Slicer"/>.
  /// </summary>
  public unsafe class IqDecimator : IDisposable
  {
    private const int StopbandRejectionDb = 80;
    private const int FilterDelay = 45;

    private int octaveDecimationFactor;
    private int rationalInterpolationFactor;
    private int rationalDecimationFactor;
    private NativeLiquidDsp.msresamp2_crcf* msresamp2;
    private NativeLiquidDsp.rresamp_crcf* rresamp;
    private readonly FifoBuffer<Complex32> pendingInput = new();
    private readonly FifoBuffer<Complex32> octaveInput = new();
    private readonly FifoBuffer<Complex32> rationalInput = new();
    private readonly FifoBuffer<Complex32> output = new();
    private bool disposed;

    public IqDecimator(int inputRate, int outputRate)
    {
      if (inputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
      if (outputRate <= 0 || outputRate >= inputRate) throw new ArgumentOutOfRangeException(nameof(outputRate));

      double usefulBandwidth = 0.95 * outputRate / 2;
      double afterOctave = CreateOctaveResampler(inputRate, outputRate, usefulBandwidth);
      CreateRationalResampler(afterOctave, outputRate, usefulBandwidth);
      ActualOutputRate = (int)Math.Round(afterOctave * rationalInterpolationFactor / (double)rationalDecimationFactor);
    }

    public Complex32[] OutputData => output.Data;
    public int ActualOutputRate { get; }

    public int Process(Complex32[] data, int count)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (count <= 0) return output.Count;

      pendingInput.EnsureExtraSpace(count);
      Array.Copy(data, 0, pendingInput.Data, pendingInput.Count, count);
      pendingInput.Count += count;

      if (msresamp2 == null)
        ApplyRationalResampler(pendingInput);
      else
      {
        ApplyOctaveResampler();
        ApplyRationalResampler(rationalInput);
      }

      return output.Count;
    }

    public void ConsumeOutput()
    {
      output.Count = 0;
    }

    public void Dispose()
    {
      if (disposed) return;
      disposed = true;
      if (msresamp2 != null) NativeLiquidDsp.msresamp2_crcf_destroy(msresamp2);
      if (rresamp != null) NativeLiquidDsp.rresamp_crcf_destroy(rresamp);
      msresamp2 = null;
      rresamp = null;
    }

    private double CreateOctaveResampler(int inputRate, int outputRate, double usefulBandwidth)
    {
      int octaveStageCount = (int)Math.Ceiling(Math.Log2(inputRate / (double)outputRate)) - 1;
      octaveDecimationFactor = 1 << Math.Max(0, octaveStageCount);
      if (octaveDecimationFactor == 1) return inputRate;

      double afterOctave = inputRate / (double)octaveDecimationFactor;
      double fc = usefulBandwidth / afterOctave;

      msresamp2 = NativeLiquidDsp.msresamp2_crcf_create(
        NativeLiquidDsp.LiquidResampType.LIQUID_RESAMP_DECIM,
        (uint)octaveStageCount,
        (float)fc,
        0,
        StopbandRejectionDb);

      return afterOctave;
    }

    private void CreateRationalResampler(double inputRate, int outputRate, double usefulBandwidth)
    {
      double factor = outputRate / inputRate;
      (rationalInterpolationFactor, rationalDecimationFactor) = Dsp.ApproximateRatio(factor, 1e-4);

      double filterRate = inputRate * rationalInterpolationFactor;
      float fc = (float)(usefulBandwidth / filterRate);

      int filterLength = 2 * FilterDelay * rationalInterpolationFactor + 1;
      var filter = NativeLiquidDsp.firfilt_crcf_create_kaiser((uint)filterLength, fc, StopbandRejectionDb, 0);
      var coeffPointer = NativeLiquidDsp.firfilt_crcf_get_coefficients(filter);

      rresamp = NativeLiquidDsp.rresamp_crcf_create(
        (uint)rationalInterpolationFactor,
        (uint)rationalDecimationFactor,
        (uint)FilterDelay,
        coeffPointer);

      NativeLiquidDsp.firfilt_crcf_destroy(filter);
    }

    private void ApplyOctaveResampler()
    {
      octaveInput.Append(pendingInput);
      pendingInput.Count = 0;

      int blockCount = octaveInput.Count / octaveDecimationFactor;
      if (blockCount == 0) return;
      rationalInput.EnsureExtraSpace(blockCount);

      fixed (Complex32* pInBuffer = octaveInput.Data)
      fixed (Complex32* pOutBuffer = rationalInput.Data)
      {
        Complex32* pBlock = pInBuffer;
        Complex32* pOut = pOutBuffer + rationalInput.Count;

        for (int blockNo = 0; blockNo < blockCount; blockNo++)
        {
          int rc = NativeLiquidDsp.msresamp2_crcf_execute(msresamp2, pBlock, out pOut[blockNo]);
          if (rc != 0) throw new Exception($"LiquidDsp error {rc}");
          pBlock += octaveDecimationFactor;
        }
      }

      octaveInput.Dump(blockCount * octaveDecimationFactor);
      rationalInput.Count += blockCount;
    }

    private void ApplyRationalResampler(FifoBuffer<Complex32> buffer)
    {
      int blockCount = buffer.Count / rationalDecimationFactor;
      if (blockCount == 0) return;

      int consumedCount = blockCount * rationalDecimationFactor;
      int outputCount = blockCount * rationalInterpolationFactor;
      output.EnsureExtraSpace(outputCount);

      fixed (Complex32* pInBuffer = buffer.Data)
      fixed (Complex32* pOutBuffer = output.Data)
      {
        Complex32* pIn = pInBuffer;
        Complex32* pOut = pOutBuffer + output.Count;

        for (int blockNo = 0; blockNo < blockCount; blockNo++)
        {
          NativeLiquidDsp.rresamp_crcf_execute(rresamp, pIn, pOut);
          pIn += rationalDecimationFactor;
          pOut += rationalInterpolationFactor;
        }
      }

      buffer.Dump(consumedCount);
      output.Count += outputCount;
    }
  }
}
