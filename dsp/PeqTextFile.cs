using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// Reads and writes an <see cref="EqualizationCurve"/> in the Equalizer APO text
/// format, e.g.:
/// <code>
/// Preamp: -6.0 dB
///
/// Filter 1: ON PK Fc 600 Hz Gain 6.0 dB Q 4.0
/// </code>
/// The building blocks (preamp line, filter lines, filter-line parsing) are shared
/// with the REW format. Parsing is defensive: blank lines, comments, disabled
/// filters (OFF), unsupported types (only peaking "PK") and malformed lines are
/// skipped; numbers accept '.' or ',' decimals and the band count is capped, so a
/// hand-edited or foreign file never throws.
/// </summary>
public static class PeqTextFile
{
    public static string Format(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        var builder = new StringBuilder();
        builder.AppendLine(FormatPreampLine(curve.PreampDb));
        builder.AppendLine();
        builder.Append(FormatFilters(curve));
        return builder.ToString();
    }

    // "Preamp: -6.0 dB"
    internal static string FormatPreampLine(double preampDb) =>
        $"Preamp: {EqTextNumbers.Format(preampDb, "0.0")} dB";

    // The block of "Filter N: ON PK Fc ... Gain ... dB Q ..." lines (no preamp).
    internal static string FormatFilters(EqualizationCurve curve)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < curve.Bands.Count; i++)
        {
            PeqBand band = curve.Bands[i];
            builder
                .Append("Filter ")
                .Append((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(": ON PK Fc ")
                .Append(EqTextNumbers.Format(band.FrequencyHz, "0.###"))
                .Append(" Hz Gain ")
                .Append(EqTextNumbers.Format(band.GainDb, "0.0"))
                .Append(" dB Q ")
                .Append(EqTextNumbers.Format(band.Q, "0.0"))
                .AppendLine();
        }

        return builder.ToString();
    }

    public static EqualizationCurve Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        double preampDb = 0;
        var bands = new List<PeqBand>();

        foreach (string rawLine in text.Split('\n'))
        {
            if (bands.Count >= EqualizationCurve.MaxBandCount)
            {
                break;
            }

            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            if (tokens[0].StartsWith("Preamp", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string token in tokens.Skip(1))
                {
                    if (EqTextNumbers.TryParse(token, out double gain))
                    {
                        preampDb = gain;
                        break;
                    }
                }

                continue;
            }

            if (tokens[0].Equals("Filter", StringComparison.OrdinalIgnoreCase) &&
                TryParseFilter(tokens, out PeqBand band))
            {
                bands.Add(band);
            }
        }

        return new EqualizationCurve(bands, preampDb);
    }

    // Reads a "Filter N: ON PK Fc F Hz Gain G dB Q Q" line. Disabled (OFF) and
    // non-peaking filters are ignored, as are lines missing any of Fc/Gain/Q.
    private static bool TryParseFilter(string[] tokens, out PeqBand band)
    {
        band = default;

        if (HasToken(tokens, "OFF") || !HasToken(tokens, "PK"))
        {
            return false;
        }

        if (!EqTextNumbers.TryParse(TokenAfter(tokens, "Fc"), out double frequencyHz) ||
            !EqTextNumbers.TryParse(TokenAfter(tokens, "Gain"), out double gainDb) ||
            !EqTextNumbers.TryParse(TokenAfter(tokens, "Q"), out double q))
        {
            return false;
        }

        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0 ||
            !double.IsFinite(q) || q <= 0 ||
            !double.IsFinite(gainDb))
        {
            return false;
        }

        band = new PeqBand(frequencyHz, q, gainDb);
        return true;
    }

    private static string? TokenAfter(string[] tokens, string keyword)
    {
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return tokens[i + 1];
            }
        }

        return null;
    }

    private static bool HasToken(string[] tokens, string keyword) =>
        tokens.Any(token => token.Equals(keyword, StringComparison.OrdinalIgnoreCase));
}
