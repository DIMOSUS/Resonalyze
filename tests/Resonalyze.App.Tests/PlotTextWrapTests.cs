namespace Resonalyze.App.Tests;

// A fake width of one per UTF-16 unit, so each case states its break in characters.
public sealed class PlotTextWrapTests
{
    [Theory]
    [InlineData("short", 10, 5)]
    [InlineData("abc def ghi", 8, 7)]
    [InlineData("abcdefghij", 4, 4)]
    // U+1F600 is a surrogate pair at 2..3: a break at 3 would halve it.
    [InlineData("ab\uD83D\uDE00cd", 3, 2)]
    // U+0301 belongs to the e before it.
    [InlineData("ae\u0301b", 2, 1)]
    // Nothing fits: the first whole element still goes on the line, or the text would never advance.
    [InlineData("\uD83D\uDE00x", 0, 2)]
    public void FittingLength_BreaksWhereGdiWouldAndNeverInsideACharacter(string text, int width, int expected)
    {
        Assert.Equal(expected, PlotTextWrap.FittingLength(text, length => length <= width));
    }
}
