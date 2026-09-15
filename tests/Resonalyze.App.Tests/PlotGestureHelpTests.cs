namespace Resonalyze.App.Tests;

public sealed class PlotGestureHelpTests
{
    [Fact]
    public void EverySectionCarriesEntriesAndEveryEntrySaysBothHalves()
    {
        Assert.NotEmpty(PlotGestureHelp.Sections);

        foreach (PlotGestureHelpSection section in PlotGestureHelp.Sections)
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Title));
            Assert.NotEmpty(section.Entries);
            foreach (PlotGestureHelpEntry entry in section.Entries)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Gesture), section.Title);
                Assert.False(string.IsNullOrWhiteSpace(entry.Effect), entry.Gesture);
            }
        }
    }

    [Fact]
    public void NoGestureIsListedTwice()
    {
        string[] gestures = Entries().Select(entry => entry.Gesture).ToArray();

        Assert.Equal(gestures.Length, gestures.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            PlotGestureHelp.Sections.Count,
            PlotGestureHelp.Sections
                .Select(section => section.Title)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    [Theory]
    [InlineData("F1")]
    // Nothing else on screen signposts these gestures.
    [InlineData("Wheel")]
    [InlineData("Ctrl + right-button drag")]
    [InlineData("Click inside the box")]
    [InlineData("Ctrl + Z")]
    [InlineData("Double click")]
    [InlineData("Home, or A")]
    public void TheGesturesWithNoOtherSignpostAreListed(string gesture)
    {
        Assert.Contains(Entries(), entry => entry.Gesture == gesture);
    }

    [Fact]
    public void TheCardNamesItselfAndSaysWhichGraphsItIsAbout()
    {
        Assert.False(string.IsNullOrWhiteSpace(PlotGestureHelp.Title));
        Assert.Contains("Virtual DSP", PlotGestureHelp.Introduction);
        Assert.Contains("EQ Wizard", PlotGestureHelp.Introduction);
    }

    private static IEnumerable<PlotGestureHelpEntry> Entries() =>
        PlotGestureHelp.Sections.SelectMany(section => section.Entries);
}
