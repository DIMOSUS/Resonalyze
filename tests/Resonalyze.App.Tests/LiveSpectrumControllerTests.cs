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
        // A transfer function has nothing to correlate without excitation, so Transfer mode swaps Silent out.
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

        controller.InvalidateCalibration();

        Assert.Null(peakHoldField.GetValue(controller));
    }

    [Fact]
    public async Task DiscardCapturedData_ClearsTheAccumulationAndTheKeptCurve()
    {
        // A stopped curve records the previous acquisition setup while the display reads options live, so a change discards it.
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
        int notified = 0;
        SetField(controller, "updateRecordButton", new Action(() => notified++));

        controller.DiscardCapturedData();

        Assert.Equal(1, notified);

        Assert.Null(noise.GetAccumulatedSpectrumSnapshot());
        Assert.Null(typeof(LiveSpectrumController)
            .GetField("lastSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(controller));
    }

    [Fact]
    public void TiltToggle_ChangesThePeakHoldDisplayKey()
    {
        // Peak hold holds finished display values, so toggling tilt compensation must change the display key.
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
        // dB SPL without calibration suppresses curves; the notice appears only when a curve was really suppressed.
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

        AddLiveSpectrumSeries(controller, model, snapshot);
        Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());

        // An OxyPlot element belongs to one PlotModel, so the notice is created per model.
        var rebuilt = new OxyPlot.PlotModel();
        AddLiveSpectrumSeries(controller, rebuilt, snapshot);
        Assert.Single(rebuilt.Annotations.OfType<OverlayTextAnnotation>());
    }

    [Fact]
    public void CaptureReadOut_IsCreatedPerModel()
    {
        // Same rule for the pooled read-out: reusing one instance across rebuilt models threw on add.
        using var sweep = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        var controller = (LiveSpectrumController)RuntimeHelpers.GetUninitializedObject(
            typeof(LiveSpectrumController));
        SetField(controller, "measurement", noise);
        SetField(controller, "liveSpectrumOptions", new LiveSpectrumOptions());
        SetField(controller, "plotModelFactory", CreateMmmFactory(sweep, noise));
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

        UpdateCaptureProgressAnnotation(controller, model);
        Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());

        var rebuilt = new OxyPlot.PlotModel();
        UpdateCaptureProgressAnnotation(controller, rebuilt);
        Assert.Single(rebuilt.Annotations.OfType<OverlayTextAnnotation>());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(3, true)]
    public void CaptureReadOut_NamesClippedFrames(int? clippedFrames, bool named)
    {
        using var sweep = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        var controller = (LiveSpectrumController)RuntimeHelpers.GetUninitializedObject(
            typeof(LiveSpectrumController));
        SetField(controller, "measurement", noise);
        SetField(controller, "liveSpectrumOptions", new LiveSpectrumOptions());
        SetField(controller, "plotModelFactory", CreateMmmFactory(sweep, noise));
        SetField(controller, "loadedCapture", new LiveCaptureDocument
        {
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = LiveAnalysisMode.Mmm,
                AveragedFrameCount = 25,
                ClippedFrameCount = clippedFrames,
                IntegratedSeconds = 17.07
            }
        });

        var model = new OxyPlot.PlotModel();
        UpdateCaptureProgressAnnotation(controller, model);

        OverlayTextAnnotation readOut =
            Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());
        Assert.Equal(named, readOut.Text?.Contains("clipped", StringComparison.Ordinal) ?? false);
        if (named)
        {
            Assert.Contains("25 frames, 3 clipped", readOut.Text, StringComparison.Ordinal);
        }
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
