using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class FROptionsTests
{
    [Fact]
    public void MagnitudeWindowModeRoundTripsThroughThePanel()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        var options = new FrequencyResponseOptions
        {
            MagnitudeWindowMode = PhaseWindowMode.FrequencyDependent,
            MagnitudeFdwCycles = 8
        };
        using var panel = new FROptions();
        panel.Init(
            measurement,
            options,
            new CurveVisibilityOptions(),
            []);

        var written = new FrequencyResponseOptions();
        panel.SetOptions(written, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.FrequencyDependent, written.MagnitudeWindowMode);
        Assert.Equal(8, written.MagnitudeFdwCycles);
    }

    [Fact]
    public void InvalidStoredCyclesFallBackToTheDefaultChoice()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        var options = new FrequencyResponseOptions
        {
            MagnitudeWindowMode = PhaseWindowMode.Fixed,
            MagnitudeFdwCycles = 123
        };
        using var panel = new FROptions();
        panel.Init(
            measurement,
            options,
            new CurveVisibilityOptions(),
            []);

        var written = new FrequencyResponseOptions();
        panel.SetOptions(written, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.Fixed, written.MagnitudeWindowMode);
        Assert.Equal(
            PhaseAnalysisSettings.DefaultFdwCycles, written.MagnitudeFdwCycles);
    }
}
