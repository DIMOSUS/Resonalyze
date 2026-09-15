using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Builds the dialog Filter string and resolves its 1-based FilterIndex, so the two cannot disagree.</summary>
internal static class EqFormatFileDialogs
{
    internal static string BuildFilter(
        IReadOnlyList<IEqProfileFormat> formats,
        string? trailingFilter = null)
    {
        string filter = string.Join(
            "|",
            formats.Select(format =>
                $"{format.Name} (*.{format.Extension})|*.{format.Extension}"));
        return trailingFilter == null ? filter : $"{filter}|{trailingFilter}";
    }

    /// <summary>Null for an index past the formats (a trailing non-format entry); below 1 resolves to the first.</summary>
    internal static IEqProfileFormat? ResolveFormat(
        IReadOnlyList<IEqProfileFormat> formats,
        int filterIndex)
    {
        int index = filterIndex - 1;
        if (index >= formats.Count)
        {
            return null;
        }

        return formats[Math.Max(0, index)];
    }
}
