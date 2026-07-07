using System.Windows.Forms;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
    private async void Form1_Shown(object? sender, EventArgs e)
    {
        StartStartupAudioWarmup();

        if (updateCheckStarted)
        {
            return;
        }

        updateCheckStarted = true;
        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(
                TimeSpan.FromSeconds(6));
            GitHubReleaseChecker.ReleaseCheckResult? result =
                await GitHubReleaseChecker.CheckForUpdateAsync(
                    cancellationTokenSource.Token);
            if (result?.UpdateAvailable == true && !IsDisposed)
            {
                ApplicationUpdateService.SetDetectedRelease(
                    result.TagName,
                    result.ReleaseUrl);
                chromeTitleBar.SetUpdateAvailable(result.ReleaseUrl);
            }
        }
        catch
        {
            // Update checks are best-effort only and must never affect startup.
        }
    }

    public async Task ChangeModeAsync(Mode mode)
    {
        CaptureActiveOverlaySlotsForCurrentMode();

        if (expSweepMeasurement.InProgress)
        {
            await expSweepMeasurement.AbortAsync();
        }
        if (timeAlignmentController.InProgress)
        {
            await timeAlignmentController.AbortAsync();
        }

        await liveSpectrumController.AbortAsync();

        CurrentMode = mode;
        plotView1.Model = null;
        UpdatePlotLabelsPanel();

        if (OverlayCollection.SupportsMode(mode))
        {
            overlayCollection.Prepare(mode);
        }

        UpdateOverlayAvailability();
    }

    private async void buttonRecord_Click(object sender, EventArgs e)
    {
        recordButtonLongPressTimer.Stop();
        if (suppressNextRecordButtonClick)
        {
            suppressNextRecordButtonClick = false;
            return;
        }

        if (dockedHistoryHost.IsOpen)
        {
            dockedHistoryHost.Close();
        }

        if (CurrentMode == Mode.LiveSpectrum)
        {
            if (!liveSpectrumController.InProgress)
            {
                await WaitForStartupAudioWarmupAsync();
            }

            await liveSpectrumController.ToggleAsync();
            UpdateRecordButtonForCurrentMode();
            return;
        }

        if (liveSpectrumController.InProgress)
        {
            await liveSpectrumController.AbortAsync();
        }

        if (expSweepMeasurement.WaitingForAverageConfirmation)
        {
            expSweepMeasurement.ContinueAverageRun();
            return;
        }

        if (expSweepMeasurement.InProgress)
        {
            await expSweepMeasurement.AbortAsync();
        }
        else
        {
            if (!measurementSettings.Measurement.HasLoopbackConfigured)
            {
                MessageBox.Show(
                    this,
                    "A loopback reference channel is required before measuring.\r\n\r\n" +
                    "Every analysis (frequency response, phase, group delay, impulse " +
                    "response and the decays) is derived from the loopback transfer IR. " +
                    "Open Measurement Options and select a loopback channel for the " +
                    "current audio backend.",
                    "Loopback required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            await WaitForStartupAudioWarmupAsync();
            if (expSweepMeasurement.InProgress)
            {
                // A second click can arrive while the warm-up is awaited;
                // starting again would call Init on a running measurement.
                return;
            }

            PrepareSweepMeasurementForRun();
            EnterMeasurementRunningState();
            _ = expSweepMeasurement.RunAsync();
        }
    }

    private void buttonRecord_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !CanLongPressCancelMeasurementSeries())
        {
            return;
        }

        recordButtonLongPressTriggered = false;
        suppressNextRecordButtonClick = false;
        recordButtonLongPressTimer.Start();
    }

    private void buttonRecord_MouseUp(object? sender, MouseEventArgs e)
    {
        recordButtonLongPressTimer.Stop();
    }

    private void buttonRecord_MouseLeave(object? sender, EventArgs e)
    {
        recordButtonLongPressTimer.Stop();
    }

    private async void RecordButtonLongPressTimer_Tick(object? sender, EventArgs e)
    {
        recordButtonLongPressTimer.Stop();
        if (!CanLongPressCancelMeasurementSeries() || recordButtonLongPressTriggered)
        {
            return;
        }

        recordButtonLongPressTriggered = true;
        suppressNextRecordButtonClick = true;
        buttonRecord.Text = "Aborting...";
        await expSweepMeasurement.AbortAsync();
    }

    private bool CanLongPressCancelMeasurementSeries() =>
        CurrentMode != Mode.LiveSpectrum &&
        expSweepMeasurement.InProgress &&
        expSweepMeasurement.AverageRunCount > 1;

    private void buttonRecordOpt_Click(object sender, EventArgs e)
    {
        if (dockedMeasurementSettingsHost.IsOpen)
        {
            dockedMeasurementSettingsHost.Close();
            return;
        }

        dockedModeSettingsHost.Close();
        dockedHistoryHost.Close();
        dockedMeasurementSettingsHost.Toggle(
            "measurement-settings",
            () => new MeasurementOptions(),
            dialog => dialog.Init(expSweepMeasurement, measurementSettings.Measurement),
            async dialog =>
            {
                try
                {
                    dialog.SetOptions(expSweepMeasurement, measurementSettings.Measurement);
                    SaveMeasurementSettings(captureMeasurementSettings: true);
                    RefreshCalibrationConsumers();
                    await ApplyMeasurementConfigurationToControllersAsync();
                    if (!liveSpectrumController.InProgress &&
                        !expSweepMeasurement.InProgress)
                    {
                        await AudioDeviceWarmup.WarmUpAsync(measurementSettings.Measurement);
                    }
                }
                catch (InvalidOperationException exception)
                {
                    MessageBox.Show(
                        this,
                        exception.Message,
                        "Measurement Options",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        this,
                        $"Failed to reinitialize the audio device.\r\n\r\n{exception.Message}",
                        "Measurement Options",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            });
    }

    private void StartStartupAudioWarmup()
    {
        if (startupAudioWarmupTask != null ||
            measurementSettings.Measurement.AudioBackend != AudioBackend.Asio)
        {
            return;
        }

        startupAudioWarmupCancellation = new CancellationTokenSource();
        startupAudioWarmupTask =
            WarmUpStartupAudioAsync(startupAudioWarmupCancellation.Token);
    }

    private async Task WarmUpStartupAudioAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            if (IsDisposed ||
                expSweepMeasurement.InProgress ||
                liveSpectrumController.InProgress)
            {
                return;
            }

            await AudioDeviceWarmup.WarmUpAsync(
                measurementSettings.Measurement,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Startup warm-up is best-effort. The normal measurement path will
            // still report driver errors if the selected ASIO setup is invalid.
        }
    }

    private async Task WaitForStartupAudioWarmupAsync()
    {
        Task? task = startupAudioWarmupTask;
        if (task == null || task.IsCompleted)
        {
            return;
        }

        try
        {
            await task;
        }
        catch
        {
            // Warm-up failures are intentionally non-fatal.
        }
    }
}
