using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One channel's before/after row of an Auto delay proposal: the settings
/// object the Apply step writes to, the display values, and the per-decision
/// confidence the engines reported. Gains carry their own adjusted flag —
/// a channel outside the gain balance (mono, no crossover, band below the
/// localization floor, or gains not requested at all) keeps its gain.
/// </summary>
internal sealed record AutoDelayChannelOutcome(
    VirtualCrossoverChannel Runtime,
    VirtualCrossoverChannelSettings Settings,
    string Name,
    double BeforeDelayMs,
    bool BeforeInvert,
    double BeforeGainDb,
    double AfterDelayMs,
    bool AfterInvert,
    double AfterGainDb,
    bool GainAdjusted,
    AlignmentConfidence? DelayConfidence,
    string DelayDetail,
    AlignmentConfidence? GainConfidence,
    string GainDetail);

/// <summary>
/// One side's predicted average summation loss (dB, &lt;= 0 — how far the
/// coherent sum falls short of the phase-blind magnitude sum over the
/// crossover window), for the current settings and for the proposal. The
/// report's headline figure.
/// </summary>
internal sealed record AutoDelaySumLossForecast(double BeforeDb, double AfterDb);

/// <summary>
/// A completed (not yet applied) Auto delay run: the per-channel outcomes,
/// the run mode and inputs, the formatted report the dialog shows, and the
/// diagnostic log the Apply step closes with the resulting metric.
/// </summary>
internal sealed record AutoDelayRunResult(
    IReadOnlyList<AutoDelayChannelOutcome> Outcomes,
    bool Stereo,
    double SceneOffsetMs,
    bool GainsRequested,
    string ReportText,
    StringBuilder Log);

/// <summary>
/// Renders the proposal as the monospace before/after table the Auto delay
/// dialog shows, with a notes section carrying each decision's short
/// reasoning. Pure text-shaping, kept UI-free so it is unit-testable.
/// </summary>
internal static class VirtualCrossoverAutoDelayReport
{
    public static string Format(
        IReadOnlyList<AutoDelayChannelOutcome> outcomes,
        bool stereo,
        double sceneOffsetMs,
        bool gainsRequested,
        AutoDelaySumLossForecast? leftSumLoss = null,
        AutoDelaySumLossForecast? rightSumLoss = null)
    {
        // Invariant numbers throughout: the report is a shareable diagnostic
        // artifact, so it must read the same regardless of the OS locale.
        var text = new StringBuilder();
        text.AppendLine(
            $"Auto delay proposal ({(stereo ? "stereo" : "single side")})  " +
            $"{DateTime.Now:yyyy-MM-dd HH:mm}");
        if (stereo)
        {
            text.AppendLine(FormattableString.Invariant(
                $"Scene offset {sceneOffsetMs:+0.00;-0.00;0.00} ms ") +
                "(positive: right side leads)" +
                (gainsRequested
                    ? FormattableString.Invariant(
                        $", gain tilt {GainBalanceEngine.SceneTiltDb(sceneOffsetMs):+0.0;-0.0;0.0} dB")
                    : ""));
        }
        if (!gainsRequested)
        {
            text.AppendLine("Gains not adjusted (checkbox off).");
        }

        // The at-a-glance summary: what the proposal changes, what it buys
        // (the same averaged sum loss the metric read-out shows), and which
        // decisions deserve a second look. The table and notes below are the
        // detail behind these lines.
        int delayChanges = outcomes.Count(outcome =>
            Math.Abs(outcome.AfterDelayMs - outcome.BeforeDelayMs) >= 0.005);
        int polarityChanges = outcomes.Count(outcome =>
            outcome.AfterInvert != outcome.BeforeInvert);
        int gainChanges = outcomes.Count(outcome =>
            outcome.GainAdjusted &&
            Math.Abs(outcome.AfterGainDb - outcome.BeforeGainDb) >= 0.05);
        text.AppendLine();
        text.AppendLine(
            $"Changes: {delayChanges} delays, {polarityChanges} polarities, " +
            $"{gainChanges} gains");
        if (stereo && (leftSumLoss != null || rightSumLoss != null))
        {
            text.AppendLine("Predicted sum loss (avg over the crossover window):");
            if (leftSumLoss != null)
            {
                text.AppendLine(FormattableString.Invariant(
                    $"  Left   {leftSumLoss.BeforeDb:0.0} -> {leftSumLoss.AfterDb:0.0} dB"));
            }
            if (rightSumLoss != null)
            {
                text.AppendLine(FormattableString.Invariant(
                    $"  Right  {rightSumLoss.BeforeDb:0.0} -> {rightSumLoss.AfterDb:0.0} dB"));
            }
        }
        else if (leftSumLoss != null)
        {
            string span = FormattableString.Invariant(
                $"{leftSumLoss.BeforeDb:0.0} -> {leftSumLoss.AfterDb:0.0} dB");
            text.AppendLine(
                $"Predicted sum loss: {span} (avg over the crossover window)");
        }
        foreach (AutoDelayChannelOutcome outcome in outcomes)
        {
            if (outcome.DelayConfidence == AlignmentConfidence.Low)
            {
                text.AppendLine(
                    $"Warning: {outcome.Name} delay has LOW confidence " +
                    $"({outcome.DelayDetail})");
            }
            if (outcome.GainConfidence == AlignmentConfidence.Low)
            {
                text.AppendLine(
                    $"Warning: {outcome.Name} gain has LOW confidence " +
                    $"({outcome.GainDetail})");
            }
        }

        text.AppendLine();
        string[] header =
            ["Channel", "Delay, ms", "Polarity", "Gain, dB", "Delay conf", "Gain conf"];
        List<string[]> rows = outcomes
            .Select(outcome => new[]
            {
                outcome.Name,
                FormattableString.Invariant(
                    $"{outcome.BeforeDelayMs:0.00} -> {outcome.AfterDelayMs:0.00}"),
                PolarityCell(outcome.BeforeInvert, outcome.AfterInvert),
                outcome.GainAdjusted
                    ? FormattableString.Invariant(
                        $"{outcome.BeforeGainDb:0.0} -> {outcome.AfterGainDb:0.0}")
                    : FormattableString.Invariant(
                        $"{outcome.BeforeGainDb:0.0} (kept)"),
                ConfidenceCell(outcome.DelayConfidence),
                ConfidenceCell(outcome.GainConfidence)
            })
            .ToList();

        int[] widths = header
            .Select((title, column) => Math.Max(
                title.Length,
                rows.Count == 0 ? 0 : rows.Max(row => row[column].Length)))
            .ToArray();
        AppendRow(text, header, widths);
        AppendRow(text, widths.Select(width => new string('-', width)).ToArray(), widths);
        foreach (string[] row in rows)
        {
            AppendRow(text, row, widths);
        }

        // Short lines throughout the prose sections: the dialog's report box
        // wraps instead of scrolling horizontally, and a soft wrap in the
        // middle of a note reads worse than these explicit two-line blocks.
        text.AppendLine();
        text.AppendLine("Notes:");
        foreach (AutoDelayChannelOutcome outcome in outcomes)
        {
            if (outcome.DelayDetail.Length == 0 && outcome.GainDetail.Length == 0)
            {
                continue;
            }

            text.AppendLine($"  {outcome.Name}");
            if (outcome.DelayDetail.Length > 0)
            {
                text.AppendLine($"    delay: {outcome.DelayDetail}");
            }
            if (outcome.GainDetail.Length > 0)
            {
                text.AppendLine($"    gain:  {outcome.GainDetail}");
            }
        }

        text.AppendLine();
        text.AppendLine("Confidence — how decisively the measurement supported the choice:");
        text.AppendLine("  delay: the chosen alignment's score margin over rival");
        text.AppendLine("         lobes and polarity (locks pin to measured physics);");
        text.AppendLine("  gain:  how flat the level relation is across the band");
        text.AppendLine("         (the L-R difference for right channels).");
        return text.ToString();
    }

    private static string PolarityCell(bool beforeInvert, bool afterInvert)
    {
        string before = beforeInvert ? "inv" : "norm";
        string after = afterInvert ? "inv" : "norm";
        return beforeInvert == afterInvert ? after : $"{before} -> {after}";
    }

    private static string ConfidenceCell(AlignmentConfidence? confidence) =>
        confidence switch
        {
            AlignmentConfidence.High => "high",
            AlignmentConfidence.Medium => "medium",
            AlignmentConfidence.Low => "LOW",
            _ => "-"
        };

    private static void AppendRow(StringBuilder text, string[] cells, int[] widths)
    {
        var line = new StringBuilder();
        for (int column = 0; column < cells.Length; column++)
        {
            if (column > 0)
            {
                line.Append("  ");
            }

            // Numbers and confidences read best right-aligned; the name column
            // stays left-aligned.
            line.Append(column == 0
                ? cells[column].PadRight(widths[column])
                : cells[column].PadLeft(widths[column]));
        }

        text.AppendLine(line.ToString().TrimEnd());
    }
}
