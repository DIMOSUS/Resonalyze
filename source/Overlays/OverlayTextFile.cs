using System.Globalization;

namespace Resonalyze;

/// <summary>
/// Reads and writes overlay curves as plain text, one "X Y" pair per line
/// (for example "123.4 -5.5"). Import is deliberately lenient: values may be
/// separated by spaces, tabs, commas or semicolons, extra columns are ignored,
/// and any line that is not a valid number pair (comments, headers, blanks) is
/// silently skipped.
/// </summary>
public static class OverlayTextFile
{
    private static readonly char[] Separators = [' ', '\t', ',', ';'];

    public static void Export(string path, IReadOnlyList<OverlayPoint> points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(points);

        IEnumerable<string> lines = points.Select(point => string.Create(
            CultureInfo.InvariantCulture,
            $"{point.X:R} {point.Y:R}"));
        File.WriteAllLines(path, lines);
    }

    public static OverlayPoint[] Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var points = new List<OverlayPoint>();
        foreach (string rawLine in File.ReadLines(path))
        {
            string[] tokens = rawLine.Split(
                Separators,
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 ||
                !double.TryParse(
                    tokens[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double x) ||
                !double.TryParse(
                    tokens[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double y) ||
                !double.IsFinite(x) ||
                double.IsInfinity(y))
            {
                // Not a valid "X Y" pair (comment, header, blank, garbage) → skip.
                continue;
            }

            points.Add(new OverlayPoint(x, y));
        }

        if (points.Count < 2)
        {
            throw new InvalidDataException(
                "The text file must contain at least two valid 'X Y' points.");
        }

        return points.ToArray();
    }
}
