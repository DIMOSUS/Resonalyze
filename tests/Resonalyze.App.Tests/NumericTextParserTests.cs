using System.Globalization;

namespace Resonalyze.App.Tests;

public sealed class NumericTextParserTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo DeDe = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo RuRu = CultureInfo.GetCultureInfo("ru-RU");

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("1,5", 1.5)]
    [InlineData("-3.25", -3.25)]
    [InlineData("-3,25", -3.25)]
    [InlineData("42", 42)]
    public void TryParse_LoneSeparatorIsDecimalInAnyCulture(string text, double expected)
    {
        foreach (CultureInfo culture in new[] { EnUs, DeDe, RuRu })
        {
            // decimal.TryParse alone would read "1.5" in de-DE as fifteen: the
            // dot is the German group separator and group sizes are not checked.
            Assert.True(NumericTextParser.TryParse(text, culture, out decimal value));
            Assert.Equal((decimal)expected, value);
        }
    }

    [Fact]
    public void TryParse_KeepsPlausibleThousandsOfTheCulture()
    {
        Assert.True(NumericTextParser.TryParse("12,000", EnUs, out decimal enUs));
        Assert.Equal(12_000m, enUs);

        Assert.True(NumericTextParser.TryParse("12.000", DeDe, out decimal deDe));
        Assert.Equal(12_000m, deDe);
    }

    [Fact]
    public void TryParse_FullThousandsFormatRoundTrips()
    {
        Assert.True(NumericTextParser.TryParse("1,234.5", EnUs, out decimal value));
        Assert.Equal(1_234.5m, value);
    }

    [Fact]
    public void TryParse_ThousandsPositionOfForeignSeparatorIsStillDecimal()
    {
        // '.' is not the en-US group separator, so "1.234" is one point two
        // three four — not twelve hundred.
        Assert.True(NumericTextParser.TryParse("1.234", EnUs, out decimal value));
        Assert.Equal(1.234m, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1..5")]
    public void TryParse_RejectsUnparsableText(string? text)
    {
        Assert.False(NumericTextParser.TryParse(text, EnUs, out _));
    }
}
