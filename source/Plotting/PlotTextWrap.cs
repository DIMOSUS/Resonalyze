using System.Globalization;

namespace Resonalyze;

/// <summary>Where a clipped plot text breaks, as GDI+ breaks it: after the last whole word that fits, else inside the word.</summary>
internal static class PlotTextWrap
{
    /// <summary>The length of <paramref name="text"/>'s first line; never splits a surrogate pair or a base from its marks.</summary>
    public static int FittingLength(string text, Func<int, bool> prefixFits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(prefixFits);
        if (prefixFits(text.Length))
        {
            return text.Length;
        }

        int wordEnd = 0;
        for (int space = text.IndexOf(' '); space > 0 && prefixFits(space); space = text.IndexOf(' ', space + 1))
        {
            wordEnd = space;
        }

        if (wordEnd > 0)
        {
            return wordEnd;
        }

        int[] elements = StringInfo.ParseCombiningCharacters(text);
        int length = elements.Length > 1 ? elements[1] : text.Length;
        for (int next = 2; next < elements.Length && prefixFits(elements[next]); next++)
        {
            length = elements[next];
        }

        return length;
    }
}
