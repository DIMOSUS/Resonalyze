using OxyPlot;
using OxyPlot.Series;

namespace Resonalyze;

internal sealed class LiveSpectrumController : IDisposable
{
    private readonly Form owner;
    private readonly NoiseMeasurement measurement;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
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
    private const string LiveSpectrumTag = "live-spectrum:primary";
    private bool disposed;

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
        Action updatePlotLabels)
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

        measurement.Init(44100, 24, 60, PlaybackChannel.Mono, 2048);
        measurement.Completed += MeasurementCompleted;
        timer.Tick += TimerTick;
    }

    public bool InProgress => measurement.InProgress;
    public bool TimerEnabled => timer.Enabled;

    public void ConfigureFrom(ExpSweepMeasurement measurementSource)
    {
        measurement.Init(
            measurementSource.SampleRate,
            measurementSource.Bits,
            60,
            measurementSource.PlaybackChannel,
            2048,
            measurementSource.OutputDeviceNumber,
            measurementSource.InputDeviceNumber,
            measurementSource.AudioBackend,
            measurementSource.AsioDriverName,
            measurementSource.AsioInputChannelOffset,
            measurementSource.AsioOutputChannelOffset);
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
        double[]? finalSnapshot = measurement.GetAccumulatedSpectrumSnapshot();
        timer.Stop();
        await measurement.AbortAsync();

        PlotModel model = plotModelFactory.CreateLiveSpectrum();
        if (finalSnapshot != null)
        {
            LineSeries series = plotModelFactory.BuildNoiseSeries(finalSnapshot);
            series.Tag = LiveSpectrumTag;
            model.Series.Add(series);
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
        double[]? snapshot = measurement.GetAccumulatedSpectrumSnapshot();
        PlotModel? model = plotView.Model;
        if (snapshot == null || model?.Title != "Live Spectrum")
        {
            return;
        }

        RemoveLiveSpectrumSeries(model);
        LineSeries series = plotModelFactory.BuildNoiseSeries(snapshot);
        series.Tag = LiveSpectrumTag;
        model.Series.Add(series);
        model.InvalidatePlot(true);
        updatePlotLabels();
    }

    private static void RemoveLiveSpectrumSeries(PlotModel model)
    {
        List<OxyPlot.Series.Series> liveSpectrumSeries = model.Series
            .Where(series => Equals(series.Tag, LiveSpectrumTag))
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
