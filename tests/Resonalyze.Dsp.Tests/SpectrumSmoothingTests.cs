namespace Resonalyze.Dsp.Tests;

public sealed class SpectrumSmoothingTests
{
    [Fact]
    public void SmoothingOctaves_DecodesPlainWidthsPsychoAndOff()
    {
        Assert.Equal(1.0 / 12, SpectrumSmoothing.SmoothingOctaves(12), precision: 12);
        Assert.Equal(
            1.0 / SpectrumSmoothing.PsychoacousticBaseInverseOctaves,
            SpectrumSmoothing.SmoothingOctaves(SpectrumSmoothing.PsychoacousticCode),
            precision: 12);
        Assert.Equal(0.0, SpectrumSmoothing.SmoothingOctaves(0));
        // Any other negative value is not a recognized code: off, not a width —
        // a naive 1.0 / x here would produce a negative smoothing width.
        Assert.Equal(0.0, SpectrumSmoothing.SmoothingOctaves(-3));
    }

    [Fact]
    public void EquivalentInverseOctaves_MapsPsychoToItsBaseWidth()
    {
        Assert.Equal(
            SpectrumSmoothing.PsychoacousticBaseInverseOctaves,
            SpectrumSmoothing.EquivalentInverseOctaves(
                SpectrumSmoothing.PsychoacousticCode));
        Assert.Equal(12, SpectrumSmoothing.EquivalentInverseOctaves(12));
        Assert.Equal(0, SpectrumSmoothing.EquivalentInverseOctaves(0));
    }

    [Fact]
    public void IsPsychoacoustic_MatchesOnlyTheCode()
    {
        Assert.True(SpectrumSmoothing.IsPsychoacoustic(
            SpectrumSmoothing.PsychoacousticCode));
        Assert.False(SpectrumSmoothing.IsPsychoacoustic(6));
        Assert.False(SpectrumSmoothing.IsPsychoacoustic(0));
        Assert.False(SpectrumSmoothing.IsPsychoacoustic(-3));
    }

    [Theory]
    [InlineData(50.0, 1.0 / 3.0)]
    [InlineData(100.0, 1.0 / 3.0)]
    [InlineData(1_000.0, 1.0 / 6.0)]
    [InlineData(10_000.0, 1.0 / 6.0)]
    public void PsychoacousticOctaves_UsesTheExpectedEndpointWidths(
        double frequency,
        double expected)
    {
        Assert.Equal(
            expected,
            SpectrumSmoothing.PsychoacousticOctaves(frequency),
            precision: 12);
    }

    [Fact]
    public void PsychoacousticOctaves_InterpolatesOnTheLogFrequencyAxis()
    {
        Assert.Equal(
            1.0 / 4.0,
            SpectrumSmoothing.PsychoacousticOctaves(Math.Sqrt(100.0 * 1_000.0)),
            precision: 12);
    }
}
