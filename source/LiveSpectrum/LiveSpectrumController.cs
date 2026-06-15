using OxyPlot;

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
        Action updateDrawButton)
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

        measurement.Init(44100, 24, 60, PlaybackChannel.Mono, 2048);
        measurement.Completed += MeasurementCompleted;
        timer.Tick += TimerTick;
    }

    public bool InProgress => measurement.InProgress;
    public bool TimerEnabled => timer.Enabled;

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
        overlayCollection.HideAll();
        _ = measurement.RunAsync();
        timer.Start();
        updateDrawButton();
    }

    private async Task StopAsync()
    {
        double[]? finalSnapshot = measurement.GetAccumulatedSpectrumSnapshot();
        timer.Stop();
        await measurement.AbortAsync();

        PlotModel model = plotModelFactory.CreateLiveSpectrum();
        if (finalSnapshot != null)
        {
            model.Series.Add(plotModelFactory.BuildNoiseSeries(finalSnapshot));
        }

        plotView.Model = model;
        updateOverlayAvailability();
        overlayCollection.Show(getCurrentMode());
        updateDrawButton();
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        double[]? snapshot = measurement.GetAccumulatedSpectrumSnapshot();
        PlotModel? model = plotView.Model;
        if (snapshot == null || model?.Title != "Live Spectrum")
        {
            return;
        }

        model.Series.Clear();
        model.Series.Add(plotModelFactory.BuildNoiseSeries(snapshot));
        model.InvalidatePlot(true);
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
        });
    }
}
