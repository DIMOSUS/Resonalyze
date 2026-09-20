using System.Globalization;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The junction tune's answer laid out for a monospace pane: what is on screen against what the search found, the
/// per-side readings as a table, and the acoustic goal's own block. Separate from
/// <see cref="AgentJunctionTune.Describe"/> on purpose — that one writes one line per item for the import's summary
/// list, which is a different medium and reads badly in columns.
/// </summary>
internal static class VirtualCrossoverJunctionTuneReport
{
    public static List<string> Build(JunctionTunePlan plan, JunctionTuneResult result)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        string lower = plan.Lower.Name;
        string upper = plan.Upper.Name;
        JunctionTuneOptions options = plan.Options;
        var lines = new List<string>
        {
            $"Junction {lower}/{upper} — {(result.Changed ? "a better crossover was found" : "the crossover on screen stands")}",
            string.Empty,
            $"  now       {Edges(result.Current, lower, upper)}"
        };
        if (!SameEdges(result.Current, result.Best))
        {
            lines.Add($"  best      {Edges(result.Best, lower, upper)}");
        }

        lines.Add(
            $"  score     {Db(result.Current.RankingScoreDb)} now, {Db(result.Best.RankingScoreDb)} best " +
            $"({Signed(result.Best.RankingScoreDb - result.Current.RankingScoreDb)} dB), " +
            $"kept unless better by {Number(options.KeepMarginDb)} dB");
        lines.Add(
            $"  searched  {result.CandidatesEvaluated} candidates over " +
            $"{Range(options.MinCrossoverHz, options.MaxCrossoverHz)}, " +
            $"ranked on {Range(result.RankingBandLowHz, result.RankingBandHighHz)}");
        lines.Add(string.Empty);

        // One table per state, so a column of numbers can be read down rather than hunted for in prose.
        Table(lines, "  Now", result.Current, result.CurrentAfterDelay);
        if (result.Changed || !SameEdges(result.Current, result.Best))
        {
            lines.Add(string.Empty);
            Table(lines, result.Changed ? "  After Apply" : "  Best on offer", result.Best, result.BestAfterDelay);
        }

        if (options.AcousticTarget is { } asked)
        {
            lines.Add(string.Empty);
            Acoustic(lines, asked, result);
        }

        if (result.RunnersUp.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("  Runners-up");
            foreach (JunctionTuneCandidate candidate in result.RunnersUp)
            {
                lines.Add(
                    $"    {Edges(candidate, lower, upper),-46} score {Db(candidate.RankingScoreDb)}" +
                    (candidate.AcousticCostDb is { } cost
                        ? $", acoustic {Number(cost)} dB"
                        : string.Empty));
            }
        }

        return lines;
    }

    private static void Table(
        List<string> lines,
        string heading,
        JunctionTuneCandidate candidate,
        IReadOnlyList<JunctionTuneAlignment> afterDelay)
    {
        lines.Add(heading);
        // Units in the heading, not in every cell: four columns of "-0.0 dB" read as noise.
        lines.Add("    side   sum loss, dB   dip, dB   ripple, dB   after the best delay");
        foreach (JunctionTuneReading side in candidate.Sides)
        {
            JunctionTuneAlignment? aligned = afterDelay.FirstOrDefault(item => item.Side == side.Side);
            lines.Add(
                $"    {side.Side,-6} {Number(side.LossDb),12} {Number(side.DipDb),9} {Number(side.RippleDb),12}   " +
                (aligned == null
                    ? "—"
                    : $"{Number(aligned.LossDb)} dB at {Signed(aligned.ExtraDelayMs)} ms" +
                      (aligned.InvertUpper ? ", upper inverted" : string.Empty)));
        }
    }

    private static void Acoustic(
        List<string> lines, JunctionAcousticTarget asked, JunctionTuneResult result)
    {
        JunctionTuneCandidate candidate = result.Changed ? result.Best : result.Current;
        JunctionAcousticFit? fit = candidate.Sides.FirstOrDefault()?.Acoustic;
        JunctionDriverSlopes? plant = result.DriverSlopes.FirstOrDefault();
        bool reached = CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb);
        lines.Add($"  Acoustic goal: {FirCrossoverDescription.FamilyName(asked.Family)} {asked.SlopeDbPerOctave}");
        lines.Add(
            $"    magnitude fit        {Number(candidate.AcousticCostDb),6} dB   " +
            $"(worst side {Number(candidate.WorstAcousticCostDb)} dB)");
        lines.Add(
            $"    nearest any filter   {Number(result.ClosestAcousticCostDb),6} dB   " +
            (reached ? "— reachable" : "— OUT OF REACH on these drivers"));
        lines.Add(
            $"    slopes, dB/oct       asked {Number(fit?.TargetSlopeDbPerOctave)}, " +
            $"got {Number(fit?.LowerSlopeDbPerOctave)} / {Number(fit?.UpperSlopeDbPerOctave)}, " +
            $"channels alone {Number(plant?.LowerDbPerOctave)} / {Number(plant?.UpperDbPerOctave)}");
        // All four figures are straight-line fits over the same region, which is what makes them comparable at all.
        lines.Add("    (every slope above is fitted the same way, so they compare with each other)");
        foreach (string sentence in reached
            ? [
                "The goal is written onto these edges, so Auto Tune aims at it instead of",
                fit is { ResidualDb: > 0 }
                    ? $"the electrical filter; the {Number(fit.ResidualDb)} dB left over is cuts, which it may make."
                    : "the electrical filter; what is left over would need a skirt boost, which it refuses."
              ]
            : new[]
              {
                "The goal is NOT written onto these edges: aiming the fit at a slope these",
                "drivers cannot reach makes the junction worse, measured."
              })
        {
            lines.Add("    " + sentence);
        }
    }

    private static string Edges(JunctionTuneCandidate candidate, string lower, string upper) =>
        $"{lower} {(candidate.LowerLowPass is { } low ? "LP " + Edge(low) : "no low-pass")}  +  " +
        $"{upper} {(candidate.UpperHighPass is { } high ? "HP " + Edge(high) : "no high-pass")}";

    private static string Edge(CrossoverEdge edge) =>
        $"{Short(edge.Family)}{edge.SlopeDbPerOctave} {Hz(edge.FrequencyHz)}";

    private static string Short(CrossoverFilterFamily family) => family switch
    {
        CrossoverFilterFamily.LinkwitzRiley => "LR",
        CrossoverFilterFamily.Butterworth => "BW",
        CrossoverFilterFamily.Bessel => "Bessel",
        _ => "Cheb"
    };

    private static string Db(double value) => Number(value) + " dB";

    private static string Range(double lowHz, double highHz) =>
        $"{lowHz.ToString("0.###", CultureInfo.InvariantCulture)}–{Hz(highHz)}";

    private static string Signed(double value) =>
        value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    /// <summary>One decimal, and never a minus sign in front of a zero: a reading of −0.04 dB printed as "-0.0"
    /// reads as a fault where it is in fact nothing at all.</summary>
    private static string Number(double? value) =>
        value is { } read
            ? (Math.Abs(read) < 0.05 ? 0 : read).ToString("0.0", CultureInfo.InvariantCulture)
            : "—";

    private static string Hz(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture) + " Hz";

    private static bool SameEdges(JunctionTuneCandidate a, JunctionTuneCandidate b) =>
        a.LowerLowPass.Equals(b.LowerLowPass) && a.UpperHighPass.Equals(b.UpperHighPass);
}
