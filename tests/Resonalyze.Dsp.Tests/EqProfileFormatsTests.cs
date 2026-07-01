namespace Resonalyze.Dsp.Tests;

public sealed class EqProfileFormatsTests
{
    private static EqualizationCurve SampleCurve() => new(
        new[]
        {
            new PeqBand(600, 4.0, 6.0),
            new PeqBand(5582, 2.0, 4.9),
            new PeqBand(1577, 1.4, -4.1)
        },
        preampDb: -6.0);

    private static void AssertRoundTrips(IEqProfileFormat format)
    {
        EqualizationCurve original = SampleCurve();

        EqualizationCurve parsed = format.Import(format.Export(original));

        Assert.Equal(original.PreampDb, parsed.PreampDb, 4);
        Assert.Equal(original.Bands.Count, parsed.Bands.Count);
        for (int i = 0; i < original.Bands.Count; i++)
        {
            Assert.Equal(original.Bands[i].FrequencyHz, parsed.Bands[i].FrequencyHz, 4);
            Assert.Equal(original.Bands[i].Q, parsed.Bands[i].Q, 4);
            Assert.Equal(original.Bands[i].GainDb, parsed.Bands[i].GainDb, 4);
        }
    }

    [Fact]
    public void BidirectionalFormats_RoundTrip()
    {
        foreach (IEqProfileFormat format in EqProfileFormats.All.Where(f => f.CanImport && f.CanExport))
        {
            AssertRoundTrips(format);
        }
    }

    [Fact]
    public void ExportOnlyFormats_AreNotImportable()
    {
        foreach (IEqProfileFormat format in EqProfileFormats.All.Where(f => !f.CanImport))
        {
            Assert.DoesNotContain(format, EqProfileFormats.Importable);
            Assert.Throws<NotSupportedException>(() => format.Import("anything"));
        }
    }

    [Fact]
    public void EasyEffects_ExportsBellBandsAndOutputGain()
    {
        string json = new EasyEffectsFormat().Export(SampleCurve());

        Assert.Contains("\"equalizer\"", json);
        Assert.Contains("\"type\": \"Bell\"", json);
        Assert.Contains("\"output-gain\": -6", json);
    }

    [Fact]
    public void EasyEffects_SkipsNonBellBands()
    {
        string json =
            "{ \"output\": { \"equalizer\": { \"output-gain\": -3, \"left\": {" +
            " \"band0\": { \"type\": \"Bell\", \"frequency\": 1000, \"gain\": 6, \"q\": 1 }," +
            " \"band1\": { \"type\": \"Lo-shelf\", \"frequency\": 100, \"gain\": 3, \"q\": 1 }," +
            " \"band2\": { \"type\": \"Bell\", \"frequency\": 4000, \"gain\": -3, \"q\": 2 } } } } }";

        EqualizationCurve curve = new EasyEffectsFormat().Import(json);

        Assert.Equal(-3, curve.PreampDb, 4);
        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(1000, curve.Bands[0].FrequencyHz, 4);
        Assert.Equal(4000, curve.Bands[1].FrequencyHz, 4);
    }

    [Fact]
    public void EasyEffects_InvalidJson_ReturnsEmptyCurve()
    {
        EqualizationCurve curve = new EasyEffectsFormat().Import("{ not valid json");

        Assert.Empty(curve.Bands);
    }

    [Fact]
    public void CamillaDsp_ExportHasPeakingFiltersAndPipeline()
    {
        string yaml = new CamillaDspYamlFormat().Export(SampleCurve());

        Assert.Contains("filters:", yaml);
        Assert.Contains("type: Peaking", yaml);
        Assert.Contains("pipeline:", yaml);
    }

    [Fact]
    public void CamillaDsp_ImportsPeakingAndGainSkippingOthers()
    {
        string yaml =
            "filters:\n" +
            "  vol:\n" +
            "    type: Gain\n" +
            "    parameters:\n" +
            "      gain: -4.0\n" +
            "  hp:\n" +
            "    type: Biquad\n" +
            "    parameters:\n" +
            "      type: Highpass\n" +
            "      freq: 30\n" +
            "      q: 0.7\n" +
            "  peaking_000:\n" +
            "    type: Biquad\n" +
            "    parameters:\n" +
            "      type: Peaking\n" +
            "      freq: 1000\n" +
            "      q: 1.0\n" +
            "      gain: 6.0\n";

        EqualizationCurve curve = new CamillaDspYamlFormat().Import(yaml);

        Assert.Equal(-4.0, curve.PreampDb, 4);
        Assert.Single(curve.Bands);
        Assert.Equal(1000, curve.Bands[0].FrequencyHz, 4);
        Assert.Equal(6.0, curve.Bands[0].GainDb, 4);
    }

    [Fact]
    public void MiniDsp_ExportsBiquadBlocks()
    {
        string text = new MiniDspFormat().Export(SampleCurve());

        Assert.Contains("biquad1,", text);
        Assert.Contains("b0=", text);
        Assert.Contains("a2=", text);
    }

    [Fact]
    public void GraphicEq_ExportsFrequencyGainPairs()
    {
        string text = new GraphicEqFormat().Export(SampleCurve());

        Assert.StartsWith("GraphicEQ:", text);
        Assert.Contains(";", text);
    }

    [Fact]
    public void Registry_ExposesImportableAndExportableFormats()
    {
        Assert.NotEmpty(EqProfileFormats.Importable);
        Assert.NotEmpty(EqProfileFormats.Exportable);
        Assert.All(EqProfileFormats.Importable, format => Assert.True(format.CanImport));
        Assert.All(EqProfileFormats.Exportable, format => Assert.True(format.CanExport));
    }

    [Fact]
    public void Rew_ExportHasHeaderAndParsesApoFilterLines()
    {
        string text = new RewFilterFormat().Export(SampleCurve());

        Assert.Contains("Filter Settings file", text);
        Assert.Contains("Equaliser: Generic", text);
        Assert.Contains("Filter 1: ON PK Fc 600 Hz Gain 6.0 dB Q 4.0", text);
    }

    [Fact]
    public void Rew_ImportsRealRewLayout()
    {
        // REW aligns fields with extra spaces; the shared parser is whitespace-agnostic.
        string text =
            "Filter Settings file\n" +
            "\n" +
            "Room EQ V5.20\n" +
            "\n" +
            "Notes:\n" +
            "\n" +
            "Equaliser: Generic\n" +
            "Filter  1: ON  PK       Fc     600 Hz  Gain   6.00 dB  Q  4.00\n" +
            "Filter  2: ON  PK       Fc    5582 Hz  Gain   4.90 dB  Q  2.00\n";

        EqualizationCurve curve = new RewFilterFormat().Import(text);

        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(600, curve.Bands[0].FrequencyHz, 4);
        Assert.Equal(6.0, curve.Bands[0].GainDb, 4);
        Assert.Equal(5582, curve.Bands[1].FrequencyHz, 4);
    }

    [Fact]
    public void Csv_ExportHasHeaderRow()
    {
        string text = new GenericCsvFormat().Export(SampleCurve());

        Assert.Contains("Preamp (dB),-6.0", text);
        Assert.Contains("Filter,Frequency (Hz),Gain (dB),Q", text);
        Assert.Contains("1,600,6.0,4.0", text);
    }

    [Fact]
    public void Csv_ImportsWithoutIndexColumnAndSkipsHeader()
    {
        string text =
            "Preamp (dB),-2.0\n" +
            "Frequency,Gain,Q\n" +   // header row -> skipped
            "600,6,4\n" +
            "5582,4.9,2\n";

        EqualizationCurve curve = new GenericCsvFormat().Import(text);

        Assert.Equal(-2.0, curve.PreampDb, 4);
        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(600, curve.Bands[0].FrequencyHz, 4);
        Assert.Equal(5582, curve.Bands[1].FrequencyHz, 4);
    }

    [Fact]
    public void Csv_SkipsMalformedRows()
    {
        string text =
            "1,600,6,4\n" +
            "2,junk,here,now\n" +
            "3,2000,5,0\n" +          // Q = 0 -> skipped
            "4,4000,-3,2\n";

        EqualizationCurve curve = new GenericCsvFormat().Import(text);

        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(600, curve.Bands[0].FrequencyHz, 4);
        Assert.Equal(4000, curve.Bands[1].FrequencyHz, 4);
    }
}
