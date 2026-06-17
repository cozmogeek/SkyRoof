using MathNet.Numerics;
using VE3NEA;
using VE3NEA.Tlm.Core;

namespace SkyRoof
{
  public class TelemetryDecocder : ThreadedProcessor<Complex32>
  {
    public StreamingPipeline Pipeline;

    public TelemetryDecocder(SignalParams signalParams)
    {
      Pipeline = new StreamingPipeline(signalParams);
    }

    protected override void Process(DataEventArgs<Complex32> args)
    {
      Pipeline?.Push(args.Data); 
    }
  }
}
