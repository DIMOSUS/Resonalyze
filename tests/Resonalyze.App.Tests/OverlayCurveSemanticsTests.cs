using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class OverlayCurveSemanticsTests
{
    private static readonly OverlayCurveSemantics SplDecibels =
        new(MagnitudeScale.SoundPressureLevel, "decibel");
    private static readonly OverlayCurveSemantics RelativeDecibels =
        new(MagnitudeScale.Relative, "decibel");
    private static readonly OverlayCurveSemantics Coherence =
        new(null, PlotModelFactory.CoherenceAxisKey);
    private static readonly OverlayCurveSemantics LiveCurve = OverlayCurveSemantics.None;

    [Fact]
    public void CurveA_OverAnSplCapture_StaysOffTheRelativeAxis()
    {
        // "A only" hands the STORED points through: an ~80 dB SPL reading would be
        // drawn as ~80 dB of relative gain if the axis let it in.
        OverlayCurveSemantics result = OverlayCurveSemantics.ForOperation(
            OverlayOperation.CurveA,
            SplDecibels,
            OverlayCurveSemantics.None);

        Assert.Equal(MagnitudeScale.SoundPressureLevel, result.Scale);
        Assert.False(result.DrawsOn(Mode.FrequencyResponse, MagnitudeScale.Relative));
        Assert.True(result.DrawsOn(
            Mode.FrequencyResponse,
            MagnitudeScale.SoundPressureLevel));
    }

    [Theory]
    [InlineData(OverlayOperation.CurveA)]
    [InlineData(OverlayOperation.AMinusB)]
    public void CoherenceOperands_KeepTheCoherenceAxis(OverlayOperation operation)
    {
        // 0…1 on the decibel axis is the visible half of the same defect: the operation
        // used to drop the operand's axis key and land on the mode's main axis.
        OverlayCurveSemantics result = OverlayCurveSemantics.ForOperation(
            operation,
            Coherence,
            operation == OverlayOperation.CurveA ? OverlayCurveSemantics.None : Coherence);

        Assert.Equal(PlotModelFactory.CoherenceAxisKey, result.YAxisKey);
    }

    [Fact]
    public void CurveA_OverALiveCoherenceCurve_KeepsThatAxisAndStatesNoScale()
    {
        // A live operand is re-read from the plot on every rebuild, so it is always on
        // the axis showing — but it still carries WHICH axis that is.
        OverlayCurveSemantics result = OverlayCurveSemantics.ForOperation(
            OverlayOperation.CurveA,
            Coherence,
            OverlayCurveSemantics.None);

        Assert.Null(result.Scale);
        Assert.Equal(PlotModelFactory.CoherenceAxisKey, result.YAxisKey);
        Assert.True(result.DrawsOn(
            Mode.FrequencyResponse,
            MagnitudeScale.SoundPressureLevel));
    }

    [Fact]
    public void OperandsOnDifferentAxes_FallBackToTheMainAxis()
    {
        // Coherence minus decibels is neither; there is no axis to inherit.
        Assert.Null(OverlayCurveSemantics
            .ForOperation(OverlayOperation.AMinusB, Coherence, RelativeDecibels)
            .YAxisKey);
    }

    [Theory]
    [InlineData(OverlayOperation.AMinusB)]
    [InlineData(OverlayOperation.BMinusA)]
    [InlineData(OverlayOperation.AbsoluteDifference)]
    public void ADifference_CancelsTheAbsoluteLevel(OverlayOperation operation)
    {
        // The field case this started from: the difference of two dB SPL captures is a
        // handful of dB that the slot's offset lifts onto the SPL axis.
        OverlayCurveSemantics result = OverlayCurveSemantics.ForOperation(
            operation,
            SplDecibels,
            SplDecibels);

        Assert.Null(result.Scale);
        Assert.True(result.DrawsOn(Mode.FrequencyResponse, MagnitudeScale.Relative));
        Assert.True(result.DrawsOn(
            Mode.FrequencyResponse,
            MagnitudeScale.SoundPressureLevel));
    }

    [Theory]
    [InlineData(OverlayOperation.Sum)]
    [InlineData(OverlayOperation.Average)]
    [InlineData(OverlayOperation.Blend)]
    public void ALevelPreservingOperation_InheritsWhicheverOperandStatesAScale(
        OverlayOperation operation)
    {
        Assert.Equal(
            MagnitudeScale.SoundPressureLevel,
            OverlayCurveSemantics.ForOperation(operation, SplDecibels, LiveCurve).Scale);
        Assert.Equal(
            MagnitudeScale.SoundPressureLevel,
            OverlayCurveSemantics.ForOperation(operation, LiveCurve, SplDecibels).Scale);
        // Two operands stating different scales leave no axis on which both halves are
        // true, so the result claims neither.
        Assert.Null(OverlayCurveSemantics
            .ForOperation(operation, SplDecibels, RelativeDecibels)
            .Scale);
    }

    [Theory]
    [InlineData(OverlayOperation.ComplexSum)]
    [InlineData(OverlayOperation.ComplexSumLoss)]
    public void TheComplexSum_StatesNothing(OverlayOperation operation)
    {
        // It is rebuilt from the two transfer impulse responses and never takes the SPL
        // lift the plot applies to its own curves; the slot's offset places it.
        Assert.Equal(
            OverlayCurveSemantics.None,
            OverlayCurveSemantics.ForOperation(operation, SplDecibels, Coherence));
    }

    [Theory]
    [InlineData(Mode.PhaseResponse)]
    [InlineData(Mode.GroupDelay)]
    [InlineData(Mode.LiveSpectrum)]
    [InlineData(Mode.ImpulseResponse)]
    public void DrawsOn_IgnoresTheScaleOutsideTheMagnitudeMode(Mode mode)
    {
        // Only Frequency Response carries the relative/SPL axis switch; elsewhere the
        // scale a capture happens to state must not keep it off the plot.
        Assert.True(SplDecibels.DrawsOn(mode, MagnitudeScale.Relative));
    }
}
