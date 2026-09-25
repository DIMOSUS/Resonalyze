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

    private const char MarkerGlyph = '◀';

    private static readonly string[] RowLabels =
        [FirstArrivalLabel, StrongestPeakLabel, EnergyOnsetLabel];

    /// <summary>Where one table's cells start: the default columns, pushed right where a cell would run into the next.</summary>
    public readonly record struct Columns(int Samples, int Meters)
    {
        public static Columns Default { get; } = new(SamplesColumn, MetersColumn);

        public static Columns Fit(IReadOnlyCollection<(string Milliseconds, string Samples)> rows)
        {
            int samples = rows.Aggregate(
                SamplesColumn, (column, row) => Math.Max(column, MillisecondsColumn + row.Milliseconds.Length + 1));
            int meters = rows.Aggregate(
                MetersColumn, (column, row) => Math.Max(column, samples + row.Samples.Length + 1));
            return new Columns(samples, meters);
        }
    }

    public static string FormatHeader() => FormatHeader(Columns.Default);

    public static string FormatHeader(Columns columns) =>
        "Measured delay:".PadRight(MillisecondsColumn) +
        "ms".PadRight(columns.Samples - MillisecondsColumn) +
        "samples".PadRight(columns.Meters - columns.Samples) +
        "meters (20°C)";

    public static string FormatLine(
        string label,
        string milliseconds,
        string samples,
        string meters) =>
        label.PadRight(MillisecondsColumn) + FormatCells(milliseconds, samples, meters, Columns.Default);

    public static string FormatCells(string milliseconds, string samples, string meters, Columns columns) =>
        milliseconds.PadRight(columns.Samples - MillisecondsColumn) +
        samples.PadRight(columns.Meters - columns.Samples) +
        meters;

    public static bool IsDelayRow(string line) =>
        RowLabels.Any(label => line.StartsWith(label, StringComparison.Ordinal));

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

    /// <summary>What a click at <paramref name="column"/> of a report line copies: a delay cell's number, or empty.</summary>
    public static string CopyableValue(string line, int column) =>
        IsDelayRow(line) ? GetValue(line, column) : string.Empty;

    /// <summary>The number of the cell <paramref name="column"/> falls in (its delta and padding included); empty in the label.</summary>
    /// <remarks>Cells are found by their content, not by fixed columns: <see cref="Columns.Fit"/> may widen a table.</remarks>
    public static string GetValue(string line, int column)
    {
        string value = string.Empty;
        int index = MillisecondsColumn;
        while (index < line.Length && index <= column)
        {
            if (line[index] == ' ')
            {
                index++;
                continue;
            }

            int end = line.IndexOf(' ', index);
            end = end < 0 ? line.Length : end;
            string token = line[index..end];
            if (!token.StartsWith('(') && token[0] != MarkerGlyph)
            {
                value = token;
            }

            index = end;
        }

        return value;
    }
}
