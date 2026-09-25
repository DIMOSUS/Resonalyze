using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// Equalizer APO text format (<c>Preamp: -6.0 dB</c>, <c>Filter 1: ON PK Fc 600 Hz Gain 6.0 dB Q 4.0</c>); parsing never
/// throws. See docs/tech/eq-auto-tuner.md#equalizer-apo-text-format for the type mapping.
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

    internal static string FormatPreampLine(double preampDb) =>
        $"Preamp: {EqTextNumbers.Format(preampDb, "0.0")} dB";

    // A first-order all-pass has no APO spelling: skipped, keeping its slot number so the gap is visible.
    internal static string FormatFilters(EqualizationCurve curve)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < curve.Bands.Count; i++)
        {
            PeqBand band = curve.Bands[i];
            if (band.Type == PeqBandType.AllPassFirstOrder)
            {
                continue;
            }

            builder
                .Append("Filter ")
                .Append((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(": ON ")
                .Append(TypeToken(band.Type))
                .Append(" Fc ")
                .Append(EqTextNumbers.Format(band.FrequencyHz, "0.###"))
                .Append(" Hz");
            if (band.Type != PeqBandType.AllPassSecondOrder)
            {
                builder
                    .Append(" Gain ")
                    .Append(EqTextNumbers.Format(band.GainDb, "0.0"))
                    .Append(" dB");
            }

            builder
                .Append(" Q ")
                .Append(EqTextNumbers.Format(band.Q, EqTextNumbers.QFormat))
                .AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>APO keyword: LSC/HSC (with Q), AP for 2nd-order all-pass; AP1 only for the Virtual DSP text sheet.</summary>
    public static string TypeToken(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => "LSC",
        PeqBandType.HighShelf => "HSC",
        PeqBandType.AllPassFirstOrder => "AP1",
        PeqBandType.AllPassSecondOrder => "AP",
        _ => "PK"
    };

    public static EqualizationCurve Parse(string text) =>
        TryParse(text, out EqualizationCurve curve)
            ? curve
            : new EqualizationCurve(Array.Empty<PeqBand>());

    /// <summary>True when a Preamp or well-formed Filter line was recognised (a preamp-only file is a valid profile).</summary>
    public static bool TryParse(string text, out EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(text);

        double preampDb = 0;
        bool recognized = false;
        var bands = new List<PeqBand>();
        // APO applies what follows a "Channel:" line to those channels only. One bank holds one channel's chain: the
        // first channel named, plus what every channel runs (before any Channel line, or under "Channel: all").
        HashSet<string>? importedChannels = null;
        bool sectionApplies = true;

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

            if (tokens[0].TrimEnd(':').Equals("Channel", StringComparison.OrdinalIgnoreCase))
            {
                var named = new HashSet<string>(
                    tokens.Skip(1).SelectMany(token => token.Split(',', StringSplitOptions.RemoveEmptyEntries)),
                    StringComparer.OrdinalIgnoreCase);
                if (named.Contains("all"))
                {
                    sectionApplies = true;
                }
                else if (importedChannels == null)
                {
                    importedChannels = named;
                    sectionApplies = named.Count > 0;
                }
                else
                {
                    sectionApplies = named.Overlaps(importedChannels);
                }

                continue;
            }

            if (!sectionApplies)
            {
                continue;
            }

            // Each Preamp line is its own gain stage in APO's chain, so they add up.
            if (tokens[0].StartsWith("Preamp", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string token in tokens.Skip(1))
                {
                    if (EqTextNumbers.TryParse(token, out double gain))
                    {
                        preampDb += gain;
                        recognized = true;
                        break;
                    }
                }

                continue;
            }

            if (IsFilterKeyword(tokens[0]) &&
                TryParseFilter(tokens, out PeqBand band))
            {
                bands.Add(band);
                recognized = true;
            }
        }

        curve = new EqualizationCurve(bands, preampDb);
        return recognized;
    }

    // APO does not interpret the filter number and lets it be omitted: "Filter 1:", "Filter1:" and "Filter:" all open a line.
    private static bool IsFilterKeyword(string token)
    {
        string name = token.TrimEnd(':');
        return name.StartsWith("Filter", StringComparison.OrdinalIgnoreCase) &&
            name.AsSpan("Filter".Length).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    // Gain may be absent only on an all-pass; Q only on a shelf (read at DefaultShelfQ).
    private static bool TryParseFilter(string[] tokens, out PeqBand band)
    {
        band = default;

        if (HasToken(tokens, "OFF") || !TryReadType(tokens, out PeqBandType type))
        {
            return false;
        }

        if (!EqTextNumbers.TryParse(TokenAfter(tokens, "Fc"), out double frequencyHz))
        {
            return false;
        }

        double gainDb = 0;
        if (type != PeqBandType.AllPassSecondOrder &&
            !EqTextNumbers.TryParse(TokenAfter(tokens, "Gain"), out gainDb))
        {
            return false;
        }

        if (!EqTextNumbers.TryParse(TokenAfter(tokens, "Q"), out double q))
        {
            if (!type.IsShelving())
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

    /// <summary>Q for a shelf stated without one: the steepest monotonic knee.</summary>
    internal const double DefaultShelfQ = 0.7071067811865476;

    // A shelf keyword followed by a number (LS 6dB, LSC 10.8 dB) uses a corner/slope parameterisation and is skipped.
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
            // APO reads Modal (REW's room-mode filter) and PEQ as the same peaking filter as PK.
            if (token.Equals("PK", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Modal", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("PEQ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (token.Equals("AP", StringComparison.OrdinalIgnoreCase))
            {
                type = PeqBandType.AllPassSecondOrder;
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
