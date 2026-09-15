using System.Globalization;

namespace Resonalyze.Dsp;

// Parsing accepts '.' and ',' decimals; formatting is invariant so files are portable.
internal static class EqTextNumbers
{
    public static bool TryParse(string? token, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string trimmed = token.Trim();
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    public static string Format(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
