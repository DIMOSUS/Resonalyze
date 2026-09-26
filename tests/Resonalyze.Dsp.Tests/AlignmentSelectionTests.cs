namespace Resonalyze.Dsp.Tests;

public sealed class AlignmentSelectionTests
{
    // Scores through LossDb so tests control the prior-free figure (the engine passes AcousticScore the same way).
    private static double Score(AlignmentCandidate candidate) => candidate.LossDb;

    [Fact]
    public void Select_TheDelayTieMarginIsMeasuredFromTheBest_NotFromThePickWithinIt()
    {
        var top = new AlignmentCandidate(3.0, false, 0.00);
        var tie = new AlignmentCandidate(1.0, false, -0.08);
        var beyond = new AlignmentCandidate(0.1, false, -0.17);

        Assert.Equal(tie, AlignmentSelection.Select([top, tie, beyond], baseDeltaMs: 0.0));
    }

    [Fact]
    public void PreferSubLeading_TrailingPickYieldsToLeadingLobeWithinMargin()
    {
        // 80 Hz sub junction, leadSign +1 (larger delay = sub leads): the leading lobe is 0.7 dB down, inside the precedence margin.
        var trailing = new AlignmentCandidate(0.1, false, -0.8, LossDb: -0.7);
        var leading = new AlignmentCandidate(5.8, true, -1.8, LossDb: -1.4);

        AlignmentCandidate chosen = AlignmentSelection.PreferSubLeading(
            [trailing, leading], trailing, Score,
            anchorMs: 1.9, leadSign: 1.0,
            marginDb: 1.0, slackMs: 0.5, reachMs: 12.5);

        Assert.Equal(leading, chosen);
    }

    [Fact]
    public void PreferSubLeading_KeepsTheTrailingPickBeyondTheMargin()
    {
        var trailing = new AlignmentCandidate(0.1, false, -0.8, LossDb: -0.7);
        var leading = new AlignmentCandidate(5.8, true, -1.8, LossDb: -2.0);

        AlignmentCandidate chosen = AlignmentSelection.PreferSubLeading(
            [trailing, leading], trailing, Score,
            anchorMs: 1.9, leadSign: 1.0,
            marginDb: 1.0, slackMs: 0.5, reachMs: 12.5);

        Assert.Equal(trailing, chosen);
    }

    [Fact]
    public void PreferSubLeading_InertWhenTheChosenAlreadyLeads()
    {
        var chosenLead = new AlignmentCandidate(2.1, false, -0.8, LossDb: -0.9);
        var deeperLead = new AlignmentCandidate(4.5, false, -0.7, LossDb: -0.5);

        AlignmentCandidate chosen = AlignmentSelection.PreferSubLeading(
            [chosenLead, deeperLead], chosenLead, Score,
            anchorMs: 1.9, leadSign: 1.0,
            marginDb: 1.0, slackMs: 0.5, reachMs: 12.5);

        Assert.Equal(chosenLead, chosen);
    }

    [Fact]
    public void PreferSubLeading_IgnoresLeadsBeyondTheReach()
    {
        var trailing = new AlignmentCandidate(0.1, false, -0.8, LossDb: -0.7);
        var farLead = new AlignmentCandidate(16.0, false, -1.0, LossDb: -0.8);

        AlignmentCandidate chosen = AlignmentSelection.PreferSubLeading(
            [trailing, farLead], trailing, Score,
            anchorMs: 1.9, leadSign: 1.0,
            marginDb: 1.0, slackMs: 0.5, reachMs: 12.5);

        Assert.Equal(trailing, chosen);
    }

    [Fact]
    public void PreferSubLeading_LeadSignFlipsWhenTheSubItselfIsSearched()
    {
        // Sub searched (leadSign -1): the sub leads where its own delay is smaller than the anchor.
        var trailing = new AlignmentCandidate(3.6, false, -0.8, LossDb: -0.7);
        var leading = new AlignmentCandidate(-2.1, true, -1.8, LossDb: -1.2);

        AlignmentCandidate chosen = AlignmentSelection.PreferSubLeading(
            [trailing, leading], trailing, Score,
            anchorMs: 1.9, leadSign: -1.0,
            marginDb: 1.0, slackMs: 0.5, reachMs: 12.5);

        Assert.Equal(leading, chosen);
    }

    [Fact]
    public void Select_SingleCandidateIsReturned()
    {
        var candidate = new AlignmentCandidate(1.5, false, -0.4);

        AlignmentCandidate chosen = AlignmentSelection.Select([candidate], 0.0);

        Assert.Equal(candidate, chosen);
    }

    [Fact]
    public void Select_RejectsEmptyCandidateList()
    {
        Assert.Throws<ArgumentException>(() =>
            AlignmentSelection.Select(Array.Empty<AlignmentCandidate>(), 0.0));
    }

    [Fact]
    public void Select_JudgesPolarityPurityAgainstTheNeighbor()
    {
        // Inverted settled neighbour: the pure pair is the equally-inverted candidate, not the absolute-flag 'rescue'.
        var mixedNormal = new AlignmentCandidate(1.6, false, -0.50);
        var pureInverted = new AlignmentCandidate(2.0, true, -0.60);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [mixedNormal, pureInverted], baseDeltaMs: 2.0,
            neighborInverted: true);

        Assert.Equal(pureInverted, chosen);
    }

    [Fact]
    public void Select_PrefersNonInvertedWithinMargin()
    {
        var inverted = new AlignmentCandidate(2.0, true, -0.50);
        var normal = new AlignmentCandidate(1.4, false, -0.65);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [inverted, normal], baseDeltaMs: 1.4);

        Assert.Equal(normal, chosen);
    }

    [Fact]
    public void Select_KeepsInvertedWinnerBeyondMargin()
    {
        var inverted = new AlignmentCandidate(2.0, true, -0.50);
        var normal = new AlignmentCandidate(1.4, false, -1.10);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [inverted, normal], baseDeltaMs: 1.4);

        Assert.Equal(inverted, chosen);
    }

    [Fact]
    public void Select_BreaksNearTiesByClosenessToBase()
    {
        // Within the tie margin the candidate closer to the arrival-based delta wins despite a lower score.
        var farBetter = new AlignmentCandidate(3.0, false, -0.50);
        var nearSlightlyWorse = new AlignmentCandidate(1.1, false, -0.55);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [farBetter, nearSlightlyWorse], baseDeltaMs: 1.0);

        Assert.Equal(nearSlightlyWorse, chosen);
    }

    [Fact]
    public void Select_DoesNotTieBreakAcrossTheMargin()
    {
        var farBetter = new AlignmentCandidate(3.0, false, -0.50);
        var nearMuchWorse = new AlignmentCandidate(1.1, false, -0.75);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [farBetter, nearMuchWorse], baseDeltaMs: 1.0);

        Assert.Equal(farBetter, chosen);
    }

    [Fact]
    public void Select_TieBreakStaysWithinChosenPolarity()
    {
        // The inverted winner's delay tie-break excludes non-inverted candidates beyond the invert margin.
        var inverted = new AlignmentCandidate(2.0, true, -0.50);
        var invertedFar = new AlignmentCandidate(4.0, true, -0.58);
        var normal = new AlignmentCandidate(1.0, false, -1.10);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [inverted, invertedFar, normal], baseDeltaMs: 3.8);

        Assert.Equal(invertedFar, chosen);
    }

    [Fact]
    public void Select_BreaksNearTiesTowardTheArrivalAcrossPolarities()
    {
        // 80 Hz latch: the normal lobe 3.50 ms from the prior beat the inverted lobe 0.54 ms away by 0.04 dB; the arrival decides.
        var normalFar = new AlignmentCandidate(7.359, false, -2.57);
        var invertedNear = new AlignmentCandidate(11.398, true, -2.61);
        var normalFarther = new AlignmentCandidate(14.077, false, -3.39);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [normalFar, invertedNear, normalFarther], baseDeltaMs: 10.856);

        Assert.Equal(invertedNear, chosen);
    }

    [Fact]
    public void Select_StillPrefersAReachableNormalAfterTheCrossPolarityTieBreak()
    {
        var invertedNear = new AlignmentCandidate(1.5, true, -0.50);
        var normalFlip = new AlignmentCandidate(2.0, false, -0.55);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [invertedNear, normalFlip], baseDeltaMs: 1.5);

        Assert.Equal(normalFlip, chosen);
    }

    [Fact]
    public void Select_KeepsTheInvertedWinnerWhenTheRescueIsBeyondTheArrivalReach()
    {
        // Inverted winner 0.79 ms from the arrival vs a normal lobe 4.98 ms out within 0.03 dB: the reach gate keeps the winner.
        var inverted = new AlignmentCandidate(0.499, true, -1.06);
        var normalFar = new AlignmentCandidate(-3.694, false, -1.10);
        var normalWorse = new AlignmentCandidate(4.752, false, -2.49);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [inverted, normalFar, normalWorse], baseDeltaMs: 1.285);

        Assert.Equal(inverted, chosen);
    }

    [Fact]
    public void Select_SwapsToACloserRescueWhenTheBestNormalIsBeyondReach()
    {
        // The reach gate filters candidates: a nearer, lower-scoring normal lobe still rescues the polarity.
        var inverted = new AlignmentCandidate(0.5, true, -1.0);
        var normalFar = new AlignmentCandidate(-3.7, false, -1.1);
        var normalNear = new AlignmentCandidate(0.9, false, -1.45);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [inverted, normalFar, normalNear], baseDeltaMs: 1.285);

        Assert.Equal(normalNear, chosen);
    }

    [Fact]
    public void DeclinedInvertRescue_ReportsTheBlockedSwap()
    {
        var inverted = new AlignmentCandidate(0.499, true, -1.06);
        var normalFar = new AlignmentCandidate(-3.694, false, -1.10);

        AlignmentCandidate? declined = AlignmentSelection.DeclinedInvertRescue(
            [inverted, normalFar], baseDeltaMs: 1.285);

        Assert.Equal(normalFar, declined);
    }

    [Fact]
    public void DeclinedInvertRescue_IsNullWhenAReachableRescueExists()
    {
        var inverted = new AlignmentCandidate(0.5, true, -1.0);
        var normalFar = new AlignmentCandidate(-3.7, false, -1.1);
        var normalNear = new AlignmentCandidate(0.9, false, -1.45);

        Assert.Null(AlignmentSelection.DeclinedInvertRescue(
            [inverted, normalFar, normalNear], baseDeltaMs: 1.285));
    }

    [Fact]
    public void DeclinedInvertRescue_IsNullWithoutAMarginNormal()
    {
        var inverted = new AlignmentCandidate(0.5, true, -1.0);
        var normalOutscored = new AlignmentCandidate(-3.7, false, -1.9);

        Assert.Null(AlignmentSelection.DeclinedInvertRescue(
            [inverted, normalOutscored], baseDeltaMs: 1.285));
    }

    private static double AcousticScore(AlignmentCandidate candidate) =>
        candidate.LossDb + VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
        (candidate.DipDb - candidate.LossDb);

    [Fact]
    public void SelectPromotionLobe_SnapsToArrivalNearestLobeNotDeepestSum()
    {
        // 1500 Hz tweeter: the deepest wide-window lobe beats the correct one by 0.14 dB a full period farther out; the arrival breaks the tie.
        // Values are the logged [diag] wide candidates (delay, invert, score, avg=Loss, dip).
        AlignmentCandidate[] wide =
        [
            new(9.231, true, -1.27, -0.42, -1.9),
            new(8.926, false, -1.27, -0.44, -1.7),
            new(8.627, true, -1.39, -0.43, -1.7),
            new(9.524, false, -1.41, -0.45, -2.2),
            new(8.336, false, -1.60, -0.46, -1.9),
            new(11.222, true, -1.71, -0.68, -2.5),
        ];
        var finePick = new AlignmentCandidate(10.320, false, -3.27, -0.88, -5.7);

        AlignmentCandidate scoreWinner = AlignmentSelection.Select(wide, 10.302);
        Assert.Equal(8.926, scoreWinner.DelayMs);

        AlignmentCandidate promoted = AlignmentSelection.SelectPromotionLobe(
            wide,
            scoreWinner,
            AcousticScore,
            fineScoreDb: AcousticScore(finePick),
            marginDb: 1.6,                          // WideWindowPromotionMarginDb
            arrivalPickMs: finePick.DelayMs,
            anchorMs: 10.302,
            reachMs: 2.5 * (1000.0 / 1500.0));      // PromotionReachPeriods · period

        Assert.Equal(9.524, promoted.DelayMs);
    }

    [Fact]
    public void SelectPromotionLobe_KeepsDeepestWhenItIsAlreadyArrivalNearest()
    {
        AlignmentCandidate[] wide =
        [
            new(9.6, false, -1.20, -0.40, -1.6),
            new(8.7, false, -1.35, -0.45, -1.9),
        ];
        var finePick = new AlignmentCandidate(10.0, false, -3.20, -0.90, -5.5);
        AlignmentCandidate gateWinner = AlignmentSelection.Select(wide, 9.8);

        AlignmentCandidate promoted = AlignmentSelection.SelectPromotionLobe(
            wide,
            gateWinner,
            AcousticScore,
            fineScoreDb: AcousticScore(finePick),
            marginDb: 1.6,
            arrivalPickMs: finePick.DelayMs,
            anchorMs: 9.8,
            reachMs: 2.5 * (1000.0 / 1500.0));

        Assert.Equal(9.6, promoted.DelayMs);
    }

    [Fact]
    public void SelectPromotionLobe_KeepsBelowMarginLobesOutOfTheSnap()
    {
        // The snap ranges only over lobes that independently earn a promotion (9.9 ms gains ~0.5 dB only).
        var gateWinner = new AlignmentCandidate(8.9, false, -1.27, -0.44, -1.7);
        AlignmentCandidate[] wide =
        [
            new(9.9, false, -2.90, -0.80, -4.8),    // nearer arrival, but weak sum
            gateWinner,
        ];
        var finePick = new AlignmentCandidate(10.3, false, -3.10, -0.85, -5.4);

        AlignmentCandidate promoted = AlignmentSelection.SelectPromotionLobe(
            wide,
            gateWinner,
            AcousticScore,
            fineScoreDb: AcousticScore(finePick),
            marginDb: 1.6,
            arrivalPickMs: finePick.DelayMs,
            anchorMs: 10.28,
            reachMs: 2.5 * (1000.0 / 1500.0));

        Assert.Equal(8.9, promoted.DelayMs);
    }

    [Fact]
    public void GateWideSeedLobe_PassesThroughAChosenWithinReach()
    {
        var chosen = new AlignmentCandidate(1.1, false, -0.90, -0.30, -1.5);
        AlignmentCandidate[] candidates =
        [
            chosen,
            new(0.2, true, -1.10, -0.35, -1.8),
        ];

        AlignmentCandidate gated = AlignmentSelection.GateWideSeedLobe(
            candidates, chosen, AcousticScore,
            anchorMs: 0.3, nearReachMs: 2.5, lobeHopMarginDb: 1.6);

        Assert.Equal(chosen, gated);
    }

    [Fact]
    public void GateWideSeedLobe_ReturnsTheArrivalLobeWhenTheHopLacksTheMargin()
    {
        // v3: a lobe 4.4 ms off beat the arrival lobe by 0.13 dB; prior-free the hop gains ~0.6 dB, under the 1.6 dB a hop needs.
        var farLobe = new AlignmentCandidate(-4.026, false, -1.00, -0.14, -0.8);
        var arrivalLobe = new AlignmentCandidate(-0.246, true, -1.13, -0.29, -2.0);
        AlignmentCandidate[] candidates =
        [
            farLobe,
            arrivalLobe,
            new(3.295, false, -2.08, -0.96, -2.7),
        ];

        AlignmentCandidate gated = AlignmentSelection.GateWideSeedLobe(
            candidates, farLobe, AcousticScore,
            anchorMs: 0.339, nearReachMs: 2.5, lobeHopMarginDb: 1.6);

        Assert.Equal(arrivalLobe, gated);
    }

    [Fact]
    public void GateWideSeedLobe_KeepsAFarLobeThatClearsTheMargin()
    {
        // A plainly better far lobe stands (the wide-window promotion standard).
        var farLobe = new AlignmentCandidate(-4.0, false, -0.60, -0.10, -0.4);
        var arrivalLobe = new AlignmentCandidate(-0.2, true, -2.30, -1.20, -3.6);

        AlignmentCandidate gated = AlignmentSelection.GateWideSeedLobe(
            [farLobe, arrivalLobe], farLobe, AcousticScore,
            anchorMs: 0.3, nearReachMs: 2.5, lobeHopMarginDb: 1.6);

        Assert.Equal(farLobe, gated);
    }

    [Fact]
    public void GateWideSeedLobe_StandsWhenNoCandidateSitsNearTheArrival()
    {
        var farLobe = new AlignmentCandidate(-4.0, false, -1.00, -0.14, -0.8);
        AlignmentCandidate[] candidates =
        [
            farLobe,
            new(3.3, false, -2.08, -0.96, -2.7),
        ];

        AlignmentCandidate gated = AlignmentSelection.GateWideSeedLobe(
            candidates, farLobe, AcousticScore,
            anchorMs: 0.339, nearReachMs: 2.5, lobeHopMarginDb: 1.6);

        Assert.Equal(farLobe, gated);
    }

    [Fact]
    public void LobeContinuation_CompletesTheCutOffLobeRatherThanHoppingToAFarOne()
    {
        // 3RC's sub junction: the wall pick at -0.62 ms continues to -0.83 ms in the widened window, where a lobe 2.87 ms off,
        // inverted, ties it on the prior-laden score and sits nearer the arrival.
        var wallPick = new AlignmentCandidate(-0.618, false, -3.38);
        var far = new AlignmentCandidate(2.872, true, -3.74);
        var continuation = new AlignmentCandidate(-0.826, false, -3.76);

        Assert.Equal(
            continuation,
            AlignmentSelection.LobeContinuation([far, continuation], wallPick, -1.345, 4.655));
        Assert.Equal(far, AlignmentSelection.Select([far, continuation], baseDeltaMs: 1.655));
    }

    [Fact]
    public void LobeContinuation_IsNullWhileTheLobeStillSitsAtTheWidenedWall()
    {
        var wallPick = new AlignmentCandidate(-1.0, false, -2.0);
        var stillAtTheWall = new AlignmentCandidate(-1.95, false, -1.5);
        var other = new AlignmentCandidate(0.5, true, -1.8);

        Assert.Null(AlignmentSelection.LobeContinuation([stillAtTheWall, other], wallPick, -2.0, 2.0));
        Assert.Null(AlignmentSelection.LobeContinuation([other], wallPick, -2.0, 2.0));
    }

    [Fact]
    public void SelectWithEdgeRetry_TakesTheContinuationAndFallsBackToSelection()
    {
        // The far lobe wins plain selection outright (0.3 dB, same polarity), so only the continuation rule can return the near one.
        var wallPick = new AlignmentCandidate(-0.95, false, -2.0);
        var far = new AlignmentCandidate(1.2, false, -1.6);
        var continuation = new AlignmentCandidate(-1.1, false, -1.9);
        var atTheWiderWall = new AlignmentCandidate(-1.98, false, -1.9);

        AlignmentCandidate? completed = AlignmentSelection.SelectWithEdgeRetry(
            half => half > 1.0 ? [far, continuation] : [wallPick], centerMs: 0, halfWindowMs: 1.0);
        AlignmentCandidate? reselected = AlignmentSelection.SelectWithEdgeRetry(
            half => half > 1.0 ? [far, atTheWiderWall] : [wallPick], centerMs: 0, halfWindowMs: 1.0);

        Assert.Equal(continuation, completed);
        Assert.Equal(far, reselected);
    }
}
