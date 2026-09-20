using System.Globalization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The junction tune's answer, short enough to read at a glance: the verdict, the two crossovers, one table of
/// readings, and the acoustic goal in three lines. What the search cost and how wide it looked belong in the status
/// line, not in the pane; the runners-up and the score arithmetic belong to whoever re-runs it with another window.
/// </summary>
/// <remarks>
/// Separate from <see cref="AgentJunctionTune.Describe"/> on purpose: that one writes one line per item for the AI
/// import's summary list, which is a different medium.
/// </remarks>
internal static class VirtualCrossoverJunctionTuneReport
{
    /// <summary>Lines the pane shows without scrolling at the designed size.</summary>
    public const int PaneLines = 16;

    public static List<string> Build(JunctionTunePlan plan, JunctionTuneResult result)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        string lower = plan.Lower.Name;
        string upper = plan.Upper.Name;
        bool moved = !SameEdges(result.Current, result.Best);
        var lines = new List<string>
        {
            result.Changed
                ? $"{lower}/{upper} — a better crossover was found."
                : $"{lower}/{upper} — the crossover on screen stands.",
            $"  now    {Edges(result.Current, lower, upper)}"
        };
        if (moved)
        {
            lines.Add(
                $"  best   {Edges(result.Best, lower, upper)}" +
                (result.Changed
                    ? string.Empty
                    : $"   ({Signed(result.Best.RankingScoreDb - result.Current.RankingScoreDb)} dB, not the " +
                      $"{Number(plan.Options.KeepMarginDb)} dB it takes)"));
        }

        lines.Add(string.Empty);
        Readings(lines, result, moved);
        if (plan.Options.AcousticTarget is { } asked)
        {
            lines.Add(string.Empty);
            Acoustic(lines, asked, result);
        }

        return lines;
    }

    /// <summary>One row per side. Where the answer moves the crossover the cells read "now → best", so the two
    /// states are compared on one line instead of in two tables.</summary>
    private static void Readings(List<string> lines, JunctionTuneResult result, bool moved)
    {
        // Header and rows share the field widths, so the columns line up by construction rather than by counting
        // spaces in a literal - which is exactly what a test caught them not doing.
        lines.Add(Row("side", "sum loss, dB", "dip, dB", "ripple, dB"));
        foreach (JunctionTuneReading now in result.Current.Sides)
        {
            JunctionTuneReading? best = result.Best.Sides.FirstOrDefault(side => side.Side == now.Side);
            lines.Add(moved && best != null
                ? Row(
                    now.Side,
                    Pair(now.LossDb, best.LossDb),
                    Pair(now.DipDb, best.DipDb),
                    Pair(now.RippleDb, best.RippleDb))
                : Row(now.Side, Number(now.LossDb), Number(now.DipDb), Number(now.RippleDb)));
        }

        // Timing's share of what is left, one line for every side rather than a row each.
        IReadOnlyList<JunctionTuneAlignment> aligned = moved && result.Changed
            ? result.BestAfterDelay
            : result.CurrentAfterDelay;
        if (aligned.Count > 0)
        {
            lines.Add(
                "  after the best delay: " +
                string.Join(", ", aligned.Select(item =>
                    $"{item.Side} {Number(item.LossDb)} dB at {Signed(item.ExtraDelayMs)} ms" +
                    (item.InvertUpper ? " inverted" : string.Empty))));
        }
    }

    private static void Acoustic(
        List<string> lines, JunctionAcousticTarget asked, JunctionTuneResult result)
    {
        JunctionTuneCandidate candidate = result.Changed ? result.Best : result.Current;
        JunctionAcousticFit? fit = candidate.Sides.FirstOrDefault()?.Acoustic;
        JunctionDriverSlopes? plant = result.DriverSlopes.FirstOrDefault();
        bool reached = CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb);
        lines.Add(
            $"  Acoustic {FirCrossoverDescription.FamilyName(asked.Family)} {asked.SlopeDbPerOctave}: " +
            $"off by {Number(candidate.AcousticCostDb)} dB, " +
            $"nearest any filter {Number(result.ClosestAcousticCostDb)} dB — " +
            (reached ? "reachable." : "OUT OF REACH."));
        lines.Add(
            $"    got {Number(fit?.LowerSlopeDbPerOctave)} / {Number(fit?.UpperSlopeDbPerOctave)} dB/oct " +
            $"against {Number(fit?.TargetSlopeDbPerOctave)} asked; the channels fall " +
            $"{Number(plant?.LowerDbPerOctave)} / {Number(plant?.UpperDbPerOctave)} alone.");
        // Three different answers, and the difference matters: the goal travels only where the crossover being
        // applied lands on it, so "the drivers could" is not the same as "this filter does".
        bool lands = CrossoverJunctionTuner.WasAcousticTargetReached(candidate.AcousticCostDb);
        lines.Add(lands
            ? "    Written onto these edges, so Auto Tune aims at it instead of the filter."
            : reached
                ? "    Within reach, but not by a filter that sums as well — so it is not carried."
                : "    Not carried to the fit: these drivers already fall too steeply for it.");
    }

    private static string Row(string side, string loss, string dip, string ripple) =>
        $"  {side,-6} {loss,12} {dip,12} {ripple,12}";

    private static string Pair(double now, double best) =>
        $"{Number(now)}→{Number(best)}";

    private static string Edges(JunctionTuneCandidate candidate, string lower, string upper) =>
        $"{lower} {(candidate.LowerLowPass is { } low ? "LP " + Edge(low) : "no low-pass")} + " +
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
