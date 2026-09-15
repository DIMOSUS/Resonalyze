using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class ExcessDelayTests
{
    private const int Length = 4096;
    private const int SampleRate = 48_000;

    [Theory]
    [InlineData(10)]
    [InlineData(37)]
    [InlineData(120)]
    public void PureDelay_RecoveredByBothEstimators(int delaySamples)
    {
        Complex[] spectrum = SpectrumOfImpulseAt(delaySamples);

        ExcessDelayResult result = ExcessDelay.Estimate(spectrum, SampleRate);

        Assert.InRange(result.PeakDelaySamples, delaySamples - 0.5, delaySamples + 0.5);
        Assert.InRange(result.SlopeDelaySamples, delaySamples - 1e-6, delaySamples + 1e-6);
    }

    [Fact]
    public void MinimumPhaseSystem_HasZeroExcessDelay()
    {
        Complex[] spectrum = TransferFunction([1.0, -0.5]);

        ExcessDelayResult result = ExcessDelay.Estimate(spectrum, SampleRate);

        Assert.InRange(result.PeakDelaySamples, -0.5, 0.5);
        Assert.InRange(result.SlopeDelaySamples, -1e-3, 1e-3);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(64)]
    public void MinimumPhasePlusDelay_RecoversTheAddedDelay(int delaySamples)
    {
        double[] minimumPhaseImpulse = [1.0, -0.7, 0.2];
        double[] impulse = new double[Length];
        for (int i = 0; i < minimumPhaseImpulse.Length; i++)
        {
            impulse[delaySamples + i] = minimumPhaseImpulse[i];
        }

        Complex[] spectrum = TransferFunction(impulse);

        ExcessDelayResult result = ExcessDelay.Estimate(spectrum, SampleRate);

        Assert.InRange(result.PeakDelaySamples, delaySamples - 0.5, delaySamples + 0.5);
        Assert.InRange(result.SlopeDelaySamples, delaySamples - 0.5, delaySamples + 0.5);
    }

    [Fact]
    public void AllPassExcess_SeparatesPeakFromCentroid()
    {
        // All-pass cascade: the energy centroid sits at delay + order while the envelope peak stays near the arrival.
        const int delay = 64;
        const double a = 0.8;
        const int order = 8;
        Complex[] spectrum = DelayedAllPass(delay, a, order);

        ExcessDelayResult result = ExcessDelay.Estimate(spectrum, SampleRate);

        Assert.InRange(result.PeakDelaySamples, delay, delay + 4.0);
        Assert.InRange(result.SlopeDelaySamples, delay + order - 1.0, delay + order + 1.0);
        Assert.True(
            result.SlopeDelaySamples > result.PeakDelaySamples + 3.0,
            $"Expected centroid well past the peak, got peak=" +
            $"{result.PeakDelaySamples}, slope={result.SlopeDelaySamples}");
    }

    private static Complex[] DelayedAllPass(int delay, double a, int order)
    {
        var spectrum = new Complex[Length];
        for (int k = 0; k < Length; k++)
        {
            double omega = 2.0 * Math.PI * k / Length;
            Complex zInv = new(Math.Cos(omega), -Math.Sin(omega));
            Complex ap = (-a + zInv) / (1.0 - a * zInv);
            Complex apPower = Complex.One;
            for (int i = 0; i < order; i++)
            {
                apPower *= ap;
            }

            Complex delayPhase = new(
                Math.Cos(omega * delay),
                -Math.Sin(omega * delay));
            spectrum[k] = delayPhase * apPower;
        }

        return spectrum;
    }

    [Fact]
    public void MillisecondFieldsMatchSampleFields()
    {
        Complex[] spectrum = SpectrumOfImpulseAt(48);

        ExcessDelayResult result = ExcessDelay.Estimate(spectrum, SampleRate);

        Assert.Equal(
            result.PeakDelaySamples * 1000.0 / SampleRate,
            result.PeakDelayMilliseconds,
            10);
        Assert.Equal(
            result.SlopeDelaySamples * 1000.0 / SampleRate,
            result.SlopeDelayMilliseconds,
            10);
    }

    [Fact]
    public void Estimate_RejectsTooShortSpectrum()
    {
        Assert.Throws<ArgumentException>(
            () => ExcessDelay.Estimate([Complex.One], SampleRate));
    }

    [Fact]
    public void Estimate_RejectsNonPositiveSampleRate()
    {
        Complex[] spectrum = SpectrumOfImpulseAt(10);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExcessDelay.Estimate(spectrum, 0));
    }

    [Fact]
    public void Peak_LandsOnTheDirectArrivalNotAStrongerReflection()
    {
        // The τ Peak reference must land on the weak direct arrival, not a 3x reflection.
        double[] impulse = new double[8_192];
        impulse[100] = 0.3;
        impulse[500] = 1.0;
        var buffer = new Complex[8_192];
        for (int i = 0; i < impulse.Length; i++)
        {
            buffer[i] = new Complex(impulse[i], 0.0);
        }
        Fourier.Forward(buffer, FourierOptions.Matlab);

        ExcessDelayResult result = ExcessDelay.Estimate(buffer, SampleRate);

        Assert.True(result.IsValid);
        Assert.InRange(result.PeakDelaySamples, 99.0, 101.0);
    }

    [Fact]
    public void Estimate_ZeroSpectrumIsInvalid()
    {
        // A zero spectrum once read as a valid τ = 0.
        var spectrum = new Complex[Length];

        ExcessDelayResult result = ExcessDelay.Estimate(spectrum, SampleRate);

        Assert.False(result.IsValid);
    }

    private static Complex[] SpectrumOfImpulseAt(int index)
    {
        double[] impulse = new double[Length];
        impulse[index] = 1.0;
        return TransferFunction(impulse);
    }

    private static Complex[] TransferFunction(double[] impulse)
    {
        var buffer = new Complex[Length];
        for (int i = 0; i < impulse.Length && i < Length; i++)
        {
            buffer[i] = new Complex(impulse[i], 0.0);
        }

        Fourier.Forward(buffer, FourierOptions.Matlab);
        return buffer;
    }
}
