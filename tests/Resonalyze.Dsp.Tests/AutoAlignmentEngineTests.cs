using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Resonalyze.Dsp.Tests;

public sealed class AutoAlignmentEngineTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 8_192;
    private const int BasePosition = 480; // 10 ms at 48 kHz.

    /// <summary>Initial IR feeds stage-1 snapshots; reprocess IR feeds stage-2 searches (differs to simulate a wrong coarse estimate).</summary>
    private sealed class TestChannel : IAlignmentChannel
    {
        public TestChannel(string name, Complex[] initialIr, Complex[]? reprocessIr = null)
        {
            Name = name;
            InitialIr = initialIr;
            ReprocessIr = reprocessIr ?? initialIr;
        }

        public string Name { get; }
        public int SampleRate => AutoAlignmentEngineTests.SampleRate;
        public int ProcessorSampleRate => SampleRate;
        public Complex[] InitialIr { get; }
        public Complex[] ReprocessIr { get; }
    }

    private static AlignmentSnapshot PredictableSnapshot(
        string name, Complex[] bypassed, DspChannelChain chain)
    {
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            bypassed, chain, SampleRate, SampleRate, out ValidSampleRange processedRange);
        return new AlignmentSnapshot(
            new TestChannel(name, processed),
            processed,
            VirtualCrossoverAnalysis.FindPeakIndex(processed),
            processedRange,
            chain,
            bypassed);
    }

    // Late copy standing in for a cabin reflection, beyond the direct-sound cut.
    private static Complex[] WithTail(
        Complex[] impulse, (int Samples, double Amplitude)? tail)
    {
        if (tail is not { } copy)
        {
            return impulse;
        }

        var withTail = (Complex[])impulse.Clone();
        for (int i = 0; i < impulse.Length; i++)
        {
            if (impulse[i] != Complex.Zero && i + copy.Samples < impulse.Length)
            {
                withTail[i + copy.Samples] += impulse[i] * copy.Amplitude;
            }
        }

        return withTail;
    }

    private static Complex[] UnitImpulse(int position, double amplitude = 1.0)
    {
        var ir = new Complex[IrLength];
        ir[position] = amplitude;
        return ir;
    }

    private static Complex[] DelayedImpulse(double delayMs, bool invert = false) =>
        VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(DelayMs: delayMs, InvertPolarity: invert),
            SampleRate,
            SampleRate);

    // A stronger later copy splits the envelope arrival (first copy) from the whitened-correlation peak.
    private static Complex[] ImpulseWithEcho(
        double offsetMs, double amplitude, double echoMs, double echoAmplitude)
    {
        var ir = new Complex[IrLength];
        ir[BasePosition + (int)Math.Round(offsetMs / 1000.0 * SampleRate)] =
            amplitude;
        ir[BasePosition + (int)Math.Round((offsetMs + echoMs) / 1000.0 * SampleRate)] +=
            echoAmplitude;
        return ir;
    }

    // Periodic front: every correlation witness ties its rivals, so the seed falls back to the envelope.
    private static Complex[] PeriodicFront(double periodMs)
    {
        var ir = new Complex[IrLength];
        int period = (int)Math.Round(periodMs / 1000.0 * SampleRate);
        ir[BasePosition] = 0.995;
        ir[BasePosition + period] = 1.0;
        ir[BasePosition + (2 * period)] = 0.995;
        return ir;
    }

    // A late reflection correlating across channels: a phantom PHAT lobe the full-record gates cannot reject.
    private static Complex[] ReflectedFront(
        double frontMs, double reflectionAfterMs, double reflectionAmplitude)
    {
        var ir = new Complex[IrLength];
        ir[BasePosition + (int)Math.Round(frontMs / 1000.0 * SampleRate)] = 1.0;
        ir[BasePosition +
            (int)Math.Round((frontMs + reflectionAfterMs) / 1000.0 * SampleRate)] =
            reflectionAmplitude;
        return ir;
    }

    // Modal-latch shape: a soft band-limited front hidden under a late build-up below the pair band.
    private static Complex[] FrontUnderLateMode(
        double frontMs, double modeMs, double modeAmplitude)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(
                DelayMs: frontMs,
                Crossover: new CrossoverSpec(
                    CrossoverKind.BandPass,
                    new CrossoverEdge(CrossoverFilterFamily.Butterworth, 800, 24),
                    new CrossoverEdge(CrossoverFilterFamily.Butterworth, 80, 24))),
            SampleRate,
            SampleRate);
        int start = BasePosition + (int)Math.Round(modeMs / 1000.0 * SampleRate);
        // An abruptly switched sine is itself a broadband click in the band's upper half.
        const double AttackSeconds = 0.008;
        const double DecaySeconds = 0.1;
        foreach (double modeHz in new[] { 65.0, 72.0, 80.0 })
        {
            for (int i = start; i < ir.Length; i++)
            {
                double t = (i - start) / (double)SampleRate;
                ir[i] += modeAmplitude *
                    (1 - Math.Exp(-t / AttackSeconds)) *
                    Math.Exp(-t / DecaySeconds) *
                    Math.Sin(2 * Math.PI * modeHz * t);
            }
        }
        return ir;
    }

    private static Dictionary<IAlignmentChannel, AlignmentOverride> Run(
        TestChannel[] byBand,
        double[] crossoversHz,
        StringBuilder log,
        (double LowHz, double HighHz)[]? bands = null,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null,
        Dictionary<IAlignmentChannel, AlignmentOverride>? alignment = null)
    {
        var snapshots = byBand.ToDictionary(
            channel => channel,
            channel => new AlignmentSnapshot(
                channel,
                channel.InitialIr,
                VirtualCrossoverAnalysis.FindPeakIndex(channel.InitialIr)));
        List<AlignmentJunction> junctions = crossoversHz
            .Select((fc, i) => new AlignmentJunction(
                snapshots[byBand[i]],
                snapshots[byBand[i + 1]],
                fc,
                bands?[i].LowHz ?? Math.Max(20, fc / 2),
                bands?[i].HighHz ?? Math.Min(20_000, fc * 2)))
            .ToList();

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            byBand
                .Select(channel =>
                {
                    AlignmentOverride o = overrides.GetValueOrDefault(channel);
                    Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
                        channel.ReprocessIr,
                        new DspChannelChain(
                            DelayMs: o.DelayMs,
                            InvertPolarity: o.InvertPolarity),
                        SampleRate,
                        SampleRate);
                    return new AlignmentSnapshot(
                        channel, ir, VirtualCrossoverAnalysis.FindPeakIndex(ir));
                })
                .ToList();

        alignment ??= new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            byBand.Select(channel => snapshots[channel]).ToList(),
            junctions,
            Reprocess,
            alignment,
            log,
            decisions);
        return alignment;
    }

    [Fact]
    public void Compute_ReportsPerChannelDecisions()
    {
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel("T", DelayedImpulse(0.0));
        var log = new StringBuilder();
        var decisions = new Dictionary<IAlignmentChannel, AlignmentDecision>();

        Run([woofer, tweeter], [1_000], log, decisions: decisions);

        Assert.Equal(AlignmentDecisionKind.Reference, decisions[woofer].Kind);
        Assert.Null(decisions[woofer].Confidence);
        Assert.Contains("reference", decisions[woofer].Detail);
        // Trusted seed: the onset lock stands down, so a free Search with the rival margin as confidence.
        Assert.Equal(AlignmentDecisionKind.Search, decisions[tweeter].Kind);
        Assert.NotNull(decisions[tweeter].Confidence);
        Assert.Contains("vs W", decisions[tweeter].Detail);
        Assert.DoesNotContain("onset-locked", decisions[tweeter].Detail);
    }

    [Fact]
    public void Compute_RecoversAnInsertedDelay_TwoWay()
    {
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel("T", DelayedImpulse(0.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [1_000], log);

        Assert.False(alignment.ContainsKey(woofer));
        AlignmentOverride result = alignment[tweeter];
        Assert.InRange(result.DelayMs, 0.95, 1.05);
        Assert.False(result.InvertPolarity);
        Assert.Equal(Math.Round(result.DelayMs, 2), result.DelayMs, 9);
        Assert.Contains("Reference: W", log.ToString());
        Assert.Contains("Pair W/T:", log.ToString());
    }

    [Fact]
    public void Compute_ReferenceIsNotTheBottomChannel_WalksDownward()
    {
        // Tweeter latest: exercises the downward-walk branch.
        var woofer = new TestChannel("W", DelayedImpulse(0.0));
        var tweeter = new TestChannel("T", DelayedImpulse(1.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [1_000], log);

        Assert.False(alignment.ContainsKey(tweeter));
        Assert.InRange(alignment[woofer].DelayMs, 0.95, 1.05);
        Assert.False(alignment[woofer].InvertPolarity);
        Assert.Contains("Reference: T", log.ToString());
    }

    [Fact]
    public void Compute_DetectsAnInvertedChannel()
    {
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel("T", DelayedImpulse(0.0, invert: true));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [1_000], log);

        AlignmentOverride result = alignment[tweeter];
        Assert.True(result.InvertPolarity);
        Assert.InRange(result.DelayMs, 0.9, 1.1);
    }

    [Fact]
    public void Compute_ChainsDelaysThroughASettledNeighbor_ThreeWay()
    {
        // The tweeter aligns to the settled mid, so it inherits the mid's 2 ms.
        var sub = new TestChannel("S", DelayedImpulse(2.0));
        var mid = new TestChannel("M", DelayedImpulse(0.0));
        var tweeter = new TestChannel("T", DelayedImpulse(0.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([sub, mid, tweeter], [200, 2_000], log);

        Assert.False(alignment.ContainsKey(sub));
        Assert.InRange(alignment[mid].DelayMs, 1.9, 2.1);
        Assert.InRange(alignment[tweeter].DelayMs, 1.9, 2.1);
        Assert.False(alignment[mid].InvertPolarity);
        Assert.False(alignment[tweeter].InvertPolarity);
        Assert.Contains("Reference: S", log.ToString());
    }

    [Fact]
    public void Compute_NegativeOptimum_ShiftsTheOtherChannelsInstead()
    {
        // Search-time optimum is an impossible -0.3 ms: zero the tweeter, push the woofer out instead.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", DelayedImpulse(0.9), reprocessIr: DelayedImpulse(1.3));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [1_000], log);

        Assert.InRange(alignment[tweeter].DelayMs, -0.001, 0.001);
        Assert.InRange(alignment[woofer].DelayMs, 0.25, 0.35);
        Assert.False(alignment[woofer].InvertPolarity);
    }

    [Fact]
    public void Compute_ResultAtTheWindowEdge_RetriesWidened()
    {
        // 600 Hz is below the onset lock's gate; the optimum (+0.1 ms) lies just outside the fine window.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", DelayedImpulse(0.0), reprocessIr: DelayedImpulse(0.9));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [600], log);

        Assert.InRange(alignment[tweeter].DelayMs, 0.05, 0.15);
        Assert.Contains("WARNING: fine result at the search edge", log.ToString());
    }

    [Fact]
    public void Compute_OptimumBeyondTheRetryReach_PromotesTheWideWindowPick()
    {
        // 600 Hz: the optimum is a period past the fine window; only the ±3 ms sweep reaches it.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", DelayedImpulse(0.0), reprocessIr: DelayedImpulse(-1.7));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [600], log);

        Assert.InRange(alignment[tweeter].DelayMs, 2.6, 2.8);
        Assert.Contains("promoted", log.ToString());
    }

    [Fact]
    public void Compute_SeedErrorAtASharpJunction_OnsetLockRecoversDirectly()
    {
        // Periodic front: PHAT and the direct cut both tie, the envelope seeds 1.2 periods off, the onset lock re-anchors.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", PeriodicFront(0.5),
            reprocessIr: DelayedImpulse(0.6));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [2_000], log, bands: [(700, 5_600)]);

        Assert.InRange(alignment[tweeter].DelayMs, 0.35, 0.45);
        Assert.Contains("ONSET-LOCKED", log.ToString());
        Assert.Contains("onset gap after", log.ToString());
        Assert.Contains("direct-cut", log.ToString());
        Assert.Contains("unusable", log.ToString());
        Assert.DoesNotContain(
            "WARNING: fine result at the search edge", log.ToString());
        Assert.DoesNotContain("promoted", log.ToString());
    }

    [Theory]
    // Matched odd-order splits null in phase at the corner: inverted by construction, not left to the sum score.
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 36, true)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 12, true)]
    [InlineData(CrossoverFilterFamily.Butterworth, 12, true)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 24, false)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 48, false)]
    public void Compute_MatchedSplit_TakesThePolarityItsFiltersAskFor(
        CrossoverFilterFamily family,
        int slopeDbPerOctave,
        bool expectInverted)
    {
        var log = new StringBuilder();
        (Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
            IAlignmentChannel lower,
            IAlignmentChannel upper) = RunFilteredJunction(
                family, slopeDbPerOctave, upperCornerHz: 2_000, log: log);

        Assert.Equal(
            expectInverted,
            alignment.GetValueOrDefault(lower).InvertPolarity !=
                alignment.GetValueOrDefault(upper).InvertPolarity);
        Assert.Contains("by construction", log.ToString());
        Assert.Contains(
            expectInverted ? "sums only inverted" : "sums in phase",
            log.ToString());
    }

    [Theory]
    // No settled polarity: the dominant extremum stands.
    [InlineData(null, true, true, false)]
    [InlineData(null, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    // Extremum in the wrong family and the cut names the right one: the seed follows the cut.
    [InlineData(true, false, true, true)]
    [InlineData(false, true, false, true)]
    // No or agreeing direct witness: unopposed (a backwards-wired driver looks exactly like this).
    [InlineData(true, false, null, false)]
    [InlineData(false, true, null, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, false)]
    public void SeedFamilyFollowsTheSettledPolarity_MovesOnlyOnAgreementWithTheCut(
        bool? settledInversion,
        bool dominantInverted,
        bool? directInverted,
        bool expected)
    {
        AutoAlignmentEngine.SettledJunctionPolarity? settled =
            settledInversion is bool inverted
                ? new AutoAlignmentEngine.SettledJunctionPolarity(inverted, "because")
                : null;
        var dominant = new CorrelationDelayCandidate(0.5, 0.4, dominantInverted);
        CorrelationDelayCandidate? direct = directInverted is bool cut
            ? new CorrelationDelayCandidate(0.4, 0.8, cut)
            : null;

        Assert.Equal(
            expected,
            AutoAlignmentEngine.SeedFamilyFollowsTheSettledPolarity(
                settled, dominant, direct));
    }

    [Theory]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 36, 2_000, true)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 24, 2_000, false)]
    public void CrossoverSettlesJunctionPolarity_ReadsTheSplitAboveTheFence(
        CrossoverFilterFamily family,
        int slopeDbPerOctave,
        double cornerHz,
        bool expectInverted)
    {
        AutoAlignmentEngine.SettledJunctionPolarity? settled =
            AutoAlignmentEngine.CrossoverSettlesJunctionPolarity(
                FilteredJunction(family, slopeDbPerOctave, cornerHz));

        Assert.NotNull(settled);
        Assert.Equal(expectInverted, settled!.Value.Inverted);
        Assert.Contains(
            expectInverted ? "sums only inverted" : "sums in phase",
            settled.Value.Because);
    }

    [Fact]
    public void CrossoverSettlesJunctionPolarity_StaysOutBelowTheFence()
    {
        // At 180 Hz cabin modes, not the crossover, shape the band: no forced answer.
        Assert.Null(AutoAlignmentEngine.CrossoverSettlesJunctionPolarity(
            FilteredJunction(CrossoverFilterFamily.LinkwitzRiley, 36, 180)));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void InheritedJunctionPolarity_ReadsTheRelationTheReferenceSideSettled(
        bool lowerReferenceInverted,
        bool upperReferenceInverted,
        bool expectInverted)
    {
        // The far side takes its counterpart's settled sign, whatever its own split asks for.
        var lowerReference = new TestChannel("W ref", UnitImpulse(BasePosition));
        var upperReference = new TestChannel("T ref", UnitImpulse(BasePosition));
        AlignmentJunction far = FilteredJunction(
            CrossoverFilterFamily.LinkwitzRiley, 36, 2_000);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [lowerReference] = new AlignmentOverride(0.0, lowerReferenceInverted),
            [upperReference] = new AlignmentOverride(0.0, upperReferenceInverted)
        };

        AutoAlignmentEngine.SettledJunctionPolarity? settled =
            AutoAlignmentEngine.InheritedJunctionPolarity(
                far,
                [],
                [
                    new StereoPairLink(
                        lowerReference, far.Lower.Channel, 100, 10_000),
                    new StereoPairLink(
                        upperReference, far.Upper.Channel, 100, 10_000)
                ],
                alignment);

        Assert.NotNull(settled);
        Assert.Equal(expectInverted, settled!.Value.Inverted);
        Assert.Contains("the far side", settled.Value.Because);
        Assert.True(
            AutoAlignmentEngine.CrossoverSettlesJunctionPolarity(far)
                is { Inverted: true });
    }

    [Fact]
    public void InheritedJunctionPolarity_TakesAMonoChannelAsItsOwnCounterpart()
    {
        AlignmentJunction far = FilteredJunction(
            CrossoverFilterFamily.LinkwitzRiley, 36, 2_000);
        var upperReference = new TestChannel("T ref", UnitImpulse(BasePosition));
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [far.Lower.Channel] = new AlignmentOverride(0.0, true),
            [upperReference] = new AlignmentOverride(0.0, false)
        };

        AutoAlignmentEngine.SettledJunctionPolarity? settled =
            AutoAlignmentEngine.InheritedJunctionPolarity(
                far,
                [far.Lower.Channel],
                [new StereoPairLink(upperReference, far.Upper.Channel, 100, 10_000)],
                alignment);

        Assert.NotNull(settled);
        Assert.True(settled!.Value.Inverted);
    }

    [Fact]
    public void InheritedJunctionPolarity_RefusesWhenOneSideHasNoCounterpart()
    {
        // Mixed authority across the junction: no single family, the seed stays put.
        AlignmentJunction far = FilteredJunction(
            CrossoverFilterFamily.LinkwitzRiley, 36, 2_000);
        var upperReference = new TestChannel("T ref", UnitImpulse(BasePosition));

        Assert.Null(AutoAlignmentEngine.InheritedJunctionPolarity(
            far,
            [],
            [new StereoPairLink(upperReference, far.Upper.Channel, 100, 10_000)],
            new Dictionary<IAlignmentChannel, AlignmentOverride>()));
        Assert.Null(AutoAlignmentEngine.InheritedJunctionPolarity(
            far, [], null, new Dictionary<IAlignmentChannel, AlignmentOverride>()));
    }

    private static AlignmentJunction FilteredJunction(
        CrossoverFilterFamily family, int slopeDbPerOctave, double cornerHz)
    {
        var lowPass = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            LowPassEdge: new CrossoverEdge(family, cornerHz, slopeDbPerOctave)));
        var highPass = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(family, cornerHz, slopeDbPerOctave)));
        return new AlignmentJunction(
            PredictableSnapshot("W", UnitImpulse(BasePosition), lowPass),
            PredictableSnapshot("T", UnitImpulse(BasePosition), highPass),
            cornerHz,
            cornerHz / 2,
            cornerHz * 2);
    }

    [Fact]
    public void Compute_MatchedOddOrderSplit_SeedsTheFamilyTheCrossoverSumsIn()
    {
        // Matched LR36 plus late cabin copies half a period apart: PHAT prefers a peak, the cut reads the trough.
        var log = new StringBuilder();
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            RunFilteredJunction(
                CrossoverFilterFamily.LinkwitzRiley, 36, upperCornerHz: 2_000,
                log: log,
                lowerTail: (240, 2.0),
                upperTail: (228, -2.0)).Alignment;

        string trace = log.ToString();
        Assert.Contains(
            "direct-cut (the matched 2000 Hz split sums only inverted; " +
            "the record's dominant peak",
            trace);
        Assert.Contains(
            "is the polarity this junction will not be searched in)", trace);
        Assert.DoesNotContain("concurs", trace);
        Assert.InRange(
            alignment[alignment.Keys.Single(item => item.Name == "T")].DelayMs,
            -0.12,
            0.12);
    }

    [Fact]
    public void Compute_MatchedEvenOrderSplit_KeepsInPhaseAgainstTheSumsWishes()
    {
        // Deliberate trade: a backwards-wired driver behind a matched even-order split is not corrected.
        var log = new StringBuilder();
        (Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
            IAlignmentChannel lower,
            IAlignmentChannel upper) = RunFilteredJunction(
                CrossoverFilterFamily.LinkwitzRiley, 24, upperCornerHz: 2_000,
                log: log, upperAmplitude: -1.0);

        Assert.Equal(
            alignment.GetValueOrDefault(lower).InvertPolarity,
            alignment.GetValueOrDefault(upper).InvertPolarity);
        Assert.Contains("sums in phase", log.ToString());
    }
    [Theory]
    [InlineData(37)]
    [InlineData(0)]
    [InlineData(-37)]
    public void PlacePairAt_PutsTheUpperChannelThatMuchLaterEitherWay(
        int slideSamples)
    {
        // Either candidate sign: the upper channel ends exactly slideSamples behind the lower.
        Complex[] lower = UnitImpulse(BasePosition);
        Complex[] upper = UnitImpulse(BasePosition);

        (Complex[] placedLower, Complex[] placedUpper) =
            AutoAlignmentEngine.PlacePairAt(lower, upper, slideSamples);

        int lowerPeak = VirtualCrossoverAnalysis.FindPeakIndex(placedLower);
        int upperPeak = VirtualCrossoverAnalysis.FindPeakIndex(placedUpper);
        Assert.Equal(slideSamples, upperPeak - lowerPeak);
        Assert.Equal(lower.Length, placedLower.Length);
        Assert.Equal(upper.Length, placedUpper.Length);
    }
    [Fact]
    public void Compute_StaggeredSplit_LeavesThePolarityToTheSearch()
    {
        // Staggered corners (2000 vs 2400 Hz) overlap rather than cross: no single phase relation, the rule stays out.
        var log = new StringBuilder();
        RunFilteredJunction(
            CrossoverFilterFamily.LinkwitzRiley, 36, upperCornerHz: 2_400, log: log);

        Assert.DoesNotContain("by construction", log.ToString());
    }

    private static (Dictionary<IAlignmentChannel, AlignmentOverride> Alignment,
        IAlignmentChannel Lower, IAlignmentChannel Upper) RunFilteredJunction(
        CrossoverFilterFamily family,
        int slopeDbPerOctave,
        double upperCornerHz,
        StringBuilder log,
        double upperAmplitude = 1.0,
        (int Samples, double Amplitude)? lowerTail = null,
        (int Samples, double Amplitude)? upperTail = null)
    {
        const double lowerCornerHz = 2_000;
        var lowPass = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            LowPassEdge: new CrossoverEdge(family, lowerCornerHz, slopeDbPerOctave)));
        var highPass = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(family, upperCornerHz, slopeDbPerOctave)));

        AlignmentSnapshot lower = PredictableSnapshot(
            "W", WithTail(UnitImpulse(BasePosition), lowerTail), lowPass);
        AlignmentSnapshot upper = PredictableSnapshot(
            "T",
            WithTail(UnitImpulse(BasePosition, upperAmplitude), upperTail),
            highPass);
        var junction = new AlignmentJunction(
            lower, upper, lowerCornerHz, lowerCornerHz / 2, lowerCornerHz * 2);

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            AlignmentSnapshot Apply(AlignmentSnapshot snapshot, DspChannelChain chain)
            {
                AlignmentOverride over = overrides.GetValueOrDefault(snapshot.Channel);
                Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
                    snapshot.BypassedImpulseResponse!,
                    chain with
                    {
                        DelayMs = over.DelayMs,
                        InvertPolarity = over.InvertPolarity
                    },
                    SampleRate,
                    SampleRate,
                    out ValidSampleRange range);
                return new AlignmentSnapshot(
                    snapshot.Channel,
                    ir,
                    VirtualCrossoverAnalysis.FindPeakIndex(ir),
                    range,
                    chain,
                    snapshot.BypassedImpulseResponse);
            }

            return [Apply(lower, lowPass), Apply(upper, highPass)];
        }

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            [lower, upper], [junction], Reprocess, alignment, log);
        return (alignment, lower.Channel, upper.Channel);
    }

    [Fact]
    public void Compute_UntrustedPhatAtAHighJunction_DirectCutWitnessSeeds()
    {
        // A stronger copy one period behind ties the PHAT rivals; the direct cut resolves the lobe.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", ImpulseWithEcho(0.0, 0.995, 0.5, 1.0),
            reprocessIr: DelayedImpulse(0.6));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [2_000], log, bands: [(700, 5_600)]);

        Assert.InRange(alignment[tweeter].DelayMs, 0.35, 0.45);
        Assert.Contains("seed direct-cut (phat:", log.ToString());
        Assert.DoesNotContain("ONSET-LOCKED", log.ToString());
    }

    [Fact]
    public void Compute_ReflectionPhantomFarFromTheArrival_IsRefusedForTheDirectCut()
    {
        // Correlated late reflections dominate PHAT five periods off; a direct-cut witness tightens the reach to 1.5 periods.
        var woofer = new TestChannel("W", ReflectedFront(1.0, 3.0, 1.4));
        var tweeter = new TestChannel("T", ReflectedFront(0.0, 5.5, 1.4));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [2_000], log, bands: [(700, 5_600)]);

        Assert.InRange(alignment[tweeter].DelayMs, 0.9, 1.1);
        Assert.Contains(
            "seed direct-cut (phat: peak beyond the arrival's reach)", log.ToString());
    }

    [Fact]
    public void Compute_TroughDominantLowJunction_SeedsFromTheTroughAndFindsTheInvertedLobe()
    {
        // 85 Hz: inverted upper channel ~15 ms early; the dominant trough seeds with a narrow window.
        var midbass = new TestChannel("B", DelayedImpulse(15.2));
        var mid = new TestChannel("C", DelayedImpulse(0.0, invert: true));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([midbass, mid], [85], log);

        string text = log.ToString();
        Assert.False(alignment.ContainsKey(midbass));
        AlignmentOverride result = alignment[mid];
        Assert.True(result.InvertPolarity);
        Assert.InRange(result.DelayMs, 15.0, 15.4);
        string pairLine = TestLog.Line(text, "Pair B/C");
        Assert.Contains("phat trough", pairLine);
        Assert.Contains("-> seed phat", pairLine);
        Assert.DoesNotContain("WIDE SEED", TestLog.Line(text, "Channel C:"));
    }

    [Fact]
    public void Compute_TrustedSeedAtALowJunction_KeepsBothPolaritiesInTheWindow()
    {
        // 85 Hz: the polarity partner is half a period (5.9 ms) out, past the old 2.5 ms cap; the window must reach it.
        var midbass = new TestChannel("B", DelayedImpulse(15.2));
        var mid = new TestChannel("C", DelayedImpulse(0.0, invert: true));
        var log = new StringBuilder();

        Run([midbass, mid], [85], log);

        string channelLine = TestLog.Line(log.ToString(), "Channel C:");
        Assert.DoesNotContain("WIDE SEED", channelLine);
        Match window = Regex.Match(
            channelLine.Replace(',', '.'),
            @"window (-?\d+\.\d+)\.\.(-?\d+\.\d+) ms");
        Assert.True(window.Success, channelLine);
        double low = double.Parse(
            window.Groups[1].Value, CultureInfo.InvariantCulture);
        double high = double.Parse(
            window.Groups[2].Value, CultureInfo.InvariantCulture);
        Assert.True(
            high - 15.2 >= 5.0 || 15.2 - low >= 5.0,
            $"the polarity partner is out of the search window:\r\n{channelLine}");
        Assert.True(
            high - low < 2.0 * 1000.0 / 85.0,
            $"the window must stay inside one period:\r\n{channelLine}");
    }

    [Fact]
    public void Compute_DominantPeakFarFromTheArrival_IsNotTrustedAsTheSeed()
    {
        // Soft direct sound under a strong late reflection: the reach rule refuses the PHAT peak (WIDE SEED).
        var midbass = new TestChannel("B", DelayedImpulse(15.0));
        var mid = new TestChannel(
            "C", ImpulseWithEcho(0.0, 0.35, 8.0, 1.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([midbass, mid], [85], log);

        string text = log.ToString();
        Assert.False(alignment.ContainsKey(midbass));
        Assert.Contains(
            "seed arrival (peak beyond the arrival's reach)",
            TestLog.Line(text, "Pair B/C"));
        Assert.Contains("WIDE SEED", TestLog.Line(text, "Channel C:"));
    }

    private static Complex[] SplitBandArrivals()
    {
        Complex[] low = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(
                GainDb: 6,
                DelayMs: 10.0,
                Crossover: new CrossoverSpec(
                    CrossoverKind.LowPass,
                    new CrossoverEdge(
                        CrossoverFilterFamily.Butterworth, 180, 36))),
            SampleRate,
            SampleRate);
        Complex[] high = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(
                GainDb: -14,
                DelayMs: 2.0,
                Crossover: new CrossoverSpec(
                    CrossoverKind.HighPass,
                    HighPassEdge: new CrossoverEdge(
                        CrossoverFilterFamily.Butterworth, 280, 36))),
            SampleRate,
            SampleRate);
        for (int i = 0; i < low.Length; i++)
        {
            low[i] += high[i];
        }
        return low;
    }

    [Fact]
    public void FindAlignmentCandidates_LevelMatch_PinsTheWinnerAcrossGains()
    {
        // Two lobes in different band halves: the winner follows gain unless the level match pins it.
        Complex[] fixedIr = SplitBandArrivals();
        double WinnerMs(double gain, bool levelMatch)
        {
            Complex[] variable = UnitImpulse(BasePosition)
                .Select(value => value * gain)
                .ToArray();
            IReadOnlyList<AlignmentCandidate> candidates =
                VirtualCrossoverAnalysis.FindAlignmentCandidates(
                    variable, [fixedIr], SampleRate, 90, 360, -1, 13,
                    levelMatch: levelMatch);
            Assert.NotEmpty(candidates);
            return candidates[0].DelayMs;
        }

        double unmatchedLoud = WinnerMs(1.0, levelMatch: false);
        double unmatchedQuiet = WinnerMs(0.1, levelMatch: false);
        Assert.True(
            Math.Abs(unmatchedLoud - unmatchedQuiet) > 2.0,
            "the synthetic must discriminate: unmatched winners " +
            $"{unmatchedLoud:0.000} vs {unmatchedQuiet:0.000} ms");

        double matched = WinnerMs(1.0, levelMatch: true);
        foreach (double gain in new[] { 0.316, 0.1 })
        {
            double winner = WinnerMs(gain, levelMatch: true);
            Assert.InRange(winner, matched - 0.35, matched + 0.35);
        }
    }

    [Fact]
    public void Compute_ModalLatchOnTheArrival_ReanchorsOnTheHalfBandReads()
    {
        // 180 Hz modal latch: the full band reads the build-up (~37 ms), the upper half the front (~15 ms).
        var midbass = new TestChannel(
            "B", FrontUnderLateMode(5.0, 15.0, 2.0));
        var mid = new TestChannel("C", DelayedImpulse(0.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([midbass, mid], [180], log);

        string text = log.ToString();
        Assert.False(alignment.ContainsKey(midbass));
        Assert.Contains("(modal latch)", text);
        Assert.Contains("arrivals 15", TestLog.Line(text, "Pair B/C"));
        Assert.InRange(alignment[mid].DelayMs, 3.0, 9.0);
    }

    // 0.20 reads -14.8 dB of prominence, 0.30 reads -11.6 dB: either side of half the search depth.
    [Theory]
    [InlineData(0.20, true)]
    [InlineData(0.30, false)]
    public void Compute_ArrivalPickedFarUnderItsBandEnergy_CannotVetoTheExtremum(
        double frontAmplitude, bool deepEnoughToStandDown)
    {
        // Deep sub pick against a neighbour read at its band peak: the reach veto withdraws, but only to the direct cut.
        var sub = new TestChannel(
            "W", ImpulseWithEcho(0.0, frontAmplitude, 8.0, 1.0));
        var midbass = new TestChannel("B", DelayedImpulse(8.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([sub, midbass], [110], log);

        double relative = alignment.GetValueOrDefault(midbass).DelayMs -
            alignment.GetValueOrDefault(sub).DelayMs;
        string text = log.ToString();
        Assert.DoesNotContain("(modal latch)", text);
        if (deepEnoughToStandDown)
        {
            Assert.InRange(relative, -1.5, 1.5);
            Assert.Contains("under its own band's energy", text);
            Assert.Contains("stands on its own strength", text);
            Assert.Contains("-> seed phat", text);
        }
        else
        {
            Assert.Contains("beyond the arrival's reach", text);
            Assert.DoesNotContain("under its own band's energy", text);
            Assert.True(
                Math.Abs(relative) > 2.5,
                $"the vetoed run should keep the arrival's answer, got {relative:0.00} ms");
        }
    }

    // Signed skew from the chains alone: zero for identical chains, null when one side is unreadable (unknown, not zero).
    [Fact]
    public void PairChainArrivalSkew_ReadsTheChainsAlone()
    {
        const int Length = 32_768;
        var subChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 48)));
        var wooferChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 300, 24),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 24)));
        AlignmentSnapshot sub = PredictableSnapshot(
            "SUB", SingleImpulse(Length, BasePosition), subChain);
        AlignmentSnapshot woofer = PredictableSnapshot(
            "W", SingleImpulse(Length, BasePosition), wooferChain);

        // A period at 55 Hz is 18.2 ms; the steep low-pass drags the lower side most of one.
        double? skew = AutoAlignmentEngine.PairChainArrivalSkewMs(
            new AlignmentJunction(sub, woofer, 55, 27.5, 110));
        Assert.NotNull(skew);
        Assert.InRange(skew.Value, 10.0, 20.0);

        AlignmentSnapshot subTwin = PredictableSnapshot(
            "SUB2", SingleImpulse(Length, BasePosition), subChain);
        Assert.Equal(
            0.0,
            AutoAlignmentEngine.PairChainArrivalSkewMs(
                new AlignmentJunction(sub, subTwin, 55, 27.5, 110)));

        var midChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 2_000, 24)));
        var tweeterChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.Butterworth, 2_000, 24)));
        double? highSkew = AutoAlignmentEngine.PairChainArrivalSkewMs(
            new AlignmentJunction(
                PredictableSnapshot(
                    "M", SingleImpulse(Length, BasePosition), midChain),
                PredictableSnapshot(
                    "T", SingleImpulse(Length, BasePosition), tweeterChain),
                2_000, 1_000, 4_000));
        Assert.NotNull(highSkew);
        Assert.InRange(Math.Abs(highSkew.Value), 0.0, 0.3);

        Complex[] bareIr = SingleImpulse(Length, BasePosition);
        var bare = new AlignmentSnapshot(
            new TestChannel("X", bareIr), bareIr, BasePosition);
        Assert.Null(
            AutoAlignmentEngine.PairChainArrivalSkewMs(
                new AlignmentJunction(bare, sub, 55, 27.5, 110)));
    }

    // Skew corrects the anchor inside the half-period reach, never widens it. Rows: (offset, reach, skew, verdict).
    [Theory]
    // Passat v2: offset −11.19, skew +6.74, reach 9.09 → corrected −4.45.
    [InlineData(-11.193, 9.09, 6.74, true)]
    // The opposite skew does not explain the offset.
    [InlineData(-11.193, 9.09, -6.74, false)]
    // 80 Hz: a full period from the corrected anchor stays refused.
    [InlineData(-19.2, 6.25, 6.7, false)]
    [InlineData(-8.5, 6.25, 6.7, true)]
    [InlineData(-12.9, 6.25, 6.74, true)]
    [InlineData(-13.0, 6.25, 6.75, false)]
    [InlineData(-11.193, 9.09, double.NaN, false)]
    public void ChainSkewExplainsSeedOffset_CorrectsTheAnchor_NeverWidensTheReach(
        double seedOffsetMs, double reachMs, double skewMs, bool expected)
    {
        Assert.Equal(
            expected,
            AutoAlignmentEngine.ChainSkewExplainsSeedOffset(
                seedOffsetMs,
                reachMs,
                double.IsNaN(skewMs) ? null : skewMs));
    }

    // A skew as large as the reach disqualifies the anchor. Rows: (reach, skew, verdict).
    [Theory]
    [InlineData(3.0, 3.864, true)]
    [InlineData(3.0, -3.864, true)]
    [InlineData(3.0, 2.99, false)]
    [InlineData(3.0, 3.0, true)]
    [InlineData(7.692, 6.739, false)]
    [InlineData(3.0, double.NaN, false)]
    public void ChainSkewDisqualifiesTheAnchor_AtTheReachItself(
        double reachMs, double skewMs, bool expected)
    {
        Assert.Equal(
            expected,
            AutoAlignmentEngine.ChainSkewDisqualifiesTheAnchor(
                reachMs,
                double.IsNaN(skewMs) ? null : skewMs));
    }

    private static CorrelationDelayCandidate Seed(
        double coefficient, double delayMs = 0.0, bool invert = false) =>
        new(delayMs, coefficient, invert);

    [Theory]
    // Below the cut's frequency |r| 0.15 (a seed's floor) cannot move a lobe; 0.5 can.
    [InlineData(110.0, -20.0, true, 0.21, false)]
    [InlineData(110.0, -20.0, true, 0.49, false)]
    [InlineData(110.0, -20.0, true, 0.50, true)]
    [InlineData(110.0, -20.0, true, 0.95, true)]
    [InlineData(110.0, -20.0, true, -0.95, true)]
    // A pick in the upper half of the search depth keeps its veto.
    [InlineData(110.0, -12.4, true, 0.95, false)]
    [InlineData(110.0, 0.0, true, 0.95, false)]
    [InlineData(110.0, -12.5, true, 0.95, false)]
    [InlineData(110.0, -12.51, true, 0.95, true)]
    [InlineData(110.0, -20.0, false, 0.95, false)]
    public void MayWithdrawSeedReachVeto_BelowTheDirectCutsFrequency(
        double crossoverHz,
        double anchorProminenceDb,
        bool anchorIsRawReads,
        double seedCoefficient,
        bool expected)
    {
        bool asked = false;
        bool withdraw = AutoAlignmentEngine.MayWithdrawSeedReachVeto(
            anchorProminenceDb,
            anchorIsRawReads,
            Seed(seedCoefficient),
            crossoverHz,
            () =>
            {
                asked = true;
                return null;
            });

        Assert.Equal(expected, withdraw);
        Assert.False(asked, "the direct cut was taken below its own frequency");
    }

    [Theory]
    [InlineData(0.0, false, true)]
    [InlineData(0.124, false, true)]   // a quarter period at 2 kHz is 0.125 ms
    [InlineData(0.126, false, false)]
    [InlineData(-0.124, false, true)]
    [InlineData(0.0, true, false)]     // the same position, opposite polarity
    public void MayWithdrawSeedReachVeto_AtTheDirectCutsFrequency(
        double corroborationDelayMs, bool corroborationInverted, bool expected)
    {
        Assert.Equal(
            expected,
            AutoAlignmentEngine.MayWithdrawSeedReachVeto(
                anchorProminenceDb: -20.0,
                anchorIsRawReads: true,
                Seed(0.95),
                crossoverHz: 2_000.0,
                () => Seed(0.9, corroborationDelayMs, corroborationInverted)));
    }

    [Fact]
    public void MayWithdrawSeedReachVeto_WithNoUsableDirectCut_KeepsTheVeto()
    {
        Assert.False(
            AutoAlignmentEngine.MayWithdrawSeedReachVeto(
                anchorProminenceDb: -20.0,
                anchorIsRawReads: true,
                Seed(0.95),
                crossoverHz: 2_000.0,
                () => null));
    }

    [Fact]
    public void Compute_DeepArrivalPickWithAContradictingDirectCut_KeepsTheReachVeto()
    {
        // Deep pick plus a dominant reflection lobe: the cut refuses to corroborate, so the veto stands.
        var woofer = new TestChannel("W", ReflectedFront(1.0, 3.0, 5.0));
        var tweeter = new TestChannel("T", ReflectedFront(0.0, 5.5, 5.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [2_000], log, bands: [(700, 5_600)]);

        string text = log.ToString();
        Assert.DoesNotContain("under its own band's energy", text);
        Assert.Contains(
            "seed direct-cut (phat: peak beyond the arrival's reach)", text);
        Assert.InRange(alignment[tweeter].DelayMs, 0.9, 1.1);
    }

    // Upper half owned by a late high-passed reflection: the probe is valid but Unverified.
    private static Complex[] FrontWithLateHighReflection(
        double frontMs, double reflectionMs, double reflectionGainDb)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(
                DelayMs: frontMs,
                Crossover: new CrossoverSpec(
                    CrossoverKind.LowPass,
                    new CrossoverEdge(
                        CrossoverFilterFamily.Butterworth, 150, 36))),
            SampleRate,
            SampleRate);
        Complex[] late = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(
                GainDb: reflectionGainDb,
                DelayMs: reflectionMs,
                Crossover: new CrossoverSpec(
                    CrossoverKind.HighPass,
                    HighPassEdge: new CrossoverEdge(
                        CrossoverFilterFamily.Butterworth, 220, 36))),
            SampleRate,
            SampleRate);
        for (int i = 0; i < ir.Length; i++)
        {
            ir[i] += late[i];
        }
        return ir;
    }

    [Fact]
    public void Compute_ModalLatchWithIncomparableProbe_KeepsTheFullBandAnchor()
    {
        // The two sides' probes time different events: no re-anchor, the reach veto stays armed.
        var midbass = new TestChannel("B", FrontUnderLateMode(5.0, 15.0, 2.0));
        var mid = new TestChannel("C", FrontWithLateHighReflection(0.0, 8.0, 8));
        var log = new StringBuilder();

        try
        {
            Run([midbass, mid], [180], log);
        }
        catch (InvalidOperationException)
        {
            // A refusal is acceptable here; the seed contract was logged before it.
        }

        string text = log.ToString();
        Assert.Contains("(modal latch)", text);
        string pairLine = TestLog.Line(text, "Pair B/C");
        Assert.Contains("arrivals 37", pairLine);
        // A conviction without a comparable replacement must not lift the veto.
        Assert.Contains("beyond the arrival's reach", pairLine);
        Assert.DoesNotContain("cannot veto it", text);
    }

    [Fact]
    public void Compute_SamePolarityRivalNearTie_IsNotTrustedAsTheSeed()
    {
        // Two same-polarity lobes a period apart, far one stronger: the rival rule (not the trough rules) must refuse.
        var midbass = new TestChannel("B", DelayedImpulse(15.0));
        var mid = new TestChannel(
            "C", ImpulseWithEcho(0.0, 0.995, 11.76, 1.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([midbass, mid], [85], log, bands: [(30, 340)]);

        string text = log.ToString();
        Assert.False(alignment.ContainsKey(midbass));
        Assert.Contains(
            "seed arrival (same-polarity rival near-tie)",
            TestLog.Line(text, "Pair B/C"));
        Assert.Contains("WIDE SEED", TestLog.Line(text, "Channel C:"));
    }

    [Fact]
    public void Compute_SameSignTroughRivalNearTie_IsNotTrustedAsTheSeed()
    {
        // Trough mirror: two inverted copies a period apart; the NegativeRival near-tie sends the seed to the envelope.
        var midbass = new TestChannel("B", DelayedImpulse(15.0));
        var mid = new TestChannel(
            "C", ImpulseWithEcho(0.0, -0.995, 11.76, -1.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([midbass, mid], [85], log, bands: [(30, 340)]);

        string text = log.ToString();
        Assert.False(alignment.ContainsKey(midbass));
        string pairLine = TestLog.Line(text, "Pair B/C");
        Assert.Contains("phat trough", pairLine);
        Assert.Contains(
            "seed arrival (same-polarity rival near-tie)", pairLine);
        Assert.Contains("WIDE SEED", TestLog.Line(text, "Channel C:"));
    }

    [Fact]
    public void Compute_RejectsInvalidInput()
    {
        var only = new TestChannel("A", DelayedImpulse(0.0));
        var other = new TestChannel("B", DelayedImpulse(0.5));
        var snapshot = new AlignmentSnapshot(
            only, only.InitialIr, BasePosition);
        var otherSnapshot = new AlignmentSnapshot(
            other, other.InitialIr, BasePosition);
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            [snapshot, otherSnapshot];

        Assert.Throws<ArgumentException>(() => AutoAlignmentEngine.Compute(
            [snapshot],
            [],
            Reprocess,
            new Dictionary<IAlignmentChannel, AlignmentOverride>(),
            new StringBuilder()));
        Assert.Throws<ArgumentException>(() => AutoAlignmentEngine.Compute(
            [snapshot, otherSnapshot],
            [],
            Reprocess,
            new Dictionary<IAlignmentChannel, AlignmentOverride>(),
            new StringBuilder()));
    }

    private sealed class OddRateChannel(string name, Complex[] ir) : IAlignmentChannel
    {
        public string Name { get; } = name;
        public int SampleRate => 44_100;
        public int ProcessorSampleRate => SampleRate;
        public Complex[] Ir { get; } = ir;
    }

    [Fact]
    public void Compute_RejectsMixedSampleRates()
    {
        // Mixed rates would silently misscale frequencies and delays.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var odd = new OddRateChannel("T", DelayedImpulse(0.0));
        var wooferSnapshot = new AlignmentSnapshot(
            woofer, woofer.InitialIr, BasePosition);
        var oddSnapshot = new AlignmentSnapshot(odd, odd.Ir, BasePosition);
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            [wooferSnapshot, oddSnapshot];

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => AutoAlignmentEngine.Compute(
                [wooferSnapshot, oddSnapshot],
                [new AlignmentJunction(wooferSnapshot, oddSnapshot, 1_000, 500, 2_000)],
                Reprocess,
                new Dictionary<IAlignmentChannel, AlignmentOverride>(),
                new StringBuilder()));
        Assert.Contains("sample rate", error.Message);
    }

    [Fact]
    public void Compute_ClearsAStaleAlignmentMap()
    {
        // Repeat call with the same dictionary: stale entries must not leak.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel("T", DelayedImpulse(0.0));
        var stale = new TestChannel("stale", DelayedImpulse(0.0));
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [stale] = new AlignmentOverride(42.0, true)
        };
        var log = new StringBuilder();

        Run([woofer, tweeter], [1_000], log, alignment: alignment);

        Assert.False(alignment.ContainsKey(stale));
    }

    [Fact]
    public void Compute_SilentJunction_RefusesTheRunInsteadOfFabricatingADelay()
    {
        // No evidence at B/C: the whole run refuses (a partial skip could leave foreign delays).
        var woofer = new TestChannel("A", DelayedImpulse(1.0));
        var silentB = new TestChannel("B", new Complex[IrLength]);
        var silentC = new TestChannel("C", new Complex[IrLength]);
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([woofer, silentB, silentC], [200, 1_000], log));

        Assert.Contains("No junction evidence", error.Message);
        Assert.Contains("refusing the run", log.ToString());
    }

    // A dead channel is noise, not zeros: its envelope SNR ~8 dB is under the 12 dB floor.
    private static Complex[] NoiseIr(int seed, double amplitude)
    {
        var random = new Random(seed);
        var ir = new Complex[IrLength];
        for (int i = 0; i < ir.Length; i++)
        {
            ir[i] = amplitude * (random.NextDouble() * 2.0 - 1.0);
        }
        return ir;
    }

    [Fact]
    public void Compute_IndependentEqualLevelNoise_RefusesTheRun()
    {
        var noiseA = new TestChannel("A", NoiseIr(1, 1.0));
        var noiseB = new TestChannel("B", NoiseIr(2, 1.0));
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([noiseA, noiseB], [1_000], log));

        Assert.Contains("No junction evidence", error.Message);
    }

    [Fact]
    public void Compute_ActiveAndLowLevelNoise_RefusesTheRun()
    {
        var woofer = new TestChannel("A", DelayedImpulse(1.0));
        var noise = new TestChannel("B", NoiseIr(3, 0.01));
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([woofer, noise], [1_000], log));

        Assert.Contains("No junction evidence", error.Message);
    }

    // A shared tone has no timeable front (envelope SNR ~-7 dB) and is ambiguous modulo its period.
    private static Complex[] SharedLineIr(double toneHz, double startMs)
    {
        var ir = new Complex[IrLength];
        int start = BasePosition + (int)Math.Round(startMs / 1000.0 * SampleRate);
        for (int i = 0; start + i < ir.Length; i++)
        {
            ir[start + i] = Math.Sin(Math.Tau * toneHz * i / SampleRate);
        }
        return ir;
    }

    [Fact]
    public void Compute_SharedNarrowLineOnALowJunction_RefusesTheRun()
    {
        var lower = new TestChannel("A", SharedLineIr(120, 1.0));
        var upper = new TestChannel("B", SharedLineIr(120, 1.2));
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([lower, upper], [120], log, bands: [(80, 175)]));

        Assert.Contains("No junction evidence", error.Message);
    }

    [Fact]
    public void Compute_ActiveFixedAndSilentVariable_RefusesTheRun()
    {
        // Fixed neighbour radiates, searched channel silent: flat loss, the prior must not invent a candidate.
        var woofer = new TestChannel("A", DelayedImpulse(1.0));
        var silent = new TestChannel("B", new Complex[IrLength]);
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([woofer, silent], [1_000], log));

        Assert.Contains("No junction evidence", error.Message);
    }

    [Fact]
    public void Compute_SilentFixedAndActiveVariable_RefusesTheRun()
    {
        var silent = new TestChannel("A", new Complex[IrLength]);
        var tweeter = new TestChannel("B", DelayedImpulse(0.0));
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([silent, tweeter], [1_000], log));

        Assert.Contains("No junction evidence", error.Message);
    }

    private static TimeAlignmentAnalysisResult Read(
        double arrivalMs, double snrDb = 40, bool valid = true) =>
        default(TimeAlignmentAnalysisResult) with
        {
            FirstArrivalDelayMilliseconds = arrivalMs,
            SignalToNoiseDecibels = snrDb,
            IsValid = valid
        };

    // Inline table, not a Theory: the certificate enum is internal.
    [Fact]
    public void ClassifyArrival_GradesTheHonestyProbe()
    {
        var table = new (double FullMs, double ProbeMs, double ProbeSnrDb,
            bool ProbeValid, AutoAlignmentEngine.ArrivalCertificate Expected)[]
        {
            (10.0, 10.4, 40.0, true, AutoAlignmentEngine.ArrivalCertificate.Verified),
            // full far LATER than its upper half: the proven modal latch
            (21.2, 13.9, 40.0, true, AutoAlignmentEngine.ArrivalCertificate.Latched),
            // full far EARLIER: the probe is blind to the front — usable, uncertified
            (8.0, 20.0, 40.0, true, AutoAlignmentEngine.ArrivalCertificate.Unverified),
            (10.0, 10.1, 5.0, true, AutoAlignmentEngine.ArrivalCertificate.Unverified),
            (10.0, 0.0, 40.0, false, AutoAlignmentEngine.ArrivalCertificate.Unverified),
            // exactly at the tolerance edge still certifies
            (12.0, 10.0, 40.0, true, AutoAlignmentEngine.ArrivalCertificate.Verified),
        };

        foreach (var row in table)
        {
            AutoAlignmentEngine.ArrivalCertificate actual =
                AutoAlignmentEngine.ClassifyArrival(
                    Read(row.FullMs),
                    Read(row.ProbeMs, row.ProbeSnrDb, row.ProbeValid),
                    toleranceMs: 2.0);
            Assert.True(row.Expected == actual,
                $"full {row.FullMs}, probe {row.ProbeMs} " +
                $"(SNR {row.ProbeSnrDb}, valid {row.ProbeValid}): " +
                $"expected {row.Expected}, got {actual}");
        }

        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Unverified,
            AutoAlignmentEngine.ClassifyArrival(
                Read(10.0, valid: false), Read(10.2), toleranceMs: 2.0));
        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Unverified,
            AutoAlignmentEngine.ClassifyArrival(
                Read(10.0, snrDb: 5.0), Read(10.2), toleranceMs: 2.0));
    }

    // Refiltering the chain-free front through the chain reproduces the processed arrival (analytic GD missed by 3.8 / 2.3 ms).
    [Theory]
    [InlineData("LR48 HP 80", 40, 160)]
    [InlineData("BW36 BP 70-200", 100, 400)]
    [InlineData("BW12 LP 200", 100, 400)]
    [InlineData("LR24 LP 2000", 750, 3000)]
    [InlineData("BW48 HP 1700", 750, 3000)]
    [InlineData("AllPass 330", 100, 400)]
    [InlineData("PEQ 120 Q8", 100, 400)]
    public void PredictedArrival_ReproducesTheProcessedFront(
        string chainName, double lowHz, double highHz)
    {
        AlignmentSnapshot snapshot = PredictableSnapshot(
            chainName, UnitImpulse(BasePosition), NamedChain(chainName));

        double measuredMs = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            snapshot.ImpulseResponse, SampleRate, lowHz, highHz,
            snapshot.ValidRange).FirstArrivalDelayMilliseconds;
        AutoAlignmentEngine.PredictionState state =
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs, lowHz, highHz, out double predictedMs);

        Assert.Equal(AutoAlignmentEngine.PredictionState.Verified, state);
        Assert.True(Math.Abs(measuredMs - predictedMs) < 0.5,
            $"{chainName}: predicted {predictedMs:0.000} ms against a " +
            $"measured {measuredMs:0.000} ms");
    }

    private static DspChannelChain NamedChain(string name) => name switch
    {
        "LR48 HP 80" => new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, 80, 48))),
        "BW36 BP 70-200" => new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 70, 36))),
        "BW12 LP 200" => new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 12))),
        "LR24 LP 2000" => new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24))),
        "BW48 HP 1700" => new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.Butterworth, 1_700, 48))),
        "AllPass 330" => new DspChannelChain(
            Peq: new EqualizationCurve(
                [new PeqBand(330, 3.5, 0, PeqBandType.AllPassSecondOrder)])),
        "PEQ 120 Q8" => new DspChannelChain(
            Peq: new EqualizationCurve([new PeqBand(120, 8.0, 6.0)])),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    // A steep LP leaves the band's energy on a room mode; the plain front must pass, so the mode is what convicts.
    [Fact]
    public void PredictedArrival_ConvictsALateModeAndClearsThePlainFront()
    {
        var chain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 70, 36)));
        Complex[] front = FrontUnderLateMode(0.0, 12.0, 0.0);
        Complex[] withMode = FrontUnderLateMode(0.0, 12.0, 0.6);

        AlignmentSnapshot clean = PredictableSnapshot("clean", front, chain);
        AlignmentSnapshot latched = PredictableSnapshot("latched", withMode, chain);

        double Read(AlignmentSnapshot side) =>
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                side.ImpulseResponse, SampleRate, 100, 400, side.ValidRange)
                .FirstArrivalDelayMilliseconds;

        double cleanMs = Read(clean);
        double latchedMs = Read(latched);
        // Guard: the mode must actually move the detector.
        Assert.True(latchedMs - cleanMs > 5.0,
            $"the fixture did not latch: clean {cleanMs:0.000}, " +
            $"with mode {latchedMs:0.000} ms");

        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Verified,
            AutoAlignmentEngine.GradeAgainstPrediction(
                clean, cleanMs, 100, 400, out _));
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Latched,
            AutoAlignmentEngine.GradeAgainstPrediction(
                latched, latchedMs, 100, 400, out _));
    }

    // Sub front plus a build-up below the corner: the read sits past what the chain explains, short of a latch conviction.
    private static Complex[] LowFrontUnderCabinBuildUp(
        int length, double buildUpMs, double amplitude)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            SingleImpulse(length, BasePosition),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 300, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 25, 24))),
            SampleRate,
            SampleRate);
        int start = BasePosition + (int)Math.Round(buildUpMs / 1000.0 * SampleRate);
        const double AttackSeconds = 0.002;
        const double DecaySeconds = 0.06;
        foreach (double modeHz in new[] { 33.0, 38.0, 44.0 })
        {
            for (int i = start; i < ir.Length; i++)
            {
                double t = (i - start) / (double)SampleRate;
                ir[i] += amplitude *
                    (1 - Math.Exp(-t / AttackSeconds)) *
                    Math.Exp(-t / DecaySeconds) *
                    Math.Sin(2 * Math.PI * modeHz * t);
            }
        }

        return ir;
    }

    private static Complex[] SingleImpulse(int length, int position)
    {
        var ir = new Complex[length];
        ir[position] = Complex.One;
        return ir;
    }

    // Like LowFrontUnderCabinBuildUp, but the build-up is inside a 150 Hz junction's band.
    private static Complex[] FrontUnderInBandBuildUp(
        int length, double buildUpMs, double amplitude)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            SingleImpulse(length, BasePosition),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 400, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 40, 24))),
            SampleRate,
            SampleRate);
        int start = BasePosition + (int)Math.Round(buildUpMs / 1000.0 * SampleRate);
        const double AttackSeconds = 0.004;
        const double DecaySeconds = 0.05;
        foreach (double modeHz in new[] { 85.0, 95.0, 108.0 })
        {
            for (int i = start; i < ir.Length; i++)
            {
                double t = (i - start) / (double)SampleRate;
                ir[i] += amplitude *
                    (1 - Math.Exp(-t / AttackSeconds)) *
                    Math.Exp(-t / DecaySeconds) *
                    Math.Sin(2 * Math.PI * modeHz * t);
            }
        }

        return ir;
    }

    [Fact]
    public void Compute_NearTiedPeakAndTrough_StillSeedFromTheExtremum()
    {
        // Steep corners make peak and trough near-equal (band width, not trust); the extremum must seed anyway.
        const int Length = 32_768;
        var midbassChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 130, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 60, 36)));
        var midChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1700, 48),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 170, 36)));
        Complex[] midbassBypassed = FrontUnderInBandBuildUp(Length, 9.0, 0.25);
        Complex[] midBypassed = SingleImpulse(
            Length, BasePosition + 4 * SampleRate / 1000);

        AlignmentSnapshot Snapshot(
            string name, Complex[] bypassed, DspChannelChain chain)
        {
            Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                bypassed, chain, SampleRate, SampleRate, out ValidSampleRange range);
            return new AlignmentSnapshot(
                new TestChannel(name, processed), processed,
                VirtualCrossoverAnalysis.FindPeakIndex(processed), range,
                chain, bypassed);
        }

        AlignmentSnapshot midbass = Snapshot("B", midbassBypassed, midbassChain);
        AlignmentSnapshot mid = Snapshot("C", midBypassed, midChain);
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            AlignmentSnapshot One(
                AlignmentSnapshot side, Complex[] bypassed, DspChannelChain chain)
            {
                AlignmentOverride over = overrides.GetValueOrDefault(side.Channel);
                Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                    bypassed,
                    chain with
                    {
                        DelayMs = over.DelayMs,
                        InvertPolarity = over.InvertPolarity
                    },
                    SampleRate,
                    SampleRate,
                    out ValidSampleRange range);
                return side with
                {
                    ImpulseResponse = processed,
                    PeakIndex = VirtualCrossoverAnalysis.FindPeakIndex(processed),
                    ValidRange = range
                };
            }

            return
            [
                One(midbass, midbassBypassed, midbassChain),
                One(mid, midBypassed, midChain)
            ];
        }

        var log = new StringBuilder();
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            [midbass, mid],
            [new AlignmentJunction(midbass, mid, 150, 75, 300)],
            Reprocess,
            alignment,
            log);

        string text = log.ToString();
        string pairLine = TestLog.Line(text, "Pair B/C");
        Assert.Contains("modal latch behind the crossover", text);
        Assert.Matches(@"dom 0,0\d\d", pairLine.Replace('.', ','));
        Assert.Contains("seed phat", pairLine);

        string channelLine = TestLog.Line(text, "Channel C:");
        Assert.Contains(" inv (", channelLine);
        Assert.Contains("; ", channelLine);
    }

    [Fact]
    public void Compute_KeepsTheConservativePathWhenTheLobeGeometryIsUnmeasured()
    {
        // 55 Hz, 36 dB/oct: lobes ~9 ms apart, equal to the arrival allowance.
        const int Length = 32_768;
        var subChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 36)));
        var wooferChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 180, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 36)));
        Complex[] subBypassed = LowFrontUnderCabinBuildUp(Length, 4.0, 0.02);
        // The 23 ms offset only re-centres the window; the build-up's length removes the interior neighbour lobe.
        Complex[] wooferBypassed = SingleImpulse(
            Length, BasePosition + 23 * SampleRate / 1000);

        AlignmentSnapshot Snapshot(string name, Complex[] bypassed, DspChannelChain chain)
        {
            Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                bypassed, chain, SampleRate, SampleRate, out ValidSampleRange range);
            return new AlignmentSnapshot(
                new TestChannel(name, processed), processed,
                VirtualCrossoverAnalysis.FindPeakIndex(processed), range,
                chain, bypassed);
        }

        AlignmentSnapshot sub = Snapshot("SUB", subBypassed, subChain);
        AlignmentSnapshot woofer = Snapshot("W", wooferBypassed, wooferChain);
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            AlignmentSnapshot One(AlignmentSnapshot side, Complex[] bypassed, DspChannelChain chain)
            {
                AlignmentOverride over = overrides.GetValueOrDefault(side.Channel);
                Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                    bypassed,
                    chain with { DelayMs = over.DelayMs, InvertPolarity = over.InvertPolarity },
                    SampleRate,
                    SampleRate,
                    out ValidSampleRange range);
                return side with
                {
                    ImpulseResponse = processed,
                    PeakIndex = VirtualCrossoverAnalysis.FindPeakIndex(processed),
                    ValidRange = range
                };
            }

            return [One(sub, subBypassed, subChain), One(woofer, wooferBypassed, wooferChain)];
        }

        var log = new StringBuilder();
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            [sub, woofer],
            [new AlignmentJunction(sub, woofer, 55, 27.5, 110)],
            Reprocess,
            alignment,
            log);

        // No interior lobe beside the seed: an unmeasured lobe spacing must not license re-anchoring.
        string trace = log.ToString();
        Assert.DoesNotContain("cannot place the junction inside a lobe", trace);
        Assert.True(
            trace.Contains("beyond the arrival's reach"),
            $"the seed should have been refused conservatively:\r\n{trace}");
    }

    // Dead-zone shape (see LatchArbitrationMinR): a short-tailed mode drags the read 1-2 allowances late.
    private static Complex[] FrontUnderShortMode(
        int length, double modeHz, double modeMs, double amplitude)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            SingleImpulse(length, BasePosition),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 300, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 25, 24))),
            SampleRate,
            SampleRate);
        int start = BasePosition + (int)Math.Round(modeMs / 1000.0 * SampleRate);
        const double AttackSeconds = 0.002;
        const double DecaySeconds = 0.012;
        for (int i = start; i < ir.Length; i++)
        {
            double t = (i - start) / (double)SampleRate;
            ir[i] += amplitude *
                (1 - Math.Exp(-t / AttackSeconds)) *
                Math.Exp(-t / DecaySeconds) *
                Math.Sin(2 * Math.PI * modeHz * t);
        }
        return ir;
    }



    private static (string Trace, AlignmentOverride Woofer) RunDeadZonePair(
        AlignmentSnapshot sub, Complex[] subBypassed, DspChannelChain subChain,
        AlignmentSnapshot woofer, Complex[] wooferBypassed,
        DspChannelChain wooferChain)
    {
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            AlignmentSnapshot One(
                AlignmentSnapshot side, Complex[] bypassed, DspChannelChain chain)
            {
                AlignmentOverride over = overrides.GetValueOrDefault(side.Channel);
                Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                    bypassed,
                    chain with
                    {
                        DelayMs = over.DelayMs,
                        InvertPolarity = over.InvertPolarity
                    },
                    SampleRate,
                    SampleRate,
                    out ValidSampleRange range);
                return side with
                {
                    ImpulseResponse = processed,
                    PeakIndex = VirtualCrossoverAnalysis.FindPeakIndex(processed),
                    ValidRange = range
                };
            }

            return
            [
                One(sub, subBypassed, subChain),
                One(woofer, wooferBypassed, wooferChain)
            ];
        }

        var log = new StringBuilder();
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            [sub, woofer],
            [new AlignmentJunction(sub, woofer, 55, 27.5, 110)],
            Reprocess,
            alignment,
            log);
        return (log.ToString(), alignment.GetValueOrDefault(woofer.Channel));
    }

    // The sub read latches 1.9 allowances late; the whitened comb is the second witness.
    // Unconvicted the sub ends at 30.0 ms inverted; convicted, 18.0 ms upright.
    [Fact]
    public void Compute_DeadZoneLatch_IsConvictedByTheWhitenedCombArbitration()
    {
        const int Length = 32_768;
        var subChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 48)));
        var wooferChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 300, 48),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 48)));
        Complex[] subBypassed = FrontUnderShortMode(Length, 30.0, 14.0, 0.03);
        Complex[] wooferBypassed = SingleImpulse(Length, BasePosition);
        AlignmentSnapshot sub = PredictableSnapshot("SUB", subBypassed, subChain);
        AlignmentSnapshot woofer = PredictableSnapshot(
            "W", wooferBypassed, wooferChain);

        double Read(AlignmentSnapshot side) =>
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                side.ImpulseResponse, SampleRate, 27.5, 110, side.ValidRange)
                .FirstArrivalDelayMilliseconds;
        double subRead = Read(sub);
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Inconsistent,
            AutoAlignmentEngine.GradeAgainstPrediction(
                sub, subRead, 27.5, 110, out double subPrediction));
        double allowance = AutoAlignmentEngine.PredictedArrivalAllowanceMs(
            27.5, 110);
        Assert.InRange((subRead - subPrediction) / allowance, 1.0, 2.0);
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Verified,
            AutoAlignmentEngine.GradeAgainstPrediction(
                woofer, Read(woofer), 27.5, 110, out _));

        (string trace, AlignmentOverride over) = RunDeadZonePair(
            sub, subBypassed, subChain, woofer, wooferBypassed, wooferChain);

        Assert.Contains("SUB: read sits in the conviction dead zone", trace);
        Assert.Contains("convicted by arbitration", trace);
        Assert.Contains("modal latch behind the crossover", trace);
        Assert.Contains("seed phat", trace);
        Assert.False(over.InvertPolarity);
        Assert.InRange(over.DelayMs, 16.0, 20.0);
    }

    // Woofer rings at 90 Hz where the sub is 48 dB/oct down: comb r 0.85 at the prediction vs 0.98 at the measured family.
    // Held by the advantage arm, not the 0.6 floor.
    [Fact]
    public void Compute_DeadZoneLatch_ArbitrationStandsDownWithoutASecondWitness()
    {
        const int Length = 32_768;
        var subChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 48)));
        var wooferChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 300, 24),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 24)));
        Complex[] subBypassed = FrontUnderShortMode(Length, 30.0, 14.0, 0.03);
        Complex[] wooferBypassed = FrontUnderShortMode(Length, 90.0, 14.0, 0.40);
        AlignmentSnapshot sub = PredictableSnapshot("SUB", subBypassed, subChain);
        AlignmentSnapshot woofer = PredictableSnapshot(
            "W", wooferBypassed, wooferChain);

        double Read(AlignmentSnapshot side) =>
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                side.ImpulseResponse, SampleRate, 27.5, 110, side.ValidRange)
                .FirstArrivalDelayMilliseconds;
        double subRead = Read(sub);
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Inconsistent,
            AutoAlignmentEngine.GradeAgainstPrediction(
                sub, subRead, 27.5, 110, out double subPrediction));
        double allowance = AutoAlignmentEngine.PredictedArrivalAllowanceMs(
            27.5, 110);
        Assert.InRange((subRead - subPrediction) / allowance, 1.0, 2.0);
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Verified,
            AutoAlignmentEngine.GradeAgainstPrediction(
                woofer, Read(woofer), 27.5, 110, out _));

        (string trace, _) = RunDeadZonePair(
            sub, subBypassed, subChain, woofer, wooferBypassed, wooferChain);

        Assert.Contains("latch arbitration stood down for SUB/W", trace);
        // Parse the trace rather than match text: the engine formats in the current culture.
        Match combReading = Regex.Match(
            trace,
            @"comb r ([-0-9]+[.,][0-9]+) at the predicted family vs ([-0-9]+[.,][0-9]+)");
        Assert.True(combReading.Success, trace);
        static double Reading(Group group) => double.Parse(
            group.Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        double atPredicted = Reading(combReading.Groups[1]);
        double atMeasured = Reading(combReading.Groups[2]);
        Assert.True(atPredicted >= 0.6, $"{atPredicted} should clear the floor");
        Assert.True(
            atPredicted < atMeasured,
            $"{atPredicted} should lose to {atMeasured}");
        Assert.DoesNotContain("convicted by arbitration", trace);
        Assert.DoesNotContain("modal latch behind the crossover", trace);
    }

    [Fact]
    public void PredictedArrival_GradesEveryState()
    {
        var chain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 36)));
        AlignmentSnapshot snapshot = PredictableSnapshot(
            "graded", UnitImpulse(BasePosition), chain);
        double measuredMs = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            snapshot.ImpulseResponse, SampleRate, 100, 400, snapshot.ValidRange)
            .FirstArrivalDelayMilliseconds;

        // The allowance here is 2.5 ms; a conviction needs twice that.
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Latched,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs + 9.0, 100, 400, out _));
        // Field false convictions sat within 1.2 allowances; true latches cleared 2.5.
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Inconsistent,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs + 3.0, 100, 400, out _));
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Inconsistent,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs - 9.0, 100, 400, out _));
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Unavailable,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot with { BypassedImpulseResponse = null },
                measuredMs, 100, 400, out _));
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Unavailable,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot with { ProcessingChain = null },
                measuredMs, 100, 400, out _));
    }

    [Fact]
    public void ArrivalProbeTolerance_CreditsTheChannelsOwnCrossoverSmear()
    {
        AlignmentSnapshot filtered = PredictableSnapshot(
            "midbass", UnitImpulse(BasePosition), NamedChain("BW36 BP 70-200"));
        var chainless = new AlignmentSnapshot(
            new TestChannel("bare", UnitImpulse(BasePosition)),
            UnitImpulse(BasePosition),
            BasePosition);

        double MeasuredMs(AlignmentSnapshot side, double lowHz) =>
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                side.ImpulseResponse, SampleRate, lowHz, 400, side.ValidRange)
                .FirstArrivalDelayMilliseconds;
        double bare = AutoAlignmentEngine.ArrivalProbeToleranceMs(
            chainless, MeasuredMs(chainless, 100), MeasuredMs(chainless, 200),
            100, 200, 400);
        double credited = AutoAlignmentEngine.ArrivalProbeToleranceMs(
            filtered, MeasuredMs(filtered, 100), MeasuredMs(filtered, 200),
            100, 200, 400);

        // Without a chain: half a period at the probe's lower edge (200 Hz -> 2.5 ms).
        Assert.Equal(2.5, bare, 6);
        // The field skew (2.88 ms) must be credited; a real latch (10.97 ms) must not.
        Assert.True(credited > 2.88,
            $"expected the filter smear to be credited past 2.88 ms; got {credited:0.000}");
        Assert.True(credited < 10.97,
            $"expected a real modal latch to stay convicted; got {credited:0.000}");
    }

    [Fact]
    public void ArrivalProbeTolerance_NeverTightensBelowTheGenericFloor()
    {
        AlignmentSnapshot highPassed = PredictableSnapshot(
            "tweeter", UnitImpulse(BasePosition), NamedChain("BW48 HP 1700"));

        double tolerance = AutoAlignmentEngine.ArrivalProbeToleranceMs(
            highPassed,
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                highPassed.ImpulseResponse, SampleRate, 750, 3_000,
                highPassed.ValidRange).FirstArrivalDelayMilliseconds,
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                highPassed.ImpulseResponse, SampleRate, 1_500, 3_000,
                highPassed.ValidRange).FirstArrivalDelayMilliseconds,
            750, 1_500, 3_000);

        Assert.True(tolerance >= Math.Max(1.0, 500.0 / 1_500),
            $"the generic floor must hold; got {tolerance:0.000}");
    }

    [Fact]
    public void NormalizeAndVerifyFeasibility_LiftsTheFieldAndRefusesAWideSpan()
    {
        var early = new TestChannel("E", DelayedImpulse(0.0));
        var late = new TestChannel("L", DelayedImpulse(1.0));
        var earlySnapshot = new AlignmentSnapshot(early, early.InitialIr, BasePosition);
        var lateSnapshot = new AlignmentSnapshot(late, late.InitialIr, BasePosition);

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [early] = new AlignmentOverride(8.0, false),
            [late] = new AlignmentOverride(28.0, false)
        };
        AutoAlignmentEngine.NormalizeAndVerifyFeasibility(
            [earlySnapshot, lateSnapshot], alignment, new StringBuilder());
        Assert.Equal(0.0, alignment[early].DelayMs, 2);
        Assert.Equal(20.0, alignment[late].DelayMs, 2);

        // Wider than the 50 ms DSP delay range: refuse loudly, do not clamp.
        alignment[early] = new AlignmentOverride(0.0, false);
        alignment[late] = new AlignmentOverride(65.0, false);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => AutoAlignmentEngine.NormalizeAndVerifyFeasibility(
                [earlySnapshot, lateSnapshot], alignment, new StringBuilder()));
        Assert.Contains("does not fit", error.Message);
    }

    [Fact]
    public void NormalizeAndVerifyFeasibility_JudgesAgainstTheDeviceOwnCeiling()
    {
        // 50 ms is the default for an unknown device; a catalog ceiling tightens it.
        var early = new TestChannel("E", DelayedImpulse(0.0));
        var late = new TestChannel("L", DelayedImpulse(1.0));
        var earlySnapshot = new AlignmentSnapshot(early, early.InitialIr, BasePosition);
        var lateSnapshot = new AlignmentSnapshot(late, late.InitialIr, BasePosition);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [early] = new AlignmentOverride(0.0, false),
            [late] = new AlignmentOverride(12.0, false)
        };

        AutoAlignmentEngine.NormalizeAndVerifyFeasibility(
            [earlySnapshot, lateSnapshot], alignment, new StringBuilder());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => AutoAlignmentEngine.NormalizeAndVerifyFeasibility(
                [earlySnapshot, lateSnapshot], alignment, new StringBuilder(),
                maxDelayMs: 10.0));
        Assert.Contains("does not fit", error.Message);
        Assert.Contains("10 ms", error.Message);
    }
}
