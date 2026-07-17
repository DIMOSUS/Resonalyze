namespace Resonalyze.Dsp.Tests;

public sealed class AlignmentSelectionTests
{
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
}
