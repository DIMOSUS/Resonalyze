using System.Globalization;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Declared role: a deviation or correction is a difference and must not be equalized as a response.</summary>
public enum OverlayCurveRole
{
    Response,

    Deviation,

    /// <summary>target − measurement (the EQ gain needed).</summary>
    EqCorrection,

    Target,

    Calculated
}

/// <summary>All fields optional: null means "not stated" (foreign or newer files), never invalid.</summary>
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

public sealed record OverlayTextCurve(
    OverlayPoint[] Points,
    OverlayTextMetadata Metadata);

/// <summary>"X Y" per line; lenient import skips any non-pair line.</summary>
/// <remarks>The <c>#</c> export header rides on that leniency: older builds and other tools' files still load.</remarks>
public static class OverlayTextFile
{
    private const string FormatMarker = "resonalyze-curve";
    private const int FormatVersion = 1;

    private static readonly char[] ColumnSeparators = [' ', '\t', ';'];

    /// <summary>Role comes from the slot's current <paramref name="kind"/>, never a stale tag; curve kind only for captured responses.</summary>
    public static OverlayTextMetadata BuildCurveMetadata(
        OverlayKind kind,
        AnalysisCurveKind? capturedCurveKind,
        MagnitudeScale scale,
        int? sampleRateHz,
        string? title)
    {
        OverlayCurveRole role = kind switch
        {
            OverlayKind.Operation => OverlayCurveRole.Calculated,
            OverlayKind.Target => OverlayCurveRole.Target,
            _ => OverlayCurveRole.Response
        };
        AnalysisCurveKind? exportedKind =
            kind == OverlayKind.Captured ? capturedCurveKind : null;
        return new OverlayTextMetadata(role, exportedKind, scale, sampleRateHz, title);
    }

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
        // Temp file + move so an interrupted export cannot truncate the user's file.
        string tempPath = path + ".tmp";
        File.WriteAllLines(tempPath, lines);
        File.Move(tempPath, path, overwrite: true);
    }

    public static OverlayPoint[] Import(string path) => ImportCurve(path).Points;

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

            string[] tokens = SplitColumns(rawLine);
            if (tokens.Length < 2 ||
                !TryParseNumber(tokens[0], out double x) ||
                !TryParseNumber(tokens[1], out double y) ||
                !double.IsFinite(x) ||
                double.IsInfinity(y))
            {
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

    // Whitespace or semicolons separate columns when a line has them, so "63\t4,5" is a decimal comma, not a third column.
    private static string[] SplitColumns(string line)
    {
        string[] tokens = line.Split(
            ColumnSeparators,
            StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2
            ? tokens
            : line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool TryParseNumber(string token, out double value)
    {
        string number = token.Trim(',');
        if (!number.Contains('.'))
        {
            number = number.Replace(',', '.');
        }

        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
            // A newline in a title would forge header lines on the next import.
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

    // Unknown keys (newer versions, notes) are ignored.
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
