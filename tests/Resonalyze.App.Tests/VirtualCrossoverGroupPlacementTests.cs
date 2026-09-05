using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Placing a whole group against a settled reference: the delay that makes the
/// two arrive together, and the relative polarity that falls out of the same
/// measurement.
/// </summary>
public sealed class VirtualCrossoverGroupPlacementTests
{
    private const int Rate = 48_000;

    // A band-limited click: enough of a packet for an arrival read and a
    // correlation peak, with nothing periodic to offer a rival lobe.
    private static Complex[] Packet(double delayMs, double scale = 1.0, int halfWidth = 96)
    {
        var ir = new Complex[16_384];
        int center = 2_048 + (int)Math.Round(delayMs * Rate / 1_000.0);
        // A short raised-cosine burst around 1 kHz: real enough to time, cheap
        // enough to keep the test fast.
        const double ToneHz = 1_000.0;
        for (int i = -halfWidth; i <= halfWidth; i++)
        {
            int index = center + i;
            if (index < 0 || index >= ir.Length)
            {
                continue;
            }

            double window = 0.5 * (1.0 + Math.Cos(Math.PI * i / halfWidth));
            ir[index] += scale * window * Math.Sin(2.0 * Math.PI * ToneHz * i / Rate);
        }

        return ir;
    }

    [Fact]
    public void Place_ReadsBackADelayItWasGiven()
    {
        // The group sits 6 ms behind the reference, so aligning it means taking
        // 6 ms OFF — the delay to add is negative, and the normalization pass is
        // what later turns that into something a processor can dial.
        Complex[] reference = Packet(0);
        Complex[] group = Packet(6.0);

        GroupPlacement? placement = VirtualCrossoverGroupPlacement.Place(
            reference, group, Rate, 500, 2_000);

        Assert.NotNull(placement);
        Assert.InRange(placement.CoArrivalDelayMs, -6.15, -5.85);
        Assert.False(placement.Inverted);
        Assert.InRange(placement.Coefficient, 0.5, 1.0);
    }

    [Fact]
    public void Place_ReadsPolarityFromTheSameMeasurement()
    {
        // Inverting the group must not change WHERE it is, only how it reads.
        // Polarity is a measurement here rather than a guess, which is why the
        // caller may apply it rather than merely suggest it.
        Complex[] reference = Packet(0);
        Complex[] inverted = Packet(4.0, scale: -1.0);

        GroupPlacement? placement = VirtualCrossoverGroupPlacement.Place(
            reference, inverted, Rate, 500, 2_000);

        Assert.NotNull(placement);
        Assert.True(placement.Inverted);
        Assert.InRange(placement.CoArrivalDelayMs, -4.15, -3.85);
    }

    [Fact]
    public void Place_RefusesABandTooNarrowToTimeAnythingIn()
    {
        Complex[] reference = Packet(0);
        Complex[] group = Packet(3.0);

        Assert.Null(VirtualCrossoverGroupPlacement.Place(
            reference, group, Rate, 1_000, 1_050));
    }

    // A packet with a STRONGER copy of itself two periods later: a reflection,
    // or the same program arriving twice. Its envelope onset is the first copy;
    // its whitened correlation against the reference is dominated by the second.
    private static Complex[] PacketWithLaterRival(double delayMs)
    {
        // Half as wide as the default packet, so the two copies sit side by
        // side rather than smearing into one envelope the arrival cannot split.
        const int HalfWidth = 48;
        Complex[] ir = Packet(delayMs, 1.0, HalfWidth);
        Complex[] rival = Packet(delayMs + 2.0, 1.3, HalfWidth);
        for (int i = 0; i < ir.Length; i++)
        {
            ir[i] += rival[i];
        }

        return ir;
    }

    [Theory]
    // A subwoofer band: a quarter period is 4 ms, so the 2 ms cap governs and
    // the width is the one every archived low-frequency placement was made at.
    [InlineData(30, 120, 2.0)]
    // The reference car's centre against the front midrange, and against the
    // whole front stage - 0.19 and 0.09 ms where the old constant read 2.
    [InlineData(400, 4_300, 0.1906)]
    [InlineData(400, 20_000, 0.0884)]
    public void RefineRange_IsTheOneExtremumTheArrivalAnchorSitsIn(
        double lowHz, double highHz, double expectedMs)
    {
        double range = VirtualCrossoverGroupPlacement.RefineRangeMs(lowHz, highHz);

        Assert.Equal(expectedMs, range, 4);
        Assert.True(range <= VirtualCrossoverGroupPlacement.MaximumRefineRangeMs);
        // Correlation extrema alternate every half period, so the whole window
        // must stay inside half a period for the anchor to decide which one is
        // searched. The cap is allowed to be wider - below 125 Hz the arrival
        // estimate's own error is the binding constraint, not the lobe spacing.
        double periodMs = 1_000.0 / Math.Sqrt(lowHz * highHz);
        Assert.True(
            2.0 * range <= periodMs / 2.0 ||
            range == VirtualCrossoverGroupPlacement.MaximumRefineRangeMs);
    }

    [Fact]
    public void Place_StaysOnTheArrivalsOwnLobeWhenAStrongerOneSitsBeside()
    {
        // The decoy is 2 ms (two periods at 1 kHz) behind the true arrival and
        // 2.3 dB louder. Both are inside the 2 ms the refinement used to open,
        // and the louder one wins a whitened correlation - so the group used to
        // be placed two periods late on a rival its own arrival never saw.
        Complex[] reference = Packet(0, halfWidth: 48);
        Complex[] group = PacketWithLaterRival(3.0);

        // The old width, asked directly: it hops.
        CorrelationDelayCandidate wide =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                reference,
                group,
                Rate,
                centerFrequencyHz: 1_000,
                passOctaves: 2.0,
                searchRangeMs: VirtualCrossoverGroupPlacement.MaximumRefineRangeMs,
                centerLagMs: -3.0,
                phaseTransform: true).BestByMagnitude;
        Assert.InRange(wide.DelayMs, -5.15, -4.85);

        // The shipping placement, which sizes its window by the band, does not.
        GroupPlacement? placement = VirtualCrossoverGroupPlacement.Place(
            reference, group, Rate, 500, 2_000);

        Assert.NotNull(placement);
        Assert.InRange(placement.CoArrivalDelayMs, -3.25, -2.75);
    }

    // The shape ChooseReference answers in, written out once.
    private static ((string Name, double LowHz, double HighHz) Channel, double LowHz, double HighHz)?
        Choose((string Name, double LowHz, double HighHz)[] chain, double lowHz, double highHz) =>
        VirtualCrossoverGroupPlacement.ChooseReference(
            chain, item => (item.LowHz, item.HighHz), lowHz, highHz);

    [Fact]
    public void Place_SaysWhenTheAnswerIsTheArrivalRatherThanAnExtremum()
    {
        // A near-equal precursor a millisecond ahead of the body pulls the
        // envelope arrival off the body's own position, and the extremum then
        // sits on the window's edge. That is the narrow window's own failure
        // mode and the price of the fix: where the old flat 2 ms could walk to a
        // better extremum - at the cost of being able to walk to any of eleven -
        // this one cannot leave the arrival's neighbourhood, so a wrong arrival
        // is a wrong placement, bounded by the window rather than by a lobe. The
        // flag is how the caller is told which of the two it got.
        Complex[] reference = Packet(0, halfWidth: 48);
        Complex[] body = Packet(3.0, halfWidth: 48);
        Complex[] precursor = Packet(2.0, scale: 0.9, halfWidth: 48);
        var group = new Complex[body.Length];
        for (int i = 0; i < group.Length; i++)
        {
            group[i] = body[i] + precursor[i];
        }

        GroupPlacement? placement = VirtualCrossoverGroupPlacement.Place(
            reference, group, Rate, 500, 2_000);

        Assert.NotNull(placement);
        Assert.True(placement.EdgePinned);
        // "The arrival stands" is the literal contract, not a figure of speech:
        // the answer is the coarse arrival difference itself, NOT the boundary
        // lag the clamped search returned. Reporting that boundary would put the
        // placement a whole refinement window from the arrival it claims to be -
        // 2 ms of it at a subwoofer band, where the cap governs.
        TimeAlignmentAnalysisResult referenceArrival =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(reference, Rate, 500, 2_000);
        TimeAlignmentAnalysisResult groupArrival =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(group, Rate, 500, 2_000);
        Assert.Equal(
            referenceArrival.FirstArrivalDelayMilliseconds -
                groupArrival.FirstArrivalDelayMilliseconds,
            placement.CoArrivalDelayMs,
            6);
        // And no polarity is claimed from a search that was clamped: false here
        // means "not measured", and the callers must not flip a channel on it.
        Assert.False(placement.Inverted);
    }

    [Fact]
    public void Midpoint_WithholdsConfidenceFromAPinnedReading()
    {
        var near = new GroupPlacement(-5.0, false, 0.8);
        var far = new GroupPlacement(-5.5, false, 0.8, EdgePinned: true);

        (_, _, CentreCorroboration corroboration) =
            VirtualCrossoverGroupPlacement.Midpoint(
                near, far, 0.5, 0.25);

        Assert.False(corroboration.Confident);
        Assert.Contains("pinned to its refinement edge", corroboration.Describe());
        Assert.DoesNotContain("scene offset", corroboration.Describe());
    }

    [Fact]
    public void ChooseReference_PicksTheDriverHoldingTheVoiceBand()
    {
        // The reference car: a centre high-passed at 400 Hz, and a front stage
        // of four. A centre is there for the voice, so it is judged in 1-4 kHz:
        // the midrange carries the whole of that band and the tweeter none of
        // it. Which is also what a tuner does by hand when they mute the rest of
        // the front and match the centre to what is left.
        (string Name, double LowHz, double HighHz)[] chain =
        [
            ("sub", 20, 60),
            ("midbass", 60, 200),
            ("mid", 200, 4_300),
            ("tweeter", 4_300, 20_000)
        ];

        ((string Name, double LowHz, double HighHz) Channel, double LowHz, double HighHz)? pick =
            Choose(chain, 400, 20_000);

        Assert.NotNull(pick);
        Assert.Equal("mid", pick.Value.Channel.Name);
        Assert.Equal(400, pick.Value.LowHz);
        Assert.Equal(4_300, pick.Value.HighHz);
    }

    [Fact]
    public void ChooseReference_DoesNotHandAnOrdinaryCentreToTheTweeter()
    {
        // The rule that looks obvious - widest overlap - fails here, and this is
        // an ordinary 3-way front, not a corner case. A centre high-passed at
        // 300 Hz overlaps this tweeter by 2.74 octaves and this midrange by 2.58,
        // because a channel with only a high-pass is booked to 20 kHz whether it
        // plays there or not. The voice band is what separates them: the midrange
        // holds 1-3 kHz of it, the tweeter only 3-4 kHz - and a voice does almost
        // nothing above 4.
        (string Name, double LowHz, double HighHz)[] chain =
        [
            ("sub", 20, 60),
            ("midbass", 60, 500),
            ("mid", 500, 3_000),
            ("tweeter", 3_000, 20_000)
        ];

        ((string Name, double LowHz, double HighHz) Channel, double LowHz, double HighHz)? pick =
            Choose(chain, 300, 20_000);

        Assert.NotNull(pick);
        Assert.Equal("mid", pick.Value.Channel.Name);
    }

    [Fact]
    public void ChooseReference_FollowsTheVoiceBandUpWhenTheCrossoverIsLow()
    {
        // The same rule the other way: crossed at 1 kHz it is the TWEETER that
        // carries the voice, and the reference follows it there rather than
        // preferring a midrange on principle.
        (string Name, double LowHz, double HighHz)[] chain =
        [
            ("mid", 200, 1_000),
            ("tweeter", 1_000, 20_000)
        ];

        ((string Name, double LowHz, double HighHz) Channel, double LowHz, double HighHz)? pick =
            Choose(chain, 300, 20_000);

        Assert.NotNull(pick);
        Assert.Equal("tweeter", pick.Value.Channel.Name);
    }

    [Fact]
    public void ChooseReference_BreaksATieTowardTheLowerBand()
    {
        // Crossed at 2 kHz the two split the voice band evenly, so the tie falls
        // to the lower one: its period is longer, and the envelope arrival, whose
        // error is a roughly fixed number of milliseconds, lands a smaller
        // fraction of a period from the extremum it has to select.
        (string Name, double LowHz, double HighHz)[] chain =
        [
            ("upper", 2_000, 20_000),
            ("lower", 200, 2_000)
        ];

        ((string Name, double LowHz, double HighHz) Channel, double LowHz, double HighHz)? pick =
            Choose(chain, 200, 20_000);

        Assert.NotNull(pick);
        Assert.Equal("lower", pick.Value.Channel.Name);
    }

    [Fact]
    public void ChooseReference_FallsBackToTheLowestOverlapWhenNoneReachesTheVoiceBand()
    {
        // A group that never reaches 1 kHz has no voice band to judge in, so the
        // tie-break carries the whole decision - and it is the same one.
        (string Name, double LowHz, double HighHz)[] chain =
        [
            ("sub", 20, 60),
            ("midbass", 60, 200)
        ];

        ((string Name, double LowHz, double HighHz) Channel, double LowHz, double HighHz)? pick =
            Choose(chain, 20, 90);

        Assert.NotNull(pick);
        Assert.Equal("sub", pick.Value.Channel.Name);
    }

    [Fact]
    public void ChooseReference_AnswersNothingWhenNoDriverOverlapsTheGroup()
    {
        // The caller falls back to the whole stage summed rather than refusing:
        // a sum always overlaps, and a wide reading is better than none.
        (string Name, double LowHz, double HighHz)[] chain = [("tweeter", 4_300, 20_000)];

        Assert.Null(VirtualCrossoverGroupPlacement.ChooseReference(
            chain, item => (item.LowHz, item.HighHz), 20, 60));
    }

    // A stereo front chain as the centre stage sees it: the same block appears
    // once per side, and a mono block appears as ONE instance in both lists.
    private sealed record Side(string Block, string Name, double LowHz, double HighHz);

    private static CentreReferenceChoice<Side> ChooseCentre(
        IReadOnlyCollection<Side> near,
        IReadOnlyCollection<Side> far,
        double lowHz,
        double highHz) =>
        VirtualCrossoverGroupPlacement.ChooseCentreReferences(
            near,
            far,
            item => (item.LowHz, item.HighHz),
            (a, b) => a.Block == b.Block && !ReferenceEquals(a, b),
            lowHz,
            highHz);

    [Fact]
    public void ChooseCentreReferences_TakesTheTwoSidesOfOneBlockOverOneBand()
    {
        var nearMid = new Side("mid", "mid R", 200, 4_300);
        var farMid = new Side("mid", "mid L", 200, 3_000);
        Side[] near = [nearMid, new Side("tweeter", "tweeter R", 4_300, 20_000)];
        Side[] far = [farMid, new Side("tweeter", "tweeter L", 3_000, 20_000)];

        CentreReferencePlan<Side>? plan = ChooseCentre(near, far, 400, 20_000).Plan;

        Assert.NotNull(plan);
        Assert.True(plan.Peers);
        Assert.Equal("mid R", Assert.Single(plan.Near).Name);
        Assert.Equal("mid L", Assert.Single(plan.Far).Name);
        // One band for both readings: the intersection of the two picks.
        Assert.Equal(400, plan.LowHz);
        Assert.Equal(3_000, plan.HighHz);
    }

    [Fact]
    public void ChooseCentreReferences_FallsBackToOwnContentWhenTheSidesPickDifferentDrivers()
    {
        // The far side is crossed low enough that its tweeter holds the image
        // band while the near side's midrange does. Reading against one and
        // against the other is not a midpoint, and the two cannot witness each
        // other - so the pair is dropped, and each side is summed from what it
        // plays that the other does not.
        Side[] near = [new Side("mid", "mid R", 200, 4_300), new Side("tweeter", "tweeter R", 4_300, 20_000)];
        Side[] far = [new Side("mid", "mid L", 200, 1_000), new Side("tweeter", "tweeter L", 1_000, 20_000)];

        CentreReferencePlan<Side>? plan = ChooseCentre(near, far, 400, 20_000).Plan;

        Assert.NotNull(plan);
        Assert.False(plan.Peers);
        Assert.Equal(["mid R", "tweeter R"], plan.Near.Select(item => item.Name));
        Assert.Equal(["mid L", "tweeter L"], plan.Far.Select(item => item.Name));
        // Both sides cover the whole range between them, so the band is the
        // centre's own.
        Assert.Equal(400, plan.LowHz);
        Assert.Equal(20_000, plan.HighHz);
    }

    [Fact]
    public void ChooseCentreReferences_TimesInTheBandTheOWNContentShares()
    {
        // The band cannot come from what the sides were BEFORE the shared block
        // was taken out of them. Here it would be the mono block's whole
        // 20 Hz-20 kHz, while what is actually being compared is a 60-200 Hz
        // midbass against a 4-20 kHz tweeter: two readings "in the same band"
        // with no content in common anywhere, corroborating each other because
        // nothing downstream asks where the band came from.
        var mono = new Side("wideband", "wideband (mono)", 20, 20_000);
        Side[] near = [mono, new Side("midbass", "midbass R", 60, 200)];
        Side[] far = [mono, new Side("tweeter", "tweeter L", 4_000, 20_000)];

        CentreReferenceChoice<Side> choice = ChooseCentre(near, far, 20, 20_000);

        Assert.Null(choice.Plan);
        Assert.Contains("share no band of their own", choice.Refusal);
        Assert.Contains("60-200 Hz", choice.Refusal);
        Assert.Contains("4000-20000 Hz", choice.Refusal);
    }

    [Fact]
    public void ChooseCentreReferences_WillNotPutTheBandInAGapBetweenDrivers()
    {
        // The near side is left with a midbass and a tweeter and NOTHING between
        // them. Taking its range as the span of the two claims content across a
        // hole two crossovers wide, and the far side's midrange sits exactly in
        // that hole - so a band chosen from spans would have both sides "playing"
        // 500-3000 Hz where one of them plays only filter leakage.
        var mono = new Side("wideband", "wideband (mono)", 20, 20_000);
        Side[] near =
        [
            mono,
            new Side("midbass", "midbass R", 60, 200),
            new Side("tweeter", "tweeter R", 4_000, 20_000)
        ];
        Side[] far = [mono, new Side("mid", "mid L", 500, 3_000)];

        CentreReferenceChoice<Side> choice = ChooseCentre(near, far, 20, 20_000);

        Assert.Null(choice.Plan);
        Assert.Contains("share no band of their own", choice.Refusal);
        // The refusal shows the intervals, not the span that filled the gap.
        Assert.Contains("60-200 Hz and 4000-20000 Hz", choice.Refusal);
    }

    [Fact]
    public void ChooseCentreReferences_ReadsStraightThroughASidesOwnCrossover()
    {
        // The other half of the same rule: a side's own two drivers meeting at a
        // corner cover straight through it, so the band is the 400-4300 Hz they
        // hold between them - not the 1000-4300 the upper one holds alone, which
        // is what treating each driver separately would leave.
        var mono = new Side("sub", "sub (mono)", 20, 20_000);
        Side[] near =
        [
            mono,
            new Side("mid", "mid R", 200, 1_000),
            new Side("tweeter", "tweeter R", 1_000, 4_300)
        ];
        Side[] far = [mono, new Side("wide", "wide L", 300, 5_000)];

        CentreReferencePlan<Side>? plan = ChooseCentre(near, far, 400, 20_000).Plan;

        Assert.NotNull(plan);
        Assert.Equal(400, plan.LowHz);
        Assert.Equal(4_300, plan.HighHz);
        Assert.Equal(["mid R", "tweeter R"], plan.Near.Select(item => item.Name));
    }

    [Fact]
    public void ChooseCentreReferences_KeepsOnlyTheDriversThatPlayInTheChosenBand()
    {
        // A name in the trace beside a number has to have contributed to it.
        var mono = new Side("sub", "sub (mono)", 20, 20_000);
        Side[] near = [mono, new Side("midbass", "midbass R", 60, 250), new Side("mid", "mid R", 250, 4_300)];
        Side[] far = [mono, new Side("mid", "mid L", 1_000, 4_300)];

        CentreReferencePlan<Side>? plan = ChooseCentre(near, far, 400, 20_000).Plan;

        Assert.NotNull(plan);
        Assert.Equal(1_000, plan.LowHz);
        Assert.Equal(4_300, plan.HighHz);
        Assert.Equal("mid R", Assert.Single(plan.Near).Name);
    }

    [Fact]
    public void ChooseCentreReferences_NarrowsTheBandToWhatBothSidesOwn()
    {
        // The same rule where it still yields a plan: the shared sub is out of
        // both references, so the band is what the midrange and the tweeter left
        // behind share - not the 400 Hz-20 kHz the whole stages did.
        var mono = new Side("sub", "sub (mono)", 20, 20_000);
        Side[] near = [mono, new Side("mid", "mid R", 200, 4_300)];
        Side[] far = [mono, new Side("tweeter", "tweeter L", 1_000, 20_000)];

        CentreReferencePlan<Side>? plan = ChooseCentre(near, far, 400, 20_000).Plan;

        Assert.NotNull(plan);
        Assert.False(plan.Peers);
        Assert.Equal(1_000, plan.LowHz);
        Assert.Equal(4_300, plan.HighHz);
    }

    [Fact]
    public void ChooseCentreReferences_RefusesOneMonoBlockAnsweringForBothSides()
    {
        // A mono block is ONE instance in both lists, so picking it on both sides
        // is one measurement counted twice: the two readings would be identical
        // and the scene-offset witness would pass on a difference of exactly
        // zero. There is no wider reference to fall back to either - the summed
        // stages ARE that one response - so the answer is no plan at all, and the
        // caller must refuse rather than place.
        var mono = new Side("wideband", "wideband (mono)", 200, 20_000);
        Side[] near = [mono];
        Side[] far = [mono];

        CentreReferenceChoice<Side> choice = ChooseCentre(near, far, 400, 20_000);

        Assert.Null(choice.Plan);
        // And it says THIS is why - not that the two sides' own content lies in
        // different places, which would send the tuner to the crossovers.
        Assert.Contains("no content of their own", choice.Refusal);
        Assert.DoesNotContain("share no band", choice.Refusal);
    }

    [Fact]
    public void ChooseCentreReferences_RefusesWhenOnlyOneSideHasContentOfItsOwn()
    {
        // The near side plays nothing here but the shared block; the far side has
        // a midrange of its own. One reading is not a midpoint, and pairing it
        // with a reference that contains the near side's only content would be
        // comparing that content with itself.
        var mono = new Side("wideband", "wideband (mono)", 200, 20_000);
        Side[] near = [mono];
        Side[] far = [mono, new Side("mid", "mid L", 200, 4_300)];

        CentreReferenceChoice<Side> choice = ChooseCentre(near, far, 400, 20_000);

        Assert.Null(choice.Plan);
        Assert.Contains("no content of their own", choice.Refusal);
    }

    [Fact]
    public void ChooseCentreReferences_KeepsTheSharedBlockOutOfBothFallbackReferences()
    {
        // The plan is what the caller sums, so this is the test that the shared
        // response cannot reach either reference - the panel has no opportunity
        // to put it back.
        var mono = new Side("sub", "sub (mono)", 20, 20_000);
        var nearMid = new Side("mid", "mid R", 200, 4_300);
        var farTweeter = new Side("tweeter", "tweeter L", 1_000, 20_000);
        Side[] near = [mono, nearMid];
        Side[] far = [mono, farTweeter];

        CentreReferencePlan<Side>? plan = ChooseCentre(near, far, 400, 20_000).Plan;

        Assert.NotNull(plan);
        Assert.False(plan.Peers);
        Assert.DoesNotContain(mono, plan.Near);
        Assert.DoesNotContain(mono, plan.Far);
    }

    [Fact]
    public void ChooseCentreReferences_RefusesWhenTheTwoPicksBarelyOverlap()
    {
        // Same block on both sides, but corners far enough apart that what they
        // share cannot be timed in. The fallback does not rescue it: these two
        // ARE the sides' own content, so the band it derives is the same sliver
        // the pair was rejected for.
        Side[] near = [new Side("mid", "mid R", 200, 4_300)];
        Side[] far = [new Side("mid", "mid L", 3_900, 20_000)];

        CentreReferenceChoice<Side> choice = ChooseCentre(near, far, 400, 20_000);

        Assert.Null(choice.Plan);
        Assert.Contains("share no band of their own", choice.Refusal);
    }

    [Fact]
    public void Midpoint_PutsTheCentreBetweenTheTwoSides()
    {
        // The centre reads 5.00 ms against one side and 5.50 against the other,
        // which is exactly the 0.5 ms the sides themselves are apart. It belongs
        // in the middle, and the two readings corroborate each other.
        var near = new GroupPlacement(-5.00, false, 0.8);
        var far = new GroupPlacement(-5.50, false, 0.8);

        (double delayMs, bool inverted, CentreCorroboration corroboration) =
            VirtualCrossoverGroupPlacement.Midpoint(
                near, far, 0.5, 0.25);

        Assert.Equal(-5.25, delayMs, 3);
        Assert.False(inverted);
        Assert.True(corroboration.Confident);
        Assert.Equal("the two sides corroborate each other", corroboration.Describe());
    }

    [Fact]
    public void Midpoint_StillPlacesTheCentreWhenTheSidesDisagreeButSaysSoInstead()
    {
        // The sides are 3 ms apart where the scene offset says 0.5: one of the
        // two readings landed on the wrong lobe. The midpoint is still the best
        // available answer, so it is returned — but the run must not present it
        // with the confidence of one the sides corroborated.
        var near = new GroupPlacement(-5.0, false, 0.8);
        var far = new GroupPlacement(-8.0, false, 0.8);

        (double delayMs, _, CentreCorroboration corroboration) =
            VirtualCrossoverGroupPlacement.Midpoint(
                near, far, 0.5, 0.25);

        Assert.Equal(-6.5, delayMs, 3);
        Assert.False(corroboration.Confident);
        // And the note says THIS is what failed, not one of the other four.
        Assert.Contains("DISAGREE by more than the scene offset", corroboration.Describe());
        Assert.DoesNotContain("too weak", corroboration.Describe());
        Assert.DoesNotContain("pinned", corroboration.Describe());
    }

    [Fact]
    public void Midpoint_WillNotFlipPolarityOnHalfAMeasurement()
    {
        // A centre reading inverted against one side and normal against the
        // other is not a centre wired backwards; it is a measurement that has
        // not settled. Flipping on that would be a coin toss dressed as a
        // reading, so the polarity stays and the confidence goes.
        var near = new GroupPlacement(-5.0, true, 0.8);
        var far = new GroupPlacement(-5.5, false, 0.8);

        (_, bool inverted, CentreCorroboration corroboration) =
            VirtualCrossoverGroupPlacement.Midpoint(
                near, far, 0.5, 0.25);

        Assert.False(inverted);
        Assert.False(corroboration.Confident);
        Assert.Contains("OPPOSITE polarities", corroboration.Describe());
        Assert.DoesNotContain("scene offset", corroboration.Describe());
    }

    [Fact]
    public void Midpoint_WithholdsConfidenceFromAWeakReading()
    {
        var near = new GroupPlacement(-5.0, false, 0.8);
        var far = new GroupPlacement(-5.5, false, 0.05);

        (_, _, CentreCorroboration corroboration) =
            VirtualCrossoverGroupPlacement.Midpoint(
                near, far, 0.5, 0.25);

        Assert.False(corroboration.Confident);
        Assert.Contains("too weak", corroboration.Describe());
        Assert.DoesNotContain("scene offset", corroboration.Describe());
    }

    [Fact]
    public void Midpoint_NamesEveryReasonWhenMoreThanOneFails()
    {
        // Two tests fail at once. Showing one would be the same guess the whole
        // report exists to remove - and the one a reader acts on first is
        // whichever the code happened to check first.
        var near = new GroupPlacement(-5.0, false, 0.05);
        var far = new GroupPlacement(-8.0, false, 0.05);

        (_, _, CentreCorroboration corroboration) =
            VirtualCrossoverGroupPlacement.Midpoint(
                near, far, 0.5, 0.25);

        Assert.Contains("DISAGREE by more than the scene offset", corroboration.Describe());
        Assert.Contains("too weak", corroboration.Describe());
    }

    [Fact]
    public void OwnContent_LeavesOutWhatBothSidesShare()
    {
        // What the fallback reference is summed from. The shared mono block is
        // removed from BOTH sides rather than left in and reasoned about: a
        // response present in both references cannot tell them apart, however
        // loud it is, and how loud it is was never something a declared-corner
        // test could see.
        var mono = new Side("wideband", "wideband (mono)", 200, 20_000);
        var nearMid = new Side("mid", "mid R", 200, 4_300);
        var farMid = new Side("mid", "mid L", 200, 4_300);
        Side[] near = [mono, nearMid];
        Side[] far = [mono, farMid];

        Assert.Equal(
            [nearMid],
            VirtualCrossoverGroupPlacement.OwnContent(
                near, far, item => (item.LowHz, item.HighHz), 400, 4_000));
        Assert.Equal(
            [farMid],
            VirtualCrossoverGroupPlacement.OwnContent(
                far, near, item => (item.LowHz, item.HighHz), 400, 4_000));
    }

    [Fact]
    public void OwnContent_IsEmptyWhenASidePlaysNothingOfItsOwnInTheBand()
    {
        // Nothing of its own is nothing to sum, and that is what makes the plan
        // refuse rather than widen: whatever this side plays here, the other side
        // plays from the same response.
        var mono = new Side("wideband", "wideband (mono)", 200, 20_000);
        Side[] near = [mono, new Side("sub", "sub R", 20, 60)];
        Side[] far = [mono, new Side("mid", "mid L", 200, 4_300)];

        Assert.Empty(VirtualCrossoverGroupPlacement.OwnContent(
            near, far, item => (item.LowHz, item.HighHz), 400, 4_000));
        Assert.Null(ChooseCentre(near, far, 400, 4_000).Plan);
    }

    [Fact]
    public void OwnContent_CountsOnlyContentInsideThePlacementBand()
    {
        // The sub is shared; the two midranges are the sides' own. In the
        // centre's band that leaves each side something - and below it, where
        // only the shared sub plays, it leaves neither anything.
        var sub = new Side("sub", "sub (mono)", 20, 60);
        Side[] near = [sub, new Side("mid", "mid R", 200, 4_300)];
        Side[] far = [sub, new Side("mid", "mid L", 200, 4_300)];

        Assert.Single(VirtualCrossoverGroupPlacement.OwnContent(
            near, far, item => (item.LowHz, item.HighHz), 400, 4_000));
        Assert.Empty(VirtualCrossoverGroupPlacement.OwnContent(
            near, far, item => (item.LowHz, item.HighHz), 20, 60));
    }
}
