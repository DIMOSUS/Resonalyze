using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverStagedAlignmentTests
{
    private static VirtualCrossoverChannel Block(string name, VirtualCrossoverZone zone)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = 48_000 };
        channel.Pair.Zone = zone;
        return channel;
    }

    [Fact]
    public void Split_LeavesAFrontOnlyProjectUnstaged()
    {
        // All-chain projects pass a null walk set (the single-stage engine call), which keeps the session battery from drifting.
        VirtualCrossoverChannel sub = Block("A", VirtualCrossoverZone.Sub);
        VirtualCrossoverChannel mid = Block("B", VirtualCrossoverZone.Front);
        VirtualCrossoverChannel tweeter = Block("C", VirtualCrossoverZone.Front);

        (List<VirtualCrossoverChannel> chain, List<VirtualCrossoverChannel> later) =
            VirtualCrossoverAlignmentStages.Split([sub, mid, tweeter]);

        Assert.Equal(3, chain.Count);
        Assert.Empty(later);
    }

    [Fact]
    public void Split_HoldsTheRearAndCentreBackForTheirOwnStages()
    {
        VirtualCrossoverChannel sub = Block("A", VirtualCrossoverZone.Sub);
        VirtualCrossoverChannel front = Block("B", VirtualCrossoverZone.Front);
        VirtualCrossoverChannel rear = Block("C", VirtualCrossoverZone.Rear);
        VirtualCrossoverChannel centre = Block("D", VirtualCrossoverZone.Center);

        (List<VirtualCrossoverChannel> chain, List<VirtualCrossoverChannel> later) =
            VirtualCrossoverAlignmentStages.Split([sub, front, rear, centre]);

        Assert.Equal(["A", "B"], chain.Select(item => item.Name));
        Assert.Equal(["C", "D"], later.Select(item => item.Name));
    }

    [Fact]
    public void Split_WalksARearOnlyProjectAsItsOwnChain()
    {
        VirtualCrossoverChannel low = Block("A", VirtualCrossoverZone.Rear);
        VirtualCrossoverChannel high = Block("B", VirtualCrossoverZone.Rear);

        (List<VirtualCrossoverChannel> chain, List<VirtualCrossoverChannel> later) =
            VirtualCrossoverAlignmentStages.Split([low, high]);

        Assert.Equal(2, chain.Count);
        Assert.Empty(later);
    }

    [Fact]
    public void TheWalkNeverPairsAFrontDriverWithTheRearFill()
    {
        // Falsifier: a rear high-passed at 290 Hz sorts between mid and tweeter, and an unstaged walk paired them at no real filter.
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
            VirtualCrossoverAlignmentStages.Split([mid, tweeter, rear]);

        Assert.Equal(["A", "B"], chain.Select(item => item.Name));
        Assert.Equal(["C"], later.Select(item => item.Name));

        Assert.Equal(
            ["A", "C", "B"],
            new[] { mid, tweeter, rear }
                .OrderBy(item => VirtualCrossoverJunctions.BandCenterHz(item.Settings))
                .Select(item => item.Name));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    // RHD: group sums arrive in engine ROLES; reading the cabin side would time rears against the opposite front.
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public void IsFarSide_FollowsTheLayoutRatherThanTheCabinSide(
        bool rightSide,
        bool rightHandDrive,
        bool far) =>
        Assert.Equal(far, StagedGroupPlacement.IsFarSide(rightSide, rightHandDrive));

    [Fact]
    public void NormalizeStagedDelays_SlidesEveryChannelAndKeepsEveryRelation()
    {
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

        StagedGroupPlacement.NormalizeStagedDelays([front, sub, rear], alignment, log);

        Assert.Equal(0.0, alignment[front].DelayMs, 6);
        Assert.Equal(2.5, alignment[sub].DelayMs, 6);
        Assert.Equal(15.0, alignment[rear].DelayMs, 6);
        Assert.True(alignment[sub].InvertPolarity);
        // Matched on the word: the log uses the machine culture's decimal separator.
        Assert.Contains("normalization", log.ToString());
        Assert.Contains("shifted", log.ToString());
    }

    [Fact]
    public void NormalizeStagedDelays_MovesTheANCHOR_ThoughItHasNoEntryOfItsOwn()
    {
        // The map is sparse: the engine omits its reference channel, and a shift over the KEYS skipped that anchor.
        var anchor = Block("A", VirtualCrossoverZone.Front);
        var sibling = Block("B", VirtualCrossoverZone.Front);
        var rear = Block("C", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [sibling] = new AlignmentOverride(2.5, false),
            [rear] = new AlignmentOverride(-3.0, false)
        };

        StagedGroupPlacement.NormalizeStagedDelays(
            [anchor, sibling, rear], alignment, new System.Text.StringBuilder());

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
        var anchor = Block("A", VirtualCrossoverZone.Front);
        var rear = Block("B", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [rear] = new AlignmentOverride(12.0, false)
        };

        StagedGroupPlacement.NormalizeStagedDelays(
            [anchor, rear], alignment, new System.Text.StringBuilder());

        Assert.Equal(12.0, alignment[rear].DelayMs, 6);
        Assert.False(alignment.ContainsKey(anchor));
    }

    [Fact]
    public void NormalizeStagedDelays_LeavesADialableSetAlone()
    {
        var front = Block("A", VirtualCrossoverZone.Front);
        var rear = Block("B", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(1.0, false),
            [rear] = new AlignmentOverride(16.0, false)
        };

        StagedGroupPlacement.NormalizeStagedDelays(
            [front, rear], alignment, new System.Text.StringBuilder());

        Assert.Equal(1.0, alignment[front].DelayMs, 6);
        Assert.Equal(16.0, alignment[rear].DelayMs, 6);
    }

    [Fact]
    public void NormalizeStagedDelays_RefusesACeilingBreach_EvenWithoutAShift()
    {
        // The ceiling check must not sit inside the shift branch: a rear fill can overflow with every delay positive.
        var front = Block("A", VirtualCrossoverZone.Front);
        var rear = Block("B", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(0.0, false),
            [rear] = new AlignmentOverride(23.0, false)
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => StagedGroupPlacement.NormalizeStagedDelays(
                [front, rear], alignment, new System.Text.StringBuilder(),
                maxDelayMs: 20.0, rearFillOffsetMs: 15.0, rearFillCarriers: [rear]));

        // Rear = 8 ms physics + 15 ms fill; a 20 ms device holds 12 ms of fill.
        Assert.Contains("does not fit", error.Message);
        Assert.Contains("20 ms", error.Message);
        Assert.Contains("15 ms rear fill", error.Message);
        Assert.Contains("up to 12 ms of fill fits", error.Message);
    }

    [Fact]
    public void NormalizeStagedDelays_FillSuggestion_SurvivesANonMonotoneSpan()
    {
        // The span is not monotone in the fill (co-arrival at -4 ms closes first), so the DSP grid is walked, not solved.
        var front = Block("A", VirtualCrossoverZone.Front);
        var rear = Block("B", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(0.0, false),
            [rear] = new AlignmentOverride(11.0, false)
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => StagedGroupPlacement.NormalizeStagedDelays(
                [front, rear], alignment, new System.Text.StringBuilder(),
                maxDelayMs: 10.0, rearFillOffsetMs: 15.0, rearFillCarriers: [rear]));

        // At 14 ms of fill the rear needs -4 + 14 = 10 ms, exactly the ceiling.
        Assert.Contains("up to 14 ms of fill fits", error.Message);
    }

    [Fact]
    public void NormalizeStagedDelays_WithoutAFillInPlay_RefusesWithoutBlamingIt()
    {
        var front = Block("A", VirtualCrossoverZone.Front);
        var centre = Block("B", VirtualCrossoverZone.Center);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(0.0, false),
            [centre] = new AlignmentOverride(26.0, false)
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => StagedGroupPlacement.NormalizeStagedDelays(
                [front, centre], alignment, new System.Text.StringBuilder(),
                maxDelayMs: 20.0));

        Assert.Contains("does not fit", error.Message);
        Assert.DoesNotContain("rear fill", error.Message);
        Assert.Contains("wider than the DSP can realize", error.Message);
    }

    [Fact]
    public void NormalizeStagedDelays_KeepsAFittingSetUnderATightCeiling()
    {
        var front = Block("A", VirtualCrossoverZone.Front);
        var rear = Block("B", VirtualCrossoverZone.Rear);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [front] = new AlignmentOverride(1.0, false),
            [rear] = new AlignmentOverride(18.0, false)
        };

        StagedGroupPlacement.NormalizeStagedDelays(
            [front, rear], alignment, new System.Text.StringBuilder(),
            maxDelayMs: 18.0, rearFillOffsetMs: 15.0, rearFillCarriers: [rear]);

        Assert.Equal(1.0, alignment[front].DelayMs, 6);
        Assert.Equal(18.0, alignment[rear].DelayMs, 6);
    }

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
        var ir = new System.Numerics.Complex[4_096];
        ir[delaySamples] = 1.0;
        return ir;
    }

    [Theory]
    // A mono side is one instance shared by both cabin lists, which is why both layouts hold.
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Slow")]
    public void ComputeStereoAlignment_WalksTheChain_WithAMonoCentreLeftToItsOwnStage(
        bool rightHandDrive)
    {
        // Field refusal: the engine's guard rejects monos outside its walk, so the mono centre (stage 3) must not be handed over.
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
            VirtualCrossoverAutoDelay.ComputeStereoAlignment(
                chainLeft, chainRight, union, twL, twR,
                bridgeBandLowHz: 2_000, bridgeBandHighHz: 20_000,
                sceneOffsetMs: 0.0, rightHandDrive,
                panel.Session.ProcessorSampleRateHz, panel.Session.ProcessorMaxDelayMs,
                alignment, decisions, log);

            Assert.DoesNotContain(centreSide, alignment.Keys);
            // The map is sparse by contract: exactly one absence (the reference) is legitimate.
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
