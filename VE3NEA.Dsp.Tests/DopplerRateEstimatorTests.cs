using System;
using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public class DopplerRateEstimatorTests
  {
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstSample_RateIsZero()
    {
      var e = new DopplerRateEstimator();
      e.Update(0.001, true, new object(), T0);
      e.FactorRate.Should().Be(0);
    }

    [Fact]
    public void TwoSamples_SameSource_DifferencesOverElapsedTime()
    {
      var e = new DopplerRateEstimator();
      var sat = new object();
      e.Update(0.001, true, sat, T0);
      e.Update(0.002, true, sat, T0.AddSeconds(0.25));
      e.FactorRate.Should().BeApproximately((0.002 - 0.001) / 0.25, 1e-12);
    }

    [Fact]
    public void SourceChange_ResetsRateToZero()
    {
      var e = new DopplerRateEstimator();
      var satA = new object();
      e.Update(0.001, true, satA, T0);
      e.Update(0.002, true, satA, T0.AddSeconds(0.25)); // valid rate
      e.Update(0.050, true, new object(), T0.AddSeconds(0.50)); // different satellite
      e.FactorRate.Should().Be(0);
    }

    [Fact]
    public void NoObservation_RateIsZero()
    {
      var e = new DopplerRateEstimator();
      var sat = new object();
      e.Update(0.001, true, sat, T0);
      e.Update(0.002, false, sat, T0.AddSeconds(0.25)); // propagator returned nothing
      e.FactorRate.Should().Be(0);
    }

    [Fact]
    public void NonPositiveElapsedTime_RateIsZero()
    {
      var e = new DopplerRateEstimator();
      var sat = new object();
      e.Update(0.001, true, sat, T0);
      e.Update(0.002, true, sat, T0); // same timestamp, dt = 0
      e.FactorRate.Should().Be(0);
    }

    [Fact]
    public void ResumesDifferencingAfterADiscontinuity()
    {
      var e = new DopplerRateEstimator();
      var satA = new object();
      e.Update(0.001, true, satA, T0);
      e.Update(0.002, true, satA, T0.AddSeconds(0.25));

      // satellite change zeroes the rate and re-seeds from the new source
      var satB = new object();
      e.Update(0.000, true, satB, T0.AddSeconds(0.50));
      e.FactorRate.Should().Be(0);

      // a second sample on the new source produces a valid rate again
      e.Update(0.003, true, satB, T0.AddSeconds(0.75));
      e.FactorRate.Should().BeApproximately((0.003 - 0.000) / 0.25, 1e-12);
    }
  }
}
