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
    // The live transfer-function curve carries a CurveTag (like every analysis curve)
    // so overlays can bind to it by key; the remaining live-spectrum helper series stay
    // string-tagged for internal bookkeeping only.
    private static readonly CurveTag LiveSpectrumTag =
        new(Mode.LiveSpectrum, AnalysisCurveKind.Primary, CurveSource.Main);
    private const string LiveSpectrumLowCoherenceTag = "live-spectrum:low-coherence";
    private const string LiveSpectrumCoherenceTag = "live-spectrum:coherence";
    private const string LiveSpectrumPeakHoldTag = "live-spectrum:peak-hold";
    private const string LiveSpectrumInputMagnitudeTag = "live-spectrum:input-magnitude";
    private const string OverloadAnnotationTag = "live-spectrum:overload";
    private const long PeakHoldSuppressionMs = 1000;
    private bool disposed;
    private bool redrawInProgress;
    // The peak-hold envelope is held over the DISPLAYED band curve (freq, dB), not the
    // raw FFT bins, so the SPL band power it shows is the peak of a real band level.
    private List<SignalPoint>? peakHoldPoints;
    private long peakHoldResumeTick;
    private LiveSpectrumSnapshot? lastSnapshot;
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

    // Whether the plot is currently rendering the absolute dB SPL (RTA) view. SPL is
    // only effective when it is both selected and backed by a matching calibration,
    // so this reflects what will actually be drawn — never a stale-calibration request.
    private bool RenderingSpl =>
        plotModelFactory.EffectiveLiveSpectrumScale == MagnitudeScale.SoundPressureLevel;

    // The plot shows only the reference-free RTA (no transfer function or coherence)
    // when the SPL view is active OR the capture has no loopback reference at all.
    private bool RtaOnly => RenderingSpl || measurement.IsRtaCapture;

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
        MicrophoneCalibrationMode CalibrationMode,
        double? SplOffsetDb);

    private PeakHoldDisplayKey renderedPeakHoldKey;

    private PeakHoldDisplayKey CurrentPeakHoldKey() => new(
        RenderingSpl ? MagnitudeScale.SoundPressureLevel : MagnitudeScale.Relative,
        RtaOnly,
        liveSpectrumOptions.SmoothingInverseOctaves,
        liveSpectrumOptions.CalibrationMode,
        // The offset only shapes the display in SPL; in relative it is irrelevant.
        RenderingSpl ? plotModelFactory.LiveSplOffsetDb : null);

    /// <summary>
    /// Clears the running average and peak-hold envelope without interrupting
    /// capture. Useful for the Infinite averaging preset.
    /// </summary>
    public void ResetAverage()
    {
        measurement.ResetAccumulation();
        SuspendPeakHold();
    }

    public void ApplyDisplayOptions()
    {
        measurement.RefreshLiveAveraging();
        if (liveSpectrumOptions.AveragingSpeed == AveragingSpeed.Infinite)
        {
            measurement.ResetAccumulation();
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

    public void ConfigureFrom(MeasurementSettingsFile.SweepMeasurementSettings measurementSettings)
    {
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
    /// This runs in EVERY app mode, not only while Live Spectrum is visible, because a
    /// calibration change can force the signal either way — losing SPL normalizes a
    /// <see cref="NoiseColor.Silent"/> RTA back to a real excitation, gaining it drops a
    /// stale periodic-pink excitation back to Silent — and drops the now-incompatible
    /// peak-hold envelope wherever the analyzer sits. It rebuilds the plot only when Live Spectrum is the visible
    /// mode (a running one is restarted if a Silent capture must fall back; an idle one
    /// simply re-renders). Returns whether the live signal type changed, so the caller
    /// can persist the normalized options. Safe whether running or idle.
    /// </summary>
    public async Task<bool> RefreshCalibrationAsync()
    {
        InvalidateCalibration();

        // A running capture whose signal no longer fits the effective scale must be
        // restarted: a Silent RTA that lost SPL needs a real excitation, and a periodic
        // pink capture that just gained SPL must drop its now-pointless excitation for
        // the ambient mic-only RTA. Either way the playback and analysis path change, so
        // stop it, normalize the signal, and restart on the corrected one.
        bool restart = measurement.InProgress && SignalNeedsNormalization();
        if (restart)
        {
            await StopAsync();
        }

        bool signalChanged = NormalizeSilentSignal();

        if (restart && getCurrentMode() == Mode.LiveSpectrum)
        {
            await StartAsync();
            return signalChanged;
        }

        // Only the VISIBLE Live Spectrum rebuilds its model here; in another mode the
        // normalization above already removed the stale Silent, and the model is rebuilt
        // when Live Spectrum next becomes visible (RestoreLastCurve).
        if (getCurrentMode() == Mode.LiveSpectrum)
        {
            RebuildModel();
        }

        return signalChanged;
    }

    // Forces the runtime signal to one the effective scale actually offers, so the
    // stored NoiseColor never diverges from what the panel and the playback show. The
    // two scale-exclusive signals swap symmetrically: Silent (an ambient RTA with no
    // excitation) is SPL-only, so off SPL it falls back to periodic pink; periodic pink
    // (the transfer-function reference) is relative-only, so under the reference-free
    // SPL RTA it falls back to Silent — which also restores the original signal after a
    // Silent→pink calibration-loss round-trip. The shared Pink/Brown/White colours are
    // valid on both scales and are never touched.
    internal static bool NormalizeSignalType(
        LiveSpectrumOptions options,
        MagnitudeScale effectiveScale)
    {
        NoiseColor normalized = NormalizedSignalType(options.NoiseColor, effectiveScale);
        if (normalized == options.NoiseColor)
        {
            return false;
        }

        options.NoiseColor = normalized;
        return true;
    }

    private static NoiseColor NormalizedSignalType(
        NoiseColor color,
        MagnitudeScale effectiveScale)
    {
        bool spl = effectiveScale == MagnitudeScale.SoundPressureLevel;
        if (color == NoiseColor.Silent && !spl)
        {
            return NoiseColor.PinkPeriodic;
        }

        if (color == NoiseColor.PinkPeriodic && spl)
        {
            return NoiseColor.Silent;
        }

        return color;
    }

    // Whether the current runtime signal is not valid for the effective scale and would
    // be swapped by NormalizeSignalType. A running measurement must restart when it is,
    // because either direction changes the playback (pink excitation ↔ zero) and the
    // analysis path (transfer vs. mic-only RTA).
    private bool SignalNeedsNormalization() =>
        NormalizedSignalType(
            liveSpectrumOptions.NoiseColor,
            plotModelFactory.EffectiveLiveSpectrumScale) != liveSpectrumOptions.NoiseColor;

    private bool NormalizeSilentSignal()
    {
        bool changed = NormalizeSignalType(
            liveSpectrumOptions,
            plotModelFactory.EffectiveLiveSpectrumScale);
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
        }

        plotView.Model = model;
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
        plotView.Model = plotModelFactory.CreateLiveSpectrum();
        overlayCollection.Show(getCurrentMode());
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
        }

        plotView.Model = model;
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
            LiveSpectrumSnapshot? snapshot = measurement.GetAccumulatedSpectrumSnapshot(
                NeedsInputMagnitude);
            PlotModel? model = plotView.Model;
            if (snapshot == null || model == null || getCurrentMode() != Mode.LiveSpectrum)
            {
                return;
            }

            lastSnapshot = snapshot;
            RemoveLiveSpectrumSeries(model);
            AddLiveSpectrumSeries(model, snapshot);
            // Keep target overlays that track the current measurement in sync with
            // the freshly drawn live trace.
            overlayCollection.RefreshCurrentMeasurementTargets();
            UpdateOverloadAnnotation(model);
            model.InvalidatePlot(true);
            updatePlotLabels();
        }
        finally
        {
            redrawInProgress = false;
        }
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

        // The plot is the reference-free microphone (RTA) spectrum whenever the SPL
        // view is active (the transfer function has no scalar SPL under noise) or the
        // capture has no loopback at all (there is no transfer function to draw). In
        // those RTA-only views the transfer function and coherence are hidden, the RTA
        // is forced on, and the peak hold envelops it instead of the transfer curve.
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

    private static void RemoveOverloadAnnotation(PlotModel? model)
    {
        if (model == null)
        {
            return;
        }

        for (int index = model.Annotations.Count - 1; index >= 0; index--)
        {
            if (model.Annotations[index] is OverlayTextAnnotation { Tag: OverloadAnnotationTag })
            {
                model.Annotations.RemoveAt(index);
            }
        }
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
                // which used to reset the UI silently.
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
