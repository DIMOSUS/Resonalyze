using System.Reflection;
using System.Runtime.CompilerServices;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class LiveSpectrumControllerTests
{
    [Fact]
    public void SilentEnteringTransferMode_NormalizesToPeriodicPink()
    {
        // Silent is the one mode-exclusive signal: a transfer function has nothing to
        // correlate against without an excitation, so entering Transfer mode swaps it
        // for the transfer reference.
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.TransferFunction,
            NoiseColor = NoiseColor.Silent
        };

        bool changed = LiveSpectrumController.NormalizeSignalType(options);

        Assert.True(changed);
        Assert.Equal(NoiseColor.PinkPeriodic, options.NoiseColor);
    }

    [Fact]
    public void NormalizeSignalType_KeepsSilentInRtaMode()
    {
        // In RTA mode Silent is valid at EITHER scale — an ambient RTA in dBFS is a
        // legitimate display — so nothing (calibration changes included) swaps it.
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.Silent
        };

        bool changed = LiveSpectrumController.NormalizeSignalType(options);

        Assert.False(changed);
        Assert.Equal(NoiseColor.Silent, options.NoiseColor);
    }

    [Theory]
    [InlineData(LiveAnalysisMode.TransferFunction, NoiseColor.Pink)]
    [InlineData(LiveAnalysisMode.TransferFunction, NoiseColor.PinkPeriodic)]
    [InlineData(LiveAnalysisMode.Rta, NoiseColor.PinkPeriodic)]
    [InlineData(LiveAnalysisMode.Rta, NoiseColor.White)]
    public void NormalizeSignalType_LeavesRealExcitationsUntouched(
        LiveAnalysisMode mode,
        NoiseColor color)
    {
        // Every real excitation is valid in both modes — periodic pink included: in
        // RTA it is simply a known (deterministic) excitation to measure.
        var options = new LiveSpectrumOptions { AnalysisMode = mode, NoiseColor = color };

        bool changed = LiveSpectrumController.NormalizeSignalType(options);

        Assert.False(changed);
        Assert.Equal(color, options.NoiseColor);
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
    public async Task DiscardCapturedData_ClearsTheAccumulationAndTheKeptCurve()
    {
        // The idle-recolour hole: a stopped curve is a record of the PREVIOUS
        // acquisition setup, while the display transform (the slope compensation
        // above all) reads the options live. When an acquisition parameter changes
        // without a restart, the host discards the stale data instead of letting
        // the next redraw silently re-interpret it as the new excitation.
        var factory = new FakeAudioSessionFactory(
            streamingFactory: _ => new RecordingStreamingSession(
                framesToRaise: 20,
                failAfterFrames: false));
        using var noise = new NoiseMeasurement(factory);
        noise.Init(
            44_100,
            24,
            0.5,
            PlaybackChannel.Mono,
            sequenceLength: 1024,
            liveSpectrumOptions: new LiveSpectrumOptions
            {
                AnalysisMode = LiveAnalysisMode.Rta,
                NoiseColor = NoiseColor.Pink
            });

        Task<bool> running = noise.RunAsync();
        LiveSpectrumSnapshot? snapshot = null;
        for (int attempt = 0; attempt < 100 && snapshot == null; attempt++)
        {
            await Task.Delay(10);
            snapshot = noise.GetAccumulatedSpectrumSnapshot();
        }
        await noise.AbortAsync();
        Assert.True(await running, noise.LastError?.ToString());
        Assert.NotNull(snapshot);

        var controller = (LiveSpectrumController)RuntimeHelpers.GetUninitializedObject(
            typeof(LiveSpectrumController));
        SetField(controller, "measurement", noise);
        SetField(controller, "plotView", new OxyPlot.WindowsForms.PlotView());
        SetField(controller, "lastSnapshot", snapshot);

        controller.DiscardCapturedData();

        Assert.Null(noise.GetAccumulatedSpectrumSnapshot());
        Assert.Null(typeof(LiveSpectrumController)
            .GetField("lastSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(controller));
    }

    [Fact]
    public void TiltToggle_ChangesThePeakHoldDisplayKey()
    {
        // The peak-hold envelope holds FINISHED display values; toggling the noise
        // tilt compensation reshapes the display, so it must change the display key
        // (ApplyDisplayOptions then drops the stale envelope instead of max-ing the
        // old values against tilted ones).
        using var sweep = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.Pink
        };
        var controller = (LiveSpectrumController)RuntimeHelpers.GetUninitializedObject(
            typeof(LiveSpectrumController));
        SetField(controller, "measurement", noise);
        SetField(controller, "liveSpectrumOptions", options);
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
                LiveSpectrum: options,
                Waterfall: new WaterfallGenerateOptions(),
                BurstDecay: new WaterfallGenerateOptions())));

        object before = CurrentPeakHoldKey(controller);
        options.CompensateNoiseTilt = true;
        object after = CurrentPeakHoldKey(controller);

        Assert.NotEqual(before, after);
    }

    private static object CurrentPeakHoldKey(LiveSpectrumController controller) =>
        typeof(LiveSpectrumController)
            .GetMethod(
                "CurrentPeakHoldKey",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [])!;

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

    [Fact]
    public void CaptureReadOut_IsCreatedPerModel()
    {
        // The sibling of the view-only notice above, and it went wrong the same way:
        // the read-out was pooled across ticks to stop it allocating thirty times a
        // second, but an OxyPlot element belongs to ONE model, and every rebuild — a
        // tab switch, a display option, a loaded capture — makes a new one. Carrying
        // one instance across them threw on the add, which left the plot blank and
        // surfaced later as "the element already belongs to a PlotModel" on the next
        // load.
        using var sweep = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        var controller = (LiveSpectrumController)RuntimeHelpers.GetUninitializedObject(
            typeof(LiveSpectrumController));
        SetField(controller, "measurement", noise);
        SetField(controller, "liveSpectrumOptions", new LiveSpectrumOptions());
        SetField(controller, "plotModelFactory", CreateMmmFactory(sweep, noise));
        // A loaded capture reports its own recipe, so the read-out needs no running
        // analyzer to have something to say.
        SetField(controller, "loadedCapture", new LiveCaptureDocument
        {
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = LiveAnalysisMode.Mmm,
                AveragedFrameCount = 25,
                IntegratedSeconds = 17.07
            }
        });

        var model = new OxyPlot.PlotModel();
        UpdateCaptureProgressAnnotation(controller, model);
        OverlayTextAnnotation readOut =
            Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());
        Assert.Contains("25 frames", readOut.Text, StringComparison.Ordinal);

        // Ticking onto the same model must not stack duplicates.
        UpdateCaptureProgressAnnotation(controller, model);
        Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());

        // And a rebuilt model gets its own, while the first keeps the one it has.
        var rebuilt = new OxyPlot.PlotModel();
        UpdateCaptureProgressAnnotation(controller, rebuilt);
        Assert.Single(rebuilt.Annotations.OfType<OverlayTextAnnotation>());
    }

    private static PlotModelFactory CreateMmmFactory(
        ExpSweepMeasurement sweep,
        NoiseMeasurement noise) =>
        new(
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
                    AnalysisMode = LiveAnalysisMode.Mmm
                },
                Waterfall: new WaterfallGenerateOptions(),
                BurstDecay: new WaterfallGenerateOptions()));

    private static void UpdateCaptureProgressAnnotation(
        LiveSpectrumController controller,
        OxyPlot.PlotModel model) =>
        typeof(LiveSpectrumController)
            .GetMethod(
                "UpdateCaptureProgressAnnotation",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [model]);

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
