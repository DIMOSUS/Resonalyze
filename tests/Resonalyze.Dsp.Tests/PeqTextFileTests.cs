namespace Resonalyze.Dsp.Tests;

public sealed class PeqTextFileTests
{
    [Fact]
    public void FormatThenParse_RoundTripsCurve()
    {
        var curve = new EqualizationCurve(
            new[]
            {
                new PeqBand(1_000, 1.0, 6.0),
                new PeqBand(3_000, 3.4, -10.0)
            },
            preampDb: -2.5);

        EqualizationCurve parsed = PeqTextFile.Parse(PeqTextFile.Format(curve));

        Assert.Equal(-2.5, parsed.PreampDb, 6);
        Assert.Equal(2, parsed.Bands.Count);
        Assert.Equal(1_000, parsed.Bands[0].FrequencyHz, 6);
        Assert.Equal(1.0, parsed.Bands[0].Q, 6);
        Assert.Equal(6.0, parsed.Bands[0].GainDb, 6);
        Assert.Equal(3_000, parsed.Bands[1].FrequencyHz, 6);
        Assert.Equal(3.4, parsed.Bands[1].Q, 6);
        Assert.Equal(-10.0, parsed.Bands[1].GainDb, 6);
    }

    [Fact]
    public void Parse_ReadsFirstLineAsPreampAndIgnoresIndex()
    {
        string text = "1.5\n1 100 0.7 3\n2 5000 2 -4\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(1.5, curve.PreampDb, 6);
        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(100, curve.Bands[0].FrequencyHz, 6);
        Assert.Equal(5000, curve.Bands[1].FrequencyHz, 6);
    }

    [Fact]
    public void Parse_SkipsBlankCommentAndMalformedLines()
    {
        string text =
            "0\n" +
            "\n" +
            "# a comment\n" +
            "// another comment\n" +
            "garbage line\n" +
            "1 1000 1 6\n" +
            "2 nope nope nope\n" +
            "3 2000 0 5\n" +     // Q = 0 is invalid -> skipped
            "4 -50 1 5\n" +      // negative frequency -> skipped
            "5 4000 2 -3\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(0, curve.PreampDb, 6);
        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(1000, curve.Bands[0].FrequencyHz, 6);
        Assert.Equal(4000, curve.Bands[1].FrequencyHz, 6);
    }

    [Fact]
    public void Parse_AcceptsThreeTokenBandsWithoutIndex()
    {
        string text = "0\n1000 1 6\n2000 2 -3\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(1000, curve.Bands[0].FrequencyHz, 6);
        Assert.Equal(2000, curve.Bands[1].FrequencyHz, 6);
    }

    [Fact]
    public void Parse_CapsBandCountToMaximum()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("0");
        for (int i = 0; i < EqualizationCurve.MaxBandCount + 10; i++)
        {
            builder.AppendLine($"{i + 1} 1000 1 1");
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
        string text = "1.5\r\n1 1000 1.0 6.0\r\n";

        EqualizationCurve curve = PeqTextFile.Parse(text);

        Assert.Equal(1.5, curve.PreampDb, 6);
        Assert.Single(curve.Bands);
        Assert.Equal(1000, curve.Bands[0].FrequencyHz, 6);
        Assert.Equal(6.0, curve.Bands[0].GainDb, 6);
    }
}
