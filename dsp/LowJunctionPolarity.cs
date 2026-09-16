using System.Numerics;

namespace Resonalyze.Dsp;

/// <param name="SameSignShiftMs">Delay to add to the variable channel to put its same-sign crest under the
/// neighbour's, in the frame <see cref="AlignmentCandidate.DelayMs"/> lives in.</param>
internal sealed record LowJunctionPolarityVote(
    bool ExpectsRelativeInversion,
    double SameSignShiftMs,
    double OppositeSignShiftMs)
{
    /// <summary>Movement the winning branch asks for.</summary>
    public double ShiftMs =>
        ExpectsRelativeInversion ? OppositeSignShiftMs : SameSignShiftMs;

    /// <summary>How much nearer the winning crest sits; two crests the same distance away name no polarity.</summary>
    public double SeparationMs =>
        Math.Abs(Math.Abs(SameSignShiftMs) - Math.Abs(OppositeSignShiftMs));

    /// <summary>The crests name a polarity only when the loser is at least a quarter period farther: a dispersive
    /// channel carries both signs at nearly the same distance, and then the nearer one is noise.</summary>
    public bool IsDecisive(double crossoverHz) =>
        crossoverHz > 0 && SeparationMs >= 250.0 / crossoverHz;
}

/// <summary>Reads a low crossover's relative polarity off the two channels' own crests, where the summation score
/// cannot tell a lobe from its half-period-plus-inversion twin. See docs/tech/auto-alignment.md#low-junction-polarity.</summary>
internal static class LowJunctionPolarity
{
    /// <summary>Score gap inside which the two polarity branches are a tie and the crests decide.</summary>
    public const double TieMarginDb = 0.5;

    /// <summary>Null when either channel has no measured crest to read.</summary>
    public static LowJunctionPolarityVote? Read(
        Complex[] neighborImpulseResponse,
        Complex[] variableImpulseResponse,
        int sampleRate,
        ValidSampleRange neighborRange = default,
        ValidSampleRange variableRange = default)
    {
        ArgumentNullException.ThrowIfNull(neighborImpulseResponse);
        ArgumentNullException.ThrowIfNull(variableImpulseResponse);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        (int neighborIndex, double neighborValue) =
            Crest(neighborImpulseResponse, neighborRange, sign: 0);
        if (neighborValue == 0)
        {
            return null;
        }

        double sign = Math.Sign(neighborValue);
        (int sameIndex, double sameValue) =
            Crest(variableImpulseResponse, variableRange, sign);
        (int oppositeIndex, double oppositeValue) =
            Crest(variableImpulseResponse, variableRange, -sign);
        if (sameValue == 0 || oppositeValue == 0)
        {
            return null;
        }

        double msPerSample = 1_000.0 / sampleRate;
        double sameShiftMs = (neighborIndex - sameIndex) * msPerSample;
        double oppositeShiftMs = (neighborIndex - oppositeIndex) * msPerSample;
        return new LowJunctionPolarityVote(
            Math.Abs(oppositeShiftMs) < Math.Abs(sameShiftMs),
            sameShiftMs,
            oppositeShiftMs);
    }

    /// <summary>The voted branch's best candidate, or <paramref name="chosen"/> where the score already separates
    /// the branches or the voted one holds no candidate.</summary>
    /// <param name="neighborInverted">Polarity of the settled neighbour: candidates carry absolute polarity.</param>
    public static AlignmentCandidate Decide(
        IReadOnlyList<AlignmentCandidate> candidates,
        AlignmentCandidate chosen,
        Func<AlignmentCandidate, double> acousticScore,
        bool expectedRelativeInversion,
        double anchorMs,
        bool neighborInverted)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(acousticScore);

        bool votedPolarity = neighborInverted ^ expectedRelativeInversion;
        if (chosen.InvertPolarity == votedPolarity)
        {
            return chosen;
        }

        // Best first: the tie-breaks read the head of the list as the score's own pick.
        List<AlignmentCandidate> voted = candidates
            .Where(item => item.InvertPolarity == votedPolarity)
            .OrderByDescending(item => item.ScoreDb)
            .ToList();
        if (voted.Count == 0)
        {
            return chosen;
        }

        // The crests only break ties: a branch the summation separates is the summation's to call.
        double gapDb = acousticScore(chosen) - voted.Max(acousticScore);
        return gapDb > TieMarginDb
            ? chosen
            : AlignmentSelection.Select(
                voted, anchorMs, neighborInverted: neighborInverted,
                expectedRelativeInversion: expectedRelativeInversion);
    }

    private static (int Index, double Value) Crest(
        Complex[] impulseResponse,
        ValidSampleRange range,
        double sign)
    {
        int from = range.IsKnown ? Math.Max(0, range.StartSample) : 0;
        int to = range.IsKnown
            ? Math.Min(impulseResponse.Length, range.EndSample)
            : impulseResponse.Length;
        int index = 0;
        double best = 0;
        for (int i = from; i < to; i++)
        {
            double value = impulseResponse[i].Real;
            if (sign != 0 && Math.Sign(value) != sign)
            {
                continue;
            }

            if (Math.Abs(value) > Math.Abs(best))
            {
                best = value;
                index = i;
            }
        }

        return (index, best);
    }
}
