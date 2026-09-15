namespace Resonalyze;

internal static class MenuText
{
    private const int MaxLength = 48;

    public static string Trim(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= MaxLength
            ? text
            : string.Concat(text.AsSpan(0, MaxLength - 3), "...");
    }
}
