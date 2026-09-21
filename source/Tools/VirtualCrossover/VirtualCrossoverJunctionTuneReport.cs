using System.Globalization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>How a figure reads against what it is compared with: better, worse, or neither.</summary>
internal enum JunctionTuneTone
{
    Plain,
    Better,
    Worse
}

/// <summary>A run of report text that carries one tone; the dialog paints it, a test reads it.</summary>
internal sealed record JunctionTuneSpan(string Text, JunctionTuneTone Tone = JunctionTuneTone.Plain);

/// <summary>One line of the report.</summary>
internal sealed record JunctionTuneLine(IReadOnlyList<JunctionTuneSpan> Spans)
{
    public static JunctionTuneLine Of(string text) => new([new JunctionTuneSpan(text)]);

    public string Text => string.Concat(Spans.Select(span => span.Text));
}

/// <summary>
/// The junction tune's answer, short enough to read at a glance and coloured where a figure moved: the verdict, the
/// two crossovers, one table of readings and the acoustic goal in three lines. What the search cost and how wide it
/// looked belong in the status line, not in the pane.
/// </summary>
/// <remarks>
/// Separate from <see cref="AgentJunctionTune.Describe"/> on purpose: that one writes one line per item for the AI
/// import's summary list, which is a different medium and has no colour to spend.
/// </remarks>
internal static class VirtualCrossoverJunctionTuneReport
{
    /// <summary>Lines the pane shows without scrolling at the designed size.</summary>
    public const int PaneLines = 16;

    /// <summary>Decibels a reading must move before it counts as having moved at all.</summary>
    private const double Noticeable = 0.05;

    private const int Cell = 12;

    public static List<JunctionTuneLine> Build(JunctionTunePlan plan, JunctionTuneResult result)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        string lower = plan.Lower.Name;
        string upper = plan.Upper.Name;
        bool moved = result.Moves;
        var lines = new List<JunctionTuneLine>
        {
            new([
                new JunctionTuneSpan($"{lower}/{upper} — "),
                result.Changed
                    ? new JunctionTuneSpan("a better crossover was found.", JunctionTuneTone.Better)
                    : moved
                        ? new JunctionTuneSpan("keeping the crossover on screen is recommended.")
                        : new JunctionTuneSpan("the crossover on screen is the best found.")
            ]),
            JunctionTuneLine.Of($"  now    {Edges(result.Current, lower, upper)}")
        };
        if (moved)
        {
            // The score is "lower is better", so a candidate that did not beat the current one reads as a plus.
            double delta = result.Best.RankingScoreDb - result.Current.RankingScoreDb;
            // "found" is the best CANDIDATE, which is not the same as advice: where it did not win, say so on the
            // same line, or the reader takes it for a recommendation. Apply writes it either way.
            lines.Add(JunctionTuneLine.Of(
                $"  found  {Edges(result.Best, lower, upper)}" +
                (result.Changed
                    ? string.Empty
                    : "   — not worth it" +
                      (delta >= 0
                          ? ": nothing on the lattice beat what you have"
                          : $": better by only {Number(-delta)} dB of the " +
                            $"{Number(plan.Options.KeepMarginDb)} dB it takes"))));
        }

        lines.Add(JunctionTuneLine.Of(string.Empty));
        Readings(lines, result, moved);
        if (plan.Options.AcousticTarget is { } asked)
        {
            lines.Add(JunctionTuneLine.Of(string.Empty));
            Acoustic(lines, asked, result);
        }

        return lines;
    }

    /// <summary>One row per side. Where the answer moves the crossover the cells read "now → best" and the second
    /// figure is coloured, which is the whole question a reader has: did this get better or worse?</summary>
    private static void Readings(List<JunctionTuneLine> lines, JunctionTuneResult result, bool moved)
    {
        lines.Add(JunctionTuneLine.Of(Row("side", "sum loss, dB", "dip, dB", "ripple, dB")));
        foreach (JunctionTuneReading now in result.Current.Sides)
        {
            JunctionTuneReading? best = result.Best.Sides.FirstOrDefault(side => side.Side == now.Side);
            if (!moved || best == null)
            {
                lines.Add(JunctionTuneLine.Of(
                    Row(now.Side, Number(now.LossDb), Number(now.DipDb), Number(now.RippleDb))));
                continue;
            }

            var spans = new List<JunctionTuneSpan> { new($"  {now.Side,-6}") };
            // Loss and dip are negative: nearer zero is better. Ripple is the other way round.
            Add(spans, now.LossDb, best.LossDb, higherIsBetter: true);
            Add(spans, now.DipDb, best.DipDb, higherIsBetter: true);
            Add(spans, now.RippleDb, best.RippleDb, higherIsBetter: false);
            lines.Add(new JunctionTuneLine(spans));
        }

        // Timing's share of what is left, one line for all the sides rather than a row each.
        IReadOnlyList<JunctionTuneAlignment> aligned = moved
            ? result.BestAfterDelay
            : result.CurrentAfterDelay;
        if (aligned.Count > 0)
        {
            lines.Add(JunctionTuneLine.Of(
                "  after the best delay: " +
                string.Join(", ", aligned.Select(item =>
                    $"{item.Side} {Number(item.LossDb)} dB at {Signed(item.ExtraDelayMs)} ms" +
                    (item.InvertUpper ? " inverted" : string.Empty)))));
        }
    }

    /// <summary>A "now→best" cell padded to the column width, with the tone on the second figure alone.</summary>
    private static void Add(List<JunctionTuneSpan> spans, double now, double best, bool higherIsBetter)
    {
        string value = Number(best);
        string cell = $" {Number(now)}→{value}".PadLeft(Cell + 1);
        double gain = higherIsBetter ? best - now : now - best;
        spans.Add(new JunctionTuneSpan(cell[..^value.Length]));
        spans.Add(new JunctionTuneSpan(
            value,
            Math.Abs(gain) < Noticeable
                ? JunctionTuneTone.Plain
                : gain > 0 ? JunctionTuneTone.Better : JunctionTuneTone.Worse));
    }

    private static void Acoustic(
        List<JunctionTuneLine> lines, JunctionAcousticTarget asked, JunctionTuneResult result)
    {
        // The crossover Apply would leave on screen: the found one wherever it differs.
        JunctionTuneCandidate candidate = result.Moves ? result.Best : result.Current;
        JunctionAcousticFit? fit = candidate.Sides.FirstOrDefault()?.Acoustic;
        JunctionDriverSlopes? plant = result.DriverSlopes.FirstOrDefault();
        bool anyFilterCould = CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb);
        // The difference matters: "these drivers could" is not "this filter does", and only the second carries the
        // goal on to the EQ stage.
        bool lands = CrossoverJunctionTuner.WasAcousticTargetReached(candidate.AcousticCostDb);
        // Say WHICH crossover the figures describe: the kept one and the challenger are different answers, and the
        // table above has just shown both.
        lines.Add(new JunctionTuneLine([
            new JunctionTuneSpan(
                $"  Acoustic {FirCrossoverDescription.FamilyName(asked.Family)} {asked.SlopeDbPerOctave}, " +
                $"{(result.Moves ? "as found" : "as it stands")}: " +
                $"off by {Number(candidate.AcousticCostDb)} dB, " +
                $"nearest any filter {Number(result.ClosestAcousticCostDb)} dB — "),
            anyFilterCould
                ? new JunctionTuneSpan("reachable.")
                : new JunctionTuneSpan("OUT OF REACH.", JunctionTuneTone.Worse)
        ]));
        lines.Add(JunctionTuneLine.Of(
            $"    got {Number(fit?.LowerSlopeDbPerOctave)} / {Number(fit?.UpperSlopeDbPerOctave)} dB/oct " +
            $"against {Number(fit?.TargetSlopeDbPerOctave)} asked; the channels fall " +
            $"{Number(plant?.LowerDbPerOctave)} / {Number(plant?.UpperDbPerOctave)} alone."));
        // The question a reader actually asks next is "so what WOULD land on it?" — which is the asked slope less
        // what the channels do by themselves, and the answer is usually a filter too soft to sum well.
        if (!lands && anyFilterCould && fit?.TargetSlopeDbPerOctave is { } askedSlope &&
            plant is { LowerDbPerOctave: { } plantLower, UpperDbPerOctave: { } plantUpper })
        {
            lines.Add(JunctionTuneLine.Of(
                $"    landing on it needs about {Number(Math.Max(0, askedSlope - plantLower))} / " +
                $"{Number(Math.Max(0, askedSlope - plantUpper))} dB/oct of filter — softer than this, " +
                "and a soft pair sums worse."));
        }

        lines.Add(new JunctionTuneLine([
            lands
                ? new JunctionTuneSpan(
                    "    Written onto these edges, so Auto Tune aims at it instead of the filter.",
                    JunctionTuneTone.Better)
                : anyFilterCould
                    ? new JunctionTuneSpan(
                        "    Within reach, but not by a filter that sums as well — so it is not carried.",
                        JunctionTuneTone.Worse)
                    : new JunctionTuneSpan(
                        "    Not carried to the fit: these drivers already fall too steeply for it.",
                        JunctionTuneTone.Worse)
        ]));
    }

    private static string Row(string side, string loss, string dip, string ripple) =>
        $"  {side,-6} {loss,Cell} {dip,Cell} {ripple,Cell}";

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
            ? (Math.Abs(read) < Noticeable ? 0 : read).ToString("0.0", CultureInfo.InvariantCulture)
            : "—";

    private static string Hz(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture) + " Hz";
}
