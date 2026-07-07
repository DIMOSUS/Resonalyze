using System.Globalization;

namespace Resonalyze;

/// <summary>
/// Culture-tolerant numeric text parsing for the dark numeric fields. A lone
/// '.' or ',' is treated as the decimal separator regardless of culture:
/// decimal.TryParse's group-separator leniency would otherwise parse "1.5"
/// typed in a comma-decimal locale as 15. The only exception is the culture's
/// own group separator in a plausible thousands position — "12,000" in en-US
/// must keep round-tripping from the thousands display format.
/// </summary>
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
