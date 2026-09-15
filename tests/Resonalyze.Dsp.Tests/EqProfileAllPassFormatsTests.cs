namespace Resonalyze.Dsp.Tests;

// All-pass support by format: CamillaDSP, CSV and Audiotec carry both orders, APO/REW only second, magnitude formats none.
public sealed class EqProfileAllPassFormatsTests
{
    private static EqualizationCurve Mixed() => new(
        new[]
        {
            new PeqBand(1_000, 4.0, -6.0),
            new PeqBand(120, 1.5, 0, PeqBandType.AllPassSecondOrder),
            new PeqBand(2_000, 1.0, 0, PeqBandType.AllPassFirstOrder)
        },
        -6.5);

    [Fact]
    public void EqualizerApo_WritesTheSecondOrderAsApWithoutAGain()
    {
        string text = new EqualizerApoFormat().Export(Mixed());

        Assert.Contains("Filter 2: ON AP Fc 120 Hz Q 1.5", text);
        Assert.DoesNotContain("AP Fc 120 Hz Gain", text);
        // APO has no first-order all-pass: skipped with its number, so the gap is visible.
        Assert.DoesNotContain("Filter 3:", text);
    }

    [Fact]
    public void EqualizerApo_ReadsAnApLine()
    {
        Assert.True(new EqualizerApoFormat().TryImport(
            "Filter 1: ON AP Fc 120 Hz Q 1.5",
            out EqualizationCurve curve));

        Assert.Equal(
            new PeqBand(120, 1.5, 0, PeqBandType.AllPassSecondOrder),
            Assert.Single(curve.Bands));
    }

    [Fact]
    public void EqualizerApo_RefusesAnApLineWithoutAQ()
    {
        // An all-pass Q is the phase turn; an assumed width would place a different filter.
        new EqualizerApoFormat().TryImport(
            "Filter 1: ON AP Fc 120 Hz", out EqualizationCurve curve);

        Assert.Empty(curve.Bands);
    }

    [Theory]
    [InlineData(typeof(GenericCsvFormat))]
    [InlineData(typeof(CamillaDspYamlFormat))]
    [InlineData(typeof(AudiotecFischerFormat))]
    public void RoundTrip_KeepsBothOrdersAndTheirPlaceInTheBank(Type formatType)
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
        }
    }

    [Theory]
    [InlineData(typeof(GenericCsvFormat))]
    [InlineData(typeof(AudiotecFischerFormat))]
    public void AGainColumn_ReadsZeroForAnAllPassWhateverTheBandCarries(Type formatType)
    {
        // A slot keeps a bell's gain after switching to all-pass; writing it would state a gain the filter lacks.
        var format = (IEqProfileFormat)Activator.CreateInstance(formatType)!;
        var curve = new EqualizationCurve(
            [new PeqBand(120, 1.5, 6.0, PeqBandType.AllPassSecondOrder)]);

        Assert.True(format.TryImport(format.Export(curve), out EqualizationCurve read));

        PeqBand band = Assert.Single(read.Bands);
        Assert.Equal(PeqBandType.AllPassSecondOrder, band.Type);
        Assert.Equal(0, band.GainDb);
    }

    [Fact]
    public void CamillaDsp_WritesTheAllPassParametersItsReaderExpects()
    {
        string text = new CamillaDspYamlFormat().Export(Mixed());

        // An undefined gain key fails CamillaDSP's config validation.
        Assert.Contains("Allpass", text);
        Assert.Contains("AllpassFO", text);
        int allpassIndex = text.IndexOf("Allpass", StringComparison.Ordinal);
        int foIndex = text.IndexOf("AllpassFO", StringComparison.Ordinal);
        Assert.True(allpassIndex >= 0 && foIndex >= 0);
    }

    [Fact]
    public void MiniDsp_RealizesAnAllPassAsItsOwnCoefficients()
    {
        var band = new PeqBand(120, 1.5, 0, PeqBandType.AllPassSecondOrder);
        string text = new MiniDspFormat(48_000).Export(new EqualizationCurve(new[] { band }));
        BiquadCoefficients expected = AllPassFilter.BuildSections(
            new AllPassSpec(AllPassType.SecondOrder, 120, 1.5), 48_000)[0];

        Assert.Contains($"b0={EqTextNumbers.Format(expected.B0, "0.00000000")}", text);
        Assert.Contains($"a2={EqTextNumbers.Format(expected.A2, "0.00000000")}", text);
    }

    [Fact]
    public void TheCapabilityIsDeclaredPerOrder()
    {
        // The capability is a default interface member, which is how callers see it.
        static bool Ap1(IEqProfileFormat format) =>
            format.SupportsAllPass(PeqBandType.AllPassFirstOrder);
        static bool Ap2(IEqProfileFormat format) =>
            format.SupportsAllPass(PeqBandType.AllPassSecondOrder);

        Assert.False(Ap1(new EqualizerApoFormat()));
        Assert.True(Ap2(new EqualizerApoFormat()));
        Assert.False(Ap1(new RewFilterFormat()));
        Assert.True(Ap2(new RewFilterFormat()));

        Assert.True(Ap1(new GenericCsvFormat()) && Ap2(new GenericCsvFormat()));
        Assert.True(Ap1(new CamillaDspYamlFormat()) && Ap2(new CamillaDspYamlFormat()));
        Assert.True(Ap1(new AudiotecFischerFormat()) && Ap2(new AudiotecFischerFormat()));
        Assert.True(Ap1(new MiniDspFormat(48_000)) && Ap2(new MiniDspFormat(48_000)));

        Assert.False(Ap1(new EasyEffectsFormat()) || Ap2(new EasyEffectsFormat()));
        Assert.False(Ap1(new GraphicEqFormat()) || Ap2(new GraphicEqFormat()));
    }
}
