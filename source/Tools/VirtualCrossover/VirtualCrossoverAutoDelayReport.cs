using System.Globalization;
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
    AlignmentDecisionKind? DelayKind,
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
/// The inputs of one Auto delay run as the dialog collected them: the scene
/// offset magnitude (ms, non-negative — the far side leads by this much),
/// the steering layout that decides which side is the far one (LHD: the
/// right side leads; RHD: the left side leads, the right lags), the gain
/// balance opt-in, and the near-side cut it aims for (dB, non-negative —
/// how much quieter the driver's side plays than the far one). Both
/// magnitudes are layout-neutral: the layout toggle, not the user, owns
/// every sign.
/// </summary>
internal sealed record AutoDelayRunRequest(
    double SceneOffsetMs,
    bool RightHandDrive,
    bool AdjustGains,
    double NearSideCutDb,
    // How far BEHIND the front stage the rear fill should arrive. Zero sums the
    // two coherently, which is what a second row of listeners wants; the default
    // is the precedence-effect offset that keeps the image on the dash for the
    // front seats while the rear adds room. Ignored by a project with no rear.
    double RearFillOffsetMs = 0)
{
    /// <summary>
    /// The tilt in the gain engine's LEFT-minus-RIGHT convention: the near
    /// side is the left one on LHD (left quieter, negative) and the right
    /// one on RHD (left louder, positive).
    /// </summary>
    public double LevelDifferenceDb =>
        RightHandDrive ? NearSideCutDb : -NearSideCutDb;
}

/// <summary>
/// A completed (not yet applied) Auto delay run: the per-channel outcomes,
/// the run mode and inputs, the formatted report the dialog shows, and the
/// diagnostic log the Apply step closes with the resulting metric.
/// </summary>
internal sealed record AutoDelayRunResult(
    IReadOnlyList<AutoDelayChannelOutcome> Outcomes,
    bool Stereo,
    AutoDelayRunRequest Request,
    string ReportText,
    StringBuilder Log);

/// <summary>
/// Renders the proposal as the monospace table the Auto delay dialog shows —
/// a channel per row, changes written "before -> after" and everything else
/// "value (kept)" — with a notes section carrying each decision's short
/// reasoning. Pure text-shaping, kept UI-free so it is unit-testable.
/// </summary>
internal static class VirtualCrossoverAutoDelayReport
{
    private const int DelayDecimals = 2;
    private const int GainDecimals = 1;

    public static string Format(
        IReadOnlyList<AutoDelayChannelOutcome> outcomes,
        bool stereo,
        AutoDelayRunRequest request,
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
                $"Scene offset {request.SceneOffsetMs:0.00} ms ") +
                (request.RightHandDrive
                    ? "(RHD: left side leads)"
                    : "(LHD: right side leads)") +
                (request.AdjustGains
                    ? FormattableString.Invariant(
                        $", near-side cut {Math.Abs(GainBalanceEngine.LevelDifferenceDb(request.LevelDifferenceDb)):0.0} dB")
                    : ""));
        }
        if (!request.AdjustGains)
        {
            text.AppendLine("Gains not adjusted (checkbox off).");
        }

        // The at-a-glance summary: what the proposal changes, what it buys
        // (the same averaged sum loss the metric read-out shows), and which
        // decisions deserve a second look. The table and notes below are the
        // detail behind these lines.
        text.AppendLine();
        AppendChangeSummary(text, outcomes, request.AdjustGains);
        AppendSumLossForecast(text, stereo, leftSumLoss, rightSumLoss);
        AppendLowConfidenceWarnings(text, outcomes);

        text.AppendLine();
        AppendTable(text, outcomes);

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
        text.AppendLine("Table — \"before -> after\" marks a value the proposal changes,");
        text.AppendLine("        \"value (kept)\" one it leaves alone (a gain also reads");
        text.AppendLine("        kept when the run left that channel out of the balance).");
        text.AppendLine("Confidence — how decisively the measurement supported the choice:");
        text.AppendLine("  delay: the chosen alignment's score margin over rival");
        text.AppendLine("         lobes and polarity;");
        text.AppendLine("         locked = pinned by an onset/scene constraint (the");
        text.AppendLine("         constraint chose, not the acoustics), ref = the fixed");
        text.AppendLine("         anchor the others align to;");
        text.AppendLine("  gain:  how flat the level relation is across the band");
        text.AppendLine("         (the L-R difference for right channels).");
        return text.ToString();
    }

    // What the proposal actually changes, named channel by channel: most rows
    // of a run are kept, so the two or three the engine wants to move are the
    // whole story, and naming them saves hunting the table for the arrows.
    private static void AppendChangeSummary(
        StringBuilder text,
        IReadOnlyList<AutoDelayChannelOutcome> outcomes,
        bool adjustGains)
    {
        if (!outcomes.Any(outcome =>
                DelayChanged(outcome)
                || PolarityChanged(outcome)
                || GainChanged(outcome)))
        {
            text.AppendLine(
                "Changes: none — the current settings already match the proposal.");
            return;
        }

        var parts = new List<string>
        {
            ChangeList("delay", "delays", outcomes.Where(DelayChanged)),
            ChangeList("polarity", "polarities", outcomes.Where(PolarityChanged))
        };
        // With the checkbox off the gain part would read "no gain changes" on
        // every run, right under the line that already said gains are off.
        if (adjustGains)
        {
            parts.Add(ChangeList("gain", "gains", outcomes.Where(GainChanged)));
        }

        text.AppendLine($"Changes: {string.Join(", ", parts)}");
    }

    private static string ChangeList(
        string singular, string plural, IEnumerable<AutoDelayChannelOutcome> changed)
    {
        string[] names = changed.Select(outcome => outcome.Name).ToArray();
        return names.Length == 0
            ? $"no {singular} changes"
            : $"{names.Length} {(names.Length == 1 ? singular : plural)} " +
              $"({string.Join(", ", names)})";
    }

    private static void AppendSumLossForecast(
        StringBuilder text,
        bool stereo,
        AutoDelaySumLossForecast? leftSumLoss,
        AutoDelaySumLossForecast? rightSumLoss)
    {
        AutoDelaySumLossForecast[] forecasts = new[] { leftSumLoss, rightSumLoss }
            .OfType<AutoDelaySumLossForecast>()
            .ToArray();
        if (forecasts.Length == 0)
        {
            return;
        }

        int width = NumberWidth(
            forecasts.SelectMany(forecast => new[] { forecast.BeforeDb, forecast.AfterDb }),
            GainDecimals);
        text.AppendLine("Predicted sum loss (avg over the crossover window):");
        if (!stereo)
        {
            text.AppendLine($"  {SumLossCell(forecasts[0], width)}");
            return;
        }

        if (leftSumLoss != null)
        {
            text.AppendLine($"  Left   {SumLossCell(leftSumLoss, width)}");
        }
        if (rightSumLoss != null)
        {
            text.AppendLine($"  Right  {SumLossCell(rightSumLoss, width)}");
        }
    }

    // This forecast is the reason to press Apply, so it states what the
    // proposal buys instead of leaving two similar numbers to be subtracted
    // by eye — and says "unchanged" where it buys nothing.
    private static string SumLossCell(AutoDelaySumLossForecast forecast, int width)
    {
        string before = Fixed(forecast.BeforeDb, GainDecimals).PadLeft(width);
        string after = Fixed(forecast.AfterDb, GainDecimals).PadLeft(width);
        if (before == after)
        {
            return $"{after} dB (unchanged)";
        }

        // Read off the ROUNDED figures this line prints, not the raw ones: a
        // pair straddling a rounding boundary (-2.449 -> -2.451) would
        // otherwise show an arrow that moved 0.1 dB next to "0.0 dB worse".
        double gained = Rounded(forecast.AfterDb, GainDecimals)
            - Rounded(forecast.BeforeDb, GainDecimals);
        return $"{before} -> {after} dB " +
            $"({Fixed(Math.Abs(gained), GainDecimals)} dB " +
            $"{(gained > 0 ? "better" : "worse")})";
    }

    // One line per kind, naming the channels. The reasons stay in the notes:
    // repeating them here doubled the longest lines of the report and made
    // the reader meet the same sentence twice.
    private static void AppendLowConfidenceWarnings(
        StringBuilder text, IReadOnlyList<AutoDelayChannelOutcome> outcomes)
    {
        AppendLowConfidence(text, "delay", outcomes
            .Where(outcome => outcome.DelayConfidence == AlignmentConfidence.Low));
        AppendLowConfidence(text, "gain", outcomes
            .Where(outcome => outcome.GainConfidence == AlignmentConfidence.Low));
    }

    private static void AppendLowConfidence(
        StringBuilder text, string noun, IEnumerable<AutoDelayChannelOutcome> low)
    {
        string[] names = low.Select(outcome => outcome.Name).ToArray();
        if (names.Length == 0)
        {
            return;
        }

        text.AppendLine(
            $"Warning: LOW {noun} confidence — {string.Join(", ", names)} " +
            "(reasons in Notes)");
    }

    private static void AppendTable(
        StringBuilder text, IReadOnlyList<AutoDelayChannelOutcome> outcomes)
    {
        // Numbers are padded to a shared width BEFORE the columns themselves
        // are measured, so decimal points line up down the page: a two-digit
        // delay used to push its whole row sideways.
        int delayWidth = NumberWidth(
            outcomes.SelectMany(outcome =>
                new[] { outcome.BeforeDelayMs, outcome.AfterDelayMs }),
            DelayDecimals);
        int gainWidth = NumberWidth(
            outcomes.SelectMany(outcome =>
                new[] { outcome.BeforeGainDb, outcome.AfterGainDb }),
            GainDecimals);
        // A gain confidence exists only where the balance actually scored a
        // channel; with the checkbox off the column is a wall of dashes, so
        // it is dropped rather than shown empty.
        bool gainConfidence = outcomes.Any(outcome => outcome.GainConfidence != null);

        List<string> header =
            ["Channel", "Delay, ms", "Polarity", "Gain, dB", "Delay conf"];
        if (gainConfidence)
        {
            header.Add("Gain conf");
        }

        List<string[]> rows = outcomes
            .Select(outcome =>
            {
                List<string> cells =
                [
                    outcome.Name,
                    ValueCell(
                        outcome.BeforeDelayMs, outcome.AfterDelayMs,
                        DelayChanged(outcome), DelayDecimals, delayWidth),
                    PolarityCell(outcome.BeforeInvert, outcome.AfterInvert),
                    ValueCell(
                        outcome.BeforeGainDb, outcome.AfterGainDb,
                        GainChanged(outcome), GainDecimals, gainWidth),
                    DelayCell(outcome.DelayKind, outcome.DelayConfidence)
                ];
                if (gainConfidence)
                {
                    cells.Add(ConfidenceCell(outcome.GainConfidence));
                }

                return cells.ToArray();
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
    }

    // "2.43 (kept)" where the proposal leaves a value alone: the old
    // "2.43 -> 2.43" made every row look like a change and buried the two
    // rows that were one.
    private static string ValueCell(
        double before, double after, bool changed, int decimals, int width)
    {
        string text = Fixed(before, decimals).PadLeft(width);
        return changed
            ? $"{text} -> {Fixed(after, decimals).PadLeft(width)}"
            : $"{text} (kept)";
    }

    // The delay column carries decision KINDS beyond confidence: a locked
    // pick was chosen by its constraint and the reference was not chosen at
    // all, so showing a confidence for either would misread as the
    // measurement's vote.
    private static string DelayCell(
        AlignmentDecisionKind? kind, AlignmentConfidence? confidence) =>
        kind switch
        {
            AlignmentDecisionKind.Reference => "ref",
            AlignmentDecisionKind.Locked => "locked",
            _ => ConfidenceCell(confidence)
        };

    private static string PolarityCell(bool beforeInvert, bool afterInvert)
    {
        string after = afterInvert ? "inv" : "norm";
        if (beforeInvert == afterInvert)
        {
            return after;
        }

        // Padded to the longer token so the arrows sit under each other, for
        // the same reason the numbers carry their own padding.
        string before = (beforeInvert ? "inv" : "norm").PadRight("norm".Length);
        return $"{before} -> {after}";
    }

    private static string ConfidenceCell(AlignmentConfidence? confidence) =>
        confidence switch
        {
            AlignmentConfidence.High => "high",
            AlignmentConfidence.Medium => "medium",
            AlignmentConfidence.Low => "LOW",
            _ => "-"
        };

    // A value counts as changed when the report would print a different
    // number for it, so the summary's channel list and the table's arrows are
    // always the same set and a move too small to show never claims a row.
    private static bool DelayChanged(AutoDelayChannelOutcome outcome) =>
        Fixed(outcome.BeforeDelayMs, DelayDecimals)
            != Fixed(outcome.AfterDelayMs, DelayDecimals);

    private static bool PolarityChanged(AutoDelayChannelOutcome outcome) =>
        outcome.AfterInvert != outcome.BeforeInvert;

    private static bool GainChanged(AutoDelayChannelOutcome outcome) =>
        outcome.GainAdjusted &&
        Fixed(outcome.BeforeGainDb, GainDecimals)
            != Fixed(outcome.AfterGainDb, GainDecimals);

    private static int NumberWidth(IEnumerable<double> values, int decimals) =>
        values.Select(value => Fixed(value, decimals).Length).DefaultIfEmpty(0).Max();

    // Rounding before formatting keeps a hair-negative value from printing as
    // "-0.00" and reading as a change against a plain "0.00".
    private static string Fixed(double value, int decimals)
    {
        double rounded = Rounded(value, decimals);
        return (rounded == 0 ? 0 : rounded)
            .ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    // What the report prints, as a number: the one place that decides how a
    // figure rounds, so a difference taken between two of them agrees with
    // the two figures themselves.
    private static double Rounded(double value, int decimals) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    private static void AppendRow(
        StringBuilder text, IReadOnlyList<string> cells, IReadOnlyList<int> widths)
    {
        var line = new StringBuilder();
        for (int column = 0; column < cells.Count; column++)
        {
            if (column > 0)
            {
                line.Append("  ");
            }

            // Every column starts at its left edge: the cells carry their own
            // internal padding, so right-aligning them as well would pull the
            // short ones away from the header they belong to.
            line.Append(cells[column].PadRight(widths[column]));
        }

        text.AppendLine(line.ToString().TrimEnd());
    }
}
