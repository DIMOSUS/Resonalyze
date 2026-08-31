namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The catalog is one line per device, and its ids are DERIVED from the names, so
/// these guard the two things that can go wrong when a line is added: a colliding id
/// (one device becomes unreachable) and a device stated for the wrong properties.
/// </summary>
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
    // The rate is the one thing here that changes what the tool computes.
    [InlineData("helix-dsp-ultra-s", 96_000, PeqQConvention.Rbj)]
    [InlineData("helix-next-v-eight-dsp-ultimate", 48_000, PeqQConvention.Rbj)]
    [InlineData("helix-p-six-dsp-ultimate", 96_000, PeqQConvention.Rbj)]
    // Cirrus Logic CS47048C: the one Symmetric-Q device in the car-audio catalog.
    [InlineData("amp-panacea-v1-v2", 96_000, PeqQConvention.Symmetric)]
    // Classic Q is a property of the MODEL: JL's own VXi does not read Q this way.
    [InlineData("jl-audio-twk-88", 48_000, PeqQConvention.Classic)]
    // The two 192 kHz processors, which a 48 or 96 kHz measurement can still tune.
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
        // A project file holds the id AND the numbers it was saved with; correcting a
        // device in a later build has to correct every project naming it, rather than
        // leaving the stale numbers standing.
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
        // A file from a newer catalog, and a hand-configured processor: both are
        // Custom here, and neither loses the simulation it described.
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
        // The catalog learns each device's per-channel delay ceiling from its
        // manual, gradually. Until a line states one, the profile answers with
        // the engine's long-standing 50 ms feasibility gate — the exact behavior
        // every device had before the catalog knew this fact — and so does a
        // Custom profile, which has nobody's manual to read.
        DspProcessorProfile named = DspProcessorCatalog.Presets[0].ToProfile();
        DspProcessorProfile custom =
            DspProcessorProfile.Custom(96_000, PeqQConvention.Rbj);

        Assert.Equal(AutoAlignmentEngine.DefaultMaxDelayMs, custom.MaxDelayMs);
        // Presets[0] is AMP Panacea, whose manual figure has not been entered
        // yet; if a number lands in its line this assertion is WRONG to keep —
        // replace it with the manual's figure.
        Assert.Equal(AutoAlignmentEngine.DefaultMaxDelayMs, named.MaxDelayMs);
    }

    [Fact]
    public void EveryStatedDelayCeilingIsAPositiveDialableNumber()
    {
        // A zero or negative ceiling would make every proposal infeasible, and
        // one past 100 ms would let the engine propose a delay the manual
        // fields (whose range is 100 ms) cannot hold — a data typo in one
        // catalog line must fail here, not in a user's refusal or a silently
        // clamped Apply.
        Assert.All(
            DspProcessorCatalog.Presets,
            preset => Assert.True(
                preset.MaxDelayMs is null or (> 0 and <= 100),
                $"{preset.Id} states a delay ceiling outside (0, 100] ms"));
    }
}
