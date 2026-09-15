using System.Globalization;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One channel's before/after proposal row. A channel outside the gain balance keeps its gain.</summary>
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

/// <summary>Predicted average summation loss per side (dB, &lt;= 0): coherent sum vs phase-blind magnitude sum over the crossover window.</summary>
internal sealed record AutoDelaySumLossForecast(double BeforeDb, double AfterDb);

/// <summary>Dialog inputs. Scene offset and near-side cut are non-negative magnitudes; the steering layout (LHD: right leads) owns every sign.</summary>
internal sealed record AutoDelayRunRequest(
    double SceneOffsetMs,
    bool RightHandDrive,
    bool AdjustGains,
    double NearSideCutDb,
    // How far BEHIND the front the rear fill arrives: 0 = coherent (second row), default = precedence offset keeping the image on the dash.
    double RearFillOffsetMs = 0)
{
    /// <summary>Tilt in the gain engine's LEFT-minus-RIGHT convention (LHD negative, RHD positive).</summary>
    public double LevelDifferenceDb =>
        RightHandDrive ? NearSideCutDb : -NearSideCutDb;
}

internal sealed record AutoDelayRunResult(
    IReadOnlyList<AutoDelayChannelOutcome> Outcomes,
    bool Stereo,
    AutoDelayRunRequest Request,
    string ReportText,
    StringBuilder Log);

/// <summary>Renders the proposal as the dialog's monospace table plus notes; UI-free for tests.</summary>
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
        // Invariant culture: the report is a shareable diagnostic.
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
        if (outcomes.Any(outcome =>
            outcome.Runtime.Pair.Zone == VirtualCrossoverZone.Rear))
        {
            // Printed whenever there is a rear, zero included: zero (co-arrival) is a deliberate choice the report must state.
            text.AppendLine(request.RearFillOffsetMs > 0
                ? FormattableString.Invariant(
                    $"Rear fill {request.RearFillOffsetMs:0.0} ms behind the front stage.")
                : "Rear fill 0.0 ms: co-arriving with the front stage.");
        }
        if (!request.AdjustGains)
        {
            text.AppendLine("Gains not adjusted (checkbox off).");
        }

        text.AppendLine();
        AppendChangeSummary(text, outcomes, request.AdjustGains);
        AppendSumLossForecast(text, stereo, leftSumLoss, rightSumLoss);
        AppendLowConfidenceWarnings(text, outcomes);

        text.AppendLine();
        AppendTable(text, outcomes);

        // Short explicit lines: the report box wraps rather than scrolls.
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

    private static string SumLossCell(AutoDelaySumLossForecast forecast, int width)
    {
        string before = Fixed(forecast.BeforeDb, GainDecimals).PadLeft(width);
        string after = Fixed(forecast.AfterDb, GainDecimals).PadLeft(width);
        if (before == after)
        {
            return $"{after} dB (unchanged)";
        }

        // From the ROUNDED figures, or a pair straddling a rounding boundary shows a moved arrow beside "0.0 dB worse".
        double gained = Rounded(forecast.AfterDb, GainDecimals)
            - Rounded(forecast.BeforeDb, GainDecimals);
        return $"{before} -> {after} dB " +
            $"({Fixed(Math.Abs(gained), GainDecimals)} dB " +
            $"{(gained > 0 ? "better" : "worse")})";
    }

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
        // Numbers padded before measuring columns so decimal points line up.
        int delayWidth = NumberWidth(
            outcomes.SelectMany(outcome =>
                new[] { outcome.BeforeDelayMs, outcome.AfterDelayMs }),
            DelayDecimals);
        int gainWidth = NumberWidth(
            outcomes.SelectMany(outcome =>
                new[] { outcome.BeforeGainDb, outcome.AfterGainDb }),
            GainDecimals);
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

    private static string ValueCell(
        double before, double after, bool changed, int decimals, int width)
    {
        string text = Fixed(before, decimals).PadLeft(width);
        return changed
            ? $"{text} -> {Fixed(after, decimals).PadLeft(width)}"
            : $"{text} (kept)";
    }

    // Locked picks and the reference were not chosen by measurement, so they show a kind, not a confidence.
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

    // Changed = prints a different number, so summary list and table arrows are always the same set.
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

    // Rounding first avoids "-0.00" reading as a change.
    private static string Fixed(double value, int decimals)
    {
        double rounded = Rounded(value, decimals);
        return (rounded == 0 ? 0 : rounded)
            .ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

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

            line.Append(cells[column].PadRight(widths[column]));
        }

        text.AppendLine(line.ToString().TrimEnd());
    }
}
