using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The rules that turn the live options and the analyzer's read into what the plot draws.</summary>
public sealed class LiveSpectrumDisplayTests
{
    [Fact]
    public void WithoutALoopbackTransferFallsBackToTheRta()
    {
        var transfer = new LiveSpectrumOptions { AnalysisMode = LiveAnalysisMode.TransferFunction };
        var mmm = new LiveSpectrumOptions { AnalysisMode = LiveAnalysisMode.Mmm };

        Assert.Equal(LiveAnalysisMode.Rta, Display(transfer, micOnly: true).Mode);
        Assert.True(Display(transfer, micOnly: true).RtaOnly);
        Assert.Equal(LiveAnalysisMode.TransferFunction, Display(transfer).Mode);
        Assert.Equal(LiveAnalysisMode.Mmm, Display(mmm, micOnly: true).Mode);
    }

    [Fact]
    public void ATransferFunctionIsAlwaysRelative()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.TransferFunction,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };

        LiveSpectrumDisplay display = Display(options, anchor: LiveSpectrumCurvesTests.Anchor(94, -20));

        Assert.Equal(MagnitudeScale.Relative, display.Scale);
        Assert.False(display.SplViewOnly);
        Assert.Equal(0.0, display.SplRenderOffsetDb);
    }

    // MMM keeps a capture's recipe fixed across a set: smoothing off, tilt on, band power, and SPL only when anchored.
    [Fact]
    public void MmmPinsItsRecipe()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            SmoothingInverseOctaves = 6,
            CompensateNoiseTilt = false,
            NoiseColor = NoiseColor.White,
            MagnitudeScale = MagnitudeScale.Relative
        };

        LiveSpectrumDisplay unanchored = Display(options);
        LiveSpectrumDisplay anchored = Display(options, anchor: LiveSpectrumCurvesTests.Anchor(94, -20));

        Assert.Equal(0, unanchored.SmoothingCode);
        Assert.Equal(NoiseColorTilt.SpectralModel(NoiseColor.PinkPeriodic), unanchored.TiltModel);
        Assert.True(unanchored.UsesBandPower);
        Assert.Equal(MagnitudeScale.Relative, unanchored.Scale);
        Assert.False(unanchored.SplViewOnly);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, anchored.Scale);
        Assert.Equal(114.0, anchored.SplRenderOffsetDb, precision: 9);
    }

    [Fact]
    public void SilentHasNoTiltToUndo()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.Silent,
            CompensateNoiseTilt = true
        };

        Assert.Null(Display(options).TiltModel);
    }

    [Fact]
    public void TheRtaIsReadWheneverItIsShownOrIsTheOnlyCurve()
    {
        var transfer = new LiveSpectrumOptions { AnalysisMode = LiveAnalysisMode.TransferFunction };
        var shown = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.TransferFunction,
            ShowInputMagnitude = true
        };
        var rta = new LiveSpectrumOptions { AnalysisMode = LiveAnalysisMode.Rta };

        Assert.False(Display(transfer).NeedsInputMagnitude);
        Assert.True(Display(shown).NeedsInputMagnitude);
        Assert.True(Display(rta).NeedsInputMagnitude);
    }

    // Peak hold holds finished display values, so every transform they went through is in the key.
    [Fact]
    public void EveryDisplayTransformMovesThePeakHoldKey()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.Pink,
            SmoothingInverseOctaves = 6
        };
        LivePeakHoldKey before = Display(options).PeakHoldKey;

        options.CompensateNoiseTilt = true;
        LivePeakHoldKey tilted = Display(options).PeakHoldKey;
        options.SmoothingInverseOctaves = 12;
        LivePeakHoldKey smoothed = Display(options).PeakHoldKey;
        options.MagnitudeScale = MagnitudeScale.SoundPressureLevel;
        LivePeakHoldKey spl = Display(options, anchor: LiveSpectrumCurvesTests.Anchor(94, -20)).PeakHoldKey;
        LivePeakHoldKey louder = Display(options, anchor: LiveSpectrumCurvesTests.Anchor(104, -20)).PeakHoldKey;

        Assert.Equal(5, new[] { before, tilted, smoothed, spl, louder }.Distinct().Count());
        // The anchor is part of the key only where it is applied.
        options.MagnitudeScale = MagnitudeScale.Relative;
        Assert.Equal(
            Display(options, anchor: LiveSpectrumCurvesTests.Anchor(94, -20)).PeakHoldKey,
            Display(options, anchor: LiveSpectrumCurvesTests.Anchor(104, -20)).PeakHoldKey);
    }

    private static LiveSpectrumDisplay Display(
        LiveSpectrumOptions options,
        bool micOnly = false,
        SplCalibration? anchor = null) =>
        LiveSpectrumDisplay.Of(options, LiveSpectrumCurvesTests.Setup(micOnly), anchor);
}
