namespace Resonalyze.App.Tests;

/// <summary>The tab table the main window lays itself out and draws from.</summary>
public sealed class ModeCatalogTests
{
    [Fact]
    public void EveryTabIsDescribed()
    {
        foreach (ModeTab tab in Enum.GetValues<ModeTab>())
        {
            Assert.Equal(tab, ModeCatalog.For(tab).Tab);
        }
    }

    // The table says which tabs are plots; the factory must have a plot for each, or a tab switch throws. Live Spectrum
    // is the exception: its controller draws it (LiveSpectrumPlotFactory).
    [Fact]
    public void EveryPlotTabHasAPlotToDraw()
    {
        using var analyzer = new TestAnalyzer();
        var factory = new PlotModelFactory(
            analyzer.Document, analyzer.Engine, _ => null, new AnalyzerViewSettings());

        foreach (ModeTab tab in Enum.GetValues<ModeTab>())
        {
            ModeDescriptor descriptor = ModeCatalog.For(tab);
            if (descriptor.HasPlotView && descriptor.Mode != Mode.LiveSpectrum)
            {
                Assert.NotNull(factory.Create(descriptor.Mode, includeCurves: false));
            }
            else
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create(descriptor.Mode, includeCurves: false));
            }
        }
    }
}
