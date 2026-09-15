using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class OverlayCurveSemanticsTests
{
    private static readonly OverlayCurveSemantics SplDecibels =
        OverlayCurveSemantics.ForCurve(MagnitudeScale.SoundPressureLevel, "decibel");
    private static readonly OverlayCurveSemantics RelativeDecibels =
        OverlayCurveSemantics.ForCurve(MagnitudeScale.Relative, "decibel");
    // A capture records the plot's magnitude scale, coherence included.
    private static readonly OverlayCurveSemantics CapturedCoherence =
        OverlayCurveSemantics.ForCurve(
            MagnitudeScale.SoundPressureLevel,
            PlotModelFactory.CoherenceAxisKey);
    private static readonly OverlayCurveSemantics LiveCoherence =
        OverlayCurveSemantics.ForCurve(
            MagnitudeScale.Relative,
            PlotModelFactory.CoherenceAxisKey);
    private static readonly OverlayCurveSemantics LiveRelativeDecibels =
        OverlayCurveSemantics.ForCurve(MagnitudeScale.Relative, "decibel");

    private static OverlayOperationResult Result(
        OverlayOperation operation,
        OverlayCurveSemantics a,
        OverlayCurveSemantics b = default) =>
        OverlayCurveSemantics.ForOperation(operation, a, b);

    [Fact]
    public void CurveA_OverAnSplCapture_StaysOffTheRelativeAxis()
    {
        OverlayCurveSemantics result = Result(OverlayOperation.CurveA, SplDecibels).Curve;

        Assert.Equal(MagnitudeScale.SoundPressureLevel, result.Scale);
        Assert.False(result.DrawsOn(Mode.FrequencyResponse, MagnitudeScale.Relative));
        Assert.True(result.DrawsOn(
            Mode.FrequencyResponse,
            MagnitudeScale.SoundPressureLevel));
    }

    [Fact]
    public void ACapturedCoherenceCurve_StatesNoMagnitudeScale()
    {
        // Coherence is 0…1 on its own axis; the recorded scale must not hide it on a dBr/SPL switch.
        Assert.Null(CapturedCoherence.Scale);
        Assert.False(CapturedCoherence.IsDecibels);
        Assert.True(CapturedCoherence.DrawsOn(
            Mode.FrequencyResponse,
            MagnitudeScale.Relative));
        Assert.True(CapturedCoherence.DrawsOn(
            Mode.FrequencyResponse,
            MagnitudeScale.SoundPressureLevel));
    }

    [Theory]
    [InlineData(OverlayOperation.CurveA)]
    [InlineData(OverlayOperation.AMinusB)]
    public void CoherenceOperands_KeepTheCoherenceAxis(OverlayOperation operation)
    {
        OverlayOperationResult result = Result(
            operation,
            CapturedCoherence,
            operation == OverlayOperation.CurveA ? default : LiveCoherence);

        Assert.True(result.IsDefined);
        Assert.Equal(PlotModelFactory.CoherenceAxisKey, result.Curve.YAxisKey);
        Assert.False(result.Curve.IsDecibels);
    }

    [Theory]
    [InlineData(OverlayOperation.AMinusB)]
    [InlineData(OverlayOperation.Sum)]
    [InlineData(OverlayOperation.Average)]
    [InlineData(OverlayOperation.Blend)]
    public void OperandsOfDifferentKinds_HaveNoResultAtAll(OverlayOperation operation)
    {
        // dB SPL minus relative dB looks like a level but is meaningless; it must not draw on both axes.
        Assert.False(Result(operation, SplDecibels, RelativeDecibels).IsDefined);
        Assert.False(Result(operation, CapturedCoherence, LiveRelativeDecibels).IsDefined);
        Assert.False(OverlayCurveSemantics.AreCompatible(SplDecibels, RelativeDecibels));
        Assert.False(OverlayCurveSemantics.AreCompatible(
            CapturedCoherence,
            LiveRelativeDecibels));
    }

    [Fact]
    public void ALiveOperand_IsJudgedByTheScaleItIsDrawnOn()
    {
        Assert.False(OverlayCurveSemantics.AreCompatible(
            SplDecibels,
            LiveRelativeDecibels));
        Assert.False(Result(
            OverlayOperation.AMinusB,
            SplDecibels,
            LiveRelativeDecibels).IsDefined);
        Assert.True(Result(
            OverlayOperation.AMinusB,
            SplDecibels,
            OverlayCurveSemantics.ForCurve(
                MagnitudeScale.SoundPressureLevel,
                "decibel")).IsDefined);
    }

    [Fact]
    public void AnOperandThatStatesNothing_IsCompatibleWithAnything()
    {
        Assert.True(OverlayCurveSemantics.AreCompatible(CapturedCoherence, LiveCoherence));
        Assert.True(OverlayCurveSemantics.AreCompatible(
            SplDecibels,
            new OverlayCurveSemantics(MagnitudeScale.SoundPressureLevel, null)));
    }

    [Theory]
    [InlineData(OverlayOperation.AMinusB)]
    [InlineData(OverlayOperation.BMinusA)]
    [InlineData(OverlayOperation.AbsoluteDifference)]
    public void ADifference_CancelsTheAbsoluteLevel(OverlayOperation operation)
    {
        OverlayOperationResult result = Result(operation, SplDecibels, SplDecibels);

        Assert.True(result.IsDefined);
        Assert.Null(result.Curve.Scale);
        Assert.True(result.Curve.DrawsOn(Mode.FrequencyResponse, MagnitudeScale.Relative));
        Assert.True(result.Curve.DrawsOn(
            Mode.FrequencyResponse,
            MagnitudeScale.SoundPressureLevel));
    }

    [Theory]
    [InlineData(OverlayOperation.Sum)]
    [InlineData(OverlayOperation.Average)]
    [InlineData(OverlayOperation.Blend)]
    public void ALevelPreservingOperation_KeepsTheLevelItReproduces(
        OverlayOperation operation)
    {
        OverlayOperationResult result = Result(operation, SplDecibels, SplDecibels);

        Assert.True(result.IsDefined);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, result.Curve.Scale);
        Assert.False(result.Curve.DrawsOn(
            Mode.FrequencyResponse,
            MagnitudeScale.Relative));
        Assert.Null(Result(operation, CapturedCoherence, LiveCoherence).Curve.Scale);
    }

    [Theory]
    [InlineData(OverlayOperation.ComplexSum)]
    [InlineData(OverlayOperation.ComplexSumLoss)]
    public void TheComplexSum_StatesNothing(OverlayOperation operation)
    {
        // Rebuilt from the two transfer IRs without the SPL lift; greyed operands cannot make it undefined.
        OverlayOperationResult result = Result(operation, SplDecibels, CapturedCoherence);

        Assert.True(result.IsDefined);
        Assert.Equal(OverlayCurveSemantics.None, result.Curve);
    }

    [Theory]
    [InlineData(Mode.PhaseResponse)]
    [InlineData(Mode.GroupDelay)]
    [InlineData(Mode.LiveSpectrum)]
    [InlineData(Mode.ImpulseResponse)]
    public void DrawsOn_IgnoresTheScaleOutsideTheMagnitudeMode(Mode mode)
    {
        Assert.True(SplDecibels.DrawsOn(mode, MagnitudeScale.Relative));
    }
}
