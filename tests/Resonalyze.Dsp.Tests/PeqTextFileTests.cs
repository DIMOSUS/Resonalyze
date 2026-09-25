using System.Globalization;

namespace Resonalyze.Dsp.Tests;

public sealed class PeqTextFileTests
{
    [Fact]
    public void FormatThenParse_RoundTripsCurve()
    {
        var curve = new EqualizationCurve(
            new[]
            {
                new PeqBand(600, 4.0, 6.0),
                new PeqBand(1577, 1.4, -4.1)
            },
            preampDb: -6.0);

        EqualizationCurve parsed = PeqTextFile.Parse(PeqTextFile.Format(curve));

        Assert.Equal(-6.0, parsed.PreampDb, 6);
        Assert.Equal(2, parsed.Bands.Count);
        Assert.Equal(600, parsed.Bands[0].FrequencyHz, 6);
        Assert.Equal(4.0, parsed.Bands[0].Q, 6);
        Assert.Equal(6.0, parsed.Bands[0].GainDb, 6);
        Assert.Equal(1577, parsed.Bands[1].FrequencyHz, 6);
        Assert.Equal(1.4, parsed.Bands[1].Q, 6);
        Assert.Equal(-4.1, parsed.Bands[1].GainDb, 6);
    }

    [Fact]
    public void Format_MatchesEqualizerApoLayout()
    {
        var curve = new EqualizationCurve(
            new[] { new PeqBand(600, 4.0, 6.0) },
            preampDb: -6.0);

        string text = PeqTextFile.Format(curve);

        Assert.Contains("Preamp: -6.0 dB", text);
        Assert.Contains("Filter 1: ON PK Fc 600 Hz Gain 6.0 dB Q 4.0", text);
    }

    [Fact]
    public void Parse_TakesTheFirstChannelsChainAndAddsItsPreampStages()
    {
        // APO runs L through -3 dB, then its bell; R through -3 dB, -2 dB and its own bell.
        string text =
            "Preamp: -3 dB\n" +
            "Channel: L\n" +
            "Filter 1: ON PK Fc 100 Hz Gain -6 dB Q 2\n" +
            "Channel: R\n" +
            "Preamp: -2 dB\n" +
            "Filter 1: ON PK Fc 120 Hz Gain -6 dB Q 2\n" +
            "Channel: all\n" +
            "Filter 2: ON PK Fc 1000 Hz Gain 2 dB Q 1\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(-3.0, curve.PreampDb, 6);
        Assert.Equal(new[] { 100.0, 1000.0 }, curve.Bands.Select(b => b.FrequencyHz));
    }

    [Fact]
    public void Parse_AFirstSectionNamingSeveralChannels_ImportsOnlyTheFirstChannelsLaterSections()
    {
        string text =
            "Channel: L R\n" +
            "Preamp: -3 dB\n" +
            "Channel: L\n" +
            "Filter 1: ON PK Fc 100 Hz Gain -4 dB Q 2\n" +
            "Channel: R\n" +
            "Filter 2: ON PK Fc 200 Hz Gain -6 dB Q 1\n" +
            "Channel:\n" +
            "Filter 3: ON PK Fc 300 Hz Gain -2 dB Q 1\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        // L runs the shared preamp and its own bell; an empty Channel line changes nothing.
        Assert.Equal(-3.0, curve.PreampDb, 6);
        Assert.Equal(new[] { 100.0 }, curve.Bands.Select(b => b.FrequencyHz));
    }

    [Fact]
    public void Parse_AddsPreampLinesAsTheStagesTheyAre()
    {
        EqualizationCurve curve = PeqTextFile.Parse("Preamp: -3 dB\nPreamp: -2.5 dB\nFilter: ON PK Fc 100 Hz Gain -6 dB Q 2\n");

        Assert.Equal(-5.5, curve.PreampDb, 6);
    }

    [Fact]
    public void Parse_ReadsFilterLinesWithTheNumberOmitted()
    {
        // APO does not interpret the filter number and lets it be left out ("Filter: ON NO Fc 50 Hz" in its reference).
        string text =
            "Preamp: -6 dB\n" +
            "Filter: ON PK Fc 600 Hz Gain 6.0 dB Q 4.0\n" +
            "Filter2: ON PK Fc 1577 Hz Gain -4.1 dB Q 1.4\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(-6.0, curve.PreampDb, 6);
        Assert.Equal(new[] { 600.0, 1577.0 }, curve.Bands.Select(b => b.FrequencyHz));
    }

    [Fact]
    public void Parse_ReadsModalAndPeqAsPeaking()
    {
        // APO lists PK, Modal and PEQ as one peaking filter; REW writes its room-mode filters as Modal.
        string text =
            "Filter 1: ON Modal Fc 45 Hz Gain -9.0 dB Q 6.0\n" +
            "Filter 2: ON PEQ Fc 120 Hz Gain -3.0 dB Q 2.0\n" +
            "Filter 3: ON PK Fc 600 Hz Gain 2.0 dB Q 1.0\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(3, curve.Bands.Count);
        Assert.All(curve.Bands, b => Assert.Equal(PeqBandType.Peaking, b.Type));
        Assert.Equal(-9.0, curve.Bands[0].GainDb, 6);
        Assert.Equal(6.0, curve.Bands[0].Q, 6);
    }

    [Fact]
    public void Parse_DoesNotTakeAWordThatOnlyStartsWithFilterForAFilterLine()
    {
        Assert.False(PeqTextFile.TryParse("Filters: ON PK Fc 600 Hz Gain 6.0 dB Q 4.0", out _));
    }

    [Fact]
    public void Parse_ReadsEqualizerApoExample()
    {
        string text =
            "Preamp: -6.0 dB\n" +
            "\n" +
            "Filter 1: ON PK Fc 600 Hz Gain 6.0 dB Q 4.0\n" +
            "Filter 2: ON PK Fc 5582 Hz Gain 4.9 dB Q 2.0\n" +
            "Filter 3: ON PK Fc 1577 Hz Gain -4.1 dB Q 1.4\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(-6.0, curve.PreampDb, 6);
        Assert.Equal(3, curve.Bands.Count);
        Assert.Equal(5582, curve.Bands[1].FrequencyHz, 6);
        Assert.Equal(4.9, curve.Bands[1].GainDb, 6);
        Assert.Equal(2.0, curve.Bands[1].Q, 6);
        Assert.Equal(-4.1, curve.Bands[2].GainDb, 6);
    }

    [Fact]
    public void Parse_SkipsDisabledAndUnsupportedFilters()
    {
        string text =
            "Preamp: 0 dB\n" +
            "Filter 1: ON PK Fc 1000 Hz Gain 6 dB Q 1\n" +
            "Filter 2: OFF PK Fc 2000 Hz Gain 3 dB Q 1\n" +   // disabled -> skipped
            "Filter 3: ON LP Fc 8000 Hz\n" +                  // low-pass -> skipped
            "Filter 4: ON PK Fc 4000 Hz Gain -3 dB Q 2\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(1000, curve.Bands[0].FrequencyHz, 6);
        Assert.Equal(4000, curve.Bands[1].FrequencyHz, 6);
    }

    [Fact]
    public void Parse_SkipsBlankCommentAndMalformedLines()
    {
        string text =
            "Preamp: -1.5 dB\n" +
            "\n" +
            "# a comment\n" +
            "garbage line\n" +
            "Filter 1: ON PK Fc 1000 Hz Gain 6 dB Q 1\n" +
            "Filter 2: ON PK Fc nope Hz Gain x dB Q y\n" +   // unparseable -> skipped
            "Filter 3: ON PK Fc 2000 Hz Gain 5 dB Q 0\n" +   // Q = 0 -> skipped
            "Filter 4: ON PK Fc 4000 Hz Gain -3 dB Q 2\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(-1.5, curve.PreampDb, 6);
        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(1000, curve.Bands[0].FrequencyHz, 6);
        Assert.Equal(4000, curve.Bands[1].FrequencyHz, 6);
    }

    [Fact]
    public void Parse_CapsBandCountToMaximum()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("Preamp: 0 dB");
        for (int i = 0; i < EqualizationCurve.MaxBandCount + 10; i++)
        {
            builder.AppendLine($"Filter {i + 1}: ON PK Fc 1000 Hz Gain 1 dB Q 1");
        }

        EqualizationCurve curve = PeqTextFile.Parse(builder.ToString());

        Assert.Equal(EqualizationCurve.MaxBandCount, curve.Bands.Count);
    }

    [Fact]
    public void Parse_EmptyOrGarbageOnly_ReturnsEmptyCurve()
    {
        EqualizationCurve curve = PeqTextFile.Parse("   \n\n# nothing here\n");

        Assert.Empty(curve.Bands);
        Assert.Equal(0, curve.PreampDb, 6);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public void Parse_ReadsADecimalCommaUnderAnyCulture(string culture)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            EqualizationCurve curve = PeqTextFile.Parse(
                "Preamp: -6,5 dB\n" +
                "Filter 1: ON PK Fc 62,5 Hz Gain -4,5 dB Q 0,707\n" +
                "Filter 2: ON PK Fc 1250.5 Hz Gain 2.5 dB Q 1.25\n" +
                "Filter 3: ON PK Fc 1,000.5 Hz Gain 1 dB Q 1\n");

            Assert.Equal(-6.5, curve.PreampDb, 9);
            Assert.Equal(2, curve.Bands.Count);
            Assert.Equal(new PeqBand(62.5, 0.707, -4.5), curve.Bands[0]);
            Assert.Equal(new PeqBand(1250.5, 1.25, 2.5), curve.Bands[1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Parse_HandlesCarriageReturnLineEndings()
    {
        string text = "Preamp: -6.0 dB\r\n\r\nFilter 1: ON PK Fc 600 Hz Gain 6.0 dB Q 4.0\r\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(-6.0, curve.PreampDb, 6);
        Assert.Single(curve.Bands);
        Assert.Equal(600, curve.Bands[0].FrequencyHz, 6);
    }
}
