using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Builds the file-dialog Filter string for a list of EQ profile formats and
/// resolves the dialog's 1-based <c>FilterIndex</c> back to the format, so
/// the string and the lookup can never disagree about ordering or the
/// off-by-one.
/// </summary>
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

    /// <summary>
    /// The format the dialog's <paramref name="filterIndex"/> selects, or
    /// null when the index points past the format list — i.e. at a trailing
    /// non-format entry appended via
    /// <see cref="BuildFilter(IReadOnlyList{IEqProfileFormat}, string?)"/>.
    /// An index below the list (defensive; dialogs start at 1) resolves to
    /// the first format.
    /// </summary>
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
