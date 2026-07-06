namespace Resonalyze;

/// <summary>
/// The fixed-column text layout of the Time Alignment delay table: formatting
/// of its lines and cells, and the reverse cell extraction the click-to-copy
/// feature reads back from the rendered text. Kept together (and unit-tested)
/// because the two must agree on the column layout.
/// </summary>
internal static class DelayTableText
{
    public const int FirstColumn = 18;
    // Widened past the old 34 so a Compare cell "value (Δ)" fits without touching
    // the Strongest Peak column.
    public const int SecondColumn = 37;

    public static string FormatLine(
        string label,
        string firstArrival,
        string strongestPeak) =>
        label.PadRight(FirstColumn) +
        firstArrival.PadRight(SecondColumn - FirstColumn) +
        strongestPeak;

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
    /// rendered table line, without the Compare "(Δ)" suffix.
    /// </summary>
    public static string GetValue(string line, int startColumn)
    {
        if (line.Length <= startColumn)
        {
            return string.Empty;
        }

        int endColumn = startColumn == FirstColumn
            ? Math.Min(SecondColumn, line.Length)
            : line.Length;
        string cell = line[startColumn..endColumn].Trim();
        // Copy just the value, not the Compare "(Δ)" suffix.
        int deltaStart = cell.IndexOf(" (", StringComparison.Ordinal);
        return deltaStart >= 0 ? cell[..deltaStart] : cell;
    }
}
