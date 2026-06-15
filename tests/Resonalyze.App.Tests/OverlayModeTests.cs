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
}
