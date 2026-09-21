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
        // A change the stated slope paid for in summation is nearer the goal, not better as a sum; the table says
        // what it cost, and the headline must not contradict it.
        bool forTheGoal = plan.Options.AcousticTarget != null &&
            result.Best.RankingScoreDb > result.Current.RankingScoreDb;
        var lines = new List<JunctionTuneLine>
        {
            new([
                new JunctionTuneSpan($"{lower}/{upper} — "),
                result.Changed
                    ? new JunctionTuneSpan(
                        forTheGoal
                            ? "a crossover nearer the acoustic goal was found."
                            : "a better crossover was found.",
                        JunctionTuneTone.Better)
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
        Readings(lines, result, moved, upper, plan.Options.OneAlignmentForAllSides);
        if (plan.Options.AcousticTarget is { } asked)
        {
            lines.Add(JunctionTuneLine.Of(string.Empty));
            Acoustic(lines, asked, result, plan.Options.SumSlackDb, lower, upper);
        }

        return lines;
    }

    /// <summary>One row per side. Where the answer moves the crossover the cells read "now → best" and the second
    /// figure is coloured, which is the whole question a reader has: did this get better or worse?</summary>
    private static void Readings(
        List<JunctionTuneLine> lines, JunctionTuneResult result, bool moved, string upper, bool oneShift)
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

        // Every figure above is read after re-aligning the upper channel, since a junction tune is followed by
        // re-tuning the delays: this line says what that re-alignment is, for the crossover the table ends on.
        IReadOnlyList<JunctionTuneAlignment> aligned = moved
            ? result.BestAfterDelay
            : result.CurrentAfterDelay;
        if (aligned.Count > 1 && oneShift)
        {
            // A mono block has one delay, so every side was read at one shift: named once, or the same figure
            // listed per side reads as two settings. The resulting polarity is still each side's own.
            string inverted = string.Join(", ", aligned.Where(item => item.InvertUpper).Select(item => item.Side));
            lines.Add(JunctionTuneLine.Of(
                $"  read after one shift of {upper} for both sides (a mono block has one delay): " +
                $"{Signed(aligned[0].ExtraDelayMs)} ms" +
                (inverted.Length == 0 ? string.Empty : $", inverted on {inverted}")));
        }
        else if (aligned.Count > 0)
        {
            lines.Add(JunctionTuneLine.Of(
                $"  read after re-aligning {upper}: " +
                string.Join(", ", aligned.Select(item =>
                    $"{item.Side} {Signed(item.ExtraDelayMs)} ms" +
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
        List<JunctionTuneLine> lines,
        JunctionAcousticTarget asked,
        JunctionTuneResult result,
        double budgetDb,
        string lower,
        string upper)
    {
        // The crossover Apply would leave on screen: the found one wherever it differs.
        JunctionTuneCandidate candidate = result.Moves ? result.Best : result.Current;
        // Whether the goal lands is the worst channel's question: every channel's EQ aims at it by itself, so an
        // average over the channels can pass while one of them is left short of it.
        JunctionAcousticMiss? worst = candidate.WorstAcousticChannel;
        bool lands = CrossoverJunctionTuner.WasAcousticTargetReached(worst?.ChargeDb);
        // The difference matters: "some allowed filter could" is not "this filter does". The goal is written as
        // asked either way; what differs is whether Auto Tune will then aim at a slope the filter actually makes.
        bool anyFilterCould = CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb);
        // Say WHICH crossover the figures describe: the kept one and the challenger are different answers, and the
        // table above has just shown both.
        lines.Add(JunctionTuneLine.Of(
            $"  Acoustic {FirCrossoverDescription.FamilyName(asked.Family)} {asked.SlopeDbPerOctave}, " +
            $"{(result.Moves ? "as found" : "as it stands")}: off by {Number(worst?.ChargeDb)} dB at worst" +
            (worst == null ? string.Empty : $" ({Channel(worst, lower, upper)})") +
            $", {Number(candidate.AcousticCostDb)} on average."));
        lines.Add(new JunctionTuneLine([
            new JunctionTuneSpan($"    nearest any filter: {Number(result.ClosestAcousticCostDb)} dB at worst — "),
            anyFilterCould
                ? new JunctionTuneSpan("reachable.")
                : new JunctionTuneSpan("OUT OF REACH.", JunctionTuneTone.Worse)
        ]));
        // Side by side: one electrical filter serves both, and the drivers on the two sides do not fall alike.
        foreach (JunctionTuneReading side in candidate.Sides)
        {
            JunctionAcousticFit? fit = side.Acoustic;
            JunctionDriverSlopes? plant = result.DriverSlopes.FirstOrDefault(item => item.Side == side.Side);
            lines.Add(JunctionTuneLine.Of(
                $"    {side.Side,-6} got {Number(fit?.LowerSlopeDbPerOctave)} / {Number(fit?.UpperSlopeDbPerOctave)} " +
                $"dB/oct against {Number(fit?.TargetSlopeDbPerOctave)} asked; the channels fall " +
                $"{Number(plant?.LowerDbPerOctave)} / {Number(plant?.UpperDbPerOctave)} alone."));
        }

        // A channel falling faster than asked all by itself is out of reach of any filter, which only steepens.
        string? tooSteep = TooSteepByItself(candidate, result.DriverSlopes, lower, upper);
        // The question a reader asks next is "so what WOULD land on it?": the asked slope less what the channel
        // that misses most does by itself, and the answer is usually a filter too soft to sum well.
        if (!lands && anyFilterCould && tooSteep == null && worst is { Upper: { } missesUpper } &&
            candidate.Sides.FirstOrDefault(side => side.Side == worst.Side)?.Acoustic?.TargetSlopeDbPerOctave
                is { } askedSlope &&
            result.DriverSlopes.FirstOrDefault(item => item.Side == worst.Side) is { } fall &&
            (missesUpper ? fall.UpperDbPerOctave : fall.LowerDbPerOctave) is { } own)
        {
            lines.Add(JunctionTuneLine.Of(
                $"    landing {Channel(worst, lower, upper)} on it takes about " +
                $"{Number(Math.Max(0, askedSlope - own))} dB/oct of filter; a filter that soft sums worse."));
        }

        // What the goal was paid for with: the found crossover against the best sum on the lattice, both read
        // re-aligned. Said only where it cost something, and against the budget the user set.
        if (result.Moves && result.BestSumScoreDb is { } bestSum &&
            result.Best.RankingScoreDb - bestSum >= Noticeable)
        {
            lines.Add(JunctionTuneLine.Of(
                $"    it costs {Number(result.Best.RankingScoreDb - bestSum)} dB of summation score against the " +
                $"best sum here (budget {Number(budgetDb)} dB)."));
        }

        lines.Add(new JunctionTuneLine([
            lands
                ? new JunctionTuneSpan(
                    "    Apply writes it onto the cards; Auto Tune aims at it instead of the filter.",
                    JunctionTuneTone.Better)
                : new JunctionTuneSpan(
                    "    Apply writes it anyway, and Auto Tune will aim at it; " +
                    (tooSteep != null
                        ? $"{tooSteep} alone already falls faster."
                        : anyFilterCould
                            ? "a filter that lands on it sums worse."
                            : "no filter in this search lands on it."),
                    JunctionTuneTone.Worse)
        ]));
    }

    /// <summary>"right C": the side, and the block when the channel is known.</summary>
    private static string Channel(JunctionAcousticMiss miss, string lower, string upper) =>
        miss.Upper is { } isUpper ? $"{miss.Side} {(isUpper ? upper : lower)}" : miss.Side;

    /// <summary>The first channel whose own fall, with the facing edge taken out, is already steeper than the asked
    /// slope: a filter multiplies, so no crossover can make that channel softer. Null where every channel could.</summary>
    private static string? TooSteepByItself(
        JunctionTuneCandidate candidate, IReadOnlyList<JunctionDriverSlopes> slopes, string lower, string upper)
    {
        foreach (JunctionTuneReading side in candidate.Sides)
        {
            if (side.Acoustic?.TargetSlopeDbPerOctave is not { } asked ||
                slopes.FirstOrDefault(item => item.Side == side.Side) is not { } fall)
            {
                continue;
            }

            if (!CrossoverJunctionTuner.IsReachable(fall.LowerDbPerOctave, asked))
            {
                return $"{side.Side} {lower}";
            }
            if (!CrossoverJunctionTuner.IsReachable(fall.UpperDbPerOctave, asked))
            {
                return $"{side.Side} {upper}";
            }
        }

        return null;
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
