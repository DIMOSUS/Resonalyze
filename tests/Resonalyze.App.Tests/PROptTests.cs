using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class PROptTests
{
    [Fact]
    public void ManualTauEstimate_UsesSelectedFdwSpectrum()
    {
        const int sampleRate = 48_000;
        const int directSample = 1_440;
        var impulse = new Complex[4_096];
        impulse[directSample] = Complex.One;
        impulse[directSample + 480] = new Complex(0.8, 0.0);

        using var measurement = new ExpSweepMeasurement();
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: impulse,
            sweepDeconvolutionPeakIndex: directSample,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: impulse,
            transferPeakIndex: directSample);

        var options = new FrequencyResponseOptions
        {
            PhaseWindowMode = PhaseWindowMode.FrequencyDependent,
            PhaseFdwCycles = 4,
            PhaseDetrendMode = PhaseDetrendMode.Manual,
            PhaseGateOffsetMs = directSample * 1_000.0 / sampleRate,
            PhaseLeftMs = 1.0,
            PhasePlateauMs = 4.0,
            PhaseRightMs = 15.0,
            Unwrap = true
        };
        using var panel = new PROpt();
        panel.Init(measurement, options, new CurveVisibilityOptions());

        IImpulseMeasurement view =
            new MeasurementPlotContext(measurement).CreatePrimaryMeasurement();
        var fdwSettings = new PhaseAnalysisSettings(
            options.PhaseWindowMode,
            options.PhaseFdwCycles,
            PhaseDetrendMode.Auto,
            options.PhaseDetrendMs,
            options.PhaseGateOffsetMs,
            options.PhaseLeftMs,
            options.PhasePlateauMs,
            options.PhaseRightMs,
            options.Unwrap,
            options.SmoothingInverseOctaves);

        (double expectedSlope, double expectedPeak) =
            DataHelper.EstimatePhaseDetrend(view, fdwSettings);
        (double actualSlope, double actualPeak) = panel.EstimateCurrentPhaseDetrend(view);

        Assert.Equal(expectedSlope, actualSlope, tolerance: 1e-9);
        Assert.Equal(expectedPeak, actualPeak, tolerance: 1e-9);

        (double fixedSlope, _) = DataHelper.EstimatePhaseDetrend(
            view,
            options.PhaseGateOffsetMs,
            options.PhaseLeftMs,
            options.PhasePlateauMs,
            options.PhaseRightMs);
        Assert.NotEqual(fixedSlope, actualSlope, tolerance: 0.00001);
    }
}
