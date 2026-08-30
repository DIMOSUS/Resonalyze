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

        VirtualCrossoverPanel.NormalizeStagedDelays(alignment, log);

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

        VirtualCrossoverPanel.NormalizeStagedDelays(alignment, new System.Text.StringBuilder());

        Assert.Equal(1.0, alignment[front].DelayMs, 6);
        Assert.Equal(16.0, alignment[rear].DelayMs, 6);
    }
}
