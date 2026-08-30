using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Where one listening group sits against a settled reference: the delay that
/// would make the two arrive together, whether the group reads inverted, and
/// how much the answer can be trusted.
/// </summary>
/// <param name="CoArrivalDelayMs">
/// The delay to ADD to the group so it arrives with the reference. Negative
/// when the group is already the later of the two — the group normalization
/// pass is what turns such an answer into something a processor can dial, by
/// delaying everything else instead.
/// </param>
/// <param name="Inverted">
/// True when the group's phase-whitened correlation against the reference peaks
/// NEGATIVE, which is a measurement of relative polarity rather than a guess.
/// </param>
/// <param name="Coefficient">
/// The extremum's height, |r| in 0..1. The confidence the caller reports.
/// </param>
internal sealed record GroupPlacement(
    double CoArrivalDelayMs,
    bool Inverted,
    double Coefficient);

/// <summary>
/// Places a whole group (a rear fill, a centre) against a reference that is
/// already settled — the front stage after the chain walk.
/// </summary>
/// <remarks>
/// Deliberately not a junction search. A rear fill shares no crossover with the
/// front stage and a centre shares none with anything, so there is no handover
/// to optimise: what these groups need is one number each, and one number is
/// what a correlation against a settled reference gives. That is also what makes
/// the staging one-way — a placement computed from a fixed reference cannot move
/// the thing it was computed from.
/// <para>
/// Two steps, the same shape the alignment engine uses at a junction: a coarse
/// band-limited ARRIVAL difference says roughly where the answer is, then the
/// phase-whitened correlation is searched in a short window centred there. The
/// coarse step matters because a rear fill can sit ten or twenty milliseconds
/// from the front stage — a correlation searched around zero would find a lobe
/// rather than the answer, and one searched across the whole range would have
/// many lobes to choose between.
/// </para>
/// </remarks>
internal static class VirtualCrossoverGroupPlacement
{
    /// <summary>
    /// How far around the coarse arrival estimate the whitened correlation is
    /// refined. Wide enough to cross the estimate's own error — the arrival read
    /// is an envelope feature and the correlation peak is a phase feature, and
    /// they differ by a fraction of a period — and narrow enough that the search
    /// cannot walk into the neighbouring lobe.
    /// </summary>
    public const double RefineRangeMs = 2.0;

    /// <summary>
    /// Below this |r| the placement is reported but not trusted: the groups play
    /// the same band from different places, so their correlation is never as
    /// clean as a crossover's, but a value this low means the band holds no
    /// common feature to time against at all.
    /// </summary>
    public const double MinimumTrustedCoefficient = 0.25;

    /// <summary>
    /// Places <paramref name="groupIr"/> against <paramref name="referenceIr"/>
    /// in the band the two share. Null when either side holds no reliable
    /// arrival there, which is the honest answer for a group that does not
    /// overlap the reference enough to be timed against it.
    /// </summary>
    public static GroupPlacement? Place(
        Complex[] referenceIr,
        Complex[] groupIr,
        int sampleRate,
        double lowHz,
        double highHz)
    {
        ArgumentNullException.ThrowIfNull(referenceIr);
        ArgumentNullException.ThrowIfNull(groupIr);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(lowHz > 0) || !(highHz > lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio))
        {
            return null;
        }

        TimeAlignmentAnalysisResult reference =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                referenceIr, sampleRate, lowHz, highHz);
        TimeAlignmentAnalysisResult group =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                groupIr, sampleRate, lowHz, highHz);
        if (!Reliable(reference) || !Reliable(group))
        {
            return null;
        }

        // How much later the group arrives. The correlation's own convention is
        // the delay to ADD to the second response, so the window it searches is
        // centred on the negative of that.
        double lateMs = group.FirstArrivalDelayMilliseconds -
            reference.FirstArrivalDelayMilliseconds;
        double centerHz = Math.Sqrt(lowHz * highHz);
        double octaves = Math.Log2(highHz / lowHz);
        CorrelationAlignmentResult correlation =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                referenceIr,
                groupIr,
                sampleRate,
                centerHz,
                octaves,
                RefineRangeMs,
                centerLagMs: -lateMs,
                phaseTransform: true);
        CorrelationDelayCandidate best = correlation.BestByMagnitude;
        return new GroupPlacement(
            best.DelayMs,
            best.InvertPolarity,
            Math.Abs(best.Coefficient));
    }

    /// <summary>
    /// The centre's placement from its two side readings: the midpoint. A centre
    /// plays a signal derived from L and R and sits between them, so the delay
    /// that puts it in the middle is the average of the two that would align it
    /// with each side alone.
    /// </summary>
    /// <remarks>
    /// The two readings are also each other's witness. They should differ by the
    /// scene offset — the same figure the stereo run applies and the metric panel
    /// verifies — because that is how far apart the sides themselves are. A
    /// larger disagreement means one of the readings landed on the wrong lobe,
    /// and the caller is told so rather than handed a confident midpoint between
    /// a right answer and a wrong one.
    /// <para>
    /// Polarity is only applied when both sides agree on it. A centre that reads
    /// inverted against one side and normal against the other is not a centre
    /// with a wiring fault; it is a measurement that has not settled, and
    /// flipping on half of it would be a coin toss dressed as a reading.
    /// </para>
    /// </remarks>
    public static (double DelayMs, bool Inverted, bool Confident) Midpoint(
        GroupPlacement reference,
        GroupPlacement far,
        double sceneOffsetMs,
        double toleranceMs)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(far);
        bool agree = reference.Inverted == far.Inverted;
        bool witnessed =
            Math.Abs(Math.Abs(reference.CoArrivalDelayMs - far.CoArrivalDelayMs) -
                Math.Abs(sceneOffsetMs)) <= toleranceMs;
        return (
            (reference.CoArrivalDelayMs + far.CoArrivalDelayMs) / 2.0,
            agree && reference.Inverted,
            agree &&
                witnessed &&
                Math.Min(reference.Coefficient, far.Coefficient) >=
                    MinimumTrustedCoefficient);
    }

    private static bool Reliable(TimeAlignmentAnalysisResult arrival) =>
        arrival.IsValid &&
        arrival.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb;
}
