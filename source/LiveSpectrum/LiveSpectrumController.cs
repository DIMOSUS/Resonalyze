using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed class LiveSpectrumController : IDisposable
{
    private readonly Form owner;
    private readonly NoiseMeasurement measurement;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private readonly OxyPlot.WindowsForms.PlotView plotView;
    private readonly PlotModelFactory plotModelFactory;
    private readonly Panel overlaysPanel;
    private readonly OverlayCollection overlayCollection;
    private readonly Func<Mode> getCurrentMode;
    private readonly Func<Task> selectLiveSpectrumAsync;
    private readonly Action updateOverlayAvailability;
    private readonly Action updateRecordButton;
    private readonly Action updatePlotLabels;
    private readonly LiveSpectrumOptions liveSpectrumOptions;
    private const string LiveSpectrumTag = "live-spectrum:primary";
    private const string LiveSpectrumCoherenceTag = "live-spectrum:coherence";
    private const string LiveSpectrumPeakHoldTag = "live-spectrum:peak-hold";
    private const string OverloadAnnotationTag = "live-spectrum:overload";
    private const long PeakHoldSuppressionMs = 1000;
    private bool disposed;
    private bool redrawInProgress;
    private double[]? peakHoldMagnitude;
    private long peakHoldResumeTick;
    private LiveSpectrumSnapshot? lastSnapshot;

    public LiveSpectrumController(
        Form owner,
        NoiseMeasurement measurement,
        OxyPlot.WindowsForms.PlotView plotView,
        PlotModelFactory plotModelFactory,
        Panel overlaysPanel,
        OverlayCollection overlayCollection,
        Func<Mode> getCurrentMode,
        Func<Task> selectLiveSpectrumAsync,
        Action updateOverlayAvailability,
        Action updateRecordButton,
        Action updatePlotLabels,
        LiveSpectrumOptions liveSpectrumOptions)
    {
        this.owner = owner;
        this.measurement = measurement;
        this.plotView = plotView;
        this.plotModelFactory = plotModelFactory;
        this.overlaysPanel = overlaysPanel;
        this.overlayCollection = overlayCollection;
        this.getCurrentMode = getCurrentMode;
        this.selectLiveSpectrumAsync = selectLiveSpectrumAsync;
        this.updateOverlayAvailability = updateOverlayAvailability;
        this.updateRecordButton = updateRecordButton;
        this.updatePlotLabels = updatePlotLabels;
        this.liveSpectrumOptions = liveSpectrumOptions;
        measurement.Completed += MeasurementCompleted;
        timer.Tick += TimerTick;
    }

    public bool InProgress => measurement.InProgress;
    public bool TimerEnabled => timer.Enabled;

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
            peakHoldMagnitude = null;
        }

        if (!measurement.InProgress)
        {
            RestoreLastCurve();
        }
    }

    // Pauses peak-hold tracking briefly so the noisy first frames captured while
    // the average ramps up from zero are not latched into the envelope.
    private void SuspendPeakHold()
    {
        peakHoldMagnitude = null;
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
            liveSpectrumOptions);
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
        peakHoldMagnitude = null;
        RemoveOverloadAnnotation(plotView.Model);
    }

    public void RestoreLastCurve()
    {
        if (measurement.InProgress)
        {
            return;
        }

        PlotModel model = plotModelFactory.CreateLiveSpectrum();
        if (lastSnapshot != null)
        {
            AddLiveSpectrumSeries(model, lastSnapshot);
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
        if (liveSpectrumOptions.Mode == LiveSpectrumMode.TransferFunction &&
            !measurement.HasConfiguredLoopback)
        {
            MessageBox.Show(
                owner,
                "Live Transfer Function requires a configured loopback input.\r\n\r\n" +
                "Open Record Settings and select a Wave or ASIO loopback channel.",
                "Loopback required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            updateRecordButton();
            return;
        }

        SuspendPeakHold();
        lastSnapshot = null;
        plotView.Model = plotModelFactory.CreateLiveSpectrum();
        overlaysPanel.Enabled = false;
        overlayCollection.Show(getCurrentMode());
        _ = measurement.RunAsync();
        timer.Start();
        updateRecordButton();
        updatePlotLabels();
    }

    private async Task StopAsync()
    {
        LiveSpectrumSnapshot? finalSnapshot = measurement.GetAccumulatedSpectrumSnapshot();
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
            LiveSpectrumSnapshot? snapshot = measurement.GetAccumulatedSpectrumSnapshot();
            PlotModel? model = plotView.Model;
            if (snapshot == null || model == null || getCurrentMode() != Mode.LiveSpectrum)
            {
                return;
            }

            lastSnapshot = snapshot;
            RemoveLiveSpectrumSeries(model);
            AddLiveSpectrumSeries(model, snapshot);
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
        if (liveSpectrumOptions.PeakHold)
        {
            UpdatePeakHold(snapshot.Magnitude);
            if (peakHoldMagnitude != null)
            {
                LineSeries peakHoldSeries =
                    plotModelFactory.BuildPeakHoldSeries(peakHoldMagnitude);
                peakHoldSeries.Tag = LiveSpectrumPeakHoldTag;
                model.Series.Add(peakHoldSeries);
            }
        }

        if (liveSpectrumOptions.ShowMainCurve)
        {
            if (snapshot.Coherence != null &&
                liveSpectrumOptions.CoherenceThresholdPercent > 0)
            {
                (LineSeries trusted, LineSeries untrusted) =
                    plotModelFactory.BuildNoiseSeriesSegmented(
                        snapshot.Magnitude,
                        snapshot.Coherence,
                        liveSpectrumOptions.CoherenceThresholdPercent);
                untrusted.Tag = LiveSpectrumTag;
                trusted.Tag = LiveSpectrumTag;
                model.Series.Add(untrusted);
                model.Series.Add(trusted);
            }
            else
            {
                LineSeries series = plotModelFactory.BuildNoiseSeries(snapshot.Magnitude);
                series.Tag = LiveSpectrumTag;
                model.Series.Add(series);
            }
        }

        if (snapshot.Coherence != null && liveSpectrumOptions.ShowCoherence)
        {
            LineSeries coherenceSeries =
                plotModelFactory.BuildCoherenceSeries(snapshot.Coherence);
            coherenceSeries.Tag = LiveSpectrumCoherenceTag;
            model.Series.Add(coherenceSeries);
        }
    }

    private void UpdatePeakHold(double[] magnitude)
    {
        if (Environment.TickCount64 < peakHoldResumeTick)
        {
            return;
        }

        if (peakHoldMagnitude == null || peakHoldMagnitude.Length != magnitude.Length)
        {
            peakHoldMagnitude = (double[])magnitude.Clone();
            return;
        }

        for (int i = 0; i < peakHoldMagnitude.Length; i++)
        {
            if (magnitude[i] > peakHoldMagnitude[i])
            {
                peakHoldMagnitude[i] = magnitude[i];
            }
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
                Equals(series.Tag, LiveSpectrumCoherenceTag) ||
                Equals(series.Tag, LiveSpectrumPeakHoldTag))
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

        owner.BeginInvoke((MethodInvoker)delegate
        {
            timer.Stop();
            updateOverlayAvailability();
            updateRecordButton();
            updatePlotLabels();
        });
    }
}
