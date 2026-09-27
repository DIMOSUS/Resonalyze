using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Pure formatting of the Virtual DSP read-outs. See docs/tech/virtual-dsp-analysis.md.</summary>
internal static class VirtualCrossoverMetric
{
    internal readonly record struct Entry(
        string Junction,
        double AverageDb,
        double? DipDb,
        double LowHz,
        double HighHz,
        bool IsTotal);

    public static string FormatLabel(IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0)
        {
            return "Sum loss avg: —";
        }

        IEnumerable<string> parts = entries.Select(entry =>
        {
            string body = $"{entry.AverageDb:0.0} dB" +
                (entry.DipDb.HasValue ? $", dip {entry.DipDb.Value:0.0} dB" : "");
            return entry.IsTotal ? "total " + body : $"{entry.Junction} {body}";
        });
        return "Sum loss avg: " + string.Join("   ", parts);
    }

    /// <summary>Per-junction "avg / dip" column. Two decimals: genuine lobe alternatives differ by hundredths of a dB.</summary>
    /// <param name="direct">Read through <see cref="SumLossWindow.Direct"/>; the header says so, the two families are not comparable.</param>
    public static string FormatCompact(IReadOnlyList<Entry> entries, bool direct = false)
    {
        string header = (direct ? "Sum loss (direct, dB)" : "Sum loss (dB)") +
            "\r\n         avg /   dip\r\n\r\n";
        if (entries.Count == 0)
        {
            return header + "—";
        }

        var builder = new System.Text.StringBuilder(header);
        foreach (Entry entry in entries)
        {
            string name = (entry.IsTotal ? "Total" : entry.Junction).PadRight(6);
            string dip = entry.DipDb.HasValue ? $"{entry.DipDb.Value,6:0.00}" : "     —";
            builder.AppendLine($"{name}{entry.AverageDb,6:0.00} /{dip}");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>One pair's final L/R read-out. Delay and level deltas are left minus right: positive delay = RIGHT leads
    /// (the Auto delay scene-offset sign), positive level = LEFT louder. Null when unreliable, unless the level is spatial.</summary>
    internal readonly record struct StereoDelta(
        string Channel,
        double? LeftMs,
        double? RightMs,
        double LowHz,
        double HighHz,
        double? LevelDeltaDb = null,
        // Latched: the full-band envelope timed the modal build-up, so the delta renders with "~".
        bool LeftLatched = false,
        bool RightLatched = false,
        // Level from the spatial averages through the chains; timing stays on the IRs.
        bool LevelFromSpatialAverage = false,
        // Arrivals are band energy onsets (engine cross-side link rule for low pairs), not first envelope peaks.
        bool EnergyOnset = false,
        // Band qualifies for onsets but SNR is short, so both sides fell back to peaks.
        bool EnergyOnsetWithheld = false)
    {
        public double? DeltaMs => LeftMs.HasValue && RightMs.HasValue
            ? LeftMs.Value - RightMs.Value
            : null;

        public bool AnyLatched => LeftLatched || RightLatched;
    }

    public static string FormatStereoDeltasCompact(IReadOnlyList<StereoDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

        static string Side(double? value, bool latched) =>
            value.HasValue
                ? latched
                    ? $"~{value.Value:0.00}".PadLeft(7)
                    : $"{value.Value,7:0.00}"
                : "      \u2014";

        var builder = new System.Text.StringBuilder(
            "Arrival (ms)\r\n         L      R  \u0394 L\u2212R\r\n");
        foreach (StereoDelta delta in deltas)
        {
            // Three format sections: a negative value rounding to zero must not render "-+0.00".
            string deltaText = delta.DeltaMs.HasValue
                ? delta.AnyLatched
                    ? ("~" + delta.DeltaMs.Value.ToString("+0.00;-0.00;0.00"))
                        .PadLeft(7)
                    : $"{delta.DeltaMs.Value,7:+0.00;-0.00;0.00}"
                : "      \u2014";
            builder.AppendLine(
                $"{delta.Channel.PadRight(3)}" +
                $"{Side(delta.LeftMs, delta.LeftLatched)}" +
                $"{Side(delta.RightMs, delta.RightLatched)}{deltaText}");
        }

        builder.AppendLine();
        builder.AppendLine("Level \u0394 L\u2212R (dB)");
        foreach (StereoDelta delta in deltas)
        {
            string levelText = delta.LevelDeltaDb.HasValue
                ? $"{delta.LevelDeltaDb.Value,7:+0.0;-0.0;0.0}"
                : "      \u2014";
            builder.AppendLine($"{delta.Channel.PadRight(3)}{levelText}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatStereoDeltasDetail(IReadOnlyList<StereoDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

        // Mixed lists are legitimate (a channel without an array); point-measured rows are marked in place.
        bool anySpatialLevel = deltas.Any(delta => delta.LevelFromSpatialAverage);
        var lines = new List<string> { "Arrival: ms from the IR start; \u0394 L\u2212R positive = right leads" };
        foreach (StereoDelta delta in deltas)
        {
            string how = delta.EnergyOnset
                ? ", energy onsets"
                : delta.EnergyOnsetWithheld
                    ? $", first peaks: SNR under {AutoAlignmentEngine.EnergyOnsetMinimumSnrDb:0} dB"
                    : string.Empty;
            string arrival = delta.LeftMs.HasValue || delta.RightMs.HasValue
                ? string.Empty
                : ", no measurable arrival";
            string pointMic = anySpatialLevel && delta.LevelDeltaDb.HasValue && !delta.LevelFromSpatialAverage
                ? ", level from the point mic"
                : string.Empty;
            lines.Add($"{delta.Channel}: {Band(delta.LowHz, delta.HighHz)}{how}{arrival}{pointMic}");
        }

        lines.Add(anySpatialLevel
            ? "Level \u0394: spatial averages through the chains; positive = LEFT louder"
            : "Level \u0394: gated band level; positive = LEFT louder");
        if (deltas.Any(delta => delta.AnyLatched))
        {
            lines.Add("~: timed on the cabin's modal build-up, so the \u0394 overstates the skew");
        }

        return string.Join("\r\n", lines);
    }

    /// <summary>A listening group against the front stage in their shared band, replacing sum loss in multi-group views.
    /// <see cref="DelayMs"/> positive = later; <see cref="LevelDb"/> negative = quieter; null without a reliable arrival.</summary>
    internal readonly record struct GroupDelta(
        VirtualCrossoverZone Zone,
        double? DelayMs,
        double? LevelDb,
        double LowHz,
        double HighHz,
        bool LevelFromSpatialAverage = false);

    public static string FormatGroupDeltasCompact(IReadOnlyList<GroupDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

        const string Header = "vs Front\r\n       Δt ms    ΔdB\r\n\r\n";
        var builder = new System.Text.StringBuilder(Header);
        foreach (GroupDelta delta in deltas)
        {
            string name = VirtualCrossoverZones.DisplayName(delta.Zone).PadRight(7);
            string time = delta.DelayMs is { } ms ? $"{ms,7:+0.00;-0.00;0.00}" : "      —";
            string level = delta.LevelDb is { } db ? $"{db,7:+0.0;-0.0;0.0}" : "      —";
            builder.AppendLine($"{name}{time}{level}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatGroupDeltasDetail(IReadOnlyList<GroupDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

        bool anySpatialLevel = deltas.Any(delta => delta.LevelFromSpatialAverage);
        var lines = new List<string> { "Each group vs the front stage in their shared band; no sum loss across groups" };
        foreach (GroupDelta delta in deltas)
        {
            string pointMic = anySpatialLevel && delta.LevelDb.HasValue && !delta.LevelFromSpatialAverage
                ? ", level from the point mic"
                : string.Empty;
            string arrival = delta.DelayMs.HasValue ? string.Empty : ", no reliable arrival";
            lines.Add(
                $"{VirtualCrossoverZones.DisplayName(delta.Zone)}: {Band(delta.LowHz, delta.HighHz)}{arrival}{pointMic}");
        }

        lines.Add(anySpatialLevel
            ? "\u0394t positive = later; \u0394dB from spatial averages, negative = quieter"
            : "\u0394t positive = later; \u0394dB negative = quieter");
        return string.Join("\r\n", lines);
    }

    internal readonly record struct PhaseEntry(
        string Junction,
        string LowerChannel,
        double CrossoverHz,
        double LowHz,
        double HighHz,
        JunctionPhaseResult Result);

    // Below this lobe margin the column shows "!": a whole-period hop cannot be ruled out (~0.19 healthy, ~0.04 narrowed).
    private const double AmbiguousLobeMargin = 0.10;

    // 10° at fc costs the sum 0.03 dB, below audibility and repeatability; phase, so it scales per junction.
    private const double SignificantFixDegrees = 10.0;

    // Fixes under 0.005 ms take a third decimal so they keep their sign.
    private static string FixText(double milliseconds) =>
        Math.Abs(milliseconds) < 0.005
            ? milliseconds.ToString("+0.000;-0.000;0.000")
            : milliseconds.ToString("+0.00;-0.00;0.00");

    /// <summary>Per-junction phase column: φ at fc, the fix on the lower channel ("i" flip, "~" flip near-tie, "!" lobe hop,
    /// "·" too small) and the current score. See docs/tech/virtual-dsp-analysis.md#junction-phase-read-out-formatting.</summary>
    public static string FormatPhaseCompact(IReadOnlyList<PhaseEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(
            "Junction phase\r\n       φfc  fix ms  score\r\n");
        foreach (PhaseEntry entry in entries)
        {
            JunctionPhaseResult result = entry.Result;
            // Three sections (.NET Core 3.0 signed zero renders "-+0.00"); φ with inconsistent bins is dashed.
            string phase =
                result.PhaseConsistency >= JunctionPhaseAlignment.MinimumPhaseConsistency
                    ? $"{result.PhaseAtCrossoverDeg,4:+0;-0;0}°"
                    : "    —";
            // Below MinimumAlignableScore the best delay is the least bad of bad alignments: fix dashed, polarity blank.
            bool alignable =
                result.BestScore >= JunctionPhaseAlignment.MinimumAlignableScore;
            double significantMs =
                SignificantFixDegrees / 360.0 * 1_000.0 / entry.CrossoverHz;
            string fix = !alignable
                ? "     —"
                : Math.Abs(result.BestExtraDelayMs) >= significantMs
                    ? $"{FixText(result.BestExtraDelayMs),6}"
                    : "     ·";
            string polarity =
                !alignable ? " "
                : result.BestInvert ? "i"
                : result.BestScore - result.OppositePolarityScore
                    < JunctionPhaseAlignment.PolarityFlipAdvantage ? "~"
                : " ";
            string score = $"{result.CurrentScore,5:0.00;-0.00;0.00}";
            // A dashed (unalignable) fix gets no period-hop warning.
            string warning =
                alignable &&
                result.LobeMargin is { } margin && margin < AmbiguousLobeMargin
                    ? " !"
                    : string.Empty;
            builder.AppendLine(
                $"{entry.Junction.PadRight(6)}{phase} {fix}{polarity} {score}{warning}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatPhaseDetail(IReadOnlyList<PhaseEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string> { "Junction phase, 8-cycle window" };
        foreach (PhaseEntry entry in entries)
        {
            JunctionPhaseResult result = entry.Result;
            string best = result.BestScore >= JunctionPhaseAlignment.MinimumAlignableScore
                ? $"best {result.BestScore:0.00} at {FixText(result.BestExtraDelayMs)} ms on {entry.LowerChannel}" +
                    (result.BestInvert
                        ? $", invert {entry.LowerChannel}"
                        : $", flip {result.OppositePolarityScore:0.00}")
                : $"no delay aligns it (ceiling {result.BestScore:0.00})";
            string rival = result.RivalScore.HasValue
                ? $"; rival {result.RivalScore.Value:0.00} at " +
                    $"{result.RivalExtraDelayMs!.Value:+0.00;-0.00;0.00} ms (margin {result.LobeMargin!.Value:0.00})"
                : string.Empty;
            string phase = result.PhaseConsistency >= JunctionPhaseAlignment.MinimumPhaseConsistency
                ? string.Empty
                : $"; \u03c6 unreliable (R {result.PhaseConsistency:0.00})";
            lines.Add($"{entry.Junction} @ {FrequencyText.Format(entry.CrossoverHz)}: {best}{rival}{phase}");
        }

        lines.Add("score \u22121\u20261, 1 = aligned; fix = delay to add to the LOWER channel");
        lines.Add("i invert \u00b7 ~ flip nearly ties \u00b7 ! lobe hop possible \u00b7 \u00b7 too small \u00b7 \u2014 nothing aligns");
        return string.Join("\r\n", lines);
    }

    public static string FormatDetail(IReadOnlyList<Entry> entries, bool direct = false)
    {
        string title = direct ? "Sum loss (direct)" : "Sum loss";
        if (entries.Count == 0)
        {
            return title + ": \u2014";
        }

        return title + " bands\r\n" + string.Join("\r\n", entries.Select(entry =>
            $"{(entry.IsTotal ? "Total" : entry.Junction)}: {Band(entry.LowHz, entry.HighHz)}")) +
            (direct ? "\r\nDirect: each channel's 8-cycle window; not comparable with Full" : string.Empty);
    }

    private static string Band(double lowHz, double highHz) =>
        $"{FrequencyText.Format(lowHz)} \u2013 {FrequencyText.Format(highHz)}";

    /// <summary>The panel's read-out column (compact) and its tooltip (detail), block by block in reading order; the
    /// headroom leads, since a clip must show without scrolling.</summary>
    /// <param name="hybrid">The hybrid's health figure while it is drawn, else null.</param>
    public static (List<ToneLine> Compact, string Detail) FormatReadOut(
        IReadOnlyList<HeadroomRow> headroom,
        IReadOnlyList<Entry> entries,
        bool direct,
        IReadOnlyList<PhaseEntry> phaseEntries,
        IReadOnlyList<GroupDelta> groupDeltas,
        IReadOnlyList<StereoDelta> stereoDeltas,
        HybridReadOut? hybrid)
    {
        string compact = FormatCompact(entries, direct);
        string detail = entries.Count > 0 ? FormatDetail(entries, direct) : string.Empty;
        string DetailBreak() => detail.Length > 0 ? "\r\n\r\n" : string.Empty;
        if (phaseEntries.Count > 0)
        {
            compact += "\r\n\r\n" + FormatPhaseCompact(phaseEntries);
            detail += DetailBreak() + FormatPhaseDetail(phaseEntries);
        }

        // Under the loss column: in a cross-group view it stands in for the withheld loss.
        if (groupDeltas.Count > 0)
        {
            compact += (compact.Length > 0 ? "\r\n\r\n" : string.Empty) +
                FormatGroupDeltasCompact(groupDeltas);
            detail += DetailBreak() + FormatGroupDeltasDetail(groupDeltas);
        }

        if (stereoDeltas.Count > 0)
        {
            compact += "\r\n\r\n" + FormatStereoDeltasCompact(stereoDeltas);
            detail += DetailBreak() + FormatStereoDeltasDetail(stereoDeltas);
        }

        if (hybrid is { } reading)
        {
            // A health reading: an array shares the IRs' loopback, so a large figure means a different input, calibration or driver.
            compact += "\r\n\r\n" + $"Spatial average {reading.Db:+0.0;-0.0} dB";
            detail += DetailBreak() + (reading.ArrayStandOff
                ? $"Furthest array {reading.Db:+0.0;-0.0} dB off its IR; each array is drawn at its own level"
                : $"Spatial averages drawn shifted {reading.Db:+0.0;-0.0} dB onto the IRs");
        }

        return (WithHeadroom(headroom, ToneLine.Plain(compact)), WithHeadroom(headroom, detail));
    }

    /// <summary>The headroom block alone, for a view that quotes nothing else.</summary>
    public static (List<ToneLine> Compact, string Detail) FormatHeadroomOnly(IReadOnlyList<HeadroomRow> headroom) =>
        (VirtualCrossoverHeadroom.FormatCompact(headroom), VirtualCrossoverHeadroom.FormatDetail(headroom));

    private static List<ToneLine> WithHeadroom(IReadOnlyList<HeadroomRow> headroom, List<ToneLine> rest)
    {
        List<ToneLine> lines = VirtualCrossoverHeadroom.FormatCompact(headroom);
        if (lines.Count > 0 && rest.Count > 0)
        {
            lines.Add(ToneLine.Of(string.Empty));
        }

        lines.AddRange(rest);
        return lines;
    }

    private static string WithHeadroom(IReadOnlyList<HeadroomRow> headroom, string rest)
    {
        string block = VirtualCrossoverHeadroom.FormatDetail(headroom);
        return block.Length > 0 && rest.Length > 0 ? block + "\r\n\r\n" + rest : block + rest;
    }
}
