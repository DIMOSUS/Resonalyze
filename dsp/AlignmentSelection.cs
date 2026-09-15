namespace Resonalyze.Dsp;

/// <summary>Physical tie-breaks between alignment candidates the score cannot separate. See docs/tech/auto-alignment.md#candidate-selection.</summary>
public static class AlignmentSelection
{
    /// <summary>An inverted winner must beat the best non-inverted candidate by this (flip + half-period impostors win by tenths of a dB).</summary>
    public const double DefaultInvertPreferenceMarginDb = 0.5;

    /// <summary>Max extra distance (ms) from the arrival a non-inverted rescue may sit; absolute because transient smear is absolute.</summary>
    public const double DefaultInvertPreferenceReachMs = 0.75;

    /// <summary>Within this margin the arrival-closest candidate wins, regardless of polarity.</summary>
    public const double DefaultDelayTieMarginDb = 0.1;

    /// <summary>
    /// Delay tie-break, then the relative non-inverted preference (within reach), then a re-break within the chosen polarity.
    /// Polarity is relative to <paramref name="neighborInverted"/>; <paramref name="expectedRelativeInversion"/> withdraws the preference.
    /// </summary>
    public static AlignmentCandidate Select(
        IReadOnlyList<AlignmentCandidate> candidates,
        double baseDeltaMs,
        double invertPreferenceMarginDb = DefaultInvertPreferenceMarginDb,
        double invertPreferenceReachMs = DefaultInvertPreferenceReachMs,
        double delayTieMarginDb = DefaultDelayTieMarginDb,
        bool neighborInverted = false,
        bool expectedRelativeInversion = false)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException(
                "At least one candidate is required.",
                nameof(candidates));
        }

        AlignmentCandidate best = candidates
            .Where(item => item.ScoreDb >= candidates[0].ScoreDb - delayTieMarginDb)
            .OrderBy(item => Math.Abs(item.DelayMs - baseDeltaMs))
            .First();
        if (!expectedRelativeInversion && (best.InvertPolarity ^ neighborInverted))
        {
            double bestDistanceMs = Math.Abs(best.DelayMs - baseDeltaMs);
            AlignmentCandidate? bestPure = candidates
                .Where(item => item.InvertPolarity == neighborInverted &&
                    Math.Abs(item.DelayMs - baseDeltaMs) - bestDistanceMs <=
                        invertPreferenceReachMs)
                .OrderByDescending(item => item.ScoreDb)
                .FirstOrDefault();
            if (bestPure != null &&
                bestPure.ScoreDb >= best.ScoreDb - invertPreferenceMarginDb)
            {
                best = bestPure;
            }
        }

        return candidates
            .Where(item => item.InvertPolarity == best.InvertPolarity &&
                item.ScoreDb >= best.ScoreDb - delayTieMarginDb)
            .OrderBy(item => Math.Abs(item.DelayMs - baseDeltaMs))
            .First();
    }

    /// <summary>Diagnostic: the rescue the reach gate declined, or null.</summary>
    public static AlignmentCandidate? DeclinedInvertRescue(
        IReadOnlyList<AlignmentCandidate> candidates,
        double baseDeltaMs,
        double invertPreferenceMarginDb = DefaultInvertPreferenceMarginDb,
        double invertPreferenceReachMs = DefaultInvertPreferenceReachMs,
        double delayTieMarginDb = DefaultDelayTieMarginDb,
        bool neighborInverted = false,
        bool expectedRelativeInversion = false)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return null;
        }

        AlignmentCandidate best = candidates
            .Where(item => item.ScoreDb >= candidates[0].ScoreDb - delayTieMarginDb)
            .OrderBy(item => Math.Abs(item.DelayMs - baseDeltaMs))
            .First();
        if (expectedRelativeInversion || best.InvertPolarity == neighborInverted)
        {
            return null;
        }

        double bestDistanceMs = Math.Abs(best.DelayMs - baseDeltaMs);
        List<AlignmentCandidate> inMargin = candidates
            .Where(item => item.InvertPolarity == neighborInverted &&
                item.ScoreDb >= best.ScoreDb - invertPreferenceMarginDb)
            .ToList();
        return inMargin.Count == 0 || inMargin.Any(item =>
                Math.Abs(item.DelayMs - baseDeltaMs) - bestDistanceMs <=
                    invertPreferenceReachMs)
            ? null
            : inMargin.OrderByDescending(item => item.ScoreDb).First();
    }

    /// <summary>Replaces a sub-trailing pick with the nearest sub-leading candidate within <paramref name="marginDb"/> and <paramref name="reachMs"/>.</summary>
    public static AlignmentCandidate PreferSubLeading(
        IEnumerable<AlignmentCandidate> pool,
        AlignmentCandidate chosen,
        Func<AlignmentCandidate, double> acousticScore,
        double anchorMs,
        double leadSign,
        double marginDb,
        double slackMs,
        double reachMs)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(acousticScore);
        if (leadSign * (chosen.DelayMs - anchorMs) >= -slackMs)
        {
            return chosen;
        }

        double chosenScore = acousticScore(chosen);
        return pool
            .Where(item =>
            {
                double leadMs = leadSign * (item.DelayMs - anchorMs);
                return leadMs >= -slackMs && leadMs <= reachMs &&
                    acousticScore(item) >= chosenScore - marginDb;
            })
            .OrderBy(item => Math.Abs(item.DelayMs - anchorMs))
            .DefaultIfEmpty(chosen)
            .First();
    }

    /// <summary>A wide-seed pick farther than <paramref name="nearReachMs"/> from the anchor must beat the best near candidate by <paramref name="lobeHopMarginDb"/>.</summary>
    public static AlignmentCandidate GateWideSeedLobe(
        IReadOnlyList<AlignmentCandidate> candidates,
        AlignmentCandidate chosen,
        Func<AlignmentCandidate, double> acousticScore,
        double anchorMs,
        double nearReachMs,
        double lobeHopMarginDb,
        bool neighborInverted = false,
        bool expectedRelativeInversion = false)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(acousticScore);
        if (Math.Abs(chosen.DelayMs - anchorMs) <= nearReachMs)
        {
            return chosen;
        }

        List<AlignmentCandidate> near = candidates
            .Where(item => Math.Abs(item.DelayMs - anchorMs) <= nearReachMs)
            .ToList();
        if (near.Count == 0)
        {
            return chosen;
        }

        AlignmentCandidate nearBest = Select(
            near, anchorMs, neighborInverted: neighborInverted,
            expectedRelativeInversion: expectedRelativeInversion);
        return acousticScore(chosen) - acousticScore(nearBest) > lobeHopMarginDb
            ? chosen
            : nearBest;
    }

    /// <summary>Arrival-nearest lobe among same-polarity candidates that each clear the promotion gate; <paramref name="gateWinner"/> always qualifies.</summary>
    public static AlignmentCandidate SelectPromotionLobe(
        IReadOnlyList<AlignmentCandidate> wideCandidates,
        AlignmentCandidate gateWinner,
        Func<AlignmentCandidate, double> acousticScore,
        double fineScoreDb,
        double marginDb,
        double arrivalPickMs,
        double anchorMs,
        double reachMs)
    {
        ArgumentNullException.ThrowIfNull(wideCandidates);
        ArgumentNullException.ThrowIfNull(gateWinner);
        ArgumentNullException.ThrowIfNull(acousticScore);

        return wideCandidates
            .Where(item => item.InvertPolarity == gateWinner.InvertPolarity &&
                acousticScore(item) - fineScoreDb > marginDb &&
                Math.Abs(item.DelayMs - arrivalPickMs) <= reachMs)
            .OrderBy(item => Math.Abs(item.DelayMs - anchorMs))
            .DefaultIfEmpty(gateWinner)
            .First();
    }
}
