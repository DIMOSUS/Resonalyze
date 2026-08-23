using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class OverlayMagnitudeScaleTests
{
    [Theory]
    [InlineData(MagnitudeScale.SoundPressureLevel, MagnitudeScale.Relative)]
    [InlineData(MagnitudeScale.Relative, MagnitudeScale.SoundPressureLevel)]
    public void Draws_RefusesACurveStatingTheOtherMagnitudeAxis(
        MagnitudeScale slotScale,
        MagnitudeScale shown)
    {
        Assert.False(OverlayMagnitudeScale.Draws(Mode.FrequencyResponse, slotScale, shown));
    }

    [Theory]
    [InlineData(MagnitudeScale.Relative)]
    [InlineData(MagnitudeScale.SoundPressureLevel)]
    public void Draws_AllowsACurveThatStatesNoAbsoluteLevelOnEitherAxis(MagnitudeScale shown)
    {
        // A difference, a target shape, a complex sum: the slot's offset places it, so
        // neither axis excludes it. Reading "no scale" as "the relative axis" is what
        // made an operation over two dB SPL captures refuse its checkbox while its
        // settings dialog drew it happily.
        Assert.True(OverlayMagnitudeScale.Draws(Mode.FrequencyResponse, null, shown));
    }

    [Theory]
    [InlineData(Mode.PhaseResponse)]
    [InlineData(Mode.GroupDelay)]
    [InlineData(Mode.LiveSpectrum)]
    [InlineData(Mode.ImpulseResponse)]
    public void Draws_IgnoresTheScaleOutsideTheMagnitudeMode(Mode mode)
    {
        // Only Frequency Response carries the relative/SPL axis switch; elsewhere the
        // scale a capture happens to state must not keep it off the plot.
        Assert.True(OverlayMagnitudeScale.Draws(
            mode,
            MagnitudeScale.SoundPressureLevel,
            MagnitudeScale.Relative));
    }

    [Theory]
    [InlineData(OverlayOperation.AMinusB)]
    [InlineData(OverlayOperation.BMinusA)]
    [InlineData(OverlayOperation.AbsoluteDifference)]
    [InlineData(OverlayOperation.ComplexSumLoss)]
    [InlineData(OverlayOperation.ComplexSum)]
    public void ForOperation_CancelsTheLevelOfADifferenceOrARatio(OverlayOperation operation)
    {
        // The field case this was built for: the difference of two dB SPL captures is a
        // handful of dB that an offset lifts onto the SPL axis.
        Assert.Null(OverlayMagnitudeScale.ForOperation(
            operation,
            MagnitudeScale.SoundPressureLevel,
            MagnitudeScale.SoundPressureLevel));
    }

    [Theory]
    [InlineData(OverlayOperation.CurveA)]
    [InlineData(OverlayOperation.Sum)]
    [InlineData(OverlayOperation.Average)]
    [InlineData(OverlayOperation.Blend)]
    public void ForOperation_InheritsTheLevelItReproduces(OverlayOperation operation)
    {
        // These operations hand the operand's own decibels through, and the points are
        // the stored ones — never recomputed for the axis on screen — so an SPL operand
        // makes the result an SPL curve, pinned like the capture it came from.
        Assert.Equal(
            MagnitudeScale.SoundPressureLevel,
            OverlayMagnitudeScale.ForOperation(
                operation,
                MagnitudeScale.SoundPressureLevel,
                null));
        Assert.False(OverlayMagnitudeScale.Draws(
            Mode.FrequencyResponse,
            OverlayMagnitudeScale.ForOperation(
                operation,
                MagnitudeScale.SoundPressureLevel,
                null),
            MagnitudeScale.Relative));
    }

    [Fact]
    public void ForOperation_TakesTheScaleOfWhicheverOperandStatesOne()
    {
        // A live operand states nothing (it is drawn on the axis showing right now), so a
        // captured one decides — from either side.
        Assert.Equal(
            MagnitudeScale.SoundPressureLevel,
            OverlayMagnitudeScale.ForOperation(
                OverlayOperation.Sum,
                null,
                MagnitudeScale.SoundPressureLevel));
        Assert.Null(OverlayMagnitudeScale.ForOperation(OverlayOperation.Sum, null, null));
    }
}
