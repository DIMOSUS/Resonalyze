using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Draws Live Spectrum into the main plot: the redraw loop while the analyzer runs, the held or loaded curve when it
/// does not, and the notices over them. What is shown and how lives in <see cref="LiveSpectrumSession"/>.
/// </summary>
/// <remarks>A mode view beside <see cref="AnalyzerPlot"/>: entering Live Spectrum draws what the session holds.</remarks>
internal sealed class LiveSpectrumController : IModeView, IDisposable
{
    private readonly Form owner;
    private readonly LiveSpectrumSession session;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    // The main plot, drawn into while Live Spectrum is its mode.
    private readonly AnalyzerPlot plot;
    private readonly OxyPlot.WindowsForms.PlotView plotView;
    // Through viewport memory so the user's zoom survives model rebuilds.
    private readonly PlotViewportMemory plotViewports;
    private readonly LiveSpectrumPlotFactory plotFactory;
    private readonly OverlaySession overlays;
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
    private bool disposed;
    private bool redrawInProgress;
    // Lets a tick with no new frame skip clone-and-render.
    private int lastDrawnFrameCount = -1;
    // Pooled per model: an OxyPlot element belongs to one model at a time.
    private OverlayTextAnnotation? captureProgressAnnotation;
    private PlotModel? captureProgressOwner;
    private string? captureProgressState;
    private int captureProgressFrames = -1;
    private int captureProgressClipped = -1;
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
        LiveSpectrumSession session,
        AnalyzerPlot plot)
    {
        this.owner = owner;
        this.session = session;
        this.plot = plot;
        plotView = plot.View;
        plotViewports = plot.Viewports;
        overlays = plot.Overlays;
        plotFactory = new LiveSpectrumPlotFactory(session.Curves);
        session.Completed += MeasurementCompleted;
        timer.Tick += TimerTick;
    }

    public bool TimerEnabled => timer.Enabled;

    /// <summary>Stops and harvests the final accumulation; <see cref="AbortAsync"/> would drop the newest frames.</summary>
    public async Task StopAndHoldAsync()
    {
        if (session.InProgress)
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

        session.ShowLoaded(document);
        RebuildModel();
    }

    // The loaded capture keeps its own scale: its levels mean what its capture-time anchor made them.
    private void ShowLoadedCaptureModel(LiveSpectrumDisplay display, LiveCaptureDocument document)
    {
        PlotModel model = LiveSpectrumPlotFactory.CreateModel(
            display, document.Recipe.MagnitudeScale);
        model.Series.Add(LiveSpectrumPlotFactory.BuildLoadedCaptureSeries(document));
        if (document.CurveDb.Length > 0)
        {
            PlotModelStyle.RaiseDecibelViewCeiling(model, document.CurveDb.Max());
        }

        UpdateCaptureProgressAnnotation(display, model);
        plotViewports.Show(model, plot.Mode);
        plot.UpdateOverlayAvailability();
        overlays.Show(plot.Mode);
        plot.RefreshLabels();
    }

    public void ResetAverage()
    {
        session.ResetAverage();
        lastDrawnFrameCount = -1;
    }

    public void ApplyDisplayOptions()
    {
        if (session.ApplyDisplayOptions())
        {
            lastDrawnFrameCount = -1;
        }

        // Rebuild even while running: coherence display adds/removes an axis.
        RebuildModel();
    }

    public async Task ReconfigureFromAsync(
        MeasurementSettingsFile.SweepMeasurementSettings measurementSettings)
    {
        bool restart = session.InProgress;
        if (restart)
        {
            await StopAsync();
        }

        session.Configure(measurementSettings);

        if (restart && plot.Mode == Mode.LiveSpectrum)
        {
            Start();
        }
    }

    public async Task ToggleAsync()
    {
        if (session.InProgress)
        {
            await StopAsync();
            return;
        }

        Start();
    }

    public async Task AbortAsync()
    {
        timer.Stop();
        await session.AbortAsync();
        plot.RefreshLabels();
    }

    /// <summary>Redraws what the session holds; a running analyzer redraws on its own clock.</summary>
    public void Redraw()
    {
        if (session.InProgress)
        {
            return;
        }

        RebuildModel();
    }

    public void Leave()
    {
    }

    public void Enter(ModeDescriptor mode)
    {
    }

    public void Present()
    {
        if (plot.Mode == Mode.LiveSpectrum)
        {
            Redraw();
        }
    }

    /// <summary>Discards the accumulation, the kept curve and a loaded capture while stopped: after an acquisition change,
    /// so old data is not re-interpreted under new parameters, and on New session.</summary>
    public void DiscardCapturedData()
    {
        session.Discard();
        lastDrawnFrameCount = -1;
        RemoveOverloadAnnotation(plotView.Model);
    }

    /// <summary>Reacts to any calibration change in every app mode; the capture keeps running (signal follows the analysis mode).</summary>
    public void RefreshCalibration()
    {
        // Runs even while another mode owns the plot: the envelope is invalid wherever the analyzer sits.
        session.InvalidateCalibration();

        if (plot.Mode == Mode.LiveSpectrum)
        {
            RebuildModel();
        }
    }

    private void RebuildModel()
    {
        LiveSpectrumDisplay display = session.Display;
        if (session.LoadedCapture is { } document)
        {
            ShowLoadedCaptureModel(display, document);
            return;
        }

        PlotModel model = LiveSpectrumPlotFactory.CreateModel(display);
        if (session.Reread(display) is { } snapshot)
        {
            AddLiveSpectrumSeries(display, model, snapshot);
            PlotModelStyle.RaiseDecibelViewCeiling(model, LiveDisplayMaxDb());
        }

        plotViewports.Show(model, plot.Mode);
        plot.UpdateOverlayAvailability();
        overlays.Show(plot.Mode);
        plot.RefreshLabels();
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
        session.Completed -= MeasurementCompleted;
    }

    private void Start()
    {
        session.Start();
        lastDrawnFrameCount = -1;
        plotViewports.Show(LiveSpectrumPlotFactory.CreateModel(session.Display), plot.Mode);
        overlays.Show(plot.Mode);
        timer.Start();
        plot.RefreshLabels();
    }

    private async Task StopAsync()
    {
        timer.Stop();
        LiveSpectrumSnapshot? finalSnapshot = await session.StopAsync();

        LiveSpectrumDisplay display = session.Display;
        PlotModel model = LiveSpectrumPlotFactory.CreateModel(display);
        if (finalSnapshot != null)
        {
            AddLiveSpectrumSeries(display, model, finalSnapshot);
            PlotModelStyle.RaiseDecibelViewCeiling(model, LiveDisplayMaxDb());
        }

        UpdateCaptureProgressAnnotation(display, model);
        plotViewports.Show(model, plot.Mode);
        plot.UpdateOverlayAvailability();
        overlays.Show(plot.Mode);
        plot.RefreshLabels();
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
            if (model == null || plot.Mode != Mode.LiveSpectrum)
            {
                return;
            }

            LiveSpectrumDisplay display = session.Display;
            // Re-render only on a new analysis frame (a 683 ms frame spans ~20 ticks of 33 ms); notices still update every tick. See docs/tech/live-spectrum.md#redraw-loop.
            int frames = session.AveragedFrameCount;
            if (frames == lastDrawnFrameCount && session.HeldSnapshot != null)
            {
                UpdateOverloadAnnotation(model);
                UpdateCaptureProgressAnnotation(display, model);
                model.InvalidatePlot(false);
                return;
            }

            if (session.ReadFrame(display) is not { } snapshot)
            {
                return;
            }

            lastDrawnFrameCount = frames;

            RemoveLiveSpectrumSeries(model);
            AddLiveSpectrumSeries(display, model, snapshot);
            // A padded loopback puts the transfer above 0 dB; expand-only ceiling raise.
            PlotModelStyle.RaiseDecibelViewCeiling(model, LiveDisplayMaxDb());
            overlays.RefreshCurrentMeasurementTargets();
            UpdateOverloadAnnotation(model);
            UpdateCaptureProgressAnnotation(display, model);
            model.InvalidatePlot(true);
            plot.RefreshLabels();
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

    private void AddLiveSpectrumSeries(
        LiveSpectrumDisplay display,
        PlotModel model,
        LiveSpectrumSnapshot snapshot)
    {
        LiveSpectrumOptions options = display.Options;
        // A reused series must never sit in two models at once.
        if (attachedModel != null && !ReferenceEquals(attachedModel, model))
        {
            RemoveLiveSpectrumSeries(attachedModel);
        }
        attachedModel = model;

        // View-only SPL: explain the missing curve. Created per model (OxyPlot element ownership); remove-then-add.
        RemoveSplViewOnlyAnnotation(model);
        if (display.SplViewOnly)
        {
            OverlayTextAnnotation notice = PlotModelFactory.CreateSplViewOnlyAnnotation(
                "No SPL calibration for the live input — showing dB SPL overlays only");
            notice.Tag = SplViewOnlyAnnotationTag;
            model.Annotations.Add(notice);
            return;
        }

        bool rtaOnly = display.RtaOnly;
        LivePeakHold peakHold = session.PeakHold;
        peakHold.Drawn(display.PeakHoldKey);

        if (options.PeakHold)
        {
            double[]? peakSource = rtaOnly ? snapshot.InputMagnitude : snapshot.Magnitude;
            if (peakSource is { Length: > 0 })
            {
                // Envelope the displayed band curve: per-bin peaks from different frames would overstate the band.
                peakHold.Hold(plotFactory.MainDisplayPoints(display, peakSource, rtaOnly));
            }
            else
            {
                peakHold.Clear();
            }

            if (peakHold.Points is { } peakHoldPoints)
            {
                if (peakHoldSeries == null)
                {
                    peakHoldSeries = LiveSpectrumPlotFactory.BuildPeakHoldSeries(display, peakHoldPoints);
                    peakHoldSeries.Tag = LiveSpectrumPeakHoldTag;
                }
                else
                {
                    LiveSpectrumPlotFactory.UpdatePeakHoldSeries(display, peakHoldSeries, peakHoldPoints);
                }
                model.Series.Add(peakHoldSeries);
            }
        }

        if (!rtaOnly && options.ShowMainCurve)
        {
            if (snapshot.Coherence != null &&
                options.CoherenceThresholdPercent > 0)
            {
                if (trustedSeries == null || untrustedSeries == null)
                {
                    (trustedSeries, untrustedSeries) =
                        plotFactory.BuildCoherenceSplitSeries(
                            display,
                            snapshot.Magnitude,
                            snapshot.Coherence,
                            options.CoherenceThresholdPercent);
                    // The trusted segment stays primary so the current-measurement target uses it.
                    untrustedSeries.Tag = LiveSpectrumLowCoherenceTag;
                    trustedSeries.Tag = LiveSpectrumTag;
                }
                else
                {
                    plotFactory.UpdateCoherenceSplitSeries(
                        display,
                        trustedSeries,
                        untrustedSeries,
                        snapshot.Magnitude,
                        snapshot.Coherence,
                        options.CoherenceThresholdPercent);
                }
                model.Series.Add(untrustedSeries);
                model.Series.Add(trustedSeries);
            }
            else
            {
                if (mainSeries == null)
                {
                    mainSeries = plotFactory.BuildTransferSeries(display, snapshot.Magnitude);
                    mainSeries.Tag = LiveSpectrumTag;
                }
                else
                {
                    plotFactory.UpdateTransferSeries(display, mainSeries, snapshot.Magnitude);
                }
                model.Series.Add(mainSeries);
            }
        }

        if (snapshot.InputMagnitude != null &&
            (options.ShowInputMagnitude || rtaOnly))
        {
            if (inputMagnitudeSeries == null)
            {
                inputMagnitudeSeries =
                    plotFactory.BuildInputMagnitudeSeries(display, snapshot.InputMagnitude);
                inputMagnitudeSeries.Tag = LiveSpectrumInputMagnitudeTag;
            }
            else
            {
                plotFactory.UpdateInputMagnitudeSeries(
                    display, inputMagnitudeSeries, snapshot.InputMagnitude);
            }
            model.Series.Add(inputMagnitudeSeries);
        }

        if (!rtaOnly && snapshot.Coherence != null && options.ShowCoherence)
        {
            if (coherenceSeries == null)
            {
                coherenceSeries = LiveSpectrumPlotFactory.BuildCoherenceSeries(display, snapshot.Coherence);
                coherenceSeries.Tag = LiveSpectrumCoherenceTag;
            }
            else
            {
                plotFactory.UpdateCoherenceSeries(display, coherenceSeries, snapshot.Coherence);
            }
            model.Series.Add(coherenceSeries);
        }
    }

    private void UpdateOverloadAnnotation(PlotModel model)
    {
        RemoveOverloadAnnotation(model);

        if (!session.HasRecentDrops)
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
            TextColor = UiPalette.Warning.ToOxy(),
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

    private void UpdateCaptureProgressAnnotation(LiveSpectrumDisplay display, PlotModel? model)
    {
        RemoveTaggedAnnotations(model, CaptureProgressAnnotationTag);
        if (model == null || session.Progress(display) is not { } progress)
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
                TextPosition = new DataPoint(0, 0),
                OffsetX = PlotZoomButtons.LeftPairClearance,
                TextFlowDirection = TextFlowDirection.TopDown,
                FontSize = 12,
                TextColor = UiPalette.TextSecondary.ToOxy(),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left
            };
            captureProgressOwner = model;
            captureProgressState = null;
        }

        if (progress.State != captureProgressState ||
            progress.Frames != captureProgressFrames ||
            progress.ClippedFrames != captureProgressClipped)
        {
            captureProgressState = progress.State;
            captureProgressFrames = progress.Frames;
            captureProgressClipped = progress.ClippedFrames;
            captureProgressAnnotation.Text = progress.Text;
            captureProgressAnnotation.TextColor = progress.ClippedFrames > 0
                ? UiPalette.Warning.ToOxy()
                : UiPalette.TextSecondary.ToOxy();
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
                plot.UpdateOverlayAvailability();
                plot.RefreshLabels();
                session.RunEnded(success);
            });
        }
        catch (InvalidOperationException)
        {
        }
    }
}
