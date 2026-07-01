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
            !chromeTitleBar.IsCustomMaximized)
        {
            base.WndProc(ref m);
            if ((int)m.Result == ChromeTitleBar.HtClient)
            {
                Point point = PointToClient(
                    ChromeTitleBar.GetPointFromLParam(m.LParam));
                m.Result = ChromeTitleBar.GetResizeHitTest(
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
        FlushMeasurementSettings();
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
        inputLevelMeterController.Dispose();
        expSweepMeasurement.Dispose();
        timeAlignmentController.Dispose();
        liveSpectrumController.Dispose();
    }
}
