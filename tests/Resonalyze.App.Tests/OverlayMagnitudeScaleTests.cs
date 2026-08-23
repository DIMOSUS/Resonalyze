using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class OverlayMagnitudeScaleTests
{
    [Theory]
    [InlineData(MagnitudeScale.SoundPressureLevel, MagnitudeScale.Relative)]
    [InlineData(MagnitudeScale.Relative, MagnitudeScale.SoundPressureLevel)]
    public void Draws_RefusesACaptureFromTheOtherMagnitudeAxis(
        MagnitudeScale captured,
        MagnitudeScale shown)
    {
        Assert.False(OverlayMagnitudeScale.Draws(
            Mode.FrequencyResponse,
            OverlayKind.Captured,
            captured,
            shown));
    }

    [Theory]
    [InlineData(OverlayKind.Operation)]
    [InlineData(OverlayKind.Target)]
    public void Draws_AllowsACalculatedSlotOnEitherMagnitudeAxis(OverlayKind kind)
    {
        // A calculated slot is stored as Relative because it has no absolute scale of
        // its own — not because it belongs to the dBr axis. Reading that tag as an axis
        // is what made an operation over two SPL captures refuse its checkbox while its
        // settings dialog drew it happily.
        Assert.True(OverlayMagnitudeScale.Draws(
            Mode.FrequencyResponse,
            kind,
            MagnitudeScale.Relative,
            MagnitudeScale.SoundPressureLevel));
        Assert.True(OverlayMagnitudeScale.Draws(
            Mode.FrequencyResponse,
            kind,
            MagnitudeScale.Relative,
            MagnitudeScale.Relative));
    }

    [Theory]
    [InlineData(Mode.PhaseResponse)]
    [InlineData(Mode.GroupDelay)]
    [InlineData(Mode.LiveSpectrum)]
    [InlineData(Mode.ImpulseResponse)]
    public void Draws_IgnoresTheScaleOutsideTheMagnitudeMode(Mode mode)
    {
        // Only Frequency Response carries the dBr/SPL axis switch; elsewhere the tag a
        // capture happens to hold must not keep it off the plot.
        Assert.True(OverlayMagnitudeScale.Draws(
            mode,
            OverlayKind.Captured,
            MagnitudeScale.SoundPressureLevel,
            MagnitudeScale.Relative));
    }
}
