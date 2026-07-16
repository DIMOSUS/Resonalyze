namespace Resonalyze.Dsp;

/// <summary>
/// Chooses the alignment candidate to apply from a near-optimal list (best
/// first, as produced by
/// <see cref="VirtualCrossoverAnalysis.FindAlignmentCandidates"/>), applying
/// physically motivated tie-breaks between candidates the score alone cannot
/// separate.
/// </summary>
public static class AlignmentSelection
{
    /// <summary>
    /// An inverted winner must beat the best non-inverted candidate by this
    /// margin (dB): room reflections routinely hand a (flip + half-period
    /// shift) impostor a few tenths of a dB inside the pair band (the r mid/
    /// tweeter cabin junction hands one +0.32 dB), while a genuinely flipped
    /// driver wins by the full arrival-prior penalty of its non-inverted
    /// impostors — several dB, not fractions. Same envelope-first principle
    /// as the wide-window promotion margin: a half-period flip hop must be
    /// plainly better, not marginally.
    /// </summary>
    public const double DefaultInvertPreferenceMarginDb = 0.5;

    /// <summary>
    /// Among same-polarity candidates within this score margin (dB), the one
    /// closest to the arrival-based estimate wins — the physically minimal
    /// correction.
    /// </summary>
    public const double DefaultDelayTieMarginDb = 0.1;

    /// <summary>
    /// Selects from <paramref name="candidates"/> (must be non-empty, best
    /// first): prefers a non-inverted candidate over an inverted winner within
    /// the invert margin, then breaks near-ties of the chosen polarity by
    /// closeness to <paramref name="baseDeltaMs"/>.
    /// </summary>
    public static AlignmentCandidate Select(
        IReadOnlyList<AlignmentCandidate> candidates,
        double baseDeltaMs,
        double invertPreferenceMarginDb = DefaultInvertPreferenceMarginDb,
        double delayTieMarginDb = DefaultDelayTieMarginDb)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException(
                "At least one candidate is required.",
                nameof(candidates));
        }

        AlignmentCandidate best = candidates[0];
        if (best.InvertPolarity)
        {
            AlignmentCandidate? bestNormal = candidates
                .Where(item => !item.InvertPolarity)
                .OrderByDescending(item => item.ScoreDb)
                .FirstOrDefault();
            if (bestNormal != null &&
                bestNormal.ScoreDb >= best.ScoreDb - invertPreferenceMarginDb)
            {
                best = bestNormal;
            }
        }

        return candidates
            .Where(item => item.InvertPolarity == best.InvertPolarity &&
                item.ScoreDb >= best.ScoreDb - delayTieMarginDb)
            .OrderBy(item => Math.Abs(item.DelayMs - baseDeltaMs))
            .First();
    }

    /// <summary>
    /// After the wide-window promotion gate has decided that a promotion happens,
    /// chooses WHICH comb lobe to promote to. Inside a comb basin the
    /// promotion-worthy lobes differ by fractions of a dB, and the deepest-summing
    /// one is not necessarily the physically correct cycle — the arrival is (the
    /// same envelope-first principle as <see cref="Select"/>'s delay tie-break,
    /// one comb over). So among the candidates that share
    /// <paramref name="gateWinner"/>'s polarity and each INDEPENDENTLY clear the
    /// gate — acoustic score (via <paramref name="acousticScore"/>, a prior-free
    /// figure comparable across search windows) beats <paramref name="fineScoreDb"/>
    /// by more than <paramref name="marginDb"/>, and delay within
    /// <paramref name="reachMs"/> of <paramref name="arrivalPickMs"/> — this
    /// returns the one nearest <paramref name="anchorMs"/>.
    /// <paramref name="gateWinner"/> itself always satisfies those predicates, so
    /// the result is never empty and never lands on a junction the gate would
    /// have declined; it only ever pulls the pick to a closer-to-arrival lobe of
    /// equal promotion standing.
    /// </summary>
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
