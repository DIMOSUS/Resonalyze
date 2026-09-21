using System.Globalization;
using Resonalyze.Dsp;

namespace Resonalyze;

internal enum JunctionTuneTone
{
    Plain,
    Better,
    Worse
}

internal sealed record JunctionTuneSpan(string Text, JunctionTuneTone Tone = JunctionTuneTone.Plain);

internal sealed record JunctionTuneLine(IReadOnlyList<JunctionTuneSpan> Spans)
{
    public static JunctionTuneLine Of(string text) => new([new JunctionTuneSpan(text)]);

    public string Text => string.Concat(Spans.Select(span => span.Text));
}

/// <summary>The dialog's report: columns and colour, where the AI summary (<see cref="AgentJunctionTune.Describe"/>)
/// writes one line per item.</summary>
internal static class VirtualCrossoverJunctionTuneReport
{
    private const double Noticeable = 0.05;

    private const int Cell = 12;

    public static List<JunctionTuneLine> Build(JunctionTunePlan plan, JunctionTuneResult result)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        string lower = plan.Lower.Name;
        string upper = plan.Upper.Name;
        bool moved = result.Moves;
        // A change the goal paid for in sum is not "better", or the headline contradicts the table.
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
            JunctionTuneLine.Of($"  now    {AgentJunctionTune.JunctionText(result.Current, lower, upper)}")
        };
        if (moved)
        {
            double delta = result.Best.RankingScoreDb - result.Current.RankingScoreDb;
            // Found is not advice: where it did not win, the same line says so.
            lines.Add(JunctionTuneLine.Of(
                $"  found  {AgentJunctionTune.JunctionText(result.Best, lower, upper)}" +
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

        // The re-alignment every figure above was read at, for the crossover the table ends on.
        IReadOnlyList<JunctionTuneAlignment> aligned = moved
            ? result.BestAfterDelay
            : result.CurrentAfterDelay;
        if (aligned.Count > 1 && oneShift)
        {
            // One shift, named once; the resulting polarity is still each side's own.
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
        JunctionAcousticMiss? worst = candidate.WorstAcousticChannel;
        bool lands = CrossoverJunctionTuner.WasAcousticTargetReached(worst?.ChargeDb);
        bool anyFilterCould = CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb);
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
        foreach (JunctionTuneReading side in candidate.Sides)
        {
            JunctionAcousticFit? fit = side.Acoustic;
            JunctionDriverSlopes? plant = result.DriverSlopes.FirstOrDefault(item => item.Side == side.Side);
            lines.Add(JunctionTuneLine.Of(
                $"    {side.Side,-6} got {Number(fit?.LowerSlopeDbPerOctave)} / {Number(fit?.UpperSlopeDbPerOctave)} " +
                $"dB/oct against {Number(fit?.TargetSlopeDbPerOctave)} asked; the channels fall " +
                $"{Number(plant?.LowerDbPerOctave)} / {Number(plant?.UpperDbPerOctave)} alone."));
        }

        string? tooSteep = TooSteepByItself(candidate, result.DriverSlopes, lower, upper);
        if (!lands && anyFilterCould && tooSteep == null && worst != null &&
            candidate.Sides.FirstOrDefault(side => side.Side == worst.Side)?.Acoustic?.TargetSlopeDbPerOctave
                is { } askedSlope &&
            result.DriverSlopes.FirstOrDefault(item => item.Side == worst.Side) is { } fall &&
            (worst.Upper ? fall.UpperDbPerOctave : fall.LowerDbPerOctave) is { } own)
        {
            lines.Add(JunctionTuneLine.Of(
                $"    landing {Channel(worst, lower, upper)} on it takes about " +
                $"{Number(Math.Max(0, askedSlope - own))} dB/oct of filter; a filter that soft sums worse."));
        }

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

    private static string Channel(JunctionAcousticMiss miss, string lower, string upper) =>
        $"{miss.Side} {(miss.Upper ? upper : lower)}";

    /// <summary>The first channel already falling faster than asked by itself, or null.</summary>
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

    private static string Signed(double value) =>
        value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    /// <summary>One decimal, never "-0.0", which reads as a fault.</summary>
    private static string Number(double? value) =>
        value is { } read
            ? (Math.Abs(read) < Noticeable ? 0 : read).ToString("0.0", CultureInfo.InvariantCulture)
            : "—";

}
