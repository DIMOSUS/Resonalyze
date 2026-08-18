namespace Resonalyze.Dsp.Tests;

// What the shelves survive on the way out to another program and back. The
// formats that state a shelf with a centre frequency and a Q hold ours exactly;
// the ones that cannot say so are declared unsupported and drop them, which the
// EQ Wizard turns into a warning before the file is written.
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

        // LSC/HSC are the variants that carry a Q, and their Fc is the middle of the
        // transition — the same shelf this library realizes. Plain LS/HS would leave
        // the reader to assume a slope.
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
        // Plain LS/HS state no slope of their own, so they come in at the steepest
        // knee that stays monotonic rather than being dropped.
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
    // The corner-frequency family: Fc is the corner, not the middle of the
    // transition, so reading it as ours would move the shelf.
    [InlineData("Filter 1: ON LS 6dB Fc 50 Hz Gain 7.2 dB")]
    [InlineData("Filter 1: ON HS 12dB Fc 500 Hz Gain 5.0 dB")]
    // The dB-per-octave spelling of LSC: the number is a slope, not our Q.
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
        // Coefficients carry any shape by construction; the check is that the
        // exporter went through the dispatcher and not through the peaking formula.
        var shelf = new PeqBand(120, 0.7, 6, PeqBandType.LowShelf);
        string text = new MiniDspFormat(48_000).Export(new EqualizationCurve(new[] { shelf }));
        BiquadCoefficients expected = ShelvingBiquad.Compute(shelf, 48_000);

        Assert.Contains($"b0={EqTextNumbers.Format(expected.B0, "0.00000000")}", text);
        Assert.Contains($"a2={EqTextNumbers.Format(expected.A2, "0.00000000")}", text);
    }

    [Fact]
    public void EasyEffects_DeclaresItCannotCarryAShelf()
    {
        // Its shelving bands are LSP filters whose steepness comes from a mode and a
        // slope multiplier, not from an RBJ shelf Q, so ours cannot be written into
        // one. Declaring that is what makes the EQ Wizard warn instead of lie.
        // Read through the interface: the capability is a default interface member,
        // which is exactly how every caller sees it.
        Assert.False(((IEqProfileFormat)new EasyEffectsFormat()).SupportsShelvingFilters);
        Assert.True(((IEqProfileFormat)new EqualizerApoFormat()).SupportsShelvingFilters);
        Assert.True(((IEqProfileFormat)new MiniDspFormat(48_000)).SupportsShelvingFilters);
    }
}
