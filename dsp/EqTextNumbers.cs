using System.Globalization;

namespace Resonalyze.Dsp;

// Parsing takes a point, or a lone comma, as the decimal separator under any culture; formatting is invariant so files are portable.
internal static class EqTextNumbers
{
    // A tenth is 20 % of a wide Q 0.5; three decimals keep a fitted Q within 0.2 % and a round one short (Q 4.0).
    public const string QFormat = "0.0##";

    public static bool TryParse(string? token, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        // PEQ writers group no thousands, so a comma is a decimal comma; beside a point it is ambiguous and refused.
        string trimmed = token.Trim();
        int comma = trimmed.IndexOf(',');
        if (comma >= 0)
        {
            if (trimmed.Contains('.') || trimmed.IndexOf(',', comma + 1) >= 0)
            {
                return false;
            }

            trimmed = trimmed.Replace(',', '.');
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static string Format(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
