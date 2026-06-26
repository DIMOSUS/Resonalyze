using System.Windows.Forms;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
    private async void Form1_Shown(object? sender, EventArgs e)
    {
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
}
