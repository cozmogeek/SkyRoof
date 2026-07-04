using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public class DopplerRampTests
  {
    // ----------------------------------------------------------------------------------------------------
    //                                  FrequencyHz: the linear schedule
    // ----------------------------------------------------------------------------------------------------
    [Fact]
    public void FrequencyHz_ZeroRate_IsConstant_RegardlessOfTime()
    {
      const double offset = 1234.5, max = 1.0;
      DopplerRamp.FrequencyHz(0.0, offset, 0, max).Should().Be(offset);
      DopplerRamp.FrequencyHz(0.5, offset, 0, max).Should().Be(offset);
      DopplerRamp.FrequencyHz(10.0, offset, 0, max).Should().Be(offset);
    }

    [Theory]
    [InlineData(0.0, 1000.0)]
    [InlineData(0.1, 1030.0)]
    [InlineData(0.25, 1075.0)]
    public void FrequencyHz_RampsLinearlyAtRate(double elapsedSec, double expectedHz)
    {
      // offset 1000 Hz, rate 300 Hz/s
      DopplerRamp.FrequencyHz(elapsedSec, 1000.0, 300.0, 1.0)
        .Should().BeApproximately(expectedHz, 1e-9);
    }

    [Fact]
    public void FrequencyHz_FreezesBeyondCap()
    {
      // at the cap (0.5 s) the value is 1000 + 300*0.5 = 1150 and stays there afterwards
      double atCap = DopplerRamp.FrequencyHz(0.5, 1000.0, 300.0, 0.5);
      atCap.Should().BeApproximately(1150.0, 1e-9);
      DopplerRamp.FrequencyHz(5.0, 1000.0, 300.0, 0.5).Should().Be(atCap);
      DopplerRamp.FrequencyHz(100.0, 1000.0, 300.0, 0.5).Should().Be(atCap);
    }

    [Fact]
    public void FrequencyHz_NegativeElapsed_ClampsToOffset()
    {
      DopplerRamp.FrequencyHz(-0.3, 1000.0, 300.0, 1.0).Should().Be(1000.0);
    }




    // ----------------------------------------------------------------------------------------------------
    //                              FrequencyRadPerSample: Hz/s to rad/sample
    // ----------------------------------------------------------------------------------------------------
    [Fact]
    public void FrequencyRadPerSample_AtSnap_MatchesPlainOffsetConversion()
    {
      const double fs = 48000.0, offsetHz = 1000.0;
      // at the snap instant (0 samples) frequency = offsetHz, w = 2*pi*f/fs
      DopplerRamp.FrequencyRadPerSample(0, offsetHz, 0, fs, 1.0)
        .Should().BeApproximately(System.Math.Tau * offsetHz / fs, 1e-12);
    }

    [Fact]
    public void FrequencyRadPerSample_AppliesRampThenConverts()
    {
      const double fs = 48000.0, offsetHz = 1000.0, rate = 300.0;
      long n = 24000;                 // 0.5 s at 48 kHz
      double expectedHz = offsetHz + rate * (n / fs); // 1150 Hz
      DopplerRamp.FrequencyRadPerSample(n, offsetHz, rate, fs, 1.0)
        .Should().BeApproximately(System.Math.Tau * expectedHz / fs, 1e-12);
    }

    [Fact]
    public void FrequencyRadPerSample_HonoursTheCap()
    {
      const double fs = 48000.0, offsetHz = 1000.0, rate = 300.0, cap = 0.5;
      long n = 48000 * 5;             // 5 s, well past the cap
      double cappedHz = offsetHz + rate * cap;        // frozen at 1150 Hz
      DopplerRamp.FrequencyRadPerSample(n, offsetHz, rate, fs, cap)
        .Should().BeApproximately(System.Math.Tau * cappedHz / fs, 1e-12);
    }




    // ----------------------------------------------------------------------------------------------------
    //                                             ChunkSize
    // ----------------------------------------------------------------------------------------------------
    [Theory]
    [InlineData(48000.0, 48)]
    [InlineData(2400000.0, 2400)]
    [InlineData(10000000.0, 10000)]
    public void ChunkSize_IsAboutOneMillisecond(double inputRate, int expected)
    {
      DopplerRamp.ChunkSize(inputRate).Should().Be(expected);
    }

    [Fact]
    public void ChunkSize_NeverBelowOne()
    {
      DopplerRamp.ChunkSize(500.0).Should().Be(1);
      DopplerRamp.ChunkSize(0.0).Should().Be(1);
    }
  }
}
