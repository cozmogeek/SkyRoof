using MathNet.Numerics;
using VE3NEA;
using VE3NEA.Tlm.Core;

namespace SkyRoof
{
  public class TelemetryDecocder : ThreadedProcessor<Complex32>
  {
    public StreamingPipeline? Pipeline;

    public TelemetryDecocder(SignalParams signalParams)
    {
      Pipeline = new StreamingPipeline(signalParams);
    }

    protected override void Process(DataEventArgs<Complex32> args)
    {
      Pipeline?.Push(args.Data); 
    }

    public override void Dispose()
    {
      // stop and join the worker thread BEFORE freeing the pipeline's native FFTW memory, otherwise an
      // in-flight Push() on that thread runs an FFT against freed buffers and corrupts the native heap.
      base.Dispose();
      Pipeline?.Dispose();
      Pipeline = null;
    }
  }
}
