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
    public void AlignmentCandidates_ReadTheFlatSumHonestlyByDefault()
    {
        // A subwoofer and its woofer partner fired from the SAME impulse
        // through the field cabin's edges (55 Hz, 36 dB/oct Butterworth):
        // two filtered copies of one impulse in a silent record CAN sum
        // flat, so any honest read of the true alignment must say so. This
        // junction spent two generations of window placement being misread —
        // a window on the pair's PEAKS started inside both drivers' rises
        // and settled the field junction a half period out; a window on the
        // pair's filtered FRONTS still cut the rise a 36 dB/oct filter
        // spreads 8-13 ms ahead of any detectable front and invented
        // -0.21 dB at the optimum (the placements were measured against each
        // other here before the band-sized window; see the gate remarks in
        // VirtualCrossoverAnalysis for the full history and figures).
        //
        // The band-sized window closed the question at a bass junction: at
        // 27.5-110 Hz the window is ~315 ms with a ~20 ms fade-in, so every
        // placement — even the peaks — admits the whole rise, and the
        // default per-channel-front read and a deliberately peak-anchored
        // shared window now agree on the flat sum to a few hundredths of a
        // dB. Placement still matters where windows are short; what holds it
        // to the front there is the detector itself (JunctionGateAnchorTests
        // pins front-vs-peak directly).
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

        // The default read: each channel windowed at its own front. The
        // 36 dB/oct edges at one corner hand over inverted, and the flat sum
        // reads flat.
        AlignmentCandidate byDefault = Best(null);
        Assert.True(
            byDefault.InvertPolarity,
            "36 dB/oct edges at one corner hand over inverted");
        Assert.InRange(byDefault.DelayMs, -4.5, -2.5);
        Assert.InRange(byDefault.LossDb, -0.05, 0.0);
        Assert.InRange(byDefault.DipDb, -0.10, 0.0);

        // The shared window forced onto the pair's earliest PEAK — the
        // placement that once drew this junction as antiphase — now reads the
        // same optimum at the same flatness: the window's length, not its
        // placement, is what buys the honesty at a bass junction.
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
        // The field plateau, reproduced: a 55 Hz sub/woofer junction swept the
        // panel's full ±1.5 crossover periods. Re-gating each probe through
        // the STATIONARY pair window lost the moved woofer into the fade a few
        // ms out, the "sum" degenerated toward the sub alone, and both
        // polarities converged to a fake near-0 dB plateau (the in-band level
        // skew grew 3 → 19 dB across the sweep on the archived cabins). The
        // rotation sweep reads the same two windowed cuts at every probe; three
        // properties pin it.
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

        // 1: no plateau anywhere in the panel's own sweep. With the two
        // channels within a few dB of each other in-band, the parallelogram
        // law |F+V|² + |F−V|² = 2(|F|²+|V|²) forbids both polarities summing
        // flat at once — at least one must always be losing audibly (the
        // band-sized window reads the shallowest min at -1.44 dB, far off the
        // optimum where partial decorrelation softens the comb; the plateau
        // this guards against read ≈ 0 for BOTH). The stationary-window sweep
        // violated this across the whole negative half, the woofer faded out.
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

        // 2: Δ = 0 is the pair's current alignment, and the drawn surface
        // must agree with the read-out's measurement of it exactly — same
        // anchor rule, same bins, same rotation as the search.
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

        // 3: through one and the same window, rotation equals physical
        // construction at every probe — the channel actually delayed through
        // its chain (the probed delay on the woofer; its magnitude on the sub
        // when negative — only the relative offset matters), measured by
        // MeasureSumLoss through that window. Anchored at the drivers' shared
        // source so the window holds every rise at every probe and the
        // reference is the truth this synthetic makes knowable. Compared as
        // LINEAR amplitude ratios — the quantity the estimator computes —
        // because dB magnifies the floor: near a deep null a 0.016 linear
        // disagreement (the construction's fixed window end truncating the
        // ringing tail the traveling cut keeps) reads as a whole dB. Measured
        // worst across ±25 ms, both polarities: 0.011 linear on the loss,
        // 0.016 on the dip. What remains outside this equality is window
        // PLACEMENT — the anchor work's business, not the sweep's: the sweep
        // must read the search's window, wherever that rule puts it.
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
                    SampleRate);
                Complex[] fixedIr = VirtualCrossoverAnalysis.ApplyChain(
                    sub,
                    new DspChannelChain(DelayMs: Math.Max(0.0, -point.DelayMs)),
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
        // Two channels whose fronts sit 12 ms apart — further than the
        // 800-1250 Hz band's 10.8 ms window spans. Windowed per channel (the
        // search's default) each cut holds its channel wherever it sits;
        // forced through one shared window at the earliest front, the late
        // channel is lost off the window's end. The sweep once derived that
        // shared anchor whenever the caller passed none, so the drawn surface
        // silently stopped being the searched one exactly on such pairs; the
        // null anchor must now reach the bins untouched, making the sweep
        // point-identical to the evaluator the search reads.
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

        // And the distinction is real on this pair: the shared window at the
        // early channel's front cannot even hold the late channel, so the
        // same probes read a different surface through it.
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
        // The Auto search always scores through the level match
        // (FindAlignmentCandidates, levelMatch: true): the lobe choice must
        // not depend on the channels' playback gains. A sweep drawn without
        // it shows a different surface whenever the pair sits at different
        // levels — equal-amplitude synthetics hid this. With the match ON the
        // sweep must again be point-identical to the matched evaluator.
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

        // A 12 dB imbalance flattens the unmatched surface: at the aligned
        // probe the matched pair cancels ~fully where the unmatched one
        // cannot lose more than the weak channel contributes.
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
