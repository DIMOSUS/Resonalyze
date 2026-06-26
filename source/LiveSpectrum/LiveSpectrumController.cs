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
    private readonly Action updateDrawButton;
    private readonly Action updateRecordButton;
    private readonly Action updateClearButton;
    private readonly Action updatePlotLabels;
    private readonly LiveSpectrumOptions liveSpectrumOptions;
    private const string LiveSpectrumTag = "live-spectrum:primary";
    private const string LiveSpectrumCoherenceTag = "live-spectrum:coherence";
    private bool disposed;
    private bool redrawInProgress;

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
        Action updateDrawButton,
        Action updateRecordButton,
        Action updateClearButton,
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
        this.updateDrawButton = updateDrawButton;
        this.updateRecordButton = updateRecordButton;
        this.updateClearButton = updateClearButton;
        this.updatePlotLabels = updatePlotLabels;
        this.liveSpectrumOptions = liveSpectrumOptions;
        measurement.Completed += MeasurementCompleted;
        timer.Tick += TimerTick;
    }

    public bool InProgress => measurement.InProgress;
    public bool TimerEnabled => timer.Enabled;

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

        updateDrawButton();
        updateRecordButton();
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

        plotView.Model = plotModelFactory.CreateLiveSpectrum();
        overlaysPanel.Enabled = false;
        overlayCollection.Show(getCurrentMode());
        _ = measurement.RunAsync();
        timer.Start();
        updateDrawButton();
        updateRecordButton();
        updateClearButton();
        updatePlotLabels();
    }

    private async Task StopAsync()
    {
        LiveSpectrumSnapshot? finalSnapshot = measurement.GetAccumulatedSpectrumSnapshot();
        timer.Stop();
        await measurement.AbortAsync();

        PlotModel model = plotModelFactory.CreateLiveSpectrum();
        if (finalSnapshot != null)
        {
            AddLiveSpectrumSeries(model, finalSnapshot);
        }

        plotView.Model = model;
        updateOverlayAvailability();
        overlayCollection.Show(getCurrentMode());
        updateDrawButton();
        updateRecordButton();
        updateClearButton();
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

            RemoveLiveSpectrumSeries(model);
            AddLiveSpectrumSeries(model, snapshot);
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
        LineSeries series = plotModelFactory.BuildNoiseSeries(snapshot.Magnitude);
        series.Tag = LiveSpectrumTag;
        model.Series.Add(series);

        if (snapshot.Coherence != null)
        {
            LineSeries coherenceSeries =
                plotModelFactory.BuildCoherenceSeries(snapshot.Coherence);
            coherenceSeries.Tag = LiveSpectrumCoherenceTag;
            model.Series.Add(coherenceSeries);
        }
    }

    private static void RemoveLiveSpectrumSeries(PlotModel model)
    {
        List<OxyPlot.Series.Series> liveSpectrumSeries = model.Series
            .Where(series =>
                Equals(series.Tag, LiveSpectrumTag) ||
                Equals(series.Tag, LiveSpectrumCoherenceTag))
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
            updateDrawButton();
            updateRecordButton();
            updateClearButton();
            updatePlotLabels();
        });
    }
}
