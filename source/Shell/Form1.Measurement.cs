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
                titleBarController.SetUpdateAvailable(result.ReleaseUrl);
            }
        }
        catch
        {
            // Update checks are best-effort only and must never affect startup.
        }
    }

    public async Task ChangeModeAsync(Mode mode)
    {
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
        UpdateClearButtonState();
        UpdatePlotLabelsPanel();

        if (OverlayCollection.SupportsMode(mode))
        {
            overlayCollection.Prepare(mode);
        }

        UpdateOverlayAvailability();
    }

    private async void buttonRecord_Click(object sender, EventArgs e)
    {
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

        if (expSweepMeasurement.InProgress)
        {
            await expSweepMeasurement.AbortAsync();
        }
        else
        {
            await WaitForStartupAudioWarmupAsync();
            PrepareSweepMeasurementForRun();
            EnterMeasurementRunningState();
            _ = expSweepMeasurement.RunAsync();
        }
    }

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
                    dialog.SetOptions(expSweepMeasurement);
                    SaveMeasurementSettings(captureMeasurementSettings: true);
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

    private async void buttonDraw_Click(object sender, EventArgs e)
    {
        if (commandController.IsDrawFrozen)
        {
            return;
        }

        DrawSelectedMode(includeCurves: true);
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
