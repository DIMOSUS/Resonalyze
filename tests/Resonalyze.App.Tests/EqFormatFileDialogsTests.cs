using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqFormatFileDialogsTests
{
    [Fact]
    public void BuildFilter_PairsEveryFormatNameWithItsPattern()
    {
        IReadOnlyList<IEqProfileFormat> formats = EqProfileFormats.Importable;

        string filter = EqFormatFileDialogs.BuildFilter(formats);

        // A dialog filter is name|pattern pairs joined by '|': 2N-1 separators.
        Assert.Equal(formats.Count * 2 - 1, filter.Count(c => c == '|'));
        string[] parts = filter.Split('|');
        for (int i = 0; i < formats.Count; i++)
        {
            Assert.StartsWith(formats[i].Name, parts[2 * i]);
            Assert.Equal($"*.{formats[i].Extension}", parts[2 * i + 1]);
        }
    }

    [Fact]
    public void BuildFilter_AppendsTheTrailingEntry()
    {
        IReadOnlyList<IEqProfileFormat> formats = EqProfileFormats.Exportable;

        string filter = EqFormatFileDialogs.BuildFilter(
            formats, "Tuning sheet (PDF) (*.pdf)|*.pdf");

        Assert.EndsWith("|Tuning sheet (PDF) (*.pdf)|*.pdf", filter);
        Assert.Equal((formats.Count + 1) * 2 - 1, filter.Count(c => c == '|'));
    }

    [Fact]
    public void ResolveFormat_MapsTheOneBasedIndexOntoTheList()
    {
        IReadOnlyList<IEqProfileFormat> formats = EqProfileFormats.Importable;

        Assert.Same(formats[0], EqFormatFileDialogs.ResolveFormat(formats, 1));
        Assert.Same(
            formats[^1],
            EqFormatFileDialogs.ResolveFormat(formats, formats.Count));
    }

    [Fact]
    public void ResolveFormat_ReturnsNullForATrailingEntry()
    {
        IReadOnlyList<IEqProfileFormat> formats = EqProfileFormats.Exportable;

        Assert.Null(EqFormatFileDialogs.ResolveFormat(formats, formats.Count + 1));
    }

    [Fact]
    public void ResolveFormat_ClampsAnIndexBelowTheList()
    {
        IReadOnlyList<IEqProfileFormat> formats = EqProfileFormats.Importable;

        Assert.Same(formats[0], EqFormatFileDialogs.ResolveFormat(formats, 0));
        Assert.Same(formats[0], EqFormatFileDialogs.ResolveFormat(formats, -3));
    }
}
