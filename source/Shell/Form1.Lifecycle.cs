using OxyPlot;

namespace Resonalyze;

public partial class Form1
{
    private async void buttonClear_Click(object sender, EventArgs e)
    {
        if (CurrentMode == Mode.LiveSpectrum &&
            liveSpectrumController.InProgress)
        {
            await liveSpectrumController.AbortAsync();
        }

        overlayCollection.HideAll();

        PlotModel? model = plotView1.Model;
        if (model == null)
        {
            UpdatePlotLabelsPanel();
            return;
        }

        model.Series.Clear();
        model.InvalidatePlot(true);
        plotView1.Refresh();
        UpdateClearButtonState();
        UpdateOverlayAvailability();
        UpdatePlotLabelsPanel();
    }

    private void UpdateMaximizedBounds()
    {
        Point center = new(
            Left + Math.Max(1, Width) / 2,
            Top + Math.Max(1, Height) / 2);
        MaximizedBounds = Screen.FromPoint(center).WorkingArea;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == ChromeTitleBarController.WmNcHitTest &&
            !titleBarController.IsCustomMaximized)
        {
            base.WndProc(ref m);
            if ((int)m.Result == ChromeTitleBarController.HtClient)
            {
                Point point = PointToClient(
                    ChromeTitleBarController.GetPointFromLParam(m.LParam));
                m.Result = ChromeTitleBarController.GetResizeHitTest(
                    point,
                    ClientSize);
            }
            return;
        }

        base.WndProc(ref m);
    }

    private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (closingPrepared)
        {
            return;
        }

        e.Cancel = true;
        Enabled = false;
        await Task.WhenAll(
            expSweepMeasurement.AbortAsync(),
            timeAlignmentController.AbortAsync(),
            liveSpectrumController.AbortAsync());

        DisposeAppResources();
        closingPrepared = true;
        BeginInvoke((MethodInvoker)Close);
    }

    private void DisposeAppResources()
    {
        if (resourcesDisposed)
        {
            return;
        }

        resourcesDisposed = true;
        dockedModeSettingsHost.Dispose();
        inputLevelMeterController.Dispose();
        expSweepMeasurement.Dispose();
        timeAlignmentController.Dispose();
        liveSpectrumController.Dispose();
    }
}
