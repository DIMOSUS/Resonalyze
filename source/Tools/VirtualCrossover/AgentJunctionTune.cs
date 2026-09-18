using System.Globalization;
using System.Text;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>A junction tune a reply asked for, ready to run: its two blocks, the sides the tuner reads and the search.</summary>
internal sealed record JunctionTunePlan(
    string Label,
    VirtualCrossoverChannel Lower,
    VirtualCrossoverChannel Upper,
    List<JunctionTuneSide> Sides,
    JunctionTuneOptions Options);

/// <summary>Junction tune without a dialog: the reply's junction resolved to its blocks, the tuner's one crossover written to
/// both sides of both blocks (as the wizard writes and Undo AI import restores), and the summary lines it leaves.</summary>
internal static class AgentJunctionTune
{
    /// <returns>The plan, or the summary line saying why the tune is skipped.</returns>
    public static (JunctionTunePlan? Plan, string? Skipped) Prepare(
        TuneJunctionOperation operation,
        AgentSessionSnapshot snapshot,
        VirtualCrossoverSession session,
        bool gateMisplaced)
    {
        string? problem = AgentProposalValidator.ResolveJunction(
            snapshot, operation.JunctionId,
            out AgentChannelSnapshot? lowerSnapshot, out AgentChannelSnapshot? upperSnapshot);
        if (problem != null)
        {
            return (null, $"Junction tune {operation.JunctionId}: skipped ({problem.TrimEnd('.')}).");
        }

        string label = $"Junction tune {lowerSnapshot!.Block}/{upperSnapshot!.Block}";
        if (session.Block(lowerSnapshot.Block) is not { } lower ||
            session.Block(upperSnapshot.Block) is not { } upper)
        {
            return (null, $"{label}: skipped (the blocks changed while the import ran).");
        }
        if (gateMisplaced)
        {
            return (null, $"{label}: skipped (the phase gate is misplaced).");
        }

        (List<JunctionTuneSide> sides, string? sideRefusal) =
            AgentProbeReader.JunctionTuneSides(lower, upper, null);
        if (sideRefusal != null)
        {
            return (null, $"{label}: skipped ({sideRefusal}).");
        }

        double currentHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
            lowerSnapshot.Settings, upperSnapshot.Settings);
        (double defaultMinHz, double defaultMaxHz) = AgentProposalValidator.DefaultJunctionWindow(currentHz);
        var families = new List<CrossoverFilterFamily>();
        foreach (string name in operation.Families ?? [])
        {
            if (AgentOperations.TryParseName(name, out CrossoverFilterFamily family))
            {
                families.Add(family);
            }
        }
        if (families.Count == 0)
        {
            families.AddRange(AgentProposalValidator.CurrentFamilies(
                lowerSnapshot.Settings, upperSnapshot.Settings));
        }

        return (
            new JunctionTunePlan(
                label,
                lower,
                upper,
                sides,
                new JunctionTuneOptions(
                    families,
                    operation.Slopes,
                    operation.MinHz ?? defaultMinHz,
                    operation.MaxHz ?? defaultMaxHz,
                    // One slope for both edges unless the reply frees them: the free search costs slopes² per corner.
                    operation.IndependentSlopes ?? false,
                    session.ProcessorSampleRateHz)),
            null);
    }

    /// <summary>The winner's crossover on both sides of both blocks; a mono block takes it once.</summary>
    public static void Write(JunctionTuneResult result, VirtualCrossoverChannel lower, VirtualCrossoverChannel upper)
    {
        CrossoverEdge lowPass = result.Best.LowerLowPass!.Value;
        CrossoverEdge highPass = result.Best.UpperHighPass!.Value;
        foreach (bool rightSide in new[] { false, true })
        {
            if (!lower.Pair.Mono || !rightSide)
            {
                VirtualCrossoverChannelSettings settings = lower.SideSettings(rightSide);
                settings.LowPassEdge = lowPass;
                settings.CrossoverKind = settings.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass
                    ? CrossoverKind.BandPass
                    : CrossoverKind.LowPass;
            }
            if (!upper.Pair.Mono || !rightSide)
            {
                VirtualCrossoverChannelSettings settings = upper.SideSettings(rightSide);
                settings.HighPassEdge = highPass;
                settings.CrossoverKind = settings.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass
                    ? CrossoverKind.BandPass
                    : CrossoverKind.HighPass;
            }
        }
    }

    /// <summary>What the tune found: the crossover kept or applied, against the search it ran, with each side's readings.</summary>
    public static void Describe(List<string> summary, JunctionTunePlan plan, JunctionTuneResult result)
    {
        string lower = plan.Lower.Name;
        string upper = plan.Upper.Name;
        string before = JunctionText(result.Current, lower, upper);
        string after = JunctionText(result.Best, lower, upper);
        JunctionTuneOptions options = plan.Options;
        string window = $"{result.CandidatesEvaluated} candidates over {Hz(options.MinCrossoverHz)}–" +
            $"{Hz(options.MaxCrossoverHz)}, ranked on {Hz(result.RankingBandLowHz)}–{Hz(result.RankingBandHighHz)}";
        string scoreDelta = (result.Best.RankingScoreDb - result.Current.RankingScoreDb)
            .ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " dB on the score";
        summary.Add(result.Changed
            ? $"{plan.Label}: applied — {before} → {after} ({window}, {scoreDelta})."
            : $"{plan.Label}: kept — {before} stands; the best of {window} ({after}, {scoreDelta}) is not " +
                $"{options.KeepMarginDb.ToString("0.00", CultureInfo.InvariantCulture)} dB better on the score" +
                (result.Best.ScoreDb > result.Current.ScoreDb
                    ? ", or reads worse on its own junction band."
                    : "."));
        AppendReadings(summary, result, best: result.Changed);
    }

    // Readings on the package's octave-each-side junction band, so they compare with what the assistant read.
    private static void AppendReadings(List<string> summary, JunctionTuneResult result, bool best)
    {
        foreach (JunctionTuneReading current in result.Current.Sides)
        {
            JunctionTuneReading? tuned = best
                ? result.Best.Sides.FirstOrDefault(side => side.Side == current.Side)
                : null;
            string Pair(double was, double now) => tuned == null
                ? Db(was)
                : $"{Db(was)} → {Db(now)}";
            var line = new StringBuilder();
            line.Append("  ").Append(current.Side).Append(": sum loss ")
                .Append(Pair(current.LossDb, tuned?.LossDb ?? 0))
                .Append(", dip ").Append(Pair(current.DipDb, tuned?.DipDb ?? 0))
                .Append(", ripple ").Append(Pair(current.RippleDb, tuned?.RippleDb ?? 0));
            JunctionTuneAlignment? alignment = (best ? result.BestAfterDelay : result.CurrentAfterDelay)
                .FirstOrDefault(item => item.Side == current.Side);
            if (alignment != null)
            {
                line.Append("; after the best delay ").Append(Db(alignment.LossDb))
                    .Append(" at ").Append(alignment.ExtraDelayMs >= 0 ? "+" : string.Empty)
                    .Append(alignment.ExtraDelayMs.ToString("0.00", CultureInfo.InvariantCulture))
                    .Append(" ms on the upper block")
                    .Append(alignment.InvertUpper ? ", with it inverted" : string.Empty);
            }
            summary.Add(line.Append('.').ToString());
        }

        static string Db(double value) =>
            (value + 0).ToString("0.0", CultureInfo.InvariantCulture) + " dB";
    }

    private static string JunctionText(JunctionTuneCandidate candidate, string lowerBlock, string upperBlock) =>
        $"{lowerBlock} {(candidate.LowerLowPass is { } low ? "LP " + EdgeText(low) : "no low-pass")} + " +
        $"{upperBlock} {(candidate.UpperHighPass is { } high ? "HP " + EdgeText(high) : "no high-pass")}";

    private static string EdgeText(CrossoverEdge edge)
    {
        string family = edge.Family switch
        {
            CrossoverFilterFamily.LinkwitzRiley => "LR",
            CrossoverFilterFamily.Butterworth => "BW",
            CrossoverFilterFamily.Bessel => "Bessel",
            _ => "Cheb"
        };
        return $"{family}{edge.SlopeDbPerOctave} {Hz(edge.FrequencyHz)}";
    }

    private static string Hz(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture) + " Hz";
}
