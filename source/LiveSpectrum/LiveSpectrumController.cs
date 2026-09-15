using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed class LiveSpectrumController : IDisposable
{
    private readonly Form owner;
    private readonly NoiseMeasurement measurement;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private readonly OxyPlot.WindowsForms.PlotView plotView;
    // Through viewport memory so the user's zoom survives model rebuilds.
    private readonly PlotViewportMemory plotViewports;
    private readonly PlotModelFactory plotModelFactory;
    private readonly OverlayCollection overlayCollection;
    private readonly Func<Mode> getCurrentMode;
    private readonly Func<Task> selectLiveSpectrumAsync;
    private readonly Action updateOverlayAvailability;
    private readonly Action updateRecordButton;
    private readonly Action updatePlotLabels;
    private readonly LiveSpectrumOptions liveSpectrumOptions;
    // Resolves the id to the curve so a run freezes the calibration itself.
    private readonly Func<string?, CapturedMicrophoneCalibration> resolveCalibration;
    private readonly Func<bool> suppressErrorDialogs;
    private static readonly CurveTag LiveSpectrumTag =
        new(Mode.LiveSpectrum, AnalysisCurveKind.Primary, CurveSource.Main);
    internal static readonly CurveTag LiveSpectrumInputMagnitudeTag =
        new(Mode.LiveSpectrum, AnalysisCurveKind.InputSpectrum, CurveSource.Main);
    private const string LiveSpectrumLowCoherenceTag = "live-spectrum:low-coherence";
    private const string LiveSpectrumCoherenceTag = "live-spectrum:coherence";
    private const string LiveSpectrumPeakHoldTag = "live-spectrum:peak-hold";
    private const string OverloadAnnotationTag = "live-spectrum:overload";
    private const string SplViewOnlyAnnotationTag = "live-spectrum:spl-view-only";
    private const string CaptureProgressAnnotationTag = "live-spectrum:capture-progress";
    private const long PeakHoldSuppressionMs = 1000;
    private bool disposed;
    private bool redrawInProgress;
    // Held over the displayed band curve, not raw bins. See docs/tech/live-spectrum.md#peak-hold.
    private List<SignalPoint>? peakHoldPoints;
    private long peakHoldResumeTick;
    private LiveSpectrumSnapshot? lastSnapshot;
    // Lets a tick with no new frame skip clone-and-render.
    private int lastDrawnFrameCount = -1;
    // A loaded capture is state: every RebuildModel must redraw it, or the accumulation replaces it.
    private LiveCaptureDocument? loadedCapture;
    private ProtectiveHighPassConfiguration configuredProtectiveHighPass =
        ProtectiveHighPassConfiguration.Off;
    // Pooled per model: an OxyPlot element belongs to one model at a time.
    private OverlayTextAnnotation? captureProgressAnnotation;
    private PlotModel? captureProgressOwner;
    private string? captureProgressState;
    private int captureProgressFrames = -1;
    // Reused across ~30 fps ticks to avoid allocation churn; remove/re-add each tick keeps z-order against overlays.
    private LineSeries? peakHoldSeries;
    private LineSeries? mainSeries;
    private LineSeries? trustedSeries;
    private LineSeries? untrustedSeries;
    private LineSeries? coherenceSeries;
    private LineSeries? inputMagnitudeSeries;
    private PlotModel? attachedModel;

    public LiveSpectrumController(
        Form owner,
        NoiseMeasurement measurement,
        OxyPlot.WindowsForms.PlotView plotView,
        PlotViewportMemory plotViewports,
        PlotModelFactory plotModelFactory,
        OverlayCollection overlayCollection,
        Func<Mode> getCurrentMode,
        Func<Task> selectLiveSpectrumAsync,
        Action updateOverlayAvailability,
        Action updateRecordButton,
        Action updatePlotLabels,
        LiveSpectrumOptions liveSpectrumOptions,
        Func<string?, CapturedMicrophoneCalibration> resolveCalibration,
        Func<bool> suppressErrorDialogs)
    {
        this.owner = owner;
        this.measurement = measurement;
        this.plotView = plotView;
        this.plotViewports = plotViewports;
        this.plotModelFactory = plotModelFactory;
        this.overlayCollection = overlayCollection;
        this.getCurrentMode = getCurrentMode;
        this.selectLiveSpectrumAsync = selectLiveSpectrumAsync;
        this.updateOverlayAvailability = updateOverlayAvailability;
        this.updateRecordButton = updateRecordButton;
        this.updatePlotLabels = updatePlotLabels;
        this.liveSpectrumOptions = liveSpectrumOptions;
        this.resolveCalibration = resolveCalibration;
        this.suppressErrorDialogs = suppressErrorDialogs;
        measurement.Completed += MeasurementCompleted;
        timer.Tick += TimerTick;
    }

    public bool InProgress => measurement.InProgress;
    public bool TimerEnabled => timer.Enabled;

    public bool HasConfiguredLoopback => measurement.HasConfiguredLoopback;

    /// <summary>Whether a view-only SPL scale would hide a curve (drives the amber SPL warning).</summary>
    public bool HasDisplayableCurve => measurement.InProgress || lastSnapshot != null;

    /// <summary>Calibration of the curve on the plot: a loaded capture's own, else the id frozen on the accumulation; null for an empty plot.</summary>
    public string? DisplayedCalibrationName =>
        loadedCapture is { } document
            ? document.Calibration?.Name ?? string.Empty
            : HasDisplayableCurve
                ? measurement.CaptureMicrophoneCalibrationName
                : null;

    /// <summary>Whether a held accumulation can be saved; a loaded capture is excluded (re-saving would restamp its recipe).</summary>
    public bool HasCaptureToSave =>
        loadedCapture == null && lastSnapshot?.InputMagnitude is { Length: > 1 };

    /// <summary>The held snapshot as a capture document, or null. Call <see cref="StopAndHoldAsync"/> first.</summary>
    public LiveCaptureDocument? BuildCaptureDocument() =>
        lastSnapshot is { } snapshot
            ? plotModelFactory.BuildLiveCaptureDocument(
                snapshot.InputMagnitude,
                snapshot.FrameCount,
                title: string.Empty)
            : null;

    /// <summary>Stops and harvests the final accumulation; <see cref="AbortAsync"/> would drop the newest frames.</summary>
    public async Task StopAndHoldAsync()
    {
        if (measurement.InProgress)
        {
            await StopAsync();
            return;
        }

        timer.Stop();
    }

    /// <summary>Replaces the plot and live state with a stored capture.</summary>
    public void ShowLoadedCapture(LiveCaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (attachedModel != null)
        {
            RemoveLiveSpectrumSeries(attachedModel);
            attachedModel = null;
        }

        lastSnapshot = null;
        SuspendPeakHold();
        peakHoldPoints = null;
        loadedCapture = document;
        RebuildModel();
    }

    // The loaded capture keeps its own scale: its levels mean what its capture-time anchor made them.
    private void ShowLoadedCaptureModel(LiveCaptureDocument document)
    {
        PlotModel model = plotModelFactory.CreateLiveSpectrum(
            document.Recipe.MagnitudeScale);
        model.Series.Add(plotModelFactory.BuildLoadedCaptureSeries(document));
        if (document.CurveDb.Length > 0)
        {
            PlotModelStyle.RaiseDecibelViewCeiling(model, document.CurveDb.Max());
        }

        UpdateCaptureProgressAnnotation(model);
        plotViewports.Show(model, getCurrentMode());
        updateOverlayAvailability();
        overlayCollection.Show(getCurrentMode());
        updatePlotLabels();
    }

    public RawCurveCapture? BuildRawRtaCapture() =>
        plotModelFactory.BuildRawRtaCurve(lastSnapshot?.InputMagnitude);

    // Follows the selection: without a matching calibration the SPL axis is view-only (SplViewOnly).
    private bool RenderingSpl =>
        plotModelFactory.EffectiveLiveSpectrumScale == MagnitudeScale.SoundPressureLevel;

    // SPL selected without calibration: axis shows, live curves suppressed. MMM never gets here (reports relative; see PlotModelFactory.LiveUsesBandPower).
    private bool SplViewOnly =>
        RenderingSpl && plotModelFactory.LiveSplOffsetDb == null;

    private bool RtaOnly =>
        plotModelFactory.EffectiveLiveAnalysisMode.IsReferenceFree();

    private bool NeedsInputMagnitude =>
        liveSpectrumOptions.ShowInputMagnitude || RtaOnly;

    // Display transform behind the peak-hold envelope; any change must drop the envelope. See docs/tech/live-spectrum.md#peak-hold.
    private readonly record struct PeakHoldDisplayKey(
        MagnitudeScale Scale,
        bool RtaOnly,
        int SmoothingInverseOctaves,
        double? SplOffsetDb,
        NoiseSpectralModel? TiltModel);

    private PeakHoldDisplayKey renderedPeakHoldKey;

    private PeakHoldDisplayKey CurrentPeakHoldKey() => new(
        RenderingSpl ? MagnitudeScale.SoundPressureLevel : MagnitudeScale.Relative,
        RtaOnly,
        // Effective code: MMM pins smoothing Off.
        plotModelFactory.EffectiveLiveSmoothingCode,
        RenderingSpl ? plotModelFactory.LiveSplOffsetDb : null,
        // Null (off) and a flat model are different transforms.
        plotModelFactory.LiveTiltModel);

    public void ResetAverage()
    {
        measurement.ResetAccumulation();
        lastDrawnFrameCount = -1;
        SuspendPeakHold();
    }

    public void ApplyDisplayOptions()
    {
        measurement.RefreshLiveAveraging();
        // Infinite average restarts on option changes, except spatial-average captures (the accumulation is the measurement). Keyed on mode, not stored speed.
        if (!liveSpectrumOptions.AnalysisMode.IsSpatialAverageCapture() &&
            liveSpectrumOptions.EffectiveAveragingSpeed == AveragingSpeed.Infinite)
        {
            measurement.ResetAccumulation();
            lastDrawnFrameCount = -1;
        }

        if (!liveSpectrumOptions.PeakHold)
        {
            peakHoldPoints = null;
        }

        if (CurrentPeakHoldKey() != renderedPeakHoldKey)
        {
            SuspendPeakHold();
        }

        // Rebuild even while running: coherence display adds/removes an axis.
        RebuildModel();
    }

    // Keeps ramp-up frames out of the envelope.
    private void SuspendPeakHold()
    {
        peakHoldPoints = null;
        peakHoldResumeTick = Environment.TickCount64 + PeakHoldSuppressionMs;
    }

    public async Task ReconfigureFromAsync(
        MeasurementSettingsFile.SweepMeasurementSettings measurementSettings)
    {
        bool restart = measurement.InProgress;
        if (restart)
        {
            await StopAsync();
        }

        ConfigureFrom(measurementSettings);

        if (restart && getCurrentMode() == Mode.LiveSpectrum)
        {
            await StartAsync();
        }
    }

    /// <summary>Updates the next run's protective high-pass without reconfiguring (and so restarting) the analyzer.</summary>
    public void ApplyProtectiveHighPass(
        MeasurementSettingsFile.SweepMeasurementSettings measurementSettings)
    {
        ArgumentNullException.ThrowIfNull(measurementSettings);
        configuredProtectiveHighPass = measurementSettings.ToProtectiveHighPass();
    }

    public void ConfigureFrom(MeasurementSettingsFile.SweepMeasurementSettings measurementSettings)
    {
        ApplyProtectiveHighPass(measurementSettings);
        measurement.Init(
            measurementSettings.SampleRate,
            measurementSettings.Bits,
            60,
            measurementSettings.PlaybackChannel,
            liveSpectrumOptions.SequenceLength,
            measurementSettings.OutputDeviceNumber,
            measurementSettings.InputDeviceNumber,
            measurementSettings.AudioBackend,
            measurementSettings.AsioDriverName,
            measurementSettings.AsioInputChannelOffset,
            measurementSettings.AsioOutputChannelOffset,
            measurementSettings.WaveInputChannelOffset,
            measurementSettings.WaveLoopbackInputChannelOffset,
            measurementSettings.AsioLoopbackInputChannelOffset,
            liveSpectrumOptions,
            measurementSettings.WasapiCaptureEndpointId,
            measurementSettings.WasapiRenderEndpointId,
            measurementSettings.WasapiBufferMilliseconds);
        NormalizeSilentSignal();
    }

    public async Task ToggleAsync()
    {
        if (measurement.InProgress)
        {
            await StopAsync();
            return;
        }

        await StartAsync();
    }

    public async Task AbortAsync()
    {
        timer.Stop();
        if (measurement.InProgress)
        {
            await measurement.AbortAsync();
        }

        updateRecordButton();
        updatePlotLabels();
    }

    public void ForgetLastCurve()
    {
        lastSnapshot = null;
        peakHoldPoints = null;
        RemoveOverloadAnnotation(plotView.Model);
    }

    public void RestoreLastCurve()
    {
        if (measurement.InProgress)
        {
            return;
        }

        RebuildModel();
    }

    /// <summary>Discards accumulation and kept curve after a stopped-analyzer acquisition change, so old data is not re-interpreted under new parameters.</summary>
    public void DiscardCapturedData()
    {
        measurement.ResetAccumulation();
        lastDrawnFrameCount = -1;
        loadedCapture = null;
        ForgetLastCurve();
        updateRecordButton();
    }

    /// <summary>Drops display state incompatible with a calibration change; runs even while another mode owns the plot.</summary>
    public void InvalidateCalibration()
    {
        SuspendPeakHold();
    }

    /// <summary>Reacts to any calibration change in every app mode; the capture keeps running (signal follows the analysis mode).</summary>
    public void RefreshCalibration()
    {
        InvalidateCalibration();

        if (getCurrentMode() == Mode.LiveSpectrum)
        {
            RebuildModel();
        }
    }

    // Silent is RTA-only (Transfer needs an excitation), so Transfer falls back to periodic pink. Other signals are valid in both modes.
    internal static bool NormalizeSignalType(LiveSpectrumOptions options)
    {
        if (options.AnalysisMode == LiveAnalysisMode.TransferFunction &&
            options.NoiseColor == NoiseColor.Silent)
        {
            options.NoiseColor = NoiseColor.PinkPeriodic;
            return true;
        }

        return false;
    }

    private bool NormalizeSilentSignal()
    {
        bool changed = NormalizeSignalType(liveSpectrumOptions);
        if (changed)
        {
            measurement.RefreshPlaybackSignal();
        }

        return changed;
    }

    private void RebuildModel()
    {
        if (loadedCapture is { } document)
        {
            ShowLoadedCaptureModel(document);
            return;
        }

        PlotModel model = plotModelFactory.CreateLiveSpectrum();
        // Prefer a fresh snapshot (accumulators survive a stop) so a scale switch gets curves the stored one lacks.
        LiveSpectrumSnapshot? snapshot =
            measurement.GetAccumulatedSpectrumSnapshot(NeedsInputMagnitude) ?? lastSnapshot;
        if (snapshot != null)
        {
            lastSnapshot = snapshot;
            AddLiveSpectrumSeries(model, snapshot);
            PlotModelStyle.RaiseDecibelViewCeiling(model, LiveDisplayMaxDb());
        }

        plotViewports.Show(model, getCurrentMode());
        updateOverlayAvailability();
        overlayCollection.Show(getCurrentMode());
        updatePlotLabels();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= TimerTick;
        timer.Dispose();
        measurement.Completed -= MeasurementCompleted;
        measurement.Dispose();
    }

    private async Task StartAsync()
    {
        if (getCurrentMode() != Mode.LiveSpectrum)
        {
            await selectLiveSpectrumAsync();
        }

        NormalizeSilentSignal();

        SuspendPeakHold();
        lastSnapshot = null;
        lastDrawnFrameCount = -1;
        loadedCapture = null;
        plotViewports.Show(plotModelFactory.CreateLiveSpectrum(), getCurrentMode());
        overlayCollection.Show(getCurrentMode());
        // Frozen for this accumulation so the divided-out filter and the saved recipe agree.
        measurement.SetCaptureProtectiveHighPass(configuredProtectiveHighPass);
        // Calibration curve frozen too: bins are re-rendered on every redraw and on Save.
        measurement.SetCaptureMicrophoneCalibration(
            resolveCalibration(liveSpectrumOptions.CalibrationId));
        _ = measurement.RunAsync();
        timer.Start();
        updateRecordButton();
        updatePlotLabels();
    }

    private async Task StopAsync()
    {
        LiveSpectrumSnapshot? finalSnapshot = measurement.GetAccumulatedSpectrumSnapshot(
            NeedsInputMagnitude);
        timer.Stop();
        await measurement.AbortAsync();

        lastSnapshot = finalSnapshot ?? lastSnapshot;
        PlotModel model = plotModelFactory.CreateLiveSpectrum();
        if (finalSnapshot != null)
        {
            AddLiveSpectrumSeries(model, finalSnapshot);
            PlotModelStyle.RaiseDecibelViewCeiling(model, LiveDisplayMaxDb());
        }

        UpdateCaptureProgressAnnotation(model);
        plotViewports.Show(model, getCurrentMode());
        updateOverlayAvailability();
        overlayCollection.Show(getCurrentMode());
        updateRecordButton();
        updatePlotLabels();
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        if (redrawInProgress)
        {
            return;
        }

        redrawInProgress = true;
        try
        {
            PlotModel? model = plotView.Model;
            if (model == null || getCurrentMode() != Mode.LiveSpectrum)
            {
                return;
            }

            // Re-render only on a new analysis frame (a 683 ms frame spans ~40 ticks); notices still update every tick. See docs/tech/live-spectrum.md#redraw-loop.
            int frames = measurement.AveragedFrameCount;
            if (frames == lastDrawnFrameCount && lastSnapshot != null)
            {
                UpdateOverloadAnnotation(model);
                UpdateCaptureProgressAnnotation(model);
                model.InvalidatePlot(false);
                return;
            }

            LiveSpectrumSnapshot? snapshot = measurement.GetAccumulatedSpectrumSnapshot(
                NeedsInputMagnitude);
            if (snapshot == null)
            {
                return;
            }

            lastDrawnFrameCount = frames;

            lastSnapshot = snapshot;
            RemoveLiveSpectrumSeries(model);
            AddLiveSpectrumSeries(model, snapshot);
            // A padded loopback puts the transfer above 0 dB; expand-only ceiling raise.
            PlotModelStyle.RaiseDecibelViewCeiling(model, LiveDisplayMaxDb());
            overlayCollection.RefreshCurrentMeasurementTargets();
            UpdateOverloadAnnotation(model);
            UpdateCaptureProgressAnnotation(model);
            model.InvalidatePlot(true);
            updatePlotLabels();
        }
        finally
        {
            redrawInProgress = false;
        }
    }

    // Live series only: overlays must not steer the view.
    private double LiveDisplayMaxDb()
    {
        double maxDb = double.NegativeInfinity;
        foreach (LineSeries? series in new[]
        {
            mainSeries, trustedSeries, untrustedSeries, inputMagnitudeSeries, peakHoldSeries
        })
        {
            if (series == null)
            {
                continue;
            }
            foreach (DataPoint point in series.Points)
            {
                if (double.IsFinite(point.Y))
                {
                    maxDb = Math.Max(maxDb, point.Y);
                }
            }
        }
        return maxDb;
    }

    private void AddLiveSpectrumSeries(PlotModel model, LiveSpectrumSnapshot snapshot)
    {
        // A reused series must never sit in two models at once.
        if (attachedModel != null && !ReferenceEquals(attachedModel, model))
        {
            RemoveLiveSpectrumSeries(attachedModel);
        }
        attachedModel = model;

        // View-only SPL: explain the missing curve. Created per model (OxyPlot element ownership); remove-then-add.
        RemoveSplViewOnlyAnnotation(model);
        if (SplViewOnly)
        {
            OverlayTextAnnotation notice = PlotModelFactory.CreateSplViewOnlyAnnotation(
                "No SPL calibration for the live input — showing dB SPL overlays only");
            notice.Tag = SplViewOnlyAnnotationTag;
            model.Annotations.Add(notice);
            return;
        }

        bool rtaOnly = RtaOnly;
        renderedPeakHoldKey = CurrentPeakHoldKey();

        if (liveSpectrumOptions.PeakHold)
        {
            double[]? peakSource = rtaOnly ? snapshot.InputMagnitude : snapshot.Magnitude;
            if (peakSource is { Length: > 0 })
            {
                // Envelope the displayed band curve: per-bin peaks from different frames would overstate the band.
                List<SignalPoint> current =
                    plotModelFactory.BuildMainDisplayPoints(peakSource, rtaOnly);
                UpdatePeakHold(current);
            }
            else
            {
                peakHoldPoints = null;
            }

            if (peakHoldPoints != null)
            {
                if (peakHoldSeries == null)
                {
                    peakHoldSeries = plotModelFactory.BuildPeakHoldSeries(peakHoldPoints);
                    peakHoldSeries.Tag = LiveSpectrumPeakHoldTag;
                }
                else
                {
                    plotModelFactory.UpdatePeakHoldSeries(peakHoldSeries, peakHoldPoints);
                }
                model.Series.Add(peakHoldSeries);
            }
        }

        if (!rtaOnly && liveSpectrumOptions.ShowMainCurve)
        {
            if (snapshot.Coherence != null &&
                liveSpectrumOptions.CoherenceThresholdPercent > 0)
            {
                if (trustedSeries == null || untrustedSeries == null)
                {
                    (trustedSeries, untrustedSeries) =
                        plotModelFactory.BuildNoiseSeriesSegmented(
                            snapshot.Magnitude,
                            snapshot.Coherence,
                            liveSpectrumOptions.CoherenceThresholdPercent);
                    // The trusted segment stays primary so the current-measurement target uses it.
                    untrustedSeries.Tag = LiveSpectrumLowCoherenceTag;
                    trustedSeries.Tag = LiveSpectrumTag;
                }
                else
                {
                    plotModelFactory.UpdateNoiseSeriesSegmented(
                        trustedSeries,
                        untrustedSeries,
                        snapshot.Magnitude,
                        snapshot.Coherence,
                        liveSpectrumOptions.CoherenceThresholdPercent);
                }
                model.Series.Add(untrustedSeries);
                model.Series.Add(trustedSeries);
            }
            else
            {
                if (mainSeries == null)
                {
                    mainSeries = plotModelFactory.BuildNoiseSeries(snapshot.Magnitude);
                    mainSeries.Tag = LiveSpectrumTag;
                }
                else
                {
                    plotModelFactory.UpdateNoiseSeries(mainSeries, snapshot.Magnitude);
                }
                model.Series.Add(mainSeries);
            }
        }

        if (snapshot.InputMagnitude != null &&
            (liveSpectrumOptions.ShowInputMagnitude || rtaOnly))
        {
            if (inputMagnitudeSeries == null)
            {
                inputMagnitudeSeries =
                    plotModelFactory.BuildInputMagnitudeSeries(snapshot.InputMagnitude);
                inputMagnitudeSeries.Tag = LiveSpectrumInputMagnitudeTag;
            }
            else
            {
                plotModelFactory.UpdateInputMagnitudeSeries(
                    inputMagnitudeSeries, snapshot.InputMagnitude);
            }
            model.Series.Add(inputMagnitudeSeries);
        }

        if (!rtaOnly && snapshot.Coherence != null && liveSpectrumOptions.ShowCoherence)
        {
            if (coherenceSeries == null)
            {
                coherenceSeries = plotModelFactory.BuildCoherenceSeries(snapshot.Coherence);
                coherenceSeries.Tag = LiveSpectrumCoherenceTag;
            }
            else
            {
                plotModelFactory.UpdateCoherenceSeries(coherenceSeries, snapshot.Coherence);
            }
            model.Series.Add(coherenceSeries);
        }
    }

    // Per-index max of displayed dB equals the band-level peak (grid stable, level monotone in power).
    private void UpdatePeakHold(List<SignalPoint> current)
    {
        if (Environment.TickCount64 < peakHoldResumeTick)
        {
            return;
        }

        if (peakHoldPoints == null || peakHoldPoints.Count != current.Count)
        {
            peakHoldPoints = new List<SignalPoint>(current);
            return;
        }

        for (int i = 0; i < current.Count; i++)
        {
            double held = Math.Max(peakHoldPoints[i].Y, current[i].Y);
            peakHoldPoints[i] = new SignalPoint(current[i].X, held);
        }
    }

    private void UpdateOverloadAnnotation(PlotModel model)
    {
        RemoveOverloadAnnotation(model);

        if (!measurement.HasRecentDrops())
        {
            return;
        }

        model.Annotations.Add(new OverlayTextAnnotation
        {
            Tag = OverloadAnnotationTag,
            Text = "⚠ Processing overload — frames dropped",
            TextPosition = new DataPoint(0.5, 0),
            TextFlowDirection = TextFlowDirection.TopDown,
            FontSize = 12,
            FontWeight = 700,
            TextColor = OxyColor.FromRgb(255, 170, 0),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
        });
    }

    private static void RemoveSplViewOnlyAnnotation(PlotModel model) =>
        RemoveTaggedAnnotations(model, SplViewOnlyAnnotationTag);

    private static void RemoveOverloadAnnotation(PlotModel? model) =>
        RemoveTaggedAnnotations(model, OverloadAnnotationTag);

    private static void RemoveTaggedAnnotations(PlotModel? model, string tag)
    {
        if (model == null)
        {
            return;
        }

        for (int index = model.Annotations.Count - 1; index >= 0; index--)
        {
            if (model.Annotations[index] is OverlayTextAnnotation annotation &&
                Equals(annotation.Tag, tag))
            {
                model.Annotations.RemoveAt(index);
            }
        }
    }

    /// <summary>MMM integration progress: the curve settles visually long before the average does.</summary>
    private void UpdateCaptureProgressAnnotation(PlotModel? model)
    {
        RemoveTaggedAnnotations(model, CaptureProgressAnnotationTag);
        if (model == null ||
            !plotModelFactory.EffectiveLiveAnalysisMode.IsSpatialAverageCapture())
        {
            return;
        }

        int frames;
        double seconds;
        string state;
        if (loadedCapture is { } document)
        {
            frames = document.Recipe.AveragedFrameCount;
            seconds = document.Recipe.IntegratedSeconds;
            state = "Loaded";
        }
        else
        {
            bool running = measurement.InProgress;
            frames = running ? measurement.AveragedFrameCount : lastSnapshot?.FrameCount ?? 0;
            int sampleRate = measurement.SampleRate;
            if (sampleRate < 1)
            {
                return;
            }

            seconds = (double)frames * measurement.AnalysisHopSize / sampleRate;
            state = running ? "Integrating" : "Capture held";
        }

        if (frames <= 0)
        {
            return;
        }

        // Text rebuilt only on change; a new instance per model (OxyPlot throws on an element owned by another model).
        if (captureProgressAnnotation == null ||
            !ReferenceEquals(captureProgressOwner, model))
        {
            captureProgressAnnotation = new OverlayTextAnnotation
            {
                Tag = CaptureProgressAnnotationTag,
                TextPosition = new DataPoint(0.01, 0),
                TextFlowDirection = TextFlowDirection.TopDown,
                FontSize = 12,
                TextColor = OxyColor.FromRgb(150, 165, 190),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left
            };
            captureProgressOwner = model;
            captureProgressState = null;
        }

        if (state != captureProgressState || frames != captureProgressFrames)
        {
            captureProgressState = state;
            captureProgressFrames = frames;
            captureProgressAnnotation.Text = $"{state} — {seconds:0} s, {frames} frames";
        }

        model.Annotations.Add(captureProgressAnnotation);
    }

    private static void RemoveLiveSpectrumSeries(PlotModel model)
    {
        List<OxyPlot.Series.Series> liveSpectrumSeries = model.Series
            .Where(series =>
                Equals(series.Tag, LiveSpectrumTag) ||
                Equals(series.Tag, LiveSpectrumLowCoherenceTag) ||
                Equals(series.Tag, LiveSpectrumCoherenceTag) ||
                Equals(series.Tag, LiveSpectrumPeakHoldTag) ||
                Equals(series.Tag, LiveSpectrumInputMagnitudeTag))
            .ToList();
        foreach (OxyPlot.Series.Series series in liveSpectrumSeries)
        {
            model.Series.Remove(series);
        }
    }

    private void MeasurementCompleted(bool success)
    {
        if (owner.IsDisposed || !owner.IsHandleCreated)
        {
            return;
        }

        try
        {
            owner.BeginInvoke((MethodInvoker)delegate
            {
                timer.Stop();
                updateOverlayAvailability();
                updateRecordButton();
                updatePlotLabels();
                // A user stop reports success; an error here is a device failure and must not reset the UI silently.
                if (!success &&
                    measurement.LastError is Exception error &&
                    !owner.IsDisposed &&
                    !suppressErrorDialogs())
                {
                    MessageBox.Show(
                        owner,
                        $"The live measurement failed.\r\n\r\n{error.Message}",
                        "Live Spectrum",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            });
        }
        catch (InvalidOperationException)
        {
        }
    }
}
