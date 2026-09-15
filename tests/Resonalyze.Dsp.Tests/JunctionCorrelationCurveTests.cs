using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class JunctionCorrelationCurveTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 16_384;
    private const int BasePosition = 2_048;

    private static int Samples(double milliseconds) =>
        (int)Math.Round(milliseconds / 1000.0 * SampleRate);

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
        // The second impulse fires 1.5 ms earlier, so the maximum is at +1.5 ms with r ≈ 1.
        Complex[] first = ImpulseAtMs(2.0);
        Complex[] second = ImpulseAtMs(0.5);

        List<SignalPoint> curve = VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
            first, second, SampleRate,
            centerFrequencyHz: 1_000, passOctaves: 2.0, searchRangeMs: 3.0);

        SignalPoint peak = curve.MaxBy(point => point.Y);
        Assert.InRange(peak.X, 1.45, 1.55);
        Assert.InRange(peak.Y, 0.95, 1.001);
        Assert.InRange(curve[0].X, -3.1, -2.9);
        Assert.InRange(curve[^1].X, 2.9, 3.1);
        Assert.Equal(curve.Count, 2 * (int)Math.Round(3.0 / 1000 * SampleRate) + 1);
    }

    [Fact]
    public void CorrelationCurve_InvertedChannelShowsANegativeTrough()
    {
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
        // Curve and FindBandLimitedCorrelationDelay share one core.
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
    public void JunctionLossSweepBothPolarities_MatchesTwoSingleSweepsExactly()
    {
        // The both-polarity sweep must be a pure factoring, bit-identical to the single-polarity calls.
        Complex[] fixedIr = ImpulseAtMs(2.0);
        Complex[] variableIr = ImpulseAtMs(0.5);

        (List<VirtualCrossoverAnalysis.JunctionSweepPoint> normal,
            List<VirtualCrossoverAnalysis.JunctionSweepPoint> inverted) =
            VirtualCrossoverAnalysis.JunctionLossSweepBothPolarities(
                variableIr, fixedIr, SampleRate,
                bandLowHz: 800, bandHighHz: 1_250,
                startDelayMs: 0.0, endDelayMs: 3.0, stepMs: 0.05);

        List<VirtualCrossoverAnalysis.JunctionSweepPoint> Single(bool invert) =>
            VirtualCrossoverAnalysis.JunctionLossSweep(
                variableIr, fixedIr, SampleRate,
                bandLowHz: 800, bandHighHz: 1_250,
                startDelayMs: 0.0, endDelayMs: 3.0, stepMs: 0.05,
                invertVariable: invert);

        Assert.Equal(Single(invert: false), normal);
        Assert.Equal(Single(invert: true), inverted);
    }

    [Fact]
    public void JunctionLossSweep_IsMinimalAtTheTrueOffsetAndCyclicAround()
    {
        // ±0.5 ms (half a period) from the optimum the sum cancels.
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
        // A negative probe once wrapped the variable channel's early front to the array end and read a fake ~0 dB;
        // each point must equal a constructed pair at the same relative offset.
        Complex[] variable = ImpulseAtSample(96);   // 2 ms into the record
        Complex[] fixedIr = ImpulseAtSample(480);   // 10 ms

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
            ImpulseAtMs(0), new DspChannelChain(Crossover: crossover), SampleRate, SampleRate);

    [Fact]
    public void AlignmentCandidates_ReadTheFlatSumHonestlyByDefault()
    {
        // 55 Hz BW36 sub/woofer from one impulse must sum flat. The band-sized window (~315 ms, ~20 ms fade-in) admits the
        // whole rise, so front- and peak-anchored reads agree (history in VirtualCrossoverAnalysis gate remarks).
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

        AlignmentCandidate byDefault = Best(null);
        Assert.True(
            byDefault.InvertPolarity,
            "36 dB/oct edges at one corner hand over inverted");
        Assert.InRange(byDefault.DelayMs, -4.5, -2.5);
        Assert.InRange(byDefault.LossDb, -0.05, 0.0);
        Assert.InRange(byDefault.DipDb, -0.10, 0.0);

        // At a bass junction the window's length, not its placement, buys the honesty.
        int pairPeak = Math.Min(
            VirtualCrossoverAnalysis.FindPeakIndex(sub),
            VirtualCrossoverAnalysis.FindPeakIndex(woofer));
        AlignmentCandidate peakAnchored = Best(pairPeak);
        Assert.True(peakAnchored.InvertPolarity);
        Assert.InRange(
            Math.Abs(peakAnchored.DelayMs - byDefault.DelayMs), 0, 0.3);
        Assert.InRange(
            Math.Abs(peakAnchored.LossDb - byDefault.LossDb), 0, 0.1);
    }

    [Fact]
    public void JunctionLossSweep_RotationKeepsTheMovedChannelInTheWindow()
    {
        // Re-gating each probe through a stationary window faded the moved woofer out and made a fake 0 dB plateau.
        Complex[] sub = Filtered(new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 36)));
        Complex[] woofer = Filtered(new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 180, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 36)));
        int pairFront = Math.Min(
            VirtualCrossoverAnalysis.FindGateAnchor(
                woofer, VirtualCrossoverAnalysis.FindPeakIndex(woofer),
                SampleRate, 27.5, 110),
            VirtualCrossoverAnalysis.FindGateAnchor(
                sub, VirtualCrossoverAnalysis.FindPeakIndex(sub),
                SampleRate, 27.5, 110));

        List<VirtualCrossoverAnalysis.JunctionSweepPoint> Sweep(
            bool invert, int anchor) =>
            VirtualCrossoverAnalysis.JunctionLossSweep(
                woofer, sub, SampleRate, 27.5, 110,
                startDelayMs: -25.0, endDelayMs: 25.0, stepMs: 0.25,
                invertVariable: invert, anchor);

        // Parallelogram law |F+V|² + |F−V|² = 2(|F|²+|V|²) forbids both polarities summing flat at once.
        List<VirtualCrossoverAnalysis.JunctionSweepPoint> normal =
            Sweep(false, pairFront);
        List<VirtualCrossoverAnalysis.JunctionSweepPoint> inverted =
            Sweep(true, pairFront);
        Assert.Equal(normal.Count, inverted.Count);
        for (int i = 0; i < normal.Count; i++)
        {
            Assert.True(
                Math.Min(normal[i].LossDb, inverted[i].LossDb) < -1.0,
                $"both polarities read flat at {normal[i].DelayMs:0.0} ms " +
                $"({normal[i].LossDb:0.00} / {inverted[i].LossDb:0.00} dB) — " +
                "the plateau of a window the moved channel left");
        }

        // Δ = 0 must match the read-out's own measurement exactly.
        (double LossDb, double DipDb)? atRest =
            VirtualCrossoverAnalysis.MeasureSumLoss(
                woofer, [sub], SampleRate, 27.5, 110,
                gateAnchorSample: pairFront);
        VirtualCrossoverAnalysis.JunctionSweepPoint zero = normal
            .MinBy(point => Math.Abs(point.DelayMs))!;
        Assert.NotNull(atRest);
        Assert.InRange(
            zero.LossDb, atRest.Value.LossDb - 1e-6, atRest.Value.LossDb + 1e-6);
        Assert.InRange(
            zero.DipDb, atRest.Value.DipDb - 1e-6, atRest.Value.DipDb + 1e-6);

        // Rotation equals physical construction through the same window. Compared linearly: dB magnifies a 0.016
        // tail-truncation difference near a null into a whole dB (worst measured 0.011 loss, 0.016 dip).
        static double Linear(double decibels) => Math.Pow(10.0, decibels / 20.0);
        foreach (bool invert in new[] { false, true })
        {
            foreach (VirtualCrossoverAnalysis.JunctionSweepPoint point in
                Sweep(invert, BasePosition))
            {
                Complex[] variable = VirtualCrossoverAnalysis.ApplyChain(
                    woofer,
                    new DspChannelChain(
                        DelayMs: Math.Max(0.0, point.DelayMs),
                        InvertPolarity: invert),
                    SampleRate,
                    SampleRate);
                Complex[] fixedIr = VirtualCrossoverAnalysis.ApplyChain(
                    sub,
                    new DspChannelChain(DelayMs: Math.Max(0.0, -point.DelayMs)),
                    SampleRate,
                    SampleRate);
                (double LossDb, double DipDb)? reference =
                    VirtualCrossoverAnalysis.MeasureSumLoss(
                        variable, [fixedIr], SampleRate, 27.5, 110,
                        gateAnchorSample: BasePosition);

                Assert.NotNull(reference);
                Assert.True(
                    Math.Abs(
                        Linear(point.LossDb) - Linear(reference.Value.LossDb))
                        < 0.025,
                    $"loss at {point.DelayMs:0.0} ms{(invert ? " inv" : "")}: " +
                    $"sweep {point.LossDb:0.00} vs constructed " +
                    $"{reference.Value.LossDb:0.00} dB");
                Assert.True(
                    Math.Abs(
                        Linear(point.DipDb) - Linear(reference.Value.DipDb))
                        < 0.025,
                    $"dip at {point.DelayMs:0.0} ms{(invert ? " inv" : "")}: " +
                    $"sweep {point.DipDb:0.00} vs constructed " +
                    $"{reference.Value.DipDb:0.00} dB");
            }
        }
    }

    [Fact]
    public void JunctionLossSweep_DefaultReadsTheSearchsOwnWindows()
    {
        // Fronts 12 ms apart exceed the 10.8 ms window: a null anchor must stay per-channel, matching the search's evaluator.
        Complex[] variable = ImpulseAtSample(96);        // 2 ms
        Complex[] fixedIr = ImpulseAtSample(96 + 576);   // 14 ms

        List<VirtualCrossoverAnalysis.JunctionSweepPoint> sweep =
            VirtualCrossoverAnalysis.JunctionLossSweep(
                variable, fixedIr, SampleRate, 800, 1_250,
                startDelayMs: 10.0, endDelayMs: 14.0, stepMs: 0.5,
                invertVariable: false);
        VirtualCrossoverAnalysis.SumLossEvaluator? evaluator =
            VirtualCrossoverAnalysis.SumLossEvaluator.Create(
                variable, [fixedIr], SampleRate, 800, 1_250);

        Assert.NotNull(evaluator);
        foreach (VirtualCrossoverAnalysis.JunctionSweepPoint point in sweep)
        {
            (double lossDb, double dipDb) = evaluator.Evaluate(point.DelayMs);
            Assert.Equal(lossDb, point.LossDb, 9);
            Assert.Equal(dipDb, point.DipDb, 9);
        }

        int sharedFront = VirtualCrossoverAnalysis.FindGateAnchor(
            variable, VirtualCrossoverAnalysis.FindPeakIndex(variable),
            SampleRate, 800, 1_250);
        List<VirtualCrossoverAnalysis.JunctionSweepPoint> shared =
            VirtualCrossoverAnalysis.JunctionLossSweep(
                variable, fixedIr, SampleRate, 800, 1_250,
                startDelayMs: 10.0, endDelayMs: 14.0, stepMs: 0.5,
                invertVariable: false, sharedFront);
        double worst = sweep
            .Zip(shared, (own, one) => Math.Abs(own.LossDb - one.LossDb))
            .Max();
        Assert.True(
            worst > 0.5,
            $"the two window rules read this pair {worst:0.00} dB apart at " +
            "most — too close to prove the anchor pass-through matters");
    }

    [Fact]
    public void JunctionLossSweep_LevelMatchReshapesUnequalChannels()
    {
        // The Auto search always level-matches; the sweep with the match on must equal the matched evaluator.
        Complex[] variable = ImpulseAtMs(0.5, amplitude: 0.25); // -12 dB
        Complex[] fixedIr = ImpulseAtMs(2.0);

        List<VirtualCrossoverAnalysis.JunctionSweepPoint> matched =
            VirtualCrossoverAnalysis.JunctionLossSweep(
                variable, fixedIr, SampleRate, 800, 1_250,
                startDelayMs: 0.0, endDelayMs: 3.0, stepMs: 0.25,
                invertVariable: false, gateAnchorSample: null,
                levelMatch: true);
        VirtualCrossoverAnalysis.SumLossEvaluator? evaluator =
            VirtualCrossoverAnalysis.SumLossEvaluator.Create(
                variable, [fixedIr], SampleRate, 800, 1_250,
                levelMatch: true);

        Assert.NotNull(evaluator);
        foreach (VirtualCrossoverAnalysis.JunctionSweepPoint point in matched)
        {
            (double lossDb, double dipDb) = evaluator.Evaluate(point.DelayMs);
            Assert.Equal(lossDb, point.LossDb, 9);
            Assert.Equal(dipDb, point.DipDb, 9);
        }

        List<VirtualCrossoverAnalysis.JunctionSweepPoint> unmatched =
            VirtualCrossoverAnalysis.JunctionLossSweep(
                variable, fixedIr, SampleRate, 800, 1_250,
                startDelayMs: 0.0, endDelayMs: 3.0, stepMs: 0.25,
                invertVariable: false);
        double worst = matched
            .Zip(unmatched, (a, b) => Math.Abs(a.LossDb - b.LossDb))
            .Max();
        Assert.True(
            worst > 3.0,
            $"a 12 dB pair reads only {worst:0.00} dB apart with and " +
            "without the match — the surfaces should differ starkly");
    }

    [Fact]
    public void JunctionLossSweep_InvertedPolarityShiftsTheCombByHalfAPeriod()
    {
        // Inverted: the optimum moves half a period and the true offset becomes the null.
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
