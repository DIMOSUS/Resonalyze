namespace Resonalyze.Dsp.Tests;

// Formats that state a shelf with Fc and Q hold ours exactly; others are declared unsupported (the EQ Wizard warns).
public sealed class EqProfileShelfFormatsTests
{
    private static EqualizationCurve Mixed() => new(
        new[]
        {
            new PeqBand(80, 0.7, 4.5, PeqBandType.LowShelf),
            new PeqBand(1_000, 4.0, -6.0),
            new PeqBand(6_300, 1.1, -3.5, PeqBandType.HighShelf)
        },
        -6.5);

    [Fact]
    public void EqualizerApo_WritesTheShelvesAsLscAndHsc()
    {
        string text = new EqualizerApoFormat().Export(Mixed());

        // LSC/HSC carry a Q and Fc at the transition's middle, like ours.
        Assert.Contains("ON LSC Fc 80 Hz Gain 4.5 dB Q 0.7", text);
        Assert.Contains("ON PK Fc 1000 Hz Gain -6.0 dB Q 4.0", text);
        Assert.Contains("ON HSC Fc 6300 Hz Gain -3.5 dB Q 1.1", text);
    }

    [Theory]
    [InlineData(typeof(EqualizerApoFormat))]
    [InlineData(typeof(RewFilterFormat))]
    [InlineData(typeof(GenericCsvFormat))]
    [InlineData(typeof(CamillaDspYamlFormat))]
    [InlineData(typeof(AudiotecFischerFormat))]
    public void RoundTrip_KeepsEachBandsShapeAndOrder(Type formatType)
    {
        var format = (IEqProfileFormat)Activator.CreateInstance(formatType)!;
        EqualizationCurve original = Mixed();

        Assert.True(format.TryImport(format.Export(original), out EqualizationCurve read));

        Assert.Equal(original.Bands.Count, read.Bands.Count);
        for (int index = 0; index < original.Bands.Count; index++)
        {
            PeqBand expected = original.Bands[index];
            PeqBand actual = read.Bands[index];
            Assert.Equal(expected.Type, actual.Type);
            Assert.Equal(expected.FrequencyHz, actual.FrequencyHz, 3);
            Assert.Equal(expected.Q, actual.Q, 3);
            Assert.Equal(expected.GainDb, actual.GainDb, 3);
        }
    }

    [Fact]
    public void EqualizerApo_ReadsAShelfWrittenWithoutAQ()
    {
        // Plain LS/HS arrive at the steepest monotonic knee.
        Assert.True(new EqualizerApoFormat().TryImport(
            "Filter 1: ON LS Fc 100 Hz Gain 5.0 dB",
            out EqualizationCurve curve));

        PeqBand band = Assert.Single(curve.Bands);
        Assert.Equal(PeqBandType.LowShelf, band.Type);
        Assert.Equal(100, band.FrequencyHz);
        Assert.Equal(5.0, band.GainDb);
        Assert.Equal(0.7071, band.Q, 3);
    }

    [Theory]
    // Corner-frequency family: reading Fc as ours would move the shelf.
    [InlineData("Filter 1: ON LS 6dB Fc 50 Hz Gain 7.2 dB")]
    [InlineData("Filter 1: ON HS 12dB Fc 500 Hz Gain 5.0 dB")]
    // dB-per-octave LSC: the number is a slope, not a Q.
    [InlineData("Filter 1: ON LSC 10.8 dB Fc 300 Hz Gain 5.0 dB")]
    public void EqualizerApo_SkipsShelvesStatedInAnotherParameterisation(string line)
    {
        new EqualizerApoFormat().TryImport(line, out EqualizationCurve curve);

        Assert.Empty(curve.Bands);
    }

    [Fact]
    public void GenericCsv_ReadsAFileWrittenBeforeTheTypeColumnExisted()
    {
        string legacy = string.Join(
            Environment.NewLine,
            "Preamp (dB),-3.0",
            "Filter,Frequency (Hz),Gain (dB),Q",
            "1,600,6.0,4.0");

        Assert.True(new GenericCsvFormat().TryImport(legacy, out EqualizationCurve curve));

        PeqBand band = Assert.Single(curve.Bands);
        Assert.Equal(PeqBandType.Peaking, band.Type);
        Assert.Equal(600, band.FrequencyHz);
    }

    [Fact]
    public void MiniDsp_RealizesAShelfAsItsOwnCoefficients()
    {
        var shelf = new PeqBand(120, 0.7, 6, PeqBandType.LowShelf);
        string text = new MiniDspFormat(48_000).Export(new EqualizationCurve(new[] { shelf }));
        BiquadCoefficients expected = ShelvingBiquad.Compute(shelf, 48_000);

        Assert.Contains($"b0={EqTextNumbers.Format(expected.B0, "0.00000000")}", text);
        Assert.Contains($"a2={EqTextNumbers.Format(expected.A2, "0.00000000")}", text);
    }

    [Fact]
    public void EasyEffects_DeclaresItCannotCarryAShelf()
    {
        // LSP shelves take a mode and slope multiplier, not an RBJ Q.
        Assert.False(((IEqProfileFormat)new EasyEffectsFormat()).SupportsShelvingFilters);
        Assert.True(((IEqProfileFormat)new EqualizerApoFormat()).SupportsShelvingFilters);
        Assert.True(((IEqProfileFormat)new MiniDspFormat(48_000)).SupportsShelvingFilters);
    }
}
