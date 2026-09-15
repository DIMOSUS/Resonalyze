namespace Resonalyze;

/// <summary>Delay table layout and the reverse cell extraction click-to-copy reads; kept together because both must agree.</summary>
internal static class DelayTableText
{
    // Widest cell with a Compare delta ("163.000 (+2.604)") is 16; a full row is 66 chars, the status box width without wrapping.
    public const int MillisecondsColumn = 16;
    public const int SamplesColumn = 33;
    public const int MetersColumn = 50;

    public const string FirstArrivalLabel = "First Arrival";
    public const string StrongestPeakLabel = "Strongest Peak";
    public const string EnergyOnsetLabel = "Energy onset";

    /// <summary>At the END of the row: a glyph of uncertain width ahead of the cells would shift the columns.</summary>
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

    public static string FormatCells(string milliseconds, string samples, string meters) =>
        milliseconds.PadRight(SamplesColumn - MillisecondsColumn) +
        samples.PadRight(MetersColumn - SamplesColumn) +
        meters;

    public static bool IsDelayRow(string line) =>
        RowLabels.Any(label => line.StartsWith(label, StringComparison.Ordinal));

    public static int? CellAt(int column) =>
        column >= MetersColumn ? MetersColumn
        : column >= SamplesColumn ? SamplesColumn
        : column >= MillisecondsColumn ? MillisecondsColumn
        : null;

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

    // Signed from the rounded magnitude, so a zero delta never reads "-0,000".
    private static string FormatSignedDelta(double delta, string valueFormat)
    {
        string magnitude = Math.Abs(delta).ToString(valueFormat);
        bool negative = delta < 0 && magnitude.Any(character => character is > '0' and <= '9');
        return (negative ? "-" : "+") + magnitude;
    }

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
        int deltaStart = cell.IndexOf(" (", StringComparison.Ordinal);
        return deltaStart >= 0 ? cell[..deltaStart] : cell;
    }
}
