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
            SaveForExit(report: false);
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
        SaveForExit(report: true);
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

    // A save that throws must not strand the close half done (cancelled, disabled, refusing another try): it is reported and the close goes on.
    private void SaveForExit(bool report)
    {
        var failures = new List<string>();
        foreach (Action save in new Action[]
                 {
                     FlushMeasurementSettings,
                     analyzerPlot.Overlays.FlushPendingSaves,
                     sessionTracker.PersistCurrentSessionState
                 })
        {
            try
            {
                save();
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
            }
        }

        if (report && failures.Count > 0)
        {
            MessageBox.Show(
                this,
                "Some settings could not be saved and are lost when Resonalyze closes.\r\n\r\n" +
                string.Join("\r\n", failures),
                "Closing",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
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
        liveSpectrumSession.Dispose();
    }
}
