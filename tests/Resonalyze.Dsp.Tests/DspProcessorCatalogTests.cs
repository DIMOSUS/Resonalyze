namespace Resonalyze.Dsp.Tests;

/// <summary>Catalog ids are derived from names: guards colliding ids and misstated device properties.</summary>
public sealed class DspProcessorCatalogTests
{
    [Fact]
    public void EveryDeviceHasItsOwnId()
    {
        string[] ids = DspProcessorCatalog.Presets.Select(preset => preset.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Theory]
    [InlineData("helix-dsp-ultra-s", 96_000, PeqQConvention.Rbj)]
    [InlineData("helix-next-v-eight-dsp-ultimate", 48_000, PeqQConvention.Rbj)]
    [InlineData("helix-p-six-dsp-ultimate", 96_000, PeqQConvention.Rbj)]
    // Cirrus Logic CS47048C: the one Symmetric-Q device in the car-audio catalog.
    [InlineData("amp-panacea-v1-v2", 96_000, PeqQConvention.Symmetric)]
    // Classic Q is a property of the MODEL: JL's own VXi does not read Q this way.
    [InlineData("jl-audio-twk-88", 48_000, PeqQConvention.Classic)]
    [InlineData("mosconi-dsp-8to12-aerospace", 192_000, PeqQConvention.Rbj)]
    [InlineData("minidsp-c-dsp-8x12", 192_000, PeqQConvention.Rbj)]
    public void ADeviceStatesItsProperties(
        string id,
        int sampleRateHz,
        PeqQConvention convention)
    {
        DspProcessorPreset preset = Assert.IsType<DspProcessorPreset>(
            DspProcessorCatalog.Preset(id));

        Assert.Equal(sampleRateHz, preset.SampleRateHz);
        Assert.Equal(convention, preset.QConvention);
    }

    [Fact]
    public void ANamedModelAlwaysAnswersFromTheCatalog()
    {
        // Correcting a device in a later build must correct every project naming it.
        DspProcessorPreset preset = DspProcessorCatalog.Presets[0];
        var stale = new DspProcessorProfile(preset.Id, 44_100, PeqQConvention.Classic);

        DspProcessorProfile resolved = DspProcessorCatalog.Resolve(stale);

        Assert.Equal(preset.SampleRateHz, resolved.SampleRateHz);
        Assert.Equal(preset.QConvention, resolved.QConvention);
        Assert.False(resolved.IsCustom);
    }

    [Fact]
    public void AnUnknownOrAbsentModelKeepsItsOwnNumbers()
    {
        var unknown = new DspProcessorProfile("brand-new-processor", 96_000, PeqQConvention.Rbj);
        DspProcessorProfile custom = DspProcessorProfile.Custom(192_000, PeqQConvention.Symmetric);

        Assert.True(unknown.IsCustom);
        Assert.True(custom.IsCustom);
        Assert.Equal(96_000, DspProcessorCatalog.Resolve(unknown).SampleRateHz);
        Assert.Equal(192_000, DspProcessorCatalog.Resolve(custom).SampleRateHz);
        Assert.Equal("Custom", custom.DisplayName);
    }

    [Fact]
    public void AnUnknownDelayCeilingReadsAsTheEnginesDefault()
    {
        // Until a catalog line states a delay ceiling, the profile answers the engine's 50 ms default.
        DspProcessorProfile named = DspProcessorCatalog.Presets[0].ToProfile();
        DspProcessorProfile custom =
            DspProcessorProfile.Custom(96_000, PeqQConvention.Rbj);

        Assert.Equal(AutoAlignmentEngine.DefaultMaxDelayMs, custom.MaxDelayMs);
        // Presets[0] (AMP Panacea) has no manual figure yet; replace this assertion when one is entered.
        Assert.Equal(AutoAlignmentEngine.DefaultMaxDelayMs, named.MaxDelayMs);
    }

    [Fact]
    public void EveryStatedDelayCeilingIsAPositiveDialableNumber()
    {
        // Ceilings must be in (0, 100] ms (the manual fields' range): a catalog typo fails here.
        Assert.All(
            DspProcessorCatalog.Presets,
            preset => Assert.True(
                preset.MaxDelayMs is null or (> 0 and <= 100),
                $"{preset.Id} states a delay ceiling outside (0, 100] ms"));
    }
}
