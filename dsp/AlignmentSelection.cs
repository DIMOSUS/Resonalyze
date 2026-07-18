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
    /// plainly better, not marginally. That "several dB" premise only holds
    /// near the arrival: a WIDE-SEED search dilutes the prior (sigma scales
    /// with the window), so the margin is additionally fenced by
    /// <see cref="DefaultInvertPreferenceReachMs"/>.
    /// </summary>
    public const double DefaultInvertPreferenceMarginDb = 0.5;

    /// <summary>
    /// How much farther from the arrival estimate (ms) a non-inverted rescue
    /// may sit than the inverted winner it replaces. The invert margin's
    /// rationale assumes the rescue IS the arrival-proximal candidate being
    /// narrowly outscored by a flip impostor; when the best non-inverted
    /// alternative instead lies a distant lobe away, swapping trades
    /// milliseconds of envelope alignment for polarity cosmetics. The field
    /// failure that pinned this: an 80 Hz sub/midbass junction where the
    /// inverted winner sat 0.79 ms from the arrival and the margin (0.03 dB!)
    /// handed the result to a non-inverted lobe 4.98 ms out — the sub started
    /// 5 ms behind the midbass. The reach is absolute, not period-scaled,
    /// because the audible cost (transient smear) is absolute: legitimate flip
    /// rescues at mid/tweeter junctions sit within a fraction of a ms, while a
    /// low junction's half-period hop costs several.
    /// </summary>
    public const double DefaultInvertPreferenceReachMs = 0.75;

    /// <summary>
    /// Among candidates within this score margin (dB), the one closest to the
    /// arrival-based estimate wins — the physically minimal correction. The
    /// tie-break is polarity-AGNOSTIC on the first pass: fractions of a dB
    /// never choose a lobe, and the flip partner a third of a period out is a
    /// lobe like any other. The field case that made it so: an 80 Hz
    /// sub/woofer junction (arrival latched 10 ms early) offered a
    /// non-inverted lobe 3.5 ms from the prior at −2.57 dB and the true
    /// inverted lobe 0.54 ms from it at −2.61 dB — a 0.04 dB "preference"
    /// that cost 3 ms of bass attack.
    /// </summary>
    public const double DefaultDelayTieMarginDb = 0.1;

    /// <summary>
    /// Selects from <paramref name="candidates"/> (must be non-empty, best
    /// first): first breaks near-ties of the score by closeness to
    /// <paramref name="baseDeltaMs"/> REGARDLESS of polarity (the envelope
    /// outranks fractions of a dB, and a flip partner is a lobe like any
    /// other); then prefers a non-inverted candidate over an inverted winner
    /// within the invert margin — but only one that does not sit more than
    /// <paramref name="invertPreferenceReachMs"/> farther from
    /// <paramref name="baseDeltaMs"/> than the winner it replaces — and
    /// finally re-breaks near-ties within the chosen polarity.
    /// </summary>
    public static AlignmentCandidate Select(
        IReadOnlyList<AlignmentCandidate> candidates,
        double baseDeltaMs,
        double invertPreferenceMarginDb = DefaultInvertPreferenceMarginDb,
        double invertPreferenceReachMs = DefaultInvertPreferenceReachMs,
        double delayTieMarginDb = DefaultDelayTieMarginDb)
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
        if (best.InvertPolarity)
        {
            double bestDistanceMs = Math.Abs(best.DelayMs - baseDeltaMs);
            AlignmentCandidate? bestNormal = candidates
                .Where(item => !item.InvertPolarity &&
                    Math.Abs(item.DelayMs - baseDeltaMs) - bestDistanceMs <=
                        invertPreferenceReachMs)
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
    /// The non-inverted candidate the invert preference would have adopted
    /// under the score margin alone, when the reach gate blocked every
    /// eligible rescue — null when no rescue was in margin, or when one within
    /// reach exists (so <see cref="Select"/> swapped instead of declining).
    /// Purely diagnostic: lets the caller log WHY an inverted winner stood.
    /// </summary>
    public static AlignmentCandidate? DeclinedInvertRescue(
        IReadOnlyList<AlignmentCandidate> candidates,
        double baseDeltaMs,
        double invertPreferenceMarginDb = DefaultInvertPreferenceMarginDb,
        double invertPreferenceReachMs = DefaultInvertPreferenceReachMs,
        double delayTieMarginDb = DefaultDelayTieMarginDb)
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
        if (!best.InvertPolarity)
        {
            return null;
        }

        double bestDistanceMs = Math.Abs(best.DelayMs - baseDeltaMs);
        List<AlignmentCandidate> inMargin = candidates
            .Where(item => !item.InvertPolarity &&
                item.ScoreDb >= best.ScoreDb - invertPreferenceMarginDb)
            .ToList();
        return inMargin.Count == 0 || inMargin.Any(item =>
                Math.Abs(item.DelayMs - baseDeltaMs) - bestDistanceMs <=
                    invertPreferenceReachMs)
            ? null
            : inMargin.OrderByDescending(item => item.ScoreDb).First();
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
