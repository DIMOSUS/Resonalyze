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
    public void JunctionLossSweep_NegativeDelaysOnAnEarlyPeakStayHonest()
    {
        // The review scenario: with the variable channel's direct sound near
        // the record's START, a negative probe used to wrap it circularly to
        // the END of the array; the shared gate then anchored on the
        // remaining fixed channel alone, and the one-channel "sum" read a
        // fake perfect ~0 dB. The guard frame must keep every probe honest:
        // each point equals a cleanly CONSTRUCTED pair at the same relative
        // offset (the gates re-anchor on the peaks, so only the relative
        // offset matters).
        Complex[] variable = ImpulseAtSample(96);   // 2 ms into the record
        Complex[] fixedIr = ImpulseAtSample(480);   // 10 ms

        // Half-millisecond steps are whole samples at 48 kHz, so every
        // reference impulse lands exactly on the shifted position.
        List<VirtualCrossoverAnalysis.JunctionSweepPoint> sweep =
            VirtualCrossoverAnalysis.JunctionLossSweep(
                variable, fixedIr, SampleRate,
                bandLowHz: 800, bandHighHz: 1_250,
                startDelayMs: -6.0, endDelayMs: -1.0, stepMs: 0.5,
                invertVariable: false);

        Assert.Equal(11, sweep.Count);
        foreach (VirtualCrossoverAnalysis.JunctionSweepPoint point in sweep)
        {
            double relativeMs = 10.0 - (2.0 + point.DelayMs);
            Complex[] referenceVariable = ImpulseAtSample(240); // 5 ms
            Complex[] referenceFixed = ImpulseAtSample(
                240 + (int)Math.Round(relativeMs / 1000.0 * SampleRate));
            (double LossDb, double DipDb)? reference =
                VirtualCrossoverAnalysis.MeasureSumLoss(
                    referenceVariable,
                    [referenceFixed],
                    SampleRate, 800, 1_250);

            Assert.NotNull(reference);
            Assert.True(
                Math.Abs(point.LossDb - reference.Value.LossDb) < 0.25,
                $"loss at {point.DelayMs:0.0} ms: sweep {point.LossDb:0.00} " +
                $"vs honest {reference.Value.LossDb:0.00}");
            Assert.True(
                Math.Abs(point.DipDb - reference.Value.DipDb) < 0.5,
                $"dip at {point.DelayMs:0.0} ms: sweep {point.DipDb:0.00} " +
                $"vs honest {reference.Value.DipDb:0.00}");
        }
    }

    private static Complex[] ImpulseAtSample(int position)
    {
        var ir = new Complex[IrLength];
        ir[position] = 1.0;
        return ir;
    }

    private static Complex[] Filtered(CrossoverSpec crossover) =>
        VirtualCrossoverAnalysis.ApplyChain(
            ImpulseAtMs(0), new DspChannelChain(Crossover: crossover), SampleRate);

    [Fact]
    public void AlignmentCandidates_JudgeTheJunctionThroughTheGivenSystemAnchor()
    {
        // A subwoofer and its woofer partner fired from the SAME impulse
        // through the field cabin's edges (55 Hz, 36 dB/oct Butterworth; the
        // woofer's own 180 Hz low-pass is what puts the optimum a couple of
        // milliseconds early). Two filtered copies of one impulse in a silent
        // record CAN sum flat: at the right delay and polarity the junction is
        // arithmetic, with no room to blame. The system-anchored window finds
        // exactly that; the pair-anchored one — its fade-in cutting into both
        // drivers' rise — cannot see a flat sum anywhere.
        Complex[] sub = Filtered(new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 36)));
        Complex[] woofer = Filtered(new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 180, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 36)));

        AlignmentCandidate Best(int? anchor) => VirtualCrossoverAnalysis
            .FindAlignmentCandidates(
                woofer, [sub], SampleRate, 27.5, 110, -9, 9,
                priorDelayMs: null, priorSigmaMs: 0, forcedPolarity: null,
                levelMatch: true, out _, gateAnchorSample: anchor)[0];

        AlignmentCandidate systemAnchored = Best(BasePosition);
        Assert.True(
            systemAnchored.InvertPolarity,
            "36 dB/oct edges at one corner hand over inverted");
        Assert.InRange(systemAnchored.DelayMs, -4.5, -2.5);
        Assert.InRange(systemAnchored.LossDb, -0.02, 0.0);
        Assert.InRange(systemAnchored.DipDb, -0.05, 0.0);

        AlignmentCandidate pairAnchored = Best(null);
        Assert.True(
            pairAnchored.LossDb < -0.10 && pairAnchored.DipDb < -0.3,
            $"the pair-anchored window cannot read the flat sum: " +
            $"avg {pairAnchored.LossDb:0.00}, dip {pairAnchored.DipDb:0.00} dB");
    }

    [Fact]
    public void AlignmentCandidates_PairAnchoredWindowMisreadsTheSameJunction()
    {
        // The same junction with the anchor left to the pair: the window then
        // starts inside the drivers' own rise and the surface it measures is
        // not the one the read-out shows — this is what let a field sub/woofer
        // junction (55 Hz, 36 dB/oct) be settled a half period out. Pinned so
        // the two anchors cannot silently converge and hide the distinction.
        Complex[] sub = Filtered(new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 36)));
        Complex[] woofer = Filtered(new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 180, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 36)));

        // The true alignment itself — zero delay, the woofer inverted — read
        // through each window.
        Complex[] inverted = woofer.Select(value => -value).ToArray();
        (double LossDb, double DipDb)? pairAnchored =
            VirtualCrossoverAnalysis.MeasureSumLoss(
                inverted, [sub], SampleRate, 27.5, 110, levelMatch: true);
        (double LossDb, double DipDb)? systemAnchored =
            VirtualCrossoverAnalysis.MeasureSumLoss(
                inverted, [sub], SampleRate, 27.5, 110, levelMatch: true,
                gateAnchorSample: BasePosition);

        Assert.NotNull(pairAnchored);
        Assert.NotNull(systemAnchored);
        Assert.True(
            systemAnchored.Value.LossDb > pairAnchored.Value.LossDb + 0.05,
            $"the system-anchored window must read the truth more honestly: " +
            $"{systemAnchored.Value.LossDb:0.00} vs {pairAnchored.Value.LossDb:0.00} dB");
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
