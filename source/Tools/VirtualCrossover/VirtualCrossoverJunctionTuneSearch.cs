using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>Tune junction's question and answer: what the dialog opens a junction with, the tuner's inputs for a
/// request, and the verdict on what it found. See docs/tech/crossover-auto-setup.md#junction-tuner.</summary>
internal static class VirtualCrossoverJunctionTuneSearch
{
    public static JunctionTuneDefaults Opening(IReadOnlyList<AdjacentPair> junctions, int index)
    {
        if (index < 0 || index >= junctions.Count)
        {
            return new JunctionTuneDefaults(20, 20_000, [CrossoverFilterFamily.LinkwitzRiley], null);
        }

        VirtualCrossoverChannelSettings lower = junctions[index].Lower.Channel.Settings;
        VirtualCrossoverChannelSettings upper = junctions[index].Upper.Channel.Settings;
        double currentHz = VirtualCrossoverJunctions.GetPairCrossoverHz(lower, upper);
        (double minHz, double maxHz) = currentHz > 0
            ? AgentProposalValidator.DefaultJunctionWindow(currentHz)
            : (junctions[index].BandLowHz, junctions[index].BandHighHz);
        return new JunctionTuneDefaults(
            minHz,
            maxHz,
            AgentProposalValidator.CurrentFamilies(lower, upper),
            (lower.RunsLowPass ? lower.AcousticLowPass : null) ?? (upper.RunsHighPass ? upper.AcousticHighPass : null),
            currentHz > 0 ? currentHz : null);
    }

    public static JunctionTuneOutcome NotInView() =>
        new([JunctionTuneLine.Of("The junction is no longer in this view.")], false, "Nothing to search.", true);

    public static JunctionTuneOutcome GateMisplaced() =>
        new(
            [JunctionTuneLine.Of("The phase gate is misplaced, so the junction cannot be read through it.")],
            false,
            "Place the gate first.",
            true);

    /// <param name="because">A phrase: capitalised and closed with a full stop here.</param>
    public static JunctionTuneOutcome Refusal(string because) =>
        new(
            [JunctionTuneLine.Of(because[..1].ToUpperInvariant() + because[1..] + ".")],
            false,
            "Refused.",
            true);

    /// <summary>The tuner's inputs for one request, or the refusal that stands in for a search.</summary>
    /// <param name="averageMode">The spatial average the hybrid view reads, null when it reads none.</param>
    public static (JunctionTunePlan? Plan, JunctionTuneOutcome? Refused) Plan(
        VirtualCrossoverSession session,
        AdjacentPair junction,
        JunctionTuneRequest request,
        VirtualCrossoverSpatialAverageMode? averageMode)
    {
        VirtualCrossoverChannel lower = junction.Lower.Channel;
        VirtualCrossoverChannel upper = junction.Upper.Channel;
        if (AgentJunctionTune.FirCrossoverRefusal(lower, upper) is { } fir)
        {
            return (null, Refusal(fir));
        }

        (List<JunctionTuneSide> sides, string? refusal) =
            AgentProbeReader.JunctionTuneSides(lower, upper, rightSideOnly: null);
        if (refusal != null)
        {
            return (null, Refusal(refusal));
        }

        if (request.AcousticGoal != null)
        {
            sides = AgentProbeReader.WithSpatialAverages(
                sides,
                lower,
                upper,
                averageMode,
                session.Calibration.SpatialAverageFor(),
                session.ProcessorSampleRateHz);
        }

        var options = new JunctionTuneOptions(
            request.Families,
            request.Slopes.Count > 0 ? request.Slopes : null,
            Math.Min(request.MinHz, request.MaxHz),
            Math.Max(request.MinHz, request.MaxHz),
            request.IndependentSlopes,
            session.ProcessorSampleRateHz,
            AcousticTarget: request.AcousticGoal,
            TargetCurveDb: request.AcousticGoal == null ? null : TargetCurvePoints(session),
            SumSlackDb: request.SumSlackDb,
            SplitCorners: request.SplitCorners,
            OneAlignmentForAllSides: AgentProbeReader.SharesOneAlignment(lower, upper));
        return (new JunctionTunePlan($"Junction tune {lower.Name}/{upper.Name}", lower, upper, sides, options), null);
    }

    /// <summary>The report, and Apply offered whenever it changes something, and only then.</summary>
    public static JunctionTuneOutcome Outcome(
        JunctionTunePlan plan, JunctionTuneRequest request, JunctionTuneResult result)
    {
        List<JunctionTuneLine> report = VirtualCrossoverJunctionTuneReport.Build(plan, result);
        string searched =
            $"{result.CandidatesEvaluated} candidates read over " +
            $"{plan.Options.MinCrossoverHz:0.###}–{plan.Options.MaxCrossoverHz:0.###} Hz.";
        JunctionTuneCandidate applied = result.Moves ? result.Best : result.Current;
        bool goalChanges = AgentJunctionTune.WouldChangeGoal(plan.Lower, plan.Upper, request.AcousticGoal, applied);
        bool goalLands = request.AcousticGoal != null && applied.AcousticGoalLands;
        bool forTheGoal = request.AcousticGoal != null &&
            result.Best.RankingScoreDb > result.Current.RankingScoreDb;
        string verdict = result.Changed
            ? forTheGoal
                ? "A crossover nearer the goal was found, within the budget; Apply writes it. "
                : "A better crossover was found; Apply writes it. "
            : result.Moves
                ? "Keeping the crossover on screen is recommended; Apply writes the found one anyway. "
                : goalChanges
                    ? "The crossover on screen is the best found; Apply writes the goal onto the cards. "
                    : "The crossover on screen is the best found; nothing to apply. ";
        return new JunctionTuneOutcome(
            report,
            result.Moves || goalChanges,
            verdict + searched,
            false,
            Recommended: (result.Changed || (!result.Moves && goalChanges)) &&
                (request.AcousticGoal == null || goalLands));
    }

    private static IReadOnlyList<SignalPoint> TargetCurvePoints(VirtualCrossoverSession session)
    {
        TargetCurveSpec spec = (session.Project.Target ?? new VirtualCrossoverTargetSettings())
            .ToCurve().Normalized().Spec;
        return EqualizationCurve.LogFrequencyGrid(20, 20_000, 400)
            .Select(hz => new SignalPoint(hz, spec.Evaluate(hz)))
            .ToList();
    }
}
