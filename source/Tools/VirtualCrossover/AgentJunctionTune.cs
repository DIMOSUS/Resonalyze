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
    /// <param name="acoustic">
    /// The acoustic crossover this tune was asked for, or null for a plain one. Written onto the edges it wrote, so
    /// the EQ stage's target follows it instead of the electrical filter — see
    /// docs/specs/acoustic-crossover-target.md. A plain tune leaves whatever the card holds alone: the wish is the
    /// user's, shown on the channel card, not a by-product of this run.
    /// </param>
    public static void Write(
        JunctionTuneResult result,
        VirtualCrossoverChannel lower,
        VirtualCrossoverChannel upper,
        JunctionAcousticTarget? acoustic = null,
        bool applyCrossover = true)
    {
        // Keeping the crossover means keeping it: a run that only states a goal must not move the edges the report
        // just said were staying.
        JunctionTuneCandidate applied = applyCrossover ? result.Best : result.Current;
        // Only where the crossover being applied actually lands on the asked edge — not merely where some candidate
        // on the lattice could have. Measured, on eight cabins: aiming the EQ stage at a slope the channel does not
        // produce is the one thing that made the finished junction worse
        // (docs/specs/acoustic-crossover-target.md#6a).
        JunctionAcousticTarget? reached =
            CrossoverJunctionTuner.WasAcousticTargetReached(applied.AcousticCostDb)
                ? acoustic
                : null;
        foreach (bool rightSide in new[] { false, true })
        {
            if (!lower.Pair.Mono || !rightSide)
            {
                VirtualCrossoverChannelSettings settings = lower.SideSettings(rightSide);
                if (applyCrossover && applied.LowerLowPass is { } lowPass)
                {
                    settings.LowPassEdge = lowPass;
                    settings.CrossoverKind =
                        settings.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass
                            ? CrossoverKind.BandPass
                            : CrossoverKind.LowPass;
                }
                if (reached != null)
                {
                    settings.AcousticLowPass = reached;
                }
            }
            if (!upper.Pair.Mono || !rightSide)
            {
                VirtualCrossoverChannelSettings settings = upper.SideSettings(rightSide);
                if (applyCrossover && applied.UpperHighPass is { } highPass)
                {
                    settings.HighPassEdge = highPass;
                    settings.CrossoverKind =
                        settings.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass
                            ? CrossoverKind.BandPass
                            : CrossoverKind.HighPass;
                }
                if (reached != null)
                {
                    settings.AcousticHighPass = reached;
                }
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
        if (options.AcousticTarget is { } asked)
        {
            AppendAcoustic(summary, asked, result);
        }
    }

    /// <summary>
    /// What the stated acoustic slope came to: the magnitude fit against the best any allowed filter could reach,
    /// the slopes fitted alike so they compare with each other, and whether the goal travels on to the EQ stage.
    /// </summary>
    private static void AppendAcoustic(
        List<string> summary, JunctionAcousticTarget asked, JunctionTuneResult result)
    {
        JunctionTuneCandidate candidate = result.Changed ? result.Best : result.Current;
        bool reached = CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb);
        string family = asked.Family switch
        {
            CrossoverFilterFamily.LinkwitzRiley => "LR",
            CrossoverFilterFamily.Butterworth => "BW",
            CrossoverFilterFamily.Bessel => "Bessel",
            _ => "Cheb"
        };
        summary.Add(
            $"  acoustic {family}{asked.SlopeDbPerOctave} asked: magnitude fit " +
            $"{Number(candidate.AcousticCostDb)} dB, worst side {Number(candidate.WorstAcousticCostDb)} dB, " +
            $"the nearest any allowed filter reaches {Number(result.ClosestAcousticCostDb)} dB — " +
            (reached ? "reached." : "OUT OF REACH."));
        JunctionAcousticFit? fit = candidate.Sides.FirstOrDefault()?.Acoustic;
        JunctionDriverSlopes? plant = result.DriverSlopes.FirstOrDefault();
        if (fit != null || plant != null)
        {
            summary.Add(
                "  slopes over the handover, all fitted the same way: asked " +
                $"{Number(fit?.TargetSlopeDbPerOctave)}, got {Number(fit?.LowerSlopeDbPerOctave)} / " +
                $"{Number(fit?.UpperSlopeDbPerOctave)}, the channels alone {Number(plant?.LowerDbPerOctave)} / " +
                $"{Number(plant?.UpperDbPerOctave)} dB/oct.");
        }

        summary.Add(reached
            ? "  the goal is written onto these edges, so Auto Tune aims at it instead of the filter" +
              (fit is { ResidualDb: > 0 }
                  ? $"; the {Number(fit.ResidualDb)} dB left over is cuts, which it may make."
                  : "; what is left over would need a skirt boost, which it refuses.")
            : "  the goal is NOT written onto these edges: aiming the fit at a slope these drivers " +
              "cannot reach makes the junction worse, measured.");
    }

    private static string Number(double? value) =>
        value is { } read ? read.ToString("0.0", CultureInfo.InvariantCulture) : "—";

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
