namespace Resonalyze.App.Tests;

// The card F1 puts on screen. It is prose rather than behaviour, so what is worth
// pinning is that it stays a complete, non-repeating list: a gesture nobody can find
// here is a gesture nobody is told about anywhere in the window.
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
    // The card has to explain the key that opened it, or a reader has no way to get
    // back to it.
    [InlineData("F1")]
    // The gestures with no other signpost in the window: nothing on screen says the
    // wheel zooms, that a box can be drawn, or that a zoom can be walked back.
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
