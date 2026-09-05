namespace Resonalyze;

/// <summary>
/// The fixed-column text layout of the Time Alignment delay table: one row per
/// instant the analysis reads (first arrival, strongest peak, energy onset),
/// one column per unit (milliseconds, samples, meters), plus the reverse cell
/// extraction the click-to-copy feature reads back from the rendered text.
/// Kept together (and unit-tested) because the two must agree on the layout.
/// </summary>
internal static class DelayTableText
{
    // Sized to the widest cell each column can hold with a Compare delta and
    // one space of separation — "163.000 (+2.604)" ms (a chain latency past
    // 100 ms) is 16 characters, "15650.3 (+116.2)" samples is 16 — so a row
    // with every delta runs 66 characters: what the status box shows at the
    // table font without wrapping. Wider columns wrapped the meters cell onto
    // a second line on the field record that first showed all three rows.
    public const int MillisecondsColumn = 16;
    public const int SamplesColumn = 33;
    public const int MetersColumn = 50;

    public const string FirstArrivalLabel = "First Arrival";
    public const string StrongestPeakLabel = "Strongest Peak";
    public const string EnergyOnsetLabel = "Energy onset";

    /// <summary>
    /// Appended after the meters cell of the row the analysis recommends for
    /// alignment; the word itself goes on a line under the table. At the END
    /// of the row on purpose: a glyph of uncertain width ahead of the cells
    /// would shift the columns it marks, and a word beside it wrapped the row.
    /// </summary>
    public const string RecommendedMarker = " ◀";

    private static readonly string[] RowLabels =
        [FirstArrivalLabel, StrongestPeakLabel, EnergyOnsetLabel];

    public static string FormatHeader() =>
        "Measured delay:".PadRight(MillisecondsColumn) +
        "ms".PadRight(SamplesColumn - MillisecondsColumn) +
        "samples".PadRight(MetersColumn - SamplesColumn) +
        "meters (20°C)";

    public static string FormatLine(
        string label,
        string milliseconds,
        string samples,
        string meters) =>
        label.PadRight(MillisecondsColumn) + FormatCells(milliseconds, samples, meters);

    /// <summary>The three unit cells of a row, from the milliseconds column on.</summary>
    public static string FormatCells(string milliseconds, string samples, string meters) =>
        milliseconds.PadRight(SamplesColumn - MillisecondsColumn) +
        samples.PadRight(MetersColumn - SamplesColumn) +
        meters;

    /// <summary>Whether a rendered status line is one of the table's rows.</summary>
    public static bool IsDelayRow(string line) =>
        RowLabels.Any(label => line.StartsWith(label, StringComparison.Ordinal));

    /// <summary>
    /// The start column of the cell a character column falls in, or null for
    /// the label.
    /// </summary>
    public static int? CellAt(int column) =>
        column >= MetersColumn ? MetersColumn
        : column >= SamplesColumn ? SamplesColumn
        : column >= MillisecondsColumn ? MillisecondsColumn
        : null;

    /// <summary>
    /// "value" for a Source cell; "value (+delta)" for a Compare cell where a
    /// reference is given. The delta always carries an explicit sign.
    /// </summary>
    public static string FormatValueWithDelta(
        double value,
        double? reference,
        string valueFormat)
    {
        string text = value.ToString(valueFormat);
        if (reference.HasValue)
        {
            double delta = value - reference.Value;
            text += " (" + FormatSignedDelta(delta, valueFormat) + ")";
        }

        return text;
    }

    // Signs the delta from its rounded magnitude, so a delta that rounds to zero
    // reads "+0,000" rather than a spurious "-0,000" (or "-+0,000") from a tiny
    // negative value the format would otherwise sign.
    private static string FormatSignedDelta(double delta, string valueFormat)
    {
        string magnitude = Math.Abs(delta).ToString(valueFormat);
        bool negative = delta < 0 && magnitude.Any(character => character is > '0' and <= '9');
        return (negative ? "-" : "+") + magnitude;
    }

    /// <summary>
    /// Extracts the cell starting at <paramref name="startColumn"/> from a
    /// rendered table line, without the Compare "(Δ)" suffix and without the
    /// recommendation marker.
    /// </summary>
    public static string GetValue(string line, int startColumn)
    {
        if (line.Length <= startColumn)
        {
            return string.Empty;
        }

        int endColumn = startColumn < SamplesColumn
            ? Math.Min(SamplesColumn, line.Length)
            : startColumn < MetersColumn
                ? Math.Min(MetersColumn, line.Length)
                : line.Length;
        string cell = line[startColumn..endColumn];
        int markerStart = cell.IndexOf('◀');
        if (markerStart >= 0)
        {
            cell = cell[..markerStart];
        }

        cell = cell.Trim();
        // Copy just the value, not the Compare "(Δ)" suffix.
        int deltaStart = cell.IndexOf(" (", StringComparison.Ordinal);
        return deltaStart >= 0 ? cell[..deltaStart] : cell;
    }
}
