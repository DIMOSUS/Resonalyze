namespace Resonalyze.Dsp.Tests;

// What the all-pass bands survive on the way out to another program and back.
// Support splits by order: CamillaDSP, the generic CSV and the Audiotec bank have
// both orders, Equalizer APO / REW only the second (APO's AP), and the magnitude
// formats none — declared, so the EQ Wizard warns instead of writing a 0 dB bell.
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

        // "AP" is APO's own spelling — second order, Fc and Q, no gain token.
        Assert.Contains("Filter 2: ON AP Fc 120 Hz Q 1.5", text);
        Assert.DoesNotContain("AP Fc 120 Hz Gain", text);
        // APO has no first-order all-pass; the band is skipped, its number with it,
        // so the gap is visible instead of a wrong filter being written.
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
        // An all-pass's Q is the phase turn itself — reading one at an assumed
        // width would place a different filter.
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
        // A band can arrive holding a gain it does not use: the wizard's slot keeps
        // the figure a bell had when it was switched to an all-pass, so switching
        // back restores it. Writing that into a file states a gain the filter does
        // not have, to a reader with no reason to doubt it.
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

        // Allpass takes freq + q and no gain; AllpassFO takes freq alone. A gain
        // key CamillaDSP does not define would fail its config validation.
        Assert.Contains("Allpass", text);
        Assert.Contains("AllpassFO", text);
        int allpassIndex = text.IndexOf("Allpass", StringComparison.Ordinal);
        int foIndex = text.IndexOf("AllpassFO", StringComparison.Ordinal);
        Assert.True(allpassIndex >= 0 && foIndex >= 0);
    }

    [Fact]
    public void MiniDsp_RealizesAnAllPassAsItsOwnCoefficients()
    {
        // Coefficients carry any shape by construction; the check is that the
        // exporter went through the dispatcher and not through the peaking formula.
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
        // Read through the interface: the capability is a default interface member,
        // which is exactly how every caller sees it.
        static bool Ap1(IEqProfileFormat format) =>
            format.SupportsAllPass(PeqBandType.AllPassFirstOrder);
        static bool Ap2(IEqProfileFormat format) =>
            format.SupportsAllPass(PeqBandType.AllPassSecondOrder);

        // APO's AP is second-order only, and REW shares its filter lines.
        Assert.False(Ap1(new EqualizerApoFormat()));
        Assert.True(Ap2(new EqualizerApoFormat()));
        Assert.False(Ap1(new RewFilterFormat()));
        Assert.True(Ap2(new RewFilterFormat()));

        // Both orders: our CSV, CamillaDSP, the Audiotec bank, raw coefficients.
        Assert.True(Ap1(new GenericCsvFormat()) && Ap2(new GenericCsvFormat()));
        Assert.True(Ap1(new CamillaDspYamlFormat()) && Ap2(new CamillaDspYamlFormat()));
        Assert.True(Ap1(new AudiotecFischerFormat()) && Ap2(new AudiotecFischerFormat()));
        Assert.True(Ap1(new MiniDspFormat(48_000)) && Ap2(new MiniDspFormat(48_000)));

        // Neither: a mode/slope parameterisation, and a sampled magnitude curve.
        Assert.False(Ap1(new EasyEffectsFormat()) || Ap2(new EasyEffectsFormat()));
        Assert.False(Ap1(new GraphicEqFormat()) || Ap2(new GraphicEqFormat()));
    }
}
