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

    // The table says which tabs are plots; the factory must have a plot for each, or a tab switch throws.
    [Fact]
    public void EveryPlotTabHasAPlotToDraw()
    {
        using var analyzer = new TestAnalyzer();
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        var factory = new PlotModelFactory(
            analyzer.Document, analyzer.Engine, noise, _ => null, new AnalyzerViewSettings());

        foreach (ModeTab tab in Enum.GetValues<ModeTab>())
        {
            ModeDescriptor descriptor = ModeCatalog.For(tab);
            if (descriptor.HasPlotView)
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
