using System.Globalization;
using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// Serialises and parses an <see cref="EqualizationCurve"/> as a simple text file:
/// the first line is the overall gain (preamp), and each following line is one PEQ
/// band as "index frequency Q gain". Parsing is defensive: blank lines, comments
/// and malformed lines are skipped, numbers accept '.' or ',' decimals, and the
/// band count is capped, so a hand-edited or corrupt file never throws.
/// </summary>
public static class PeqTextFile
{
    public static string Format(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        var builder = new StringBuilder();
        builder.AppendLine(FormatNumber(curve.PreampDb));
        for (int i = 0; i < curve.Bands.Count; i++)
        {
            PeqBand band = curve.Bands[i];
            builder.AppendLine(string.Join(
                ' ',
                (i + 1).ToString(CultureInfo.InvariantCulture),
                FormatNumber(band.FrequencyHz),
                FormatNumber(band.Q),
                FormatNumber(band.GainDb)));
        }

        return builder.ToString();
    }

    public static EqualizationCurve Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        double preampDb = 0;
        bool preampRead = false;
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

            string[] tokens = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);

            // The first meaningful, single-number line is the overall gain.
            if (!preampRead && tokens.Length == 1 && TryParseNumber(tokens[0], out double gain))
            {
                preampDb = gain;
                preampRead = true;
                continue;
            }

            if (TryParseBand(tokens, out PeqBand band))
            {
                bands.Add(band);
            }
        }

        return new EqualizationCurve(bands, preampDb);
    }

    // Accepts "index F Q G" (4 tokens) or "F Q G" (3 tokens); anything else is
    // skipped. Frequency and Q must be positive to be a usable band.
    private static bool TryParseBand(string[] tokens, out PeqBand band)
    {
        band = default;

        int offset = tokens.Length >= 4 ? 1 : 0;
        if (tokens.Length - offset < 3)
        {
            return false;
        }

        if (!TryParseNumber(tokens[offset], out double frequencyHz) ||
            !TryParseNumber(tokens[offset + 1], out double q) ||
            !TryParseNumber(tokens[offset + 2], out double gainDb))
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

    private static bool TryParseNumber(string token, out double value)
    {
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return double.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
