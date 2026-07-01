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
