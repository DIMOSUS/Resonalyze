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
            // Cancelling the close during OS shutdown makes Windows report the app
            // as blocking shutdown, and the process may be killed before the async
            // teardown finishes. Persist user data synchronously and let the close
            // proceed; device teardown is left to the OS.
            closingPrepared = true;
            startupAudioWarmupCancellation?.Cancel();
            FlushMeasurementSettings();
            overlayCollection.FlushPendingSaves();
            PersistCurrentSessionState();
            return;
        }

        e.Cancel = true;
        if (closingInProgress)
        {
            // A second close request while the aborts are still awaited would
            // re-run the whole teardown; the first pass finishes the close.
            return;
        }

        closingInProgress = true;
        Enabled = false;
        FlushMeasurementSettings();
        overlayCollection.FlushPendingSaves();
        PersistCurrentSessionState();
        startupAudioWarmupCancellation?.Cancel();
        await Task.WhenAll(
            expSweepMeasurement.AbortAsync(),
            timeAlignmentController.AbortAsync(),
            liveSpectrumController.AbortAsync(),
            startupAudioWarmupTask ?? Task.CompletedTask);

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
        startupAudioWarmupCancellation?.Cancel();
        startupAudioWarmupCancellation?.Dispose();
        compareMenuStrip?.Dispose();
        dockedModeSettingsHost.Dispose();
        dockedMeasurementSettingsHost.Dispose();
        dockedHistoryHost.Dispose();
        measurementSettingsSaveTimer.Stop();
        measurementSettingsSaveTimer.Dispose();
        recordButtonLongPressTimer.Stop();
        recordButtonLongPressTimer.Dispose();
        inputLevelMeterController.Dispose();
        expSweepMeasurement.Dispose();
        timeAlignmentController.Dispose();
        liveSpectrumController.Dispose();
    }
}
