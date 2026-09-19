namespace Resonalyze;

public partial class Form1
{
    private void UpdateMaximizedBounds()
    {
        Point center = new(
            Left + Math.Max(1, Width) / 2,
            Top + Math.Max(1, Height) / 2);
        MaximizedBounds = Screen.FromPoint(center).WorkingArea;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == ChromeTitleBar.WmNcHitTest &&
            !chromeTitleBar.IsCustomMaximized &&
            WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if ((int)m.Result == ChromeTitleBar.HtClient)
            {
                Point point = PointToClient(
                    ChromeTitleBar.GetPointFromLParam(m.LParam));
                m.Result = ChromeTitleBar.GetResizeHitTest(
                    point,
                    ClientSize,
                    chromeTitleBar.ScaledResizeGripSize);
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

        if (e.CloseReason == CloseReason.WindowsShutDown)
        {
            // Cancelling during OS shutdown reports the app as blocking; persist synchronously and leave device teardown to the OS.
            closingPrepared = true;
            shutdownFastClose = true;
            startupAudioWarmup.Cancel();
            FlushMeasurementSettings();
            analyzerPlot.Overlays.FlushPendingSaves();
            sessionTracker.PersistCurrentSessionState();
            return;
        }

        e.Cancel = true;
        if (closingInProgress)
        {
            // A second close during the awaited aborts would re-run teardown.
            return;
        }

        closingInProgress = true;
        Enabled = false;
        FlushMeasurementSettings();
        analyzerPlot.Overlays.FlushPendingSaves();
        sessionTracker.PersistCurrentSessionState();
        startupAudioWarmup.Cancel();
        await Task.WhenAll(
            expSweepMeasurement.AbortAsync(),
            timeAlignmentController.AbortAsync(),
            liveSpectrumController.AbortAsync(),
            startupAudioWarmup.WaitAsync());

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
        if (shutdownFastClose)
        {
            // OS shutdown: Dispose waits on in-flight work and would stall; cancelling is enough.
            startupAudioWarmup.Cancel();
            return;
        }

        startupAudioWarmup.Dispose();
        compareMenuStrip?.Dispose();
        dockedModeSettingsHost.Dispose();
        dockedMeasurementSettingsHost.Dispose();
        dockedHistoryHost.Dispose();
        measurementSettingsSaver.Dispose();
        recordButtonLongPress.Dispose();
        inputLevelMeterController.Dispose();
        expSweepMeasurement.Dispose();
        timeAlignmentController.Dispose();
        liveSpectrumController.Dispose();
    }
}
