using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class GDOptTests
{
    [Fact]
    public void WindowModeAndCyclesRoundTripThroughThePanel()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        var options = new FrequencyResponseOptions
        {
            GroupDelayWindowMode = PhaseWindowMode.FrequencyDependent,
            GroupDelayFdwCycles = 8
        };
        using var panel = new GDOpt();
        panel.Init(measurement, options, new CurveVisibilityOptions());
        Assert.True(panel.FdwCyclesEnabled);

        var written = new FrequencyResponseOptions
        {
            GroupDelayWindowMode = PhaseWindowMode.Fixed,
            GroupDelayFdwCycles = 4
        };
        panel.SetOptions(written, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.FrequencyDependent, written.GroupDelayWindowMode);
        Assert.Equal(8, written.GroupDelayFdwCycles);
    }

    [Fact]
    public void CyclesAreOnlyLiveUnderFdw_AndInvalidStoredCyclesFallBack()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        var options = new FrequencyResponseOptions
        {
            GroupDelayWindowMode = PhaseWindowMode.Fixed,
            GroupDelayFdwCycles = 123
        };
        using var panel = new GDOpt();
        panel.Init(measurement, options, new CurveVisibilityOptions());
        Assert.False(panel.FdwCyclesEnabled);

        var written = new FrequencyResponseOptions();
        panel.SetOptions(written, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.Fixed, written.GroupDelayWindowMode);
        Assert.Equal(PhaseAnalysisSettings.DefaultFdwCycles, written.GroupDelayFdwCycles);
    }
}
