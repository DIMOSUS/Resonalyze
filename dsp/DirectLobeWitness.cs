namespace Resonalyze.Dsp;

/// <param name="Candidate">The candidate the direct wavefronts prefer, or null where none of them reaches
/// <see cref="DirectLobeWitness.MinimumR"/>.</param>
/// <param name="CandidateR">Coherence that candidate attains at its own lobe, signed by its polarity.</param>
/// <param name="ChosenR">The same figure for the standing pick.</param>
/// <param name="BestLagMs">Lag of the curve's strongest coherence, whether or not a candidate sits there.</param>
internal sealed record DirectLobeReading(
    AlignmentCandidate? Candidate,
    double CandidateR,
    double ChosenR,
    double BestLagMs,
    double BestR);

/// <summary>Judges the CHOSEN lobe against the direct sound's whitened correlation over a full period either way:
/// the tie arbitration two steps earlier only weighs the flip partner. See docs/tech/auto-alignment.md#direct-lobe-check.</summary>
internal static class DirectLobeWitness
{
    /// <summary>Same floor the tie arbitration trusts: below it the wavefronts are not coherent enough to vote.</summary>
    public const double MinimumR = 0.6;

    /// <summary>Five times the tie arbitration's advantage: neighbouring lobes of one comb differ little, so only a
    /// gulf may move a pick the summation already made.</summary>
    public const double LobeAdvantage = 0.25;

    /// <summary>Coherence a candidate attains: the best sign-consistent value within a quarter period of its own
    /// delay — its lobe, never the partner's half a period away.</summary>
    public static double CoherenceOf(
        IReadOnlyList<SignalPoint> curve,
        AlignmentCandidate candidate,
        double halfPeriodMs)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(candidate);

        double best = double.NegativeInfinity;
        foreach (SignalPoint point in curve)
        {
            if (Math.Abs(point.X - candidate.DelayMs) <= 0.5 * halfPeriodMs)
            {
                best = Math.Max(best, candidate.InvertPolarity ? -point.Y : point.Y);
            }
        }

        return best;
    }

    /// <summary>Null when the curve is empty. <paramref name="candidates"/> should already be confined to the
    /// lobes the caller would accept.</summary>
    public static DirectLobeReading? Read(
        IReadOnlyList<SignalPoint> curve,
        IReadOnlyList<AlignmentCandidate> candidates,
        AlignmentCandidate chosen,
        double halfPeriodMs)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(chosen);
        if (curve.Count == 0)
        {
            return null;
        }

        SignalPoint best = curve[0];
        foreach (SignalPoint point in curve)
        {
            if (Math.Abs(point.Y) > Math.Abs(best.Y))
            {
                best = point;
            }
        }

        double chosenR = CoherenceOf(curve, chosen, halfPeriodMs);
        AlignmentCandidate? preferred = null;
        double preferredR = double.NegativeInfinity;
        foreach (AlignmentCandidate candidate in candidates)
        {
            double r = CoherenceOf(curve, candidate, halfPeriodMs);
            if (r > preferredR)
            {
                preferred = candidate;
                preferredR = r;
            }
        }

        return new DirectLobeReading(
            preferred, preferredR, chosenR, best.X, Math.Abs(best.Y));
    }

    /// <summary>True where the reading names a different lobe or polarity by a gulf, not by comb noise.</summary>
    public static bool Overturns(DirectLobeReading reading, AlignmentCandidate chosen)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(chosen);

        return reading.Candidate != null &&
            reading.Candidate != chosen &&
            reading.CandidateR >= MinimumR &&
            reading.CandidateR - reading.ChosenR > LobeAdvantage;
    }

    /// <summary>Advantage that merely justifies LOOKING at a lag: a probe proposes nothing by itself, so it may sit
    /// at the tie arbitration's own resolution rather than at <see cref="LobeAdvantage"/>.</summary>
    public const double ProbeAdvantage = 0.10;

    /// <summary>True where the direct sound is decisive about a lag no candidate occupies: the search window never
    /// offered the lobe the wavefronts want, which no arbitration among candidates can repair.</summary>
    public static bool NamesAnUnreachedLobe(
        DirectLobeReading reading,
        AlignmentCandidate chosen,
        double halfPeriodMs)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(chosen);

        return reading.BestR >= MinimumR &&
            reading.BestR - Math.Max(reading.ChosenR, reading.CandidateR) > ProbeAdvantage &&
            Math.Abs(reading.BestLagMs - chosen.DelayMs) > 0.5 * halfPeriodMs;
    }
}
