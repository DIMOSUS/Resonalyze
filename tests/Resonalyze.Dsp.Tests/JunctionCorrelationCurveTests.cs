using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The drawable junction diagnostics behind the Virtual DSP correlation view:
/// the band-limited correlation curve and the honest junction-loss sweep.
/// Synthetic impulses at known offsets make every lobe position and polarity
/// verifiable arithmetic.
/// </summary>
public sealed class JunctionCorrelationCurveTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 16_384;
    private const int BasePosition = 2_048;

    private static Complex[] ImpulseAtMs(double offsetMs, double amplitude = 1.0)
    {
        var ir = new Complex[IrLength];
        int position = BasePosition +
            (int)Math.Round(offsetMs / 1000.0 * SampleRate);
        ir[position] = amplitude;
        return ir;
    }

    [Fact]
    public void CorrelationCurve_PeaksAtTheTrueOffsetWithFullCoefficient()
    {
        // The second impulse fires 1.5 ms EARLIER, so aligning it to the first
        // takes +1.5 ms of delay: the curve's maximum must sit there and — the
        // channels being identical in the band — reach r ≈ 1.
        Complex[] first = ImpulseAtMs(2.0);
        Complex[] second = ImpulseAtMs(0.5);

        List<SignalPoint> curve = VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
            first, second, SampleRate,
            centerFrequencyHz: 1_000, passOctaves: 2.0, searchRangeMs: 3.0);

        SignalPoint peak = curve.MaxBy(point => point.Y);
        Assert.InRange(peak.X, 1.45, 1.55);
        Assert.InRange(peak.Y, 0.95, 1.001);
        // The window is the requested ±3 ms at sample resolution.
        Assert.InRange(curve[0].X, -3.1, -2.9);
        Assert.InRange(curve[^1].X, 2.9, 3.1);
        Assert.Equal(curve.Count, 2 * (int)Math.Round(3.0 / 1000 * SampleRate) + 1);
    }

    [Fact]
    public void CorrelationCurve_InvertedChannelShowsANegativeTrough()
    {
        // An inverted second channel: the alignment lobe flips sign — the
        // deepest trough marks the delay, and its coefficient approaches -1.
        Complex[] first = ImpulseAtMs(2.0);
        Complex[] second = ImpulseAtMs(0.5, -1.0);

        List<SignalPoint> curve = VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
            first, second, SampleRate,
            centerFrequencyHz: 1_000, passOctaves: 2.0, searchRangeMs: 3.0);

        SignalPoint trough = curve.MinBy(point => point.Y);
        Assert.InRange(trough.X, 1.45, 1.55);
        Assert.InRange(trough.Y, -1.001, -0.95);
    }

    [Fact]
    public void CorrelationCurve_MatchesTheDelaySearchExtrema()
    {
        // The curve and FindBandLimitedCorrelationDelay share one computation
        // core: the search's reported peak must be the curve's maximum, at the
        // same lag and coefficient (within the search's sub-sample refinement).
        Complex[] first = ImpulseAtMs(1.0);
        Complex[] second = ImpulseAtMs(0.25);

        List<SignalPoint> curve = VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
            first, second, SampleRate,
            centerFrequencyHz: 500, passOctaves: 1.0, searchRangeMs: 3.0,
            phaseTransform: true);
        CorrelationAlignmentResult search =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first, second, SampleRate,
                centerFrequencyHz: 500, passOctaves: 1.0, searchRangeMs: 3.0,
                phaseTransform: true);

        SignalPoint peak = curve.MaxBy(point => point.Y);
        Assert.InRange(
            Math.Abs(peak.X - search.PositivePeak.DelayMs),
            0,
            1000.0 / SampleRate);
        Assert.InRange(
            Math.Abs(peak.Y - search.PositivePeak.Coefficient), 0, 0.02);
    }

    [Fact]
    public void JunctionLossSweep_IsMinimalAtTheTrueOffsetAndCyclicAround()
    {
        // Two identical 1 kHz-band impulses 1.5 ms apart: the loss bottoms out
        // (≈0 dB) at +1.5 ms on the variable channel, and a half period of the
        // band center away (±0.5 ms) the sum cancels — the comb the display
        // exists to show.
        Complex[] fixedIr = ImpulseAtMs(2.0);
        Complex[] variableIr = ImpulseAtMs(0.5);

        List<VirtualCrossoverAnalysis.JunctionSweepPoint> sweep =
            VirtualCrossoverAnalysis.JunctionLossSweep(
                variableIr, fixedIr, SampleRate,
                bandLowHz: 800, bandHighHz: 1_250,
                startDelayMs: 0.0, endDelayMs: 3.0, stepMs: 0.05,
                invertVariable: false);

        VirtualCrossoverAnalysis.JunctionSweepPoint best =
            sweep.MaxBy(point => point.LossDb)!;
        Assert.InRange(best.DelayMs, 1.4, 1.6);
        // <= 0 by the triangle inequality; the bound allows the float epsilon.
        Assert.InRange(best.LossDb, -0.1, 1e-9);

        VirtualCrossoverAnalysis.JunctionSweepPoint halfPeriodOff = sweep
            .MinBy(point => Math.Abs(point.DelayMs - 1.0))!;
        Assert.True(
            halfPeriodOff.LossDb < -6.0,
            $"expected a deep cancellation half a period off, got {halfPeriodOff.LossDb:0.0} dB");
    }

    [Fact]
    public void JunctionLossSweep_InvertedPolarityShiftsTheCombByHalfAPeriod()
    {
        // With the variable channel inverted the comb flips: the optimum moves
        // to the half-period-away lag and the true offset becomes the null.
        Complex[] fixedIr = ImpulseAtMs(2.0);
        Complex[] variableIr = ImpulseAtMs(0.5);

        List<VirtualCrossoverAnalysis.JunctionSweepPoint> sweep =
            VirtualCrossoverAnalysis.JunctionLossSweep(
                variableIr, fixedIr, SampleRate,
                bandLowHz: 800, bandHighHz: 1_250,
                startDelayMs: 0.0, endDelayMs: 3.0, stepMs: 0.05,
                invertVariable: true);

        VirtualCrossoverAnalysis.JunctionSweepPoint atTrueOffset = sweep
            .MinBy(point => Math.Abs(point.DelayMs - 1.5))!;
        Assert.True(
            atTrueOffset.LossDb < -6.0,
            $"inverted sum at the true offset should cancel, got {atTrueOffset.LossDb:0.0} dB");
        VirtualCrossoverAnalysis.JunctionSweepPoint bestInverted =
            sweep.MaxBy(point => point.LossDb)!;
        Assert.True(
            Math.Abs(bestInverted.DelayMs - 1.5) > 0.3,
            "the inverted optimum must sit away from the non-inverted one");
    }
}
