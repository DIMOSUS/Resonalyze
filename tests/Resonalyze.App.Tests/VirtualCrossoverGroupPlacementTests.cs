using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverGroupPlacementTests
{
    private const int Rate = 48_000;

    private static Complex[] Packet(double delayMs, double scale = 1.0, int halfWidth = 96)
    {
        var ir = new Complex[16_384];
        int center = 2_048 + (int)Math.Round(delayMs * Rate / 1_000.0);
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
        // 6 ms late, so the delay to add is negative; normalization later makes it dialable.
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

    // A stronger copy two periods later: envelope onset is the first copy, whitened correlation the second.
    private static Complex[] PacketWithLaterRival(double delayMs)
    {
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
    // Sub band: a quarter period is 4 ms, so the 2 ms cap governs.
    [InlineData(30, 120, 2.0)]
    [InlineData(400, 4_300, 0.1906)]
    [InlineData(400, 20_000, 0.0884)]
    public void RefineRange_IsTheOneExtremumTheArrivalAnchorSitsIn(
        double lowHz, double highHz, double expectedMs)
    {
        double range = VirtualCrossoverGroupPlacement.RefineRangeMs(lowHz, highHz);

        Assert.Equal(expectedMs, range, 4);
        Assert.True(range <= VirtualCrossoverGroupPlacement.MaximumRefineRangeMs);
        // Correlation extrema alternate every half period, so the window must stay inside half a period.
        double periodMs = 1_000.0 / Math.Sqrt(lowHz * highHz);
        Assert.True(
            2.0 * range <= periodMs / 2.0 ||
            range == VirtualCrossoverGroupPlacement.MaximumRefineRangeMs);
    }

    [Fact]
    public void Place_StaysOnTheArrivalsOwnLobeWhenAStrongerOneSitsBeside()
    {
        // Decoy 2 ms (two periods) late and 2.3 dB louder: a flat 2 ms refinement would place the group on it.
        Complex[] reference = Packet(0, halfWidth: 48);
        Complex[] group = PacketWithLaterRival(3.0);

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

        GroupPlacement? placement = VirtualCrossoverGroupPlacement.Place(
            reference, group, Rate, 500, 2_000);

        Assert.NotNull(placement);
        Assert.InRange(placement.CoArrivalDelayMs, -3.25, -2.75);
    }

    private static ((string Name, double LowHz, double HighHz) Channel, double LowHz, double HighHz)?
        Choose((string Name, double LowHz, double HighHz)[] chain, double lowHz, double highHz) =>
        VirtualCrossoverGroupPlacement.ChooseReference(
            chain, item => (item.LowHz, item.HighHz), lowHz, highHz);

    [Fact]
    public void Place_SaysWhenTheAnswerIsTheArrivalRatherThanAnExtremum()
    {
        // A near-equal precursor pulls the arrival off the body; the narrow window cannot leave its neighbourhood and flags the clamp.
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
        // A clamped search reports the coarse arrival difference itself, not the boundary lag.
        TimeAlignmentAnalysisResult referenceArrival =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(reference, Rate, 500, 2_000);
        TimeAlignmentAnalysisResult groupArrival =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(group, Rate, 500, 2_000);
        Assert.Equal(
            referenceArrival.FirstArrivalDelayMilliseconds -
                groupArrival.FirstArrivalDelayMilliseconds,
            placement.CoArrivalDelayMs,
            6);
        // False here means "not measured": callers must not flip on it.
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
        // A centre is judged in the 1-4 kHz voice band, where the midrange plays all of it.
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
        // Widest overlap fails: a high-pass-only tweeter is booked to 20 kHz and overlaps a 300 Hz centre more than the midrange.
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
        // A voice-band tie goes to the lower driver: fixed-ms arrival error is a smaller fraction of its period.
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
        (string Name, double LowHz, double HighHz)[] chain = [("tweeter", 4_300, 20_000)];

        Assert.Null(VirtualCrossoverGroupPlacement.ChooseReference(
            chain, item => (item.LowHz, item.HighHz), 20, 60));
    }

    // A mono block is ONE instance appearing in both lists.
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
        Assert.Equal(400, plan.LowHz);
        Assert.Equal(3_000, plan.HighHz);
    }

    [Fact]
    public void ChooseCentreReferences_FallsBackToOwnContentWhenTheSidesPickDifferentDrivers()
    {
        // Far tweeter vs near midrange hold the image band: readings cannot witness each other, so the pair is dropped.
        Side[] near = [new Side("mid", "mid R", 200, 4_300), new Side("tweeter", "tweeter R", 4_300, 20_000)];
        Side[] far = [new Side("mid", "mid L", 200, 1_000), new Side("tweeter", "tweeter L", 1_000, 20_000)];

        CentreReferencePlan<Side>? plan = ChooseCentre(near, far, 400, 20_000).Plan;

        Assert.NotNull(plan);
        Assert.False(plan.Peers);
        Assert.Equal(["mid R", "tweeter R"], plan.Near.Select(item => item.Name));
        Assert.Equal(["mid L", "tweeter L"], plan.Far.Select(item => item.Name));
        Assert.Equal(400, plan.LowHz);
        Assert.Equal(20_000, plan.HighHz);
    }

    [Fact]
    public void ChooseCentreReferences_TimesInTheBandTheOWNContentShares()
    {
        // The band must come from sides AFTER the shared block is removed, or disjoint drivers look like one band.
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
        // A span over a hole two crossovers wide would claim content where one side plays only leakage.
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
        Assert.Contains("60-200 Hz and 4000-20000 Hz", choice.Refusal);
    }

    [Fact]
    public void ChooseCentreReferences_ReadsStraightThroughASidesOwnCrossover()
    {
        // A side's own two drivers meeting at a corner cover straight through it.
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
        // The same mono block on both sides would witness itself with a difference of exactly zero; no plan.
        var mono = new Side("wideband", "wideband (mono)", 200, 20_000);
        Side[] near = [mono];
        Side[] far = [mono];

        CentreReferenceChoice<Side> choice = ChooseCentre(near, far, 400, 20_000);

        Assert.Null(choice.Plan);
        Assert.Contains("no content of their own", choice.Refusal);
        Assert.DoesNotContain("share no band", choice.Refusal);
    }

    [Fact]
    public void ChooseCentreReferences_RefusesWhenOnlyOneSideHasContentOfItsOwn()
    {
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
        Side[] near = [new Side("mid", "mid R", 200, 4_300)];
        Side[] far = [new Side("mid", "mid L", 3_900, 20_000)];

        CentreReferenceChoice<Side> choice = ChooseCentre(near, far, 400, 20_000);

        Assert.Null(choice.Plan);
        Assert.Contains("share no band of their own", choice.Refusal);
    }

    [Fact]
    public void Midpoint_PutsTheCentreBetweenTheTwoSides()
    {
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
        // Sides 3 ms apart where the scene offset says 0.5: one reading is on the wrong lobe; midpoint returned without confidence.
        var near = new GroupPlacement(-5.0, false, 0.8);
        var far = new GroupPlacement(-8.0, false, 0.8);

        (double delayMs, _, CentreCorroboration corroboration) =
            VirtualCrossoverGroupPlacement.Midpoint(
                near, far, 0.5, 0.25);

        Assert.Equal(-6.5, delayMs, 3);
        Assert.False(corroboration.Confident);
        Assert.Contains("DISAGREE by more than the scene offset", corroboration.Describe());
        Assert.DoesNotContain("too weak", corroboration.Describe());
        Assert.DoesNotContain("pinned", corroboration.Describe());
    }

    [Fact]
    public void Midpoint_WillNotFlipPolarityOnHalfAMeasurement()
    {
        // Inverted against one side and normal against the other is unsettled, not miswired: polarity stays, confidence goes.
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
        // The shared block is removed from both references: a response in both cannot tell them apart.
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
        var sub = new Side("sub", "sub (mono)", 20, 60);
        Side[] near = [sub, new Side("mid", "mid R", 200, 4_300)];
        Side[] far = [sub, new Side("mid", "mid L", 200, 4_300)];

        Assert.Single(VirtualCrossoverGroupPlacement.OwnContent(
            near, far, item => (item.LowHz, item.HighHz), 400, 4_000));
        Assert.Empty(VirtualCrossoverGroupPlacement.OwnContent(
            near, far, item => (item.LowHz, item.HighHz), 20, 60));
    }
}
