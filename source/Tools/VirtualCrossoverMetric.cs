namespace Resonalyze;

/// <summary>
/// The Virtual DSP sum-loss read-outs: one entry per junction pair (or the
/// total across the crossover window) and the three text renderings — the
/// single-line sheet subtitle, the compact host-panel column and the full
/// tooltip/log breakdown. Pure formatting, unit-tested.
/// </summary>
internal static class VirtualCrossoverMetric
{
    /// <summary>
    /// One sum-loss read-out: a junction pair (or the total across the crossover
    /// window), its average and dip in dB, and the band it was measured over.
    /// (The polarity-flip null depth used to be a third column; it proved
    /// uninformative in practice and was dropped along with its computation.)
    /// </summary>
    internal readonly record struct Entry(
        string Junction,
        double AverageDb,
        double? DipDb,
        double LowHz,
        double HighHz,
        bool IsTotal);

    /// <summary>
    /// Compact single line for the sheet subtitle: no frequency ranges, so it
    /// stays short with many channels.
    /// </summary>
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

    /// <summary>
    /// Compact per-junction column for the narrow host read-out panel: a
    /// monospace "name  avg / dip" line each, no frequency ranges (those are
    /// on hover).
    /// </summary>
    public static string FormatCompact(IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0)
        {
            return "Sum loss (dB)\r\n  avg / dip\r\n\r\n—";
        }

        var builder = new System.Text.StringBuilder(
            "Sum loss (dB)\r\n  avg / dip\r\n\r\n");
        foreach (Entry entry in entries)
        {
            string name = (entry.IsTotal ? "Total" : entry.Junction).PadRight(6);
            string dip = entry.DipDb.HasValue ? $"{entry.DipDb.Value,5:0.0}" : "    —";
            builder.AppendLine($"{name}{entry.AverageDb,5:0.0} /{dip}");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// One channel pair's final inter-side timing read-out: the difference of
    /// the two sides' band-limited envelope arrivals (their fully processed
    /// responses, delays included) in the pair's shared band. Positive means
    /// the RIGHT side leads — the same sign convention as the Auto delay scene
    /// offset, so after a stereo run every row should read the offset. Null
    /// when an arrival is unmeasurable (a silent band).
    /// </summary>
    internal readonly record struct StereoDelta(
        string Channel,
        double? DeltaMs,
        double LowHz,
        double HighHz);

    /// <summary>
    /// Compact per-channel Δ block for the host read-out panel, appended below
    /// the sum-loss column.
    /// </summary>
    public static string FormatStereoDeltasCompact(IReadOnlyList<StereoDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder("\u0394 L\u2212R (ms)\r\n");
        foreach (StereoDelta delta in deltas)
        {
            string value = delta.DeltaMs.HasValue
                ? $"{delta.DeltaMs.Value,6:+0.00;-0.00}"
                : "     \u2014";
            builder.AppendLine($"{delta.Channel.PadRight(6)}{value}");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Full \u0394 breakdown for the tooltip, with the measured bands.</summary>
    public static string FormatStereoDeltasDetail(IReadOnlyList<StereoDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

        return "\u0394 L\u2212R: envelope arrival of the processed left side minus the " +
            "right one\r\n(positive: right leads; after a stereo Auto delay " +
            "every channel should read the scene offset)\r\n" +
            string.Join("\r\n", deltas.Select(delta =>
                delta.DeltaMs.HasValue
                    ? $"{delta.Channel}: {delta.DeltaMs.Value:+0.000;-0.000} ms " +
                      $"({FrequencyText.Format(delta.LowHz)} \u2013 " +
                      $"{FrequencyText.Format(delta.HighHz)})"
                    : $"{delta.Channel}: \u2014 (no measurable arrival)"));
    }

    /// <summary>
    /// Full multi-line breakdown for the tooltip and the Auto delay log, one
    /// read-out per line, including the band each was measured over.
    /// </summary>
    public static string FormatDetail(IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0)
        {
            return "Sum loss avg: —";
        }

        return "Sum loss avg\r\n" + string.Join("\r\n", entries.Select(entry =>
        {
            string name = entry.IsTotal ? "Total" : entry.Junction;
            string dip = entry.DipDb.HasValue ? $", dip {entry.DipDb.Value:0.0} dB" : "";
            return $"{name}: {entry.AverageDb:0.0} dB avg{dip} " +
                $"({FrequencyText.Format(entry.LowHz)} – {FrequencyText.Format(entry.HighHz)})";
        }));
    }
}
