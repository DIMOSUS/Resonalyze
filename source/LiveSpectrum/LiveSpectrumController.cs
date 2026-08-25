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
    // Model swaps go through the viewport memory, not straight at the view: a
    // start, a stop or a settings change rebuilds the live model, and the zoom the
    // user set while watching the analyzer has to survive that.
    private readonly PlotViewportMemory plotViewports;
    private readonly PlotModelFactory plotModelFactory;
    private readonly OverlayCollection overlayCollection;
    private readonly Func<Mode> getCurrentMode;
    private readonly Func<Task> selectLiveSpectrumAsync;
    private readonly Action updateOverlayAvailability;
    private readonly Action updateRecordButton;
    private readonly Action updatePlotLabels;
    private readonly LiveSpectrumOptions liveSpectrumOptions;
    // Same guard the sweep path has (Form1.ShowMeasurementError): no modal
    // error dialog while the owner is tearing down.
    private readonly Func<bool> suppressErrorDialogs;
    // The live transfer-function curve and the reference-free RTA carry a CurveTag
    // (like every analysis curve) so overlays can bind to them by key and capture
    // their raw form; the remaining live-spectrum helper series stay string-tagged
    // for internal bookkeeping only.
    private static readonly CurveTag LiveSpectrumTag =
        new(Mode.LiveSpectrum, AnalysisCurveKind.Primary, CurveSource.Main);
    // The RTA is a curve in its own right, not a helper: it is the only trace in the
    // RTA-only views and the source a moving-microphone tune is built from.
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
    // The peak-hold envelope is held over the DISPLAYED band curve (freq, dB), not the
    // raw FFT bins, so the SPL band power it shows is the peak of a real band level.
    private List<SignalPoint>? peakHoldPoints;
    private long peakHoldResumeTick;
    private LiveSpectrumSnapshot? lastSnapshot;
    // The accumulation's frame count as of the last drawn tick, so a tick that has no
    // new frame behind it can skip the clone-and-re-render entirely.
    private int lastDrawnFrameCount = -1;
    // A stored capture put on the plot in place of the live trace. It is STATE, not a
    // one-off draw: every rebuild of the model — a tab switch, a display option, a
    // calibration change — goes through RebuildModel, and a loaded capture that were
    // only painted once would be replaced by the surviving accumulation the instant
    // any of those happened, which is exactly what Load appeared to do.
    private LiveCaptureDocument? loadedCapture;
    // The filter the NEXT run will be taken through; see ApplyProtectiveHighPass.
    private ProtectiveHighPassConfiguration configuredProtectiveHighPass =
        ProtectiveHighPassConfiguration.Off;
    // The capture read-out, kept between ticks like the series below and for the
    // same reason. It is pooled PER MODEL, not globally: an OxyPlot element belongs
    // to one model at a time, and a rebuild makes a new one.
    private OverlayTextAnnotation? captureProgressAnnotation;
    private PlotModel? captureProgressOwner;
    private string? captureProgressState;
    private int captureProgressFrames = -1;
    // The ~30 fps redraw reuses these series and refills their points in place;
    // recreating the plot objects (and their point lists) every tick was pure
    // allocation churn. They are removed from and re-added to the model each
    // tick, which keeps today's z-order against overlays.
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
        this.suppressErrorDialogs = suppressErrorDialogs;
        measurement.Completed += MeasurementCompleted;
        timer.Tick += TimerTick;
    }

    public bool InProgress => measurement.InProgress;
    public bool TimerEnabled => timer.Enabled;

    /// <summary>
    /// Whether the configured input carries a loopback reference channel — the
    /// prerequisite of the Transfer Function mode. The options panel colours its
    /// Transfer choice amber by this when the selection cannot take effect.
    /// </summary>
    public bool HasConfiguredLoopback => measurement.HasConfiguredLoopback;

    /// <summary>
    /// Whether the live plot currently has a curve to show — a running capture or a
    /// kept last snapshot — i.e. whether a view-only SPL scale would actually hide
    /// something. The options panel colours its dB SPL choice amber by this, so the
    /// warning marks a real conflict and not a freshly started application.
    /// </summary>
    public bool HasDisplayableCurve => measurement.InProgress || lastSnapshot != null;

    /// <summary>
    /// Whether a held accumulation could be written as a capture document. A loaded
    /// capture does not count: it is already a file, and re-saving it as if it were a
    /// fresh measurement would restamp it with this session's recipe.
    /// </summary>
    public bool HasCaptureToSave =>
        loadedCapture == null && lastSnapshot?.InputMagnitude is { Length: > 1 };

    /// <summary>
    /// The accumulated capture as a whole document, or null when there is nothing to
    /// store. Built from the held snapshot — bins and frame count together, as they
    /// were read under one lock — so the recipe describes the spectrum beside it.
    /// Call <see cref="StopAndHoldAsync"/> first; that is what takes the final
    /// accumulation.
    /// </summary>
    public LiveCaptureDocument? BuildCaptureDocument() =>
        lastSnapshot is { } snapshot
            ? plotModelFactory.BuildLiveCaptureDocument(
                snapshot.InputMagnitude,
                snapshot.FrameCount,
                title: string.Empty)
            : null;

    /// <summary>
    /// Stops a running analyzer the way the record button does — harvesting the final
    /// accumulation into the held snapshot — and does nothing when it is already
    /// stopped. <see cref="AbortAsync"/> is the wrong call for a capture: it stops the
    /// analyzer without taking that last reading, leaving the newest frames unsaved.
    /// </summary>
    public async Task StopAndHoldAsync()
    {
        if (measurement.InProgress)
        {
            await StopAsync();
            return;
        }

        timer.Stop();
    }

    /// <summary>
    /// Replaces the plot with a stored capture. The live state goes with it: a loaded
    /// capture is a different measurement, and leaving the running accumulation's
    /// series or peak-hold envelope behind would blend two of them on one axis.
    /// </summary>
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

    // The stored capture on its OWN axis. Everything else about the live plot follows
    // the current options; the scale cannot, because the levels in the file mean what
    // the anchor at capture time made them mean.
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

    /// <summary>
    /// The raw form of the RTA trace as last drawn, for an overlay capturing it. The
    /// controller owns this because the RTA data lives in the drawn snapshot, not in the
    /// plot factory; the factory turns the samples into the scale-appropriate raw curve.
    /// Null when no RTA has been drawn (never started, or the trace is switched off).
    /// </summary>
    public RawCurveCapture? BuildRawRtaCapture() =>
        plotModelFactory.BuildRawRtaCurve(lastSnapshot?.InputMagnitude);

    // Whether the plot is in the absolute dB SPL (RTA) view. This follows the
    // SELECTION: without a matching calibration the view still shows the SPL axis,
    // but view-only (see SplViewOnly) — live curves are suppressed rather than the
    // scale silently falling back to relative.
    private bool RenderingSpl =>
        plotModelFactory.EffectiveLiveSpectrumScale == MagnitudeScale.SoundPressureLevel;

    // dB SPL selected with no matching calibration: the axis and the SPL overlays
    // show, but live curves have no absolute level to be lifted to and are not
    // drawn. The record button resets the scale to relative before an actual run,
    // so this covers idle redraws of a stale snapshot (a scale switch after a stop)
    // and the moment a running analyzer loses its calibration.
    // MMM cannot reach this state: without an anchor it reports a RELATIVE scale and
    // keeps drawing its band levels, because what a spatial average needs is the
    // band-power rendering, not an absolute reference — see
    // PlotModelFactory.LiveUsesBandPower.
    private bool SplViewOnly =>
        RenderingSpl && plotModelFactory.LiveSplOffsetDb == null;

    // The plot shows only the reference-free RTA (no transfer function or coherence)
    // when the effective analysis mode is RTA — selected, or forced by a missing
    // loopback reference. SPL no longer forces this: the scale is only effective in
    // RTA mode to begin with (see PlotModelFactory.EffectiveLiveSpectrumScale).
    private bool RtaOnly =>
        plotModelFactory.EffectiveLiveAnalysisMode.IsReferenceFree();

    // The reference-free RTA is normally optional, but it IS the only curve in the
    // RTA-only views, so it is always computed there even if its checkbox is off.
    private bool NeedsInputMagnitude =>
        liveSpectrumOptions.ShowInputMagnitude || RtaOnly;

    // The display transform behind the peak-hold envelope at the last drawn frame.
    // peakHoldPoints holds FINISHED display values, so any change to how a level maps
    // to the display — the scale, the RTA-only shaping, the smoothing band width, the
    // microphone-correction mode, or the SPL offset (e.g. a re-calibration to a new
    // offset) — makes the old envelope incompatible; it must be dropped, not max-ed
    // against the new values.
    private readonly record struct PeakHoldDisplayKey(
        MagnitudeScale Scale,
        bool RtaOnly,
        int SmoothingInverseOctaves,
        string? CalibrationId,
        double? SplOffsetDb,
        NoiseSpectralModel? TiltModel);

    private PeakHoldDisplayKey renderedPeakHoldKey;

    private PeakHoldDisplayKey CurrentPeakHoldKey() => new(
        RenderingSpl ? MagnitudeScale.SoundPressureLevel : MagnitudeScale.Relative,
        RtaOnly,
        // The EFFECTIVE code: MMM pins the smoothing Off, so keying on the stored
        // option would call two different display transforms the same and max a
        // 1/6-octave envelope against unsmoothed band levels.
        plotModelFactory.EffectiveLiveSmoothingCode,
        liveSpectrumOptions.CalibrationId,
        // The offset only shapes the display in SPL; in relative it is irrelevant.
        RenderingSpl ? plotModelFactory.LiveSplOffsetDb : null,
        // Null (off) and a flat model (white noise, band-law-only compensation)
        // are different display transforms; the nullable keeps them distinct.
        plotModelFactory.LiveTiltModel);

    /// <summary>
    /// Clears the running average and peak-hold envelope without interrupting
    /// capture. Useful for the Infinite averaging preset.
    /// </summary>
    public void ResetAverage()
    {
        measurement.ResetAccumulation();
        lastDrawnFrameCount = -1;
        SuspendPeakHold();
    }

    public void ApplyDisplayOptions()
    {
        measurement.RefreshLiveAveraging();
        // An Infinite average never forgets, so applying display options restarts it —
        // the RTA behaviour this has always had. A spatial-average capture is exempt
        // in BOTH directions: there the accumulation is the measurement itself, and a
        // display checkbox must not be able to throw away minutes of walking the
        // microphone. Keyed on the mode, not on the stored speed, which in MMM is only
        // the user's remembered RTA preference and would decide this at random.
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

        // Any change to the display transform (scale, smoothing, mic correction, SPL
        // offset) makes the held display points incompatible; drop the envelope.
        if (CurrentPeakHoldKey() != renderedPeakHoldKey)
        {
            SuspendPeakHold();
        }

        // Rebuild the model even while running: display options such as the coherence
        // curve add or remove the coherence axis, and a running TimerTick would otherwise
        // attach the coherence series to a model that has no matching axis.
        RebuildModel();
    }

    // Pauses peak-hold tracking briefly so the noisy first frames captured while
    // the average ramps up from zero are not latched into the envelope.
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

    /// <summary>
    /// The protective high-pass the next run will be taken through, kept current from
    /// the measurement settings.
    /// </summary>
    /// <remarks>
    /// Its own entry point because the settings path that carries it does NOT always
    /// reach <see cref="ConfigureFrom"/>: an edit that leaves the audio session alone
    /// stops short of reconfiguring the analyzer, deliberately, since reconfiguring
    /// restarts a running one. A filter change must still arrive — it decides what a
    /// capture divides back out — and arriving here costs nothing and interrupts
    /// nothing.
    /// </remarks>
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

    /// <summary>
    /// Rebuilds the plot from the last displayed snapshot when the user returns
    /// to the Live Spectrum mode without restarting, so the curve, peak hold and
    /// overlays that were on screen reappear instead of an empty plot.
    /// </summary>
    /// <summary>
    /// Discards the remembered curve and peak-hold envelope so they are not
    /// restored after the plot is cleared.
    /// </summary>
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

    /// <summary>
    /// Discards the accumulated spectra and the remembered curve. The host calls
    /// this when an acquisition parameter (analysis mode, signal colour, window,
    /// FFT length, overlap) changes while the analyzer is STOPPED: the kept curve
    /// is a record of the previous setup, and the display transform reads the
    /// options live — redrawing old data under the new parameters would silently
    /// re-interpret it (the slope compensation, for one, would re-tilt a stopped
    /// pink RTA as if the excitation had been white). A running analyzer needs no
    /// call — its restart already begins a fresh accumulation.
    /// </summary>
    public void DiscardCapturedData()
    {
        measurement.ResetAccumulation();
        lastDrawnFrameCount = -1;
        // The loaded capture goes too: it was taken under the previous acquisition
        // parameters, and its recipe no longer describes what this analyzer would do.
        loadedCapture = null;
        ForgetLastCurve();
    }

    /// <summary>
    /// Drops display state that is incompatible with any calibration change.
    /// This must run even while another mode owns the visible plot.
    /// </summary>
    public void InvalidateCalibration()
    {
        SuspendPeakHold();
    }

    /// <summary>
    /// Reacts to any calibration change — an SPL anchor added/cleared, its offset
    /// re-measured, or a different microphone-correction file bound to the same mode.
    /// This runs in EVERY app mode, not only while Live Spectrum is visible, because
    /// the change makes the peak-hold envelope incompatible wherever the analyzer
    /// sits; the plot itself is rebuilt only when Live Spectrum is the visible mode.
    /// The capture is never touched: the signal follows the ANALYSIS MODE, not the
    /// calibration — a Silent RTA that loses SPL simply keeps running on the
    /// relative axis. Safe whether running or idle.
    /// </summary>
    public void RefreshCalibration()
    {
        InvalidateCalibration();

        if (getCurrentMode() == Mode.LiveSpectrum)
        {
            RebuildModel();
        }
    }

    // Forces the runtime signal to one the selected analysis mode actually offers, so
    // the stored NoiseColor never diverges from what the panel and the playback show.
    // Silent (an ambient RTA with no excitation) is the one mode-exclusive signal: a
    // transfer function has nothing to correlate against without an excitation, so
    // entering Transfer mode falls it back to periodic pink (the transfer reference).
    // Every real excitation is valid in both modes and is never touched.
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

    // Recreates the plot model (and therefore its axes) from the current options, redraws
    // the last snapshot, and restores overlays. Safe to call while running: the next
    // TimerTick simply renders onto the fresh model.
    private void RebuildModel()
    {
        // A loaded capture owns the plot until a run replaces it, so every path that
        // rebuilds the model redraws IT rather than the accumulation it stands in for.
        if (loadedCapture is { } document)
        {
            ShowLoadedCaptureModel(document);
            return;
        }

        PlotModel model = plotModelFactory.CreateLiveSpectrum();
        // Prefer a freshly computed snapshot so a scale switch picks up curves the
        // stored one may lack (e.g. the RTA when SPL is turned on): the accumulators
        // survive a stop, so this still works when the analyzer is paused. Fall back
        // to the last drawn snapshot when no accumulation is available.
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

        // With no loopback the analyzer runs as a single-channel RTA (mic auto-power)
        // instead of a dual-channel transfer function; both are valid, so starting is
        // no longer gated on a configured loopback.
        SuspendPeakHold();
        lastSnapshot = null;
        lastDrawnFrameCount = -1;
        // A new run is what replaces a loaded capture; until then it stays on screen.
        loadedCapture = null;
        plotViewports.Show(plotModelFactory.CreateLiveSpectrum(), getCurrentMode());
        overlayCollection.Show(getCurrentMode());
        // Frozen for the life of this accumulation, immediately before it begins: what
        // the curve divides out and what the saved recipe records are then the same
        // filter, and a setting edited mid-pass cannot re-tilt a walk already underway.
        measurement.SetCaptureProtectiveHighPass(configuredProtectiveHighPass);
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

        // The held capture keeps its read-out: what Save is about to store is the
        // accumulation this count describes, so it must stay on screen after the stop.
        UpdateCaptureProgressAnnotation(model);
        plotViewports.Show(model, getCurrentMode());
        updateOverlayAvailability();
        overlayCollection.Show(getCurrentMode());
        updateRecordButton();
        updatePlotLabels();
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        // Guard against re-entrancy if a redraw takes longer than the timer
        // interval. The measurement runs on background threads and is never
        // gated by this UI work, so a busy CPU only thins the display rate.
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

            // Redraw only what has actually changed. A snapshot clones the
            // accumulators under the data lock and rebuilds the whole display curve
            // from them, which is worth doing once per analysis FRAME — not thirty
            // times a second regardless. With the long frames a spatial average needs
            // (683 ms at 32768 and 48 kHz) a frame lands once in some forty ticks, so
            // the other thirty-nine would clone a quarter of a megabyte and re-render
            // it to an identical curve, contending with the audio thread for the lock
            // each time. The notices still follow every tick: an overload is a
            // shortage of frames, so gating it on new frames would silence it exactly
            // when it matters.
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
            // A live transfer through a padded loopback sits above 0 dB;
            // raise the default view to it (expand-only — a user zoom keeps
            // its own view state) so the trace is not drawn above the frame.
            PlotModelStyle.RaiseDecibelViewCeiling(model, LiveDisplayMaxDb());
            // Keep target overlays that track the current measurement in sync with
            // the freshly drawn live trace.
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

    // The loudest displayed point across the live series this controller owns.
    // Deliberately NOT a scan of the whole model: overlay series live in the
    // same model and must not steer the measurement view.
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
        // A reused series must never sit in two models at once: detach the set
        // from the previous model when the plot model has been rebuilt.
        if (attachedModel != null && !ReferenceEquals(attachedModel, model))
        {
            RemoveLiveSpectrumSeries(attachedModel);
        }
        attachedModel = model;

        // A view-only SPL plot draws no live curves: at raw (un-lifted) dBFS on the
        // absolute axis they would read as absurd sound-pressure levels. Say WHY the
        // curve is absent instead of leaving a silently empty plot. The notice is
        // managed here rather than at model creation so it appears only when a curve
        // really was suppressed (never on a plot that has nothing to show anyway).
        // Like the overload annotation, the instance is created per model and tracked
        // by Tag: an OxyPlot element belongs to ONE PlotModel, so a cached instance
        // would throw the moment a rebuilt model tried to adopt it while the
        // discarded model still held it. Remove-then-add keeps a live tick from
        // stacking duplicates and takes the notice down when view-only ends.
        RemoveSplViewOnlyAnnotation(model);
        if (SplViewOnly)
        {
            OverlayTextAnnotation notice = PlotModelFactory.CreateSplViewOnlyAnnotation(
                "No SPL calibration for the live input — showing dB SPL overlays only");
            notice.Tag = SplViewOnlyAnnotationTag;
            model.Annotations.Add(notice);
            return;
        }

        // The plot is the reference-free microphone (RTA) spectrum whenever the
        // effective analysis mode is RTA — selected, or forced by a capture with no
        // loopback at all (there is no transfer function to draw). In the RTA-only
        // views the transfer function and coherence are hidden, the RTA is forced
        // on, and the peak hold envelops it instead of the transfer curve.
        bool rtaOnly = RtaOnly;
        renderedPeakHoldKey = CurrentPeakHoldKey();

        if (liveSpectrumOptions.PeakHold)
        {
            double[]? peakSource = rtaOnly ? snapshot.InputMagnitude : snapshot.Magnitude;
            if (peakSource is { Length: > 0 })
            {
                // Envelope the DISPLAYED band curve, not the raw bins: in SPL the
                // display sums bin powers per band, so per-bin peaks summed later
                // would add maxima from different frames and overstate the band.
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
                    // Keep the trusted (above-threshold) curve as the canonical primary
                    // trace so the current-measurement target source uses it, not the
                    // low-coherence segment.
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

        // Reference-free RTA magnitude of the microphone input, overlaid on the
        // same dB axis. It is independent of coherence and the reference channel,
        // so it is never split or dimmed by the coherence threshold. In the RTA-only
        // views it is the only trace, forced on (and lifted to dB SPL by the factory).
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

        // Coherence describes the transfer-function estimate, which the RTA-only
        // views do not show, so it is drawn only alongside the transfer function.
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

    // Holds the per-band maximum of the displayed curve over time. The grid frequency
    // per index is stable across ticks, so a per-index max of the dB level is the peak
    // of the band level actually shown (a band level is monotone in its power).
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

    // Remove-then-add is how every one of these notices is kept in sync with a live
    // tick: it stops duplicates stacking up and takes the notice down again the
    // moment its condition ends.
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

    /// <summary>
    /// How much has been integrated, shown while MMM is the mode. A spatial average
    /// has no other progress: the curve stops visibly moving long before the average
    /// is actually settled, so without a count the only guide to "long enough" is the
    /// operator's patience.
    /// </summary>
    private void UpdateCaptureProgressAnnotation(PlotModel? model)
    {
        RemoveTaggedAnnotations(model, CaptureProgressAnnotationTag);
        if (model == null ||
            !plotModelFactory.EffectiveLiveAnalysisMode.IsSpatialAverageCapture())
        {
            return;
        }

        // A loaded capture reports what its own recipe records, not what this
        // analyzer happens to hold: the two are different measurements, and the file
        // is the one on screen.
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
            // While running, the live counter; once held, the count that belongs to
            // the snapshot Save will store, so the read-out and the file agree.
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

        // Kept between ticks, and its text rebuilt only when the reading changes:
        // this runs on the ~30 fps tick while the count it reports advances once a
        // frame — once a second at the frame lengths a spatial average uses — so a
        // fresh annotation and a fresh string each time is the allocation churn the
        // series above are pooled to avoid.
        //
        // A NEW one per model, though. OxyPlot refuses an element that still belongs
        // to another model, and every rebuild — a tab switch, a display option, a
        // loaded capture — builds a new one; carrying a single instance across them
        // threw on the add, which left the plot empty and surfaced later as an
        // unrelated-looking "element already belongs to a PlotModel" on the next
        // load. The reused series avoid this by detaching from the previous model
        // (see attachedModel); an annotation is cheap enough to simply not share.
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
                // A user stop cancels the capture and reports success; reaching
                // here with an error means the device or driver failed mid-run,
                // which must not reset the UI silently.
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
            // The handle was destroyed between the guard and the call while the
            // form closes; Dispose stops the timer.
        }
    }
}
