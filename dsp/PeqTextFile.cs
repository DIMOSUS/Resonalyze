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
/// filters (OFF), unsupported types and malformed lines are skipped; numbers accept
/// '.' or ',' decimals and the band count is capped, so a hand-edited or foreign
/// file never throws.
/// </summary>
/// <remarks>
/// Three of Equalizer APO's types map onto a <see cref="PeqBand"/>: <c>PK</c>, and
/// the shelves <c>LSC</c>/<c>HSC</c> written with a Q — which is the same
/// centre-frequency, half-gain-at-Fc shelf the library realizes. Plain <c>LS</c>
/// and <c>HS</c> carry no Q and are read at the default shelf Q.
///
/// The <c>LS 6dB</c> / <c>LS 12dB</c> family (and its high-shelf twin) is NOT read:
/// those state a CORNER frequency instead of the middle of the transition, so
/// taking their Fc as ours would move the shelf. They are skipped like any other
/// unsupported type rather than imported to the wrong place.
/// </remarks>
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

    // The block of "Filter N: ON <type> Fc ... Gain ... dB Q ..." lines (no preamp).
    internal static string FormatFilters(EqualizationCurve curve)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < curve.Bands.Count; i++)
        {
            PeqBand band = curve.Bands[i];
            builder
                .Append("Filter ")
                .Append((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(": ON ")
                .Append(TypeToken(band.Type))
                .Append(" Fc ")
                .Append(EqTextNumbers.Format(band.FrequencyHz, "0.###"))
                .Append(" Hz Gain ")
                .Append(EqTextNumbers.Format(band.GainDb, "0.0"))
                .Append(" dB Q ")
                .Append(EqTextNumbers.Format(band.Q, "0.0"))
                .AppendLine();
        }

        return builder.ToString();
    }

    // The shelves are written as LSC/HSC — the variant that carries a Q — rather
    // than as plain LS/HS, whose slope the reader would have to assume.
    private static string TypeToken(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => "LSC",
        PeqBandType.HighShelf => "HSC",
        _ => "PK"
    };

    public static EqualizationCurve Parse(string text) =>
        TryParse(text, out EqualizationCurve curve)
            ? curve
            : new EqualizationCurve(Array.Empty<PeqBand>());

    /// <summary>
    /// Parses and reports whether anything was recognised — a <c>Preamp:</c> or a
    /// well-formed <c>Filter</c> line. A file with only a preamp is a valid
    /// neutral profile, so band count cannot stand in for this.
    /// </summary>
    public static bool TryParse(string text, out EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(text);

        double preampDb = 0;
        bool recognized = false;
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
                        recognized = true;
                        break;
                    }
                }

                continue;
            }

            if (tokens[0].Equals("Filter", StringComparison.OrdinalIgnoreCase) &&
                TryParseFilter(tokens, out PeqBand band))
            {
                bands.Add(band);
                recognized = true;
            }
        }

        curve = new EqualizationCurve(bands, preampDb);
        return recognized;
    }

    // Reads a "Filter N: ON <type> Fc F Hz Gain G dB Q Q" line. Disabled (OFF) and
    // unsupported types are ignored, as are lines missing Fc or Gain. Q may be
    // absent only on a plain LS/HS, which states no slope of its own.
    private static bool TryParseFilter(string[] tokens, out PeqBand band)
    {
        band = default;

        if (HasToken(tokens, "OFF") || !TryReadType(tokens, out PeqBandType type))
        {
            return false;
        }

        if (!EqTextNumbers.TryParse(TokenAfter(tokens, "Fc"), out double frequencyHz) ||
            !EqTextNumbers.TryParse(TokenAfter(tokens, "Gain"), out double gainDb))
        {
            return false;
        }

        // A bell without a Q is malformed; a shelf without one is the LS/HS spelling.
        if (!EqTextNumbers.TryParse(TokenAfter(tokens, "Q"), out double q))
        {
            if (type == PeqBandType.Peaking)
            {
                return false;
            }

            q = DefaultShelfQ;
        }

        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0 ||
            !double.IsFinite(q) || q <= 0 ||
            !double.IsFinite(gainDb))
        {
            return false;
        }

        band = new PeqBand(frequencyHz, q, gainDb, type);
        return true;
    }

    /// <summary>
    /// The Q a shelf written without one is read at: the steepest knee that still
    /// rises monotonically, which is what a filter stating no slope means.
    /// </summary>
    internal const double DefaultShelfQ = 0.7071067811865476;

    // Recognises the filter type. A shelf keyword followed by a number states its
    // steepness in another parameterisation — the corner-frequency "LS 6dB" family,
    // or "LSC 10.8 dB" in dB per octave — where Fc is not the middle of the
    // transition and the slope is not our Q. Those are skipped rather than read
    // into a shelf that would sit somewhere else.
    private static bool TryReadType(string[] tokens, out PeqBandType type)
    {
        type = PeqBandType.Peaking;
        for (int index = 0; index < tokens.Length; index++)
        {
            string token = tokens[index];
            bool low = token.Equals("LS", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("LSC", StringComparison.OrdinalIgnoreCase);
            bool high = token.Equals("HS", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("HSC", StringComparison.OrdinalIgnoreCase);
            if (token.Equals("PK", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!low && !high)
            {
                continue;
            }

            if (index + 1 < tokens.Length && StatesItsOwnSlope(tokens[index + 1]))
            {
                return false;
            }

            type = low ? PeqBandType.LowShelf : PeqBandType.HighShelf;
            return true;
        }

        return false;
    }

    // "6dB", "12dB" or a bare number ahead of the "dB" of a dB/octave slope.
    private static bool StatesItsOwnSlope(string token) =>
        EqTextNumbers.TryParse(token, out _) ||
        (token.EndsWith("dB", StringComparison.OrdinalIgnoreCase) &&
            EqTextNumbers.TryParse(token[..^2], out _));

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
