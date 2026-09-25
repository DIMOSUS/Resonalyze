using System.Globalization;

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

        // A layout with no preamp place (a car DSP bank) reads it back as 0 by declaration.
        Assert.Equal(format.CarriesPreamp ? original.PreampDb : 0, parsed.PreampDb, 4);
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

        Assert.Contains("\"type\": \"Bell\"", json);
        Assert.Contains("\"output-gain\": -6", json);
    }

    [Fact]
    public void EasyEffects_ExportNamesTheInstanceAndListsItInThePluginOrder()
    {
        // EasyEffects 7 refuses a preset whose pipeline has no "plugins_order", and loads only the instances it lists.
        using var document = System.Text.Json.JsonDocument.Parse(new EasyEffectsFormat().Export(SampleCurve()));
        System.Text.Json.JsonElement output = document.RootElement.GetProperty("output");

        Assert.Equal("equalizer#0", Assert.Single(output.GetProperty("plugins_order").EnumerateArray()).GetString());
        Assert.Equal(3, output.GetProperty("equalizer#0").GetProperty("num-bands").GetInt32());
    }

    [Fact]
    public void EasyEffects_ReadsAnEasyEffects7PresetWithANumberedInstance()
    {
        string json =
            "{ \"output\": { \"blocklist\": [], \"plugins_order\": [ \"limiter#0\", \"equalizer#1\" ]," +
            " \"limiter#0\": { \"bypass\": false }," +
            " \"equalizer#1\": { \"input-gain\": -1.5, \"output-gain\": -2, \"left\": {" +
            " \"band0\": { \"type\": \"Bell\", \"mute\": false, \"frequency\": 1000, \"gain\": 6, \"q\": 1 }," +
            " \"band1\": { \"type\": \"Bell\", \"mute\": true, \"frequency\": 2000, \"gain\": 6, \"q\": 1 }," +
            " \"band2\": { \"type\": \"Bell\", \"frequency\": 4000, \"gain\": -3, \"q\": 2 } } } } }";

        EqualizationCurve curve = Import(new EasyEffectsFormat(), json);

        // Input and output gain are both flat stages around the bands; a muted band does not sound.
        Assert.Equal(-3.5, curve.PreampDb, 4);
        Assert.Equal(new[] { 1000.0, 4000.0 }, curve.Bands.Select(b => b.FrequencyHz));
    }

    [Fact]
    public void EasyEffects_SkipsNonBellBands()
    {
        string json =
            "{ \"output\": { \"equalizer\": { \"output-gain\": -3, \"left\": {" +
            " \"band0\": { \"type\": \"Bell\", \"frequency\": 1000, \"gain\": 6, \"q\": 1 }," +
            " \"band1\": { \"type\": \"Lo-shelf\", \"frequency\": 100, \"gain\": 3, \"q\": 1 }," +
            " \"band2\": { \"type\": \"Bell\", \"frequency\": 4000, \"gain\": -3, \"q\": 2 } } } } }";

        EqualizationCurve curve = Import(new EasyEffectsFormat(), json);

        Assert.Equal(-3, curve.PreampDb, 4);
        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(1000, curve.Bands[0].FrequencyHz, 4);
        Assert.Equal(4000, curve.Bands[1].FrequencyHz, 4);
    }

    [Fact]
    public void EasyEffects_InvalidJson_IsNotRecognised()
    {
        Assert.False(new EasyEffectsFormat().TryImport("{ not valid json", out EqualizationCurve curve));
        Assert.Empty(curve.Bands);
    }

    [Fact]
    public void EasyEffects_ReadsAnEqualizerAtTheRootWithoutTheOutputWrapper()
    {
        // Older presets: equalizer at the JSON root and bands without a 'left' host.
        string json =
            "{ \"num-bands\": 1, \"output-gain\": -2," +
            " \"band0\": { \"type\": \"Bell\", \"frequency\": 1000, \"gain\": 6, \"q\": 1 } }";

        EqualizationCurve curve = Import(new EasyEffectsFormat(), json);

        Assert.Equal(-2, curve.PreampDb, 4);
        Assert.Single(curve.Bands);
        Assert.Equal(1000, curve.Bands[0].FrequencyHz, 4);
    }

    [Fact]
    public void EasyEffects_ValidJsonThatIsNotAPreset_IsNotRecognised()
    {
        Assert.False(new EasyEffectsFormat().TryImport(
            "{ \"something\": 1, \"else\": true }", out EqualizationCurve curve));
        Assert.Empty(curve.Bands);
    }

    [Fact]
    public void EasyEffects_DropsDegenerateBellBands()
    {
        string json =
            "{ \"output\": { \"equalizer\": { \"output-gain\": 0, \"left\": {" +
            " \"band0\": { \"type\": \"Bell\", \"frequency\": 1000, \"gain\": 6, \"q\": 0 }," +
            " \"band1\": { \"type\": \"Bell\", \"frequency\": -100, \"gain\": 3, \"q\": 1 }," +
            " \"band2\": { \"type\": \"Bell\", \"frequency\": 4000, \"gain\": -3, \"q\": 2 } } } } }";

        EqualizationCurve curve = Import(new EasyEffectsFormat(), json);

        Assert.Single(curve.Bands);
        Assert.Equal(4000, curve.Bands[0].FrequencyHz, 4);
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
    public void CamillaDsp_PipelineStepListsItsChannelsAsCamillaDsp3Requires()
    {
        // CamillaDSP 3 rejects a step's v2 "channel" field as unknown; it takes "channels: [..]".
        var root = (IDictionary<object, object>)new YamlDotNet.Serialization.DeserializerBuilder().Build()
            .Deserialize<object>(new CamillaDspYamlFormat().Export(SampleCurve()))!;

        var step = (IDictionary<object, object>)Assert.Single((IList<object>)root["pipeline"]);
        Assert.False(step.ContainsKey("channel"));
        Assert.Equal(new object[] { "0", "1" }, (IList<object>)step["channels"]);
        Assert.Equal(4, ((IList<object>)step["names"]).Count);
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

        EqualizationCurve curve = Import(new CamillaDspYamlFormat(), yaml);

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
    public void MiniDsp_ExportsThePreampGainAndBandCoefficients()
    {
        // Export-only formats have no round trip: pin the coefficients (gain biquad for the preamp plus one per band).
        EqualizationCurve curve = SampleCurve();
        string text = new MiniDspFormat().Export(curve);

        double[] b0 = Coefficients(text, "b0=");
        double[] a2 = Coefficients(text, "a2=");

        Assert.Equal(1 + curve.Bands.Count, b0.Length); // preamp + bands
        Assert.Equal(Math.Pow(10.0, curve.PreampDb / 20.0), b0[0], 6); // 10^(-6/20)

        BiquadCoefficients firstBand = PeakingBiquad.Compute(curve.Bands[0], 48_000);
        Assert.Equal(firstBand.B0, b0[1], 6);
        Assert.Equal(firstBand.A2, a2[1], 6);
    }

    [Fact]
    public void MiniDsp_IsExportableAtEveryMiniDspProcessorsRate()
    {
        foreach (DspProcessorPreset preset in DspProcessorCatalog.Presets
            .Where(preset => preset.Manufacturer == "miniDSP"))
        {
            string name = new MiniDspFormat(preset.SampleRateHz).Name;
            Assert.Contains(EqProfileFormats.Exportable, format => format.Name == name);
        }
    }

    private static double[] Coefficients(string text, string prefix) => text
        .Split('\n')
        .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
        .Select(line => double.Parse(
            line[prefix.Length..].TrimEnd(',', '\r'), CultureInfo.InvariantCulture))
        .ToArray();

    [Fact]
    public void GraphicEq_ExportsFrequencyGainPairs()
    {
        string text = new GraphicEqFormat().Export(SampleCurve());

        Assert.StartsWith("GraphicEQ:", text);
        Assert.Contains(";", text);
    }

    [Fact]
    public void GraphicEq_ExportsAscendingFrequenciesWithMatchingGains()
    {
        EqualizationCurve curve = SampleCurve();
        string text = new GraphicEqFormat().Export(curve);

        string[] pairs = text["GraphicEQ: ".Length..]
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        double[] freqs = pairs
            .Select(p => double.Parse(p.Split(' ')[0], CultureInfo.InvariantCulture))
            .ToArray();
        double[] gains = pairs
            .Select(p => double.Parse(p.Split(' ')[1], CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(64, pairs.Length);
        Assert.Equal(20.0, freqs[0]);
        Assert.Equal(20_000.0, freqs[^1]);
        // Only the frequency column is ascending, pinning column order.
        for (int i = 1; i < freqs.Length; i++)
        {
            Assert.True(freqs[i] > freqs[i - 1], "Frequencies must ascend.");
        }
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 64);
        for (int i = 0; i < grid.Count; i++)
        {
            Assert.Equal(
                Math.Round(DigitalEqualizationResponse.MagnitudeDbAt(
                    curve, grid[i], 48_000), 1),
                gains[i],
                3);
        }
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
        // REW aligns fields with extra spaces.
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

        EqualizationCurve curve = Import(new RewFilterFormat(), text);

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

        EqualizationCurve curve = Import(new GenericCsvFormat(), text);

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

        EqualizationCurve curve = Import(new GenericCsvFormat(), text);

        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(600, curve.Bands[0].FrequencyHz, 4);
        Assert.Equal(4000, curve.Bands[1].FrequencyHz, 4);
    }

    // Import is a default interface member, reachable only through the interface.
    private static EqualizationCurve Import(IEqProfileFormat format, string text) =>
        format.Import(text);
}
