using System.Numerics;

namespace Resonalyze.Dsp;

/// <param name="PeakDelayMs">Where the pick's lobe peaks, as a delay of the searched channel.</param>
internal sealed record PhaseLobeReading(
    AlignmentCandidate Pick,
    double PickScore,
    double PeakDelayMs,
    double ChosenScore)
{
    /// <summary>False where no lag brings the band into phase: the curve then ranks nothing.</summary>
    public bool NamesALobe => PickScore >= JunctionPhaseLobe.MinimumScore;
}

/// <summary>Ranks a high junction's delay lobes by the junction read-out's phase score, which reads the direct sound
/// where the summation reads the cabin behind it. See docs/tech/auto-alignment.md#phase-lobe.</summary>
internal static class JunctionPhaseLobe
{
    public const double MinimumScore = 0.75;

    /// <summary>Phase-score gain a far-side trim must show; a trim is bounded, so it needs no floor.</summary>
    public const double TrimMinimumGain = 0.02;

    /// <summary>A quarter of the DSP's 0.01 ms tick, so every tick a trim tries has a point of the curve.</summary>
    public const double TrimStepMs = 0.0025;

    private const int StepsPerPeriod = 48;

    // Long enough that above 1 kHz the 8-cycle window alone shapes the read, whatever gate the panel is set to.
    private const double GateLeftMs = 5.0;
    private const double GatePlateauMs = 50.0;
    private const double GateRightMs = 20.0;

    /// <summary>The read-out's band score against a delay added to <paramref name="searched"/>, over
    /// ±<paramref name="rangeMs"/>; an inverted relation reads as the negated score. Null where the junction reads nothing.</summary>
    public static List<SignalPoint>? Curve(
        PlacementChannel searched,
        PlacementChannel neighbor,
        bool searchedIsLower,
        int sampleRate,
        int? processorSampleRate,
        double crossoverHz,
        double bandLowHz,
        double bandHighHz,
        double rangeMs,
        double? stepMs = null)
    {
        List<Complex[]> spectra = JunctionPhaseSpectra.Build(
            [searched, neighbor], [sampleRate, sampleRate], sampleRate,
            pinnedOffsetMs: null, GateLeftMs, GatePlateauMs, GateRightMs);
        List<SignalPoint>? upperLag = JunctionPhaseAlignment.SweepCurve(
            spectra[searchedIsLower ? 0 : 1],
            spectra[searchedIsLower ? 1 : 0],
            sampleRate, crossoverHz, bandLowHz, bandHighHz,
            rangeMs, stepMs ?? 1_000.0 / crossoverHz / StepsPerPeriod, processorSampleRate);
        if (upperLag == null || !searchedIsLower)
        {
            return upperLag;
        }

        // The sweep's lag delays the upper channel: delaying the lower one is the same lag negated.
        var curve = new List<SignalPoint>(upperLag.Count);
        for (int i = upperLag.Count - 1; i >= 0; i--)
        {
            curve.Add(new SignalPoint(-upperLag[i].X, upperLag[i].Y));
        }

        return curve;
    }

    /// <summary>The lobe the candidate stands in: the best score within half a period of its delay, in its polarity,
    /// and where it peaks. A maximum on the reach's edge is a slope: the candidate keeps its delay and the score there.</summary>
    public static (double DelayMs, double Score) PeakOf(
        IReadOnlyList<SignalPoint> curve,
        AlignmentCandidate candidate,
        double halfPeriodMs)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(candidate);

        int first = -1;
        int last = -1;
        int best = -1;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < curve.Count; i++)
        {
            if (Math.Abs(curve[i].X - candidate.DelayMs) > halfPeriodMs)
            {
                continue;
            }

            first = first < 0 ? i : first;
            last = i;
            double score = candidate.InvertPolarity ? -curve[i].Y : curve[i].Y;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        if (best >= 0 && best != first && best != last)
        {
            return (curve[best].X, bestScore);
        }

        double standing = ScoreAt(curve, candidate.DelayMs);
        return (candidate.DelayMs, candidate.InvertPolarity ? -standing : standing);
    }

    /// <summary>The score nearest <paramref name="delayMs"/>; the curve must not be empty.</summary>
    public static double ScoreAt(IReadOnlyList<SignalPoint> curve, double delayMs)
    {
        ArgumentNullException.ThrowIfNull(curve);

        SignalPoint nearest = curve[0];
        foreach (SignalPoint point in curve)
        {
            if (Math.Abs(point.X - delayMs) < Math.Abs(nearest.X - delayMs))
            {
                nearest = point;
            }
        }

        return nearest.Y;
    }

    /// <summary>Null when the curve is empty. The phase ranks delay lobes, not polarities: the pick keeps the
    /// expected relation, or leaves it only where the standing pick already did and the phase plainly agrees.</summary>
    /// <param name="expectedInvert">The searched channel's polarity in the relation the filters expect.</param>
    public static PhaseLobeReading? Read(
        IReadOnlyList<SignalPoint> curve,
        IReadOnlyList<AlignmentCandidate> candidates,
        AlignmentCandidate chosen,
        double halfPeriodMs,
        bool expectedInvert)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(chosen);
        if (curve.Count == 0)
        {
            return null;
        }

        (double DelayMs, double Score) Peak(AlignmentCandidate candidate) => PeakOf(curve, candidate, halfPeriodMs);
        double ScoreOf(AlignmentCandidate candidate) => Peak(candidate).Score;
        AlignmentCandidate? Best(bool invert) => candidates
            .Append(chosen)
            .Where(item => item.InvertPolarity == invert)
            .MaxBy(ScoreOf);

        AlignmentCandidate? expected = Best(expectedInvert);
        AlignmentCandidate? other = Best(!expectedInvert);
        bool leavesTheExpected = other != null &&
            (expected == null ||
                (chosen.InvertPolarity != expectedInvert &&
                    ScoreOf(other) >= ScoreOf(expected) + JunctionPhaseAlignment.PolarityFlipAdvantage));
        AlignmentCandidate pick = leavesTheExpected ? other! : expected!;
        // An optimum under the standing pick's own peak is that pick: only its delay is the phase's to move.
        if (pick.InvertPolarity == chosen.InvertPolarity &&
            Math.Abs(Peak(pick).DelayMs - Peak(chosen).DelayMs) <= 0.5 * halfPeriodMs)
        {
            pick = chosen;
        }

        (double peakMs, double score) = Peak(pick);
        return new PhaseLobeReading(pick, score, peakMs, ScoreOf(chosen));
    }
}
