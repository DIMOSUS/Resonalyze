namespace Resonalyze.App.Tests;

public sealed class OverlayModeTests
{
    [Theory]
    [InlineData(Mode.ImpulseResponse)]
    [InlineData(Mode.FrequencyResponse)]
    [InlineData(Mode.PhaseResponse)]
    [InlineData(Mode.GroupDelay)]
    [InlineData(Mode.LiveSpectrum)]
    [InlineData(Mode.Autocorrelation)]
    public void SupportsMode_ReturnsTrueForSupportedModes(Mode mode)
    {
        Assert.True(OverlayCollection.SupportsMode(mode));
    }

    [Theory]
    [InlineData(Mode.None)]
    [InlineData(Mode.CumulativeSpectrumDecay)]
    [InlineData(Mode.BurstDecay)]
    public void SupportsMode_ReturnsFalseForUnsupportedModes(Mode mode)
    {
        Assert.False(OverlayCollection.SupportsMode(mode));
    }

    [Theory]
    [InlineData(Mode.FrequencyResponse)]
    [InlineData(Mode.PhaseResponse)]
    [InlineData(Mode.GroupDelay)]
    [InlineData(Mode.LiveSpectrum)]
    public void SmoothingSupportsMode_ReturnsTrueForFrequencyAxes(Mode mode)
    {
        Assert.True(OverlaySmoothing.SupportsMode(mode));
    }

    [Theory]
    [InlineData(Mode.ImpulseResponse)]
    [InlineData(Mode.Autocorrelation)]
    public void SmoothingSupportsMode_ReturnsFalseForTimeAxes(Mode mode)
    {
        Assert.False(OverlaySmoothing.SupportsMode(mode));
    }

    [Theory]
    [InlineData(Mode.FrequencyResponse)]
    [InlineData(Mode.LiveSpectrum)]
    public void TargetsSupportMode_ReturnsTrueForMagnitudeModes(Mode mode)
    {
        Assert.True(OverlayTargets.SupportsMode(mode));
    }

    // A target is a dB magnitude shape, so it has no meaning on the phase,
    // group delay or time axes.
    [Theory]
    [InlineData(Mode.PhaseResponse)]
    [InlineData(Mode.GroupDelay)]
    [InlineData(Mode.ImpulseResponse)]
    [InlineData(Mode.Autocorrelation)]
    [InlineData(Mode.None)]
    public void TargetsSupportMode_ReturnsFalseElsewhere(Mode mode)
    {
        Assert.False(OverlayTargets.SupportsMode(mode));
    }
}
