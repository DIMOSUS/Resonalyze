using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The two decisions the staged Auto delay run turns on: which channels the
/// engine walks, and the rigid shift that makes the finished set dialable.
/// </summary>
public sealed class VirtualCrossoverStagedAlignmentTests
{
    private static VirtualCrossoverChannel Block(string name, VirtualCrossoverZone zone)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = 48_000 };
        channel.Pair.Zone = zone;
        return channel;
    }

    [Fact]
    public void SplitAlignmentStages_LeavesAFrontOnlyProjectUnstaged()
    {
        // The compatibility guarantee where it is acted on. Everything lands in
        // the chain and nothing after it, so the caller passes a null walk set —
        // the old engine call — and skips both later passes. This is why the
        // session battery cannot drift: not because the staged path happens to
        // agree, but because such a project never enters it.
        VirtualCrossoverChannel sub = Block("A", VirtualCrossoverZone.Sub);
        VirtualCrossoverChannel mid = Block("B", VirtualCrossoverZone.Front);
        VirtualCrossoverChannel tweeter = Block("C", VirtualCrossoverZone.Front);

        (List<VirtualCrossoverChannel> chain, List<VirtualCrossoverChannel> later) =
            VirtualCrossoverPanel.SplitAlignmentStages([sub, mid, tweeter]);

        Assert.Equal(3, chain.Count);
        Assert.Empty(later);
    }

    [Fact]
    public void SplitAlignmentStages_HoldsTheRearAndCentreBackForTheirOwnStages()
    {
        VirtualCrossoverChannel sub = Block("A", VirtualCrossoverZone.Sub);
        VirtualCrossoverChannel front = Block("B", VirtualCrossoverZone.Front);
        VirtualCrossoverChannel rear = Block("C", VirtualCrossoverZone.Rear);
        VirtualCrossoverChannel centre = Block("D", VirtualCrossoverZone.Center);

        (List<VirtualCrossoverChannel> chain, List<VirtualCrossoverChannel> later) =
            VirtualCrossoverPanel.SplitAlignmentStages([sub, front, rear, centre]);

        Assert.Equal(["A", "B"], chain.Select(item => item.Name));
        Assert.Equal(["C", "D"], later.Select(item => item.Name));
    }

    [Fact]
    public void SplitAlignmentStages_WalksARearOnlyProjectAsItsOwnChain()
    {
        // Nothing to be placed against. A rear-only project is a legitimate
        // thing to align — it is simply its own chain — and holding it back
        // would leave every one of its channels waiting for a front stage that
        // does not exist.
        VirtualCrossoverChannel low = Block("A", VirtualCrossoverZone.Rear);
        VirtualCrossoverChannel high = Block("B", VirtualCrossoverZone.Rear);

        (List<VirtualCrossoverChannel> chain, List<VirtualCrossoverChannel> later) =
            VirtualCrossoverPanel.SplitAlignmentStages([low, high]);

        Assert.Equal(2, chain.Count);
        Assert.Empty(later);
    }

    [Fact]
    public void TheWalkNeverPairsAFrontDriverWithTheRearFill()
    {
        // The defect this whole change exists to remove, written as the thing
        // that used to happen. The reference car's rear pair is high-passed at
        // 290 Hz, so by band centre it lands between the midrange and the
        // tweeter — and the walk, which pairs neighbours, declared a midrange
        // handing over to a rear fill at the midrange's own low-pass corner. No
        // filter does that.
        //
        // Staging is what stops it: the walk set is the front chain, so the rear
        // is not a neighbour of anything. Run against the unstaged list the same
        // assertion fails, which is what makes this a falsifier rather than a
        // restatement.
        var mid = Block("A", VirtualCrossoverZone.Front);
        mid.Settings.CrossoverKind = CrossoverKind.BandPass;
        mid.Settings.HighPassEdge =
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 290, 24);
        mid.Settings.LowPassEdge =
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_500, 24);
        var tweeter = Block("B", VirtualCrossoverZone.Front);
        tweeter.Settings.CrossoverKind = CrossoverKind.HighPass;
        tweeter.Settings.HighPassEdge =
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_500, 24);
        var rear = Block("C", VirtualCrossoverZone.Rear);
        rear.Settings.CrossoverKind = CrossoverKind.HighPass;
        rear.Settings.HighPassEdge =
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 290, 24);

        (List<VirtualCrossoverChannel> chain, List<VirtualCrossoverChannel> later) =
            VirtualCrossoverPanel.SplitAlignmentStages([mid, tweeter, rear]);

        Assert.Equal(["A", "B"], chain.Select(item => item.Name));
        Assert.Equal(["C"], later.Select(item => item.Name));

        // Ordered by band centre the rear sits BETWEEN the two front drivers, so
        // an unstaged walk would have paired it with both. Pinned here so the
        // ordering that caused the defect is on record rather than assumed.
        Assert.Equal(
            ["A", "C", "B"],
            new[] { mid, tweeter, rear }
                .OrderBy(item => VirtualCrossoverJunctions.BandCenterHz(item.Settings))
                .Select(item => item.Name));
    }

    [Theory]
    // Left-hand drive: the driver's side is the left, so the right is the far one.
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    // Right-hand drive mirrors it, and this is the case that was wrong: the
    // group sums reach the placement in the engine's ROLES, so reading the cabin
    // side there timed every rear driver against the opposite side's front stage.
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public void IsFarSide_FollowsTheLayoutRatherThanTheCabinSide(
        bool rightSide,
        bool rightHandDrive,
        bool far) =>
        Assert.Equal(far, VirtualCrossoverPanel.IsFarSide(rightSide, rightHandDrive));

    [Fact]
    public void NormalizeStagedDelays_SlidesEveryChannelAndKeepsEveryRelation()
    {
        // The rear fill was pushed behind the front stage, which asked the front
        // stage to go negative. Nothing is dialable until this pass; afterwards
        // the earliest sits at zero and every gap between channels is the one
        // the stages computed.
        var front = Block("A", VirtualCrossoverZone.Front);
        var sub = Block("B", VirtualCrossoverZone.Sub);
        var rear = Block("C", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(-4.0, false),
            [sub] = new AlignmentOverride(-1.5, true),
            [rear] = new AlignmentOverride(11.0, false)
        };
        var log = new System.Text.StringBuilder();

        VirtualCrossoverPanel.NormalizeStagedDelays([front, sub, rear], alignment, log);

        Assert.Equal(0.0, alignment[front].DelayMs, 6);
        Assert.Equal(2.5, alignment[sub].DelayMs, 6);
        Assert.Equal(15.0, alignment[rear].DelayMs, 6);
        // Polarity is a property of the channel, not of where the set sits.
        Assert.True(alignment[sub].InvertPolarity);
        // The shift is reported, so a reader of the trace can tell a staged run's
        // absolute delays from the relative ones the stages computed. Matched on
        // the word rather than the number: the log is written in the machine's
        // own culture, where the decimal separator may be a comma.
        Assert.Contains("normalization", log.ToString());
        Assert.Contains("shifted", log.ToString());
    }

    [Fact]
    public void NormalizeStagedDelays_MovesTheANCHOR_ThoughItHasNoEntryOfItsOwn()
    {
        // The map is SPARSE by design: the alignment engine documents absence as
        // "nothing proposed" and deliberately leaves its reference channel out
        // rather than manufacture a zero-delay proposal for it
        // (AutoAlignmentEngine.NormalizeAndVerifyFeasibility says so in as many
        // words). A shift driven off the map's KEYS therefore skipped exactly
        // that channel: the anchor stayed at zero while its own siblings moved
        // around it, so the "rigid-body" shift silently rewrote the one relation
        // in the whole run that must not change — the front chain's internal
        // alignment the engine had just settled.
        //
        // The earlier version of this test put the anchor in the dictionary by
        // hand and so could never have caught it.
        var anchor = Block("A", VirtualCrossoverZone.Front);
        var sibling = Block("B", VirtualCrossoverZone.Front);
        var rear = Block("C", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            // No entry for the anchor at all — it sits at an implied zero.
            [sibling] = new AlignmentOverride(2.5, false),
            [rear] = new AlignmentOverride(-3.0, false)
        };

        VirtualCrossoverPanel.NormalizeStagedDelays(
            [anchor, sibling, rear], alignment, new System.Text.StringBuilder());

        // Everything moved by the same +3, the anchor included, so the front
        // chain's 2.5 ms gap is still 2.5 ms.
        Assert.Equal(3.0, alignment[anchor].DelayMs, 6);
        Assert.Equal(5.5, alignment[sibling].DelayMs, 6);
        Assert.Equal(0.0, alignment[rear].DelayMs, 6);
        Assert.Equal(
            2.5,
            alignment[sibling].DelayMs - alignment[anchor].DelayMs,
            6);
    }

    [Fact]
    public void NormalizeStagedDelays_ReadsAnAbsentAnchorAsTheEarliestChannel()
    {
        // And the minimum has to see it too. With every proposed delay positive
        // but the implied-zero anchor below them all, there is nothing to shift:
        // the set already starts at zero.
        var anchor = Block("A", VirtualCrossoverZone.Front);
        var rear = Block("B", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [rear] = new AlignmentOverride(12.0, false)
        };

        VirtualCrossoverPanel.NormalizeStagedDelays(
            [anchor, rear], alignment, new System.Text.StringBuilder());

        Assert.Equal(12.0, alignment[rear].DelayMs, 6);
        // Untouched: materializing a zero for it would be harmless here, but the
        // pass must not invent proposals it was not asked for.
        Assert.False(alignment.ContainsKey(anchor));
    }

    [Fact]
    public void NormalizeStagedDelays_LeavesADialableSetAlone()
    {
        // No shift where none is needed: moving a set that already starts at a
        // positive delay would add latency the tune did not ask for.
        var front = Block("A", VirtualCrossoverZone.Front);
        var rear = Block("B", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(1.0, false),
            [rear] = new AlignmentOverride(16.0, false)
        };

        VirtualCrossoverPanel.NormalizeStagedDelays(
            [front, rear], alignment, new System.Text.StringBuilder());

        Assert.Equal(1.0, alignment[front].DelayMs, 6);
        Assert.Equal(16.0, alignment[rear].DelayMs, 6);
    }

    [Fact]
    public void NormalizeStagedDelays_RefusesACeilingBreach_EvenWithoutAShift()
    {
        // The ceiling verdict must not ride on whether a shift happened: a rear
        // fill pushes the LATEST channel out while every delay stays positive,
        // so a check placed inside the shift branch would wave exactly the case
        // it exists for straight through to a device that cannot dial it.
        var front = Block("A", VirtualCrossoverZone.Front);
        var rear = Block("B", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(0.0, false),
            [rear] = new AlignmentOverride(23.0, false)
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => VirtualCrossoverPanel.NormalizeStagedDelays(
                [front, rear], alignment, new System.Text.StringBuilder(),
                maxDelayMs: 20.0, rearFillOffsetMs: 15.0, rearFillCarriers: [rear]));

        // The refusal names the culprit and the answer: the rear delay is
        // 8 ms of physics plus 15 ms of preference, and a 20 ms device holds
        // exactly 12 ms of that preference.
        Assert.Contains("does not fit", error.Message);
        Assert.Contains("20 ms", error.Message);
        Assert.Contains("15 ms rear fill", error.Message);
        Assert.Contains("up to 12 ms of fill fits", error.Message);
    }

    [Fact]
    public void NormalizeStagedDelays_FillSuggestion_SurvivesANonMonotoneSpan()
    {
        // The span is NOT monotone in the fill. Here the rear's co-arrival
        // placement came out at -4 ms (the rear physically farther), so the
        // fill first lifts it toward zero — CLOSING the dialable span — and
        // only past +4 starts widening it. A closed-form "subtract the excess"
        // would land on 11 ms and still not fit; the walk down the DSP's own
        // grid finds the true largest fill.
        var front = Block("A", VirtualCrossoverZone.Front);
        var rear = Block("B", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(0.0, false),
            // -4 ms co-arrival + 15 ms requested fill.
            [rear] = new AlignmentOverride(11.0, false)
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => VirtualCrossoverPanel.NormalizeStagedDelays(
                [front, rear], alignment, new System.Text.StringBuilder(),
                maxDelayMs: 10.0, rearFillOffsetMs: 15.0, rearFillCarriers: [rear]));

        // At 14 ms of fill the rear needs -4 + 14 = 10 ms — exactly the ceiling.
        Assert.Contains("up to 14 ms of fill fits", error.Message);
    }

    [Fact]
    public void NormalizeStagedDelays_WithoutAFillInPlay_RefusesWithoutBlamingIt()
    {
        // A spread the device cannot realize with no rear fill involved — the
        // honest message is the engine's own, not a suggestion to lower a knob
        // that is already at zero.
        var front = Block("A", VirtualCrossoverZone.Front);
        var centre = Block("B", VirtualCrossoverZone.Center);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(0.0, false),
            [centre] = new AlignmentOverride(26.0, false)
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => VirtualCrossoverPanel.NormalizeStagedDelays(
                [front, centre], alignment, new System.Text.StringBuilder(),
                maxDelayMs: 20.0));

        Assert.Contains("does not fit", error.Message);
        Assert.DoesNotContain("rear fill", error.Message);
        Assert.Contains("wider than the DSP can realize", error.Message);
    }

    [Fact]
    public void NormalizeStagedDelays_KeepsAFittingSetUnderATightCeiling()
    {
        // The device ceiling is a gate, not a scaler: a set that fits a tight
        // ceiling passes through untouched, fill and all.
        var front = Block("A", VirtualCrossoverZone.Front);
        var rear = Block("B", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(1.0, false),
            [rear] = new AlignmentOverride(18.0, false)
        };

        VirtualCrossoverPanel.NormalizeStagedDelays(
            [front, rear], alignment, new System.Text.StringBuilder(),
            maxDelayMs: 18.0, rearFillOffsetMs: 15.0, rearFillCarriers: [rear]);

        Assert.Equal(1.0, alignment[front].DelayMs, 6);
        Assert.Equal(18.0, alignment[rear].DelayMs, 6);
    }

    // A stereo channel pair with a synthetic measurement on each side; a mono
    // one carries a single (left) side. Deltas at distinct offsets are all the
    // engine needs: every stage reads band-limited arrivals and correlations,
    // and a clean impulse has both.
    private static VirtualCrossoverChannel Measured(
        string name,
        VirtualCrossoverZone zone,
        bool mono,
        Action<VirtualCrossoverChannelSettings> crossover,
        int leftDelaySamples,
        int rightDelaySamples = 0)
    {
        var channel = new VirtualCrossoverChannel(name);
        channel.Pair.Zone = zone;
        channel.Pair.Mono = mono;
        crossover(channel.Pair.Left);
        channel.Pair.Left.SourceFilePath = $"{name}-l.json";
        channel.SideState(false).TransferImpulseResponse =
            Impulse(leftDelaySamples);
        channel.SideState(false).SampleRate = 48_000;
        if (!mono)
        {
            crossover(channel.Pair.Right);
            channel.Pair.Right.SourceFilePath = $"{name}-r.json";
            channel.SideState(true).TransferImpulseResponse =
                Impulse(rightDelaySamples);
            channel.SideState(true).SampleRate = 48_000;
        }

        return channel;
    }

    private static System.Numerics.Complex[] Impulse(int delaySamples)
    {
        var ir = new System.Numerics.Complex[16_384];
        ir[delaySamples] = 1.0;
        return ir;
    }

    [Theory]
    // Both layouts: the engine's guard demands the monos live in its
    // REFERENCE walk, and a right-hand drive swaps which cabin side that is -
    // the fix holds on both only because a mono side is ONE instance shared
    // by both cabin lists.
    [InlineData(false)]
    [InlineData(true)]
    public void ComputeStereoAlignment_WalksTheChain_WithAMonoCentreLeftToItsOwnStage(
        bool rightHandDrive)
    {
        // The reference car's refusal, as a fixture. A staged stereo run
        // narrows the engine's walks to the front chain but hands it the
        // UNION's mono channels - and the centre is mono by definition while
        // belonging to stage 3, so the engine's own validity guard ("every
        // mono channel must be part of the left walk that tunes it") refused
        // the plan the panel assembled. The guard is right; the plan was
        // wrong. The mono channels the engine may be given are the ones its
        // walk tunes: the front chain's shared subwoofer, not the centre.
        StaTest.Run(() =>
        {
            using var panel = new VirtualCrossoverPanel();
            VirtualCrossoverChannel sub = Measured(
                "A", VirtualCrossoverZone.Sub, mono: true,
                settings =>
                {
                    settings.CrossoverKind = CrossoverKind.LowPass;
                    settings.LowPassEdge = new CrossoverEdge(
                        CrossoverFilterFamily.LinkwitzRiley, 120, 24);
                },
                leftDelaySamples: 520);
            VirtualCrossoverChannel woofer = Measured(
                "B", VirtualCrossoverZone.Front, mono: false,
                settings =>
                {
                    settings.CrossoverKind = CrossoverKind.BandPass;
                    settings.HighPassEdge = new CrossoverEdge(
                        CrossoverFilterFamily.LinkwitzRiley, 120, 24);
                    settings.LowPassEdge = new CrossoverEdge(
                        CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);
                },
                leftDelaySamples: 480,
                rightDelaySamples: 468);
            VirtualCrossoverChannel tweeter = Measured(
                "C", VirtualCrossoverZone.Front, mono: false,
                settings =>
                {
                    settings.CrossoverKind = CrossoverKind.HighPass;
                    settings.HighPassEdge = new CrossoverEdge(
                        CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);
                },
                leftDelaySamples: 500,
                rightDelaySamples: 462);
            VirtualCrossoverChannel centre = Measured(
                "D", VirtualCrossoverZone.Center, mono: true,
                settings =>
                {
                    settings.CrossoverKind = CrossoverKind.HighPass;
                    settings.HighPassEdge = new CrossoverEdge(
                        CrossoverFilterFamily.LinkwitzRiley, 290, 24);
                },
                leftDelaySamples: 505);

            // The exact shapes RunStereoProposalAsync hands over: the walks
            // narrowed to the front chain, the union carrying everything for
            // the reprocessor (stages 2-3 render their snapshots through it).
            var subSide = new VirtualCrossoverSideAlignmentChannel(sub, false);
            var woofL = new VirtualCrossoverSideAlignmentChannel(woofer, false);
            var woofR = new VirtualCrossoverSideAlignmentChannel(woofer, true);
            var twL = new VirtualCrossoverSideAlignmentChannel(tweeter, false);
            var twR = new VirtualCrossoverSideAlignmentChannel(tweeter, true);
            var centreSide = new VirtualCrossoverSideAlignmentChannel(centre, false);
            List<VirtualCrossoverSideAlignmentChannel> chainLeft =
                [subSide, woofL, twL];
            List<VirtualCrossoverSideAlignmentChannel> chainRight =
                [subSide, woofR, twR];
            List<VirtualCrossoverSideAlignmentChannel> union =
                [subSide, woofL, twL, woofR, twR, centreSide];

            var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
            var decisions = new Dictionary<IAlignmentChannel, AlignmentDecision>();
            var log = new System.Text.StringBuilder();
            panel.ComputeStereoAlignment(
                chainLeft, chainRight, union, twL, twR,
                bridgeBandLowHz: 2_000, bridgeBandHighHz: 20_000,
                sceneOffsetMs: 0.0, rightHandDrive,
                alignment, decisions, log);

            // Stage 1 does not touch the centre - it has no junctions and is
            // placed by its own stage against both settled sides.
            Assert.DoesNotContain(centreSide, alignment.Keys);
            // The chain WAS walked: the engine writes an override for every
            // walked channel except (at most) its own reference - the map is
            // sparse by contract, so exactly one absence is legitimate.
            List<IAlignmentChannel> chain = [subSide, woofL, twL, woofR, twR];
            Assert.True(
                chain.Count(member => !alignment.ContainsKey(member)) <= 1,
                "the front chain was not walked: " + string.Join(
                    ", ",
                    chain.Where(member => !alignment.ContainsKey(member))
                        .Select(member => member.Name)));
        });
    }
}
