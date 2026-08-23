using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class OverlayCurveSemanticsTests
{
    private static readonly OverlayCurveSemantics SplDecibels =
        OverlayCurveSemantics.ForCurve(MagnitudeScale.SoundPressureLevel, "decibel");
    private static readonly OverlayCurveSemantics RelativeDecibels =
        OverlayCurveSemantics.ForCurve(MagnitudeScale.Relative, "decibel");
    // A capture records whatever magnitude scale the plot was showing — coherence
    // included — so this is what a real captured coherence slot holds.
    private static readonly OverlayCurveSemantics CapturedCoherence =
        OverlayCurveSemantics.ForCurve(
            MagnitudeScale.SoundPressureLevel,
            PlotModelFactory.CoherenceAxisKey);
    // A live curve is built the same way, from the scale showing right now.
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
        // "A only" hands the STORED points through: an ~80 dB SPL reading would be
        // drawn as ~80 dB of relative gain if the axis let it in.
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
        // Coherence is a 0…1 ratio on its own axis. The capture still recorded the
        // magnitude scale on screen at the time, and reading that as the curve's own
        // would hide it the moment the plot switched between dBr and dB SPL.
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
        // 0…1 on the decibel axis is the visible half of the same defect: the operation
        // used to drop the operand's axis key and land on the mode's main axis.
        OverlayOperationResult result = Result(
            operation,
            CapturedCoherence,
            operation == OverlayOperation.CurveA ? default : LiveCoherence);

        Assert.True(result.IsDefined);
        Assert.Equal(PlotModelFactory.CoherenceAxisKey, result.Curve.YAxisKey);
        // And a slope in dB per octave has no business on a coherence ratio.
        Assert.False(result.Curve.IsDecibels);
    }

    [Theory]
    [InlineData(OverlayOperation.AMinusB)]
    [InlineData(OverlayOperation.Sum)]
    [InlineData(OverlayOperation.Average)]
    [InlineData(OverlayOperation.Blend)]
    public void OperandsOfDifferentKinds_HaveNoResultAtAll(OverlayOperation operation)
    {
        // dB SPL minus relative decibels is ~80 of nothing: numerically it looks like a
        // level, and treating that as "states no scale" would let it draw on BOTH axes.
        // Same for coherence against decibels.
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
        // A live curve is redrawn on every rebuild, which does not make its numbers
        // vague: on a relative plot they ARE relative, and taking a dB SPL capture away
        // from them is the same ~80 dB of nothing as between two captures.
        Assert.False(OverlayCurveSemantics.AreCompatible(
            SplDecibels,
            LiveRelativeDecibels));
        Assert.False(Result(
            OverlayOperation.AMinusB,
            SplDecibels,
            LiveRelativeDecibels).IsDefined);
        // On the dB SPL plot the same live curve is dB SPL, and the pair is fine.
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
        // A coherence trace states no magnitude scale, and an imported or legacy capture
        // states no axis. Neither may veto what it says nothing about.
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
        // The field case this started from: the difference of two dB SPL captures is a
        // handful of dB that the slot's offset lifts onto the SPL axis.
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
        // Compatible operands agree, so the result is pinned exactly as they are.
        OverlayOperationResult result = Result(operation, SplDecibels, SplDecibels);

        Assert.True(result.IsDefined);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, result.Curve.Scale);
        Assert.False(result.Curve.DrawsOn(
            Mode.FrequencyResponse,
            MagnitudeScale.Relative));
        // An operand with no scale of its own to state — a coherence trace — leaves the
        // other to answer, on the axis they share.
        Assert.Null(Result(operation, CapturedCoherence, LiveCoherence).Curve.Scale);
    }

    [Theory]
    [InlineData(OverlayOperation.ComplexSum)]
    [InlineData(OverlayOperation.ComplexSumLoss)]
    public void TheComplexSum_StatesNothing(OverlayOperation operation)
    {
        // It is rebuilt from the two transfer impulse responses and never takes the SPL
        // lift the plot applies to its own curves; the slot's offset places it. Its
        // operand boxes are greyed out, so their mismatch cannot make it undefined.
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
        // Only Frequency Response carries the relative/SPL axis switch; elsewhere the
        // scale a capture happens to state must not keep it off the plot.
        Assert.True(SplDecibels.DrawsOn(mode, MagnitudeScale.Relative));
    }
}
