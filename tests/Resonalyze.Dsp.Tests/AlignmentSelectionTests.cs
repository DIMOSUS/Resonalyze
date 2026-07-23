namespace Resonalyze.Dsp.Tests;

public sealed class AlignmentSelectionTests
{
    // PreferSubLeading scores through LossDb so the tests control the
    // prior-free figure directly (the engine passes its dip-penalized
    // AcousticScore the same way).
    private static double Score(AlignmentCandidate candidate) => candidate.LossDb;

    [Fact]
    public void PreferSubLeading_TrailingPickYieldsToLeadingLobeWithinMargin()
    {
        // The field shape (an 80 Hz sub junction, leadSign +1: the stack is
        // searched, larger delay = sub leads): the chosen candidate leaves
        // the sub trailing by ~1.8 ms and the leading lobe sits 0.7 dB down
        // — inside the precedence margin, so psychoacoustics decide.
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
        // Already on the leading side (and even inside the slack): nothing
        // to re-decide, whatever else the pool holds.
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
        // A sub leading by whole periods is detached the other way: the only
        // "leading" candidate sits past the reach, so the trailing pick stands.
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
        // With the SUB searched (leadSign -1), the sub leads where its OWN
        // delay is smaller than the anchor.
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
    public void Select_PrefersNonInvertedWithinMargin()
    {
        // The inverted impostor wins by less than the invert margin, so the
        // non-inverted candidate must be chosen.
        var inverted = new AlignmentCandidate(2.0, true, -0.50);
        var normal = new AlignmentCandidate(1.4, false, -0.65);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [inverted, normal], baseDeltaMs: 1.4);

        Assert.Equal(normal, chosen);
    }

    [Fact]
    public void Select_KeepsInvertedWinnerBeyondMargin()
    {
        // A genuinely flipped driver wins by more than the margin and stays.
        var inverted = new AlignmentCandidate(2.0, true, -0.50);
        var normal = new AlignmentCandidate(1.4, false, -1.10);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [inverted, normal], baseDeltaMs: 1.4);

        Assert.Equal(inverted, chosen);
    }

    [Fact]
    public void Select_BreaksNearTiesByClosenessToBase()
    {
        // Two non-inverted candidates within the tie margin: the one closer to
        // the arrival-based base delta wins even though it scores lower.
        var farBetter = new AlignmentCandidate(3.0, false, -0.50);
        var nearSlightlyWorse = new AlignmentCandidate(1.1, false, -0.55);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [farBetter, nearSlightlyWorse], baseDeltaMs: 1.0);

        Assert.Equal(nearSlightlyWorse, chosen);
    }

    [Fact]
    public void Select_DoesNotTieBreakAcrossTheMargin()
    {
        // Outside the tie margin the score decides, regardless of distance.
        var farBetter = new AlignmentCandidate(3.0, false, -0.50);
        var nearMuchWorse = new AlignmentCandidate(1.1, false, -0.75);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [farBetter, nearMuchWorse], baseDeltaMs: 1.0);

        Assert.Equal(farBetter, chosen);
    }

    [Fact]
    public void Select_TieBreakStaysWithinChosenPolarity()
    {
        // The inverted winner keeps its polarity group for the delay tie-break:
        // a near-tied non-inverted candidate beyond the invert margin must not
        // participate.
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
        // The 80 Hz sub/woofer junction under a modal-latched arrival,
        // verbatim: the non-inverted lobe 3.50 ms from the prior outscored
        // the true inverted lobe 0.54 ms from it by 0.04 dB, and the old
        // same-polarity-only tie-break let the score hand the sub a 3.5 ms
        // attack lag. Fractions of a dB never choose a lobe — the arrival
        // does, regardless of polarity.
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
        // The cross-polarity tie-break hands the near-tie to an inverted
        // candidate at the arrival; the invert preference then still swaps to
        // a non-inverted partner that sits within the arrival reach — the
        // classic flip rescue is unaffected by the new first pass.
        var invertedNear = new AlignmentCandidate(1.5, true, -0.50);
        var normalFlip = new AlignmentCandidate(2.0, false, -0.55);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [invertedNear, normalFlip], baseDeltaMs: 1.5);

        Assert.Equal(normalFlip, chosen);
    }

    [Fact]
    public void Select_KeepsTheInvertedWinnerWhenTheRescueIsBeyondTheArrivalReach()
    {
        // The 80 Hz sub/midbass field failure verbatim: the inverted winner
        // sits 0.79 ms from the arrival (the whitened correlation put its
        // trough at r -0.97 there), while the best non-inverted candidate is a
        // lobe 4.98 ms out that the WIDE-SEED-diluted prior let within
        // 0.03 dB. Swapping parked the sub 5 ms behind the midbass; the reach
        // gate must keep the inverted winner.
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
        // The reach gate filters candidates, not the preference itself: with
        // the best-scoring non-inverted lobe beyond reach, a lower-scoring one
        // near the arrival still rescues the polarity.
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
        // A within-reach normal candidate means Select swapped rather than
        // declined — nothing to report even though a farther one also sits in
        // margin.
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

    // AutoAlignmentEngine.AcousticScore: the prior-free figure the promotion
    // compares across search windows.
    private static double AcousticScore(AlignmentCandidate candidate) =>
        candidate.LossDb + VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
        (candidate.DipDb - candidate.LossDb);

    [Fact]
    public void SelectPromotionLobe_SnapsToArrivalNearestLobeNotDeepestSum()
    {
        // The field failure verbatim: a left tweeter at a 1500 Hz mid/tweeter
        // split (period 1000/1500 ≈ 0.667 ms). The arrival-anchored fine pick
        // (10.320 ms) combs to a -5.7 dB dip, so a promotion is warranted, but
        // the wide diagnostic window holds a comb of same-polarity lobes. The
        // deepest sum, 8.926 ms, beats the user's physically-correct 9.524 ms by
        // only 0.14 dB while sitting a full period farther from the arrival — so
        // score alone hops the tweeter one lobe too far. The arrival must break
        // that near-tie, exactly as it does for the fine pick. Values are the
        // logged [diag] wide candidates (delay, invert, score, avg=Loss, dip).
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

        // The score-only winner IS the wrong lobe — this is the bug the snap fixes.
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
        // A closer-to-arrival lobe that also sums deeper leaves nothing to snap:
        // the gate winner is returned unchanged.
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
        // A lobe closer to the arrival that does NOT clear the gate on its own
        // (it barely beats the fine pick) must not be snapped to — the snap only
        // ranges over lobes that independently earn a promotion. Here the closer
        // 9.9 ms lobe gains only ~0.5 dB over the fine pick, so the gate winner
        // stands.
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
        // The chosen candidate sits inside the trusted window's reach — the
        // gate has nothing to defend against and must not touch the pick.
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
        // The v3 field failure: at an 80 Hz sub/midbass junction the wide-seed
        // window admitted a lobe 4.4 ms off the arrival that beat the
        // arrival-adjacent inverted candidate by 0.13 dB — 0.03 dB past the
        // tie margin — and started the midbass 4 ms early. Prior-free the hop
        // gains only ~0.6 dB, far below the 1.6 dB a lobe hop needs, so the
        // gate must hand the pick back to the arrival lobe.
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
        // A genuine recovery: the arrival-adjacent candidate sums badly and the
        // far lobe is plainly (not marginally) better on the prior-free score,
        // so it stands — the same standard the wide-window promotion applies.
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
        // With no local optimum inside the reach there is no arrival lobe to
        // defend — the search's own pick stands rather than inventing one.
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
}
