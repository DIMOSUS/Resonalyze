using System.Reflection;
using System.Runtime.CompilerServices;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class LiveSpectrumControllerTests
{
    [Fact]
    public void MissingEffectiveSplCalibration_NormalizesSilentToPeriodicPink()
    {
        var options = new LiveSpectrumOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.Silent
        };

        bool changed = LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.Relative);

        Assert.True(changed);
        Assert.Equal(NoiseColor.PinkPeriodic, options.NoiseColor);
    }

    [Fact]
    public void NormalizeSignalType_KeepsSilentWhileSplIsEffective()
    {
        var options = new LiveSpectrumOptions { NoiseColor = NoiseColor.Silent };

        bool changed = LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.SoundPressureLevel);

        Assert.False(changed);
        Assert.Equal(NoiseColor.Silent, options.NoiseColor);
    }

    [Fact]
    public void NormalizeSignalType_LeavesANonSilentSignalUntouched()
    {
        var options = new LiveSpectrumOptions { NoiseColor = NoiseColor.Pink };

        bool changed = LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.Relative);

        Assert.False(changed);
        Assert.Equal(NoiseColor.Pink, options.NoiseColor);
    }

    [Fact]
    public void RestoredEffectiveSpl_NormalizesPeriodicPinkBackToSilent()
    {
        // The symmetric half of the fallback: after a Silent→pink calibration-loss the
        // stored signal is periodic pink while the requested scale is still SPL. When SPL
        // becomes effective again, periodic pink is invalid there (it is the transfer
        // reference, pointless in the reference-free RTA), so it must swap back to Silent
        // — never left playing an excitation the SPL panel cannot even display.
        var options = new LiveSpectrumOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.PinkPeriodic
        };

        bool changed = LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.SoundPressureLevel);

        Assert.True(changed);
        Assert.Equal(NoiseColor.Silent, options.NoiseColor);
    }

    [Fact]
    public void NormalizeSignalType_KeepsPeriodicPinkOnTheRelativeScale()
    {
        var options = new LiveSpectrumOptions { NoiseColor = NoiseColor.PinkPeriodic };

        bool changed = LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.Relative);

        Assert.False(changed);
        Assert.Equal(NoiseColor.PinkPeriodic, options.NoiseColor);
    }

    [Fact]
    public void CalibrationInvalidatedOutsideLiveSpectrum_DropsPeakHoldBeforeRestore()
    {
        var controller = (LiveSpectrumController)RuntimeHelpers.GetUninitializedObject(
            typeof(LiveSpectrumController));
        FieldInfo peakHoldField = typeof(LiveSpectrumController).GetField(
            "peakHoldPoints",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        peakHoldField.SetValue(
            controller,
            new List<SignalPoint> { new(1000.0, 85.0) });

        // PersistCalibration invokes this even when another mode owns the plot.
        controller.InvalidateCalibration();

        Assert.Null(peakHoldField.GetValue(controller));
    }

    [Fact]
    public void ViewOnlySpl_ExplainsTheSuppressedCurveInsteadOfAnEmptyPlot()
    {
        // dB SPL selected with no calibration configured: the snapshot's curves are
        // suppressed (raw dBFS on an absolute axis would be garbage), and the model
        // must say why instead of silently showing an empty plot. The notice is added
        // by the series path, so it appears only when a curve really was suppressed.
        using var sweep = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        var controller = (LiveSpectrumController)RuntimeHelpers.GetUninitializedObject(
            typeof(LiveSpectrumController));
        SetField(controller, "measurement", noise);
        SetField(controller, "liveSpectrumOptions", new LiveSpectrumOptions());
        SetField(controller, "plotModelFactory", new PlotModelFactory(
            sweep,
            noise,
            _ => null,
            new PlotPresentationOptions(
                FrequencyResponse: new FrequencyResponseOptions(),
                PhaseResponse: new FrequencyResponseOptions(),
                GroupDelay: new FrequencyResponseOptions(),
                FrequencyResponseVisibility: new CurveVisibilityOptions(),
                PhaseResponseVisibility: new CurveVisibilityOptions(),
                GroupDelayVisibility: new CurveVisibilityOptions(),
                ImpulseResponse: new ImpulseResponseOptions(),
                LiveSpectrum: new LiveSpectrumOptions
                {
                    MagnitudeScale = MagnitudeScale.SoundPressureLevel
                },
                Waterfall: new WaterfallGenerateOptions(),
                BurstDecay: new WaterfallGenerateOptions())));

        var model = new OxyPlot.PlotModel();
        var snapshot = new LiveSpectrumSnapshot(
            [-30.0, -32.0], Coherence: null, InputMagnitude: [-30.0, -32.0]);
        AddLiveSpectrumSeries(controller, model, snapshot);

        Assert.Empty(model.Series);
        OverlayTextAnnotation note =
            Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());
        Assert.Contains("overlays only", note.Text, StringComparison.OrdinalIgnoreCase);

        // A live tick re-adds the series into the SAME model: the notice must not
        // stack up into duplicates.
        AddLiveSpectrumSeries(controller, model, snapshot);
        Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());

        // RebuildModel (a smoothing change, leaving and re-entering the mode)
        // creates a NEW model while the old one still holds its notice. An OxyPlot
        // element belongs to one PlotModel, so the notice must be created per model
        // — reusing the first instance threw InvalidOperationException here.
        var rebuilt = new OxyPlot.PlotModel();
        AddLiveSpectrumSeries(controller, rebuilt, snapshot);
        Assert.Single(rebuilt.Annotations.OfType<OverlayTextAnnotation>());
    }

    private static void SetField(object target, string name, object value) =>
        typeof(LiveSpectrumController)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static void AddLiveSpectrumSeries(
        LiveSpectrumController controller,
        OxyPlot.PlotModel model,
        LiveSpectrumSnapshot snapshot) =>
        typeof(LiveSpectrumController)
            .GetMethod(
                "AddLiveSpectrumSeries",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [model, snapshot]);
}
