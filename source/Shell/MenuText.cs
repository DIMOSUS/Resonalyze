namespace Resonalyze;

/// <summary>
/// Shared truncation for dropdown menu-item captions, which must stay on one short line
/// rather than stretch the menu to a file's full path or an overlay's full title.
/// </summary>
internal static class MenuText
{
    private const int MaxLength = 48;

    /// <summary>
    /// Returns <paramref name="text"/> unchanged when it fits, otherwise the first
    /// <c>MaxLength - 3</c> characters followed by an ellipsis.
    /// </summary>
    public static string Trim(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= MaxLength
            ? text
            : string.Concat(text.AsSpan(0, MaxLength - 3), "...");
    }
}
