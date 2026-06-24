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
        if (CurrentMode == Mode.TimeAlignment)
        {
            await timeAlignmentController.ToggleAsync();
            return;
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
            EnterMeasurementRunningState();
            _ = expSweepMeasurement.RunAsync();
        }
    }

    private void buttonRecordOpt_Click(object sender, EventArgs e)
    {
        using var opt = new MeasurementOptions();
        opt.Init(expSweepMeasurement);

        if (ShowSettingsDialog(opt) == DialogResult.OK)
        {
            try
            {
                opt.SetOptions(expSweepMeasurement);
                ApplyMeasurementConfigurationToControllers();
                SaveMeasurementSettings();
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
        }
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
