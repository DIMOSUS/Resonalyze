using System.Globalization;

namespace Resonalyze;

/// <summary>A lone '.' or ',' is the decimal separator in any culture (TryParse would read "1.5" as 15 in comma locales),
/// except the culture's group separator in a thousands position ("12,000" in en-US).</summary>
internal static class NumericTextParser
{
    public static bool TryParse(string? text, CultureInfo culture, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        int separatorIndex = -1;
        int separatorCount = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '.' or ',')
            {
                separatorIndex = i;
                separatorCount++;
            }
        }

        if (separatorCount == 1 && !IsPlausibleThousands(text, separatorIndex, culture))
        {
            text = string.Concat(
                text.AsSpan(0, separatorIndex),
                culture.NumberFormat.NumberDecimalSeparator,
                text.AsSpan(separatorIndex + 1));
        }

        if (decimal.TryParse(text, NumberStyles.Number, culture, out value))
        {
            return true;
        }

        return decimal.TryParse(
            text,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool IsPlausibleThousands(
        string text,
        int separatorIndex,
        CultureInfo culture)
    {
        string groupSeparator = culture.NumberFormat.NumberGroupSeparator;
        if (groupSeparator.Length != 1 ||
            text[separatorIndex] != groupSeparator[0] ||
            separatorIndex == 0 ||
            !char.IsAsciiDigit(text[separatorIndex - 1]))
        {
            return false;
        }

        if (text.Length - separatorIndex - 1 != 3)
        {
            return false;
        }

        for (int i = separatorIndex + 1; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}
