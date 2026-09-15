namespace Resonalyze.App.Tests;

public sealed class OverlaySlotNameTests
{
    [Theory]
    [InlineData("Overlay 4: Input Spectrum (RTA)", 4, "Input Spectrum (RTA)")]
    [InlineData("Overlay 12: Input Spectrum (RTA)", 12, "Input Spectrum (RTA)")]
    [InlineData("overlay 4: main", 4, "main")]
    public void Shorten_DropsTheGeneratedPrefix(string title, int slot, string expected)
    {
        Assert.Equal(expected, OverlaySlotName.Shorten(title, slot));
    }

    [Theory]
    [InlineData("4: Main", 4, "Main")]
    [InlineData("12:Main", 12, "Main")]
    public void Shorten_DropsTheBareNumberedPrefix(string title, int slot, string expected)
    {
        Assert.Equal(expected, OverlaySlotName.Shorten(title, slot));
    }

    [Theory]
    [InlineData("Overlayed response", 4)]
    [InlineData("Overlay correction", 4)]
    [InlineData("Overlays of the left door", 4)]
    public void Shorten_KeepsANameThatMerelyStartsWithTheWordOverlay(string title, int slot)
    {
        Assert.Equal(title, OverlaySlotName.Shorten(title, slot));
    }

    [Theory]
    [InlineData("4 Ω response", 4)]
    [InlineData("4 inch midrange", 4)]
    [InlineData("2 way", 2)]
    [InlineData("40 Hz notch", 4)]
    [InlineData("4kHz shelf", 4)]
    public void Shorten_KeepsALeadingNumberThatBelongsToTheName(string title, int slot)
    {
        Assert.Equal(title, OverlaySlotName.Shorten(title, slot));
    }

    [Theory]
    [InlineData("Overlay 5: Input Spectrum", 4)]
    [InlineData("5: Main", 4)]
    public void Shorten_KeepsAPrefixNamingADifferentSlot(string title, int slot)
    {
        Assert.Equal(title, OverlaySlotName.Shorten(title, slot));
    }

    [Theory]
    [InlineData("Overlay 4:", 4)]
    [InlineData("4:", 4)]
    [InlineData("4: ", 4)]
    public void Shorten_KeepsATitleThatIsNothingButThePrefix(string title, int slot)
    {
        Assert.Equal(title, OverlaySlotName.Shorten(title, slot));
    }

    [Theory]
    [InlineData("r sub", 6)]
    [InlineData("110 SPL", 1)]
    [InlineData("", 3)]
    public void Shorten_LeavesAPlainNameAlone(string title, int slot)
    {
        Assert.Equal(title, OverlaySlotName.Shorten(title, slot));
    }

    [Fact]
    public void ForSave_AnOccupiedSlotKeepsItsName()
    {
        Assert.Equal(
            "Left tweeter",
            OverlaySlotName.ForSave(
                slotOccupied: true,
                "Left tweeter",
                slot: 4,
                "Frequency Response"));
    }

    [Fact]
    public void ForSave_AnEmptySlotGetsTheAutomaticName()
    {
        Assert.Equal(
            "Overlay 4: Frequency Response",
            OverlaySlotName.ForSave(
                slotOccupied: false,
                "",
                slot: 4,
                "Frequency Response"));
    }
}
