using System.Globalization;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// What a text curve represents, as declared in an exported file's header. A plain
/// response can be equalized; a deviation or correction curve is a difference and must
/// not be mistaken for one.
/// </summary>
public enum OverlayCurveRole
{
    /// <summary>A measured or captured response (the swept magnitude, an RTA, …).</summary>
    Response,

    /// <summary>measurement − target.</summary>
    Deviation,

    /// <summary>target − measurement (the EQ gain needed to reach the target).</summary>
    EqCorrection
}

/// <summary>
/// The header a Resonalyze-exported curve carries, so a consumer knows what the numbers
/// mean instead of guessing. Every field is optional: a file written by another tool has
/// no header at all, and one written by a later version may carry keys this build does
/// not know. Nulls therefore mean "not stated", never "invalid".
/// </summary>
public sealed record OverlayTextMetadata(
    OverlayCurveRole? Role = null,
    AnalysisCurveKind? CurveKind = null,
    MagnitudeScale? Scale = null,
    int? SampleRateHz = null,
    string? Title = null)
{
    public static readonly OverlayTextMetadata Empty = new();

    public bool IsEmpty =>
        Role == null && CurveKind == null && Scale == null &&
        SampleRateHz == null && string.IsNullOrEmpty(Title);
}

/// <summary>The points of a text curve together with whatever header it declared.</summary>
public sealed record OverlayTextCurve(
    OverlayPoint[] Points,
    OverlayTextMetadata Metadata);

/// <summary>
/// Reads and writes overlay curves as plain text, one "X Y" pair per line
/// (for example "123.4 -5.5"). Import is deliberately lenient: values may be
/// separated by spaces, tabs, commas or semicolons, extra columns are ignored,
/// and any line that is not a valid number pair (comments, headers, blanks) is
/// silently skipped.
/// </summary>
/// <remarks>
/// Exports additionally carry a <c>#</c>-prefixed header describing the curve (role,
/// kind, magnitude scale, sample rate, title). It rides on the existing leniency: a
/// comment line was already skipped by the pair parser, so new files load in older
/// builds and headerless files from other tools (REW and friends) load here.
/// </remarks>
public static class OverlayTextFile
{
    private const string FormatMarker = "resonalyze-curve";
    private const int FormatVersion = 1;

    private static readonly char[] Separators = [' ', '\t', ',', ';'];

    public static void Export(string path, IReadOnlyList<OverlayPoint> points) =>
        Export(path, points, metadata: null);

    public static void Export(
        string path,
        IReadOnlyList<OverlayPoint> points,
        OverlayTextMetadata? metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(points);

        IEnumerable<string> lines = BuildHeaderLines(metadata).Concat(
            points.Select(point => string.Create(
                CultureInfo.InvariantCulture,
                $"{point.X:R} {point.Y:R}")));
        // Temp file + move so an interrupted export cannot truncate an existing
        // file the user picked (mirrors OverlayFile.Save).
        string tempPath = path + ".tmp";
        File.WriteAllLines(tempPath, lines);
        File.Move(tempPath, path, overwrite: true);
    }

    public static OverlayPoint[] Import(string path) => ImportCurve(path).Points;

    /// <summary>
    /// Imports the points together with the declared header. Callers that care what the
    /// curve IS (rather than only its samples) use this; the header is
    /// <see cref="OverlayTextMetadata.Empty"/> for a file that declares nothing.
    /// </summary>
    public static OverlayTextCurve ImportCurve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var points = new List<OverlayPoint>();
        var metadata = new HeaderBuilder();
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.TrimStart();
            if (line.StartsWith('#'))
            {
                metadata.Read(line);
                continue;
            }

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

        return new OverlayTextCurve(points.ToArray(), metadata.Build());
    }

    private static IEnumerable<string> BuildHeaderLines(OverlayTextMetadata? metadata)
    {
        if (metadata == null || metadata.IsEmpty)
        {
            yield break;
        }

        yield return $"# {FormatMarker} v{FormatVersion}";
        if (metadata.Role is { } role)
        {
            yield return $"# role: {role}";
        }
        if (metadata.CurveKind is { } kind)
        {
            yield return $"# kind: {kind}";
        }
        if (metadata.Scale is { } scale)
        {
            yield return $"# scale: {scale}";
        }
        if (metadata.SampleRateHz is { } sampleRate)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"# sample-rate: {sampleRate}");
        }
        if (!string.IsNullOrWhiteSpace(metadata.Title))
        {
            // A newline in a title would forge extra header lines on the next import.
            yield return $"# title: {Sanitize(metadata.Title)}";
        }
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString().Trim();
    }

    // Accumulates recognized header keys; anything unknown (a key from a later
    // version, a hand-written note) is ignored rather than failing the import.
    private sealed class HeaderBuilder
    {
        private OverlayCurveRole? role;
        private AnalysisCurveKind? curveKind;
        private MagnitudeScale? scale;
        private int? sampleRateHz;
        private string? title;

        public void Read(string commentLine)
        {
            string body = commentLine.TrimStart('#').Trim();
            int separator = body.IndexOf(':');
            if (separator <= 0)
            {
                return;
            }

            string key = body[..separator].Trim();
            string value = body[(separator + 1)..].Trim();
            if (value.Length == 0)
            {
                return;
            }

            switch (key.ToLowerInvariant())
            {
                case "role":
                    if (Enum.TryParse(value, ignoreCase: true, out OverlayCurveRole parsedRole) &&
                        Enum.IsDefined(parsedRole))
                    {
                        role = parsedRole;
                    }

                    break;
                case "kind":
                    if (Enum.TryParse(value, ignoreCase: true, out AnalysisCurveKind parsedKind) &&
                        Enum.IsDefined(parsedKind))
                    {
                        curveKind = parsedKind;
                    }

                    break;
                case "scale":
                    if (Enum.TryParse(value, ignoreCase: true, out MagnitudeScale parsedScale) &&
                        Enum.IsDefined(parsedScale))
                    {
                        scale = parsedScale;
                    }

                    break;
                case "sample-rate":
                    if (int.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int parsedRate) &&
                        parsedRate > 0)
                    {
                        sampleRateHz = parsedRate;
                    }

                    break;
                case "title":
                    title = value;
                    break;
            }
        }

        public OverlayTextMetadata Build() =>
            role == null && curveKind == null && scale == null &&
            sampleRateHz == null && title == null
                ? OverlayTextMetadata.Empty
                : new OverlayTextMetadata(role, curveKind, scale, sampleRateHz, title);
    }
}
