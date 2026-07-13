using System.Windows.Forms;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
    private async void Form1_Shown(object? sender, EventArgs e)
    {
        StartStartupAudioWarmup();
        NotifyLegacyDualDeviceLoopbackReset();

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
        if (recordButtonLongPress.ConsumeClickSuppression())
        {
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
                await startupAudioWarmup.WaitAsync();
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

            await startupAudioWarmup.WaitAsync();
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

    // One-time notice after loading a settings file written by a version that
    // still captured the loopback from a separate input device: the loopback
    // selection was reset (its channel offsets are meaningless on the shared
    // device), so the user must pick a loopback channel again.
    private void NotifyLegacyDualDeviceLoopbackReset()
    {
        if (!measurementSettings.LegacyDualDeviceLoopbackReset)
        {
            return;
        }

        MessageBox.Show(
            this,
            "Your previous configuration captured the loopback reference from a " +
            "separate input device. This capability was removed: the two devices " +
            "run on independent clocks, which silently degraded phase, group " +
            "delay and time alignment.\r\n\r\n" +
            "The loopback must now be a second channel of the microphone device " +
            "(or ASIO). The loopback selection was reset — open Measurement " +
            "Options and choose a loopback channel before measuring.",
            "Loopback configuration reset",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
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
                        await audioSessionFactory.WarmUpAsync(
                            CreateAudioWarmupRequest(measurementSettings.Measurement),
                            CancellationToken.None);
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
        if (measurementSettings.Measurement.AudioBackend == AudioBackend.Asio)
        {
            startupAudioWarmup.Start();
        }
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

            await audioSessionFactory.WarmUpAsync(
                CreateAudioWarmupRequest(measurementSettings.Measurement),
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

    // Maps the persisted audio settings onto a neutral warm-up request. Loopback
    // that coincides with the microphone channel is treated as "no loopback" so
    // the warm-up opens only the channels the routing actually needs.
    private static AudioSessionRequest CreateAudioWarmupRequest(
        MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        int? waveLoopback = settings.WaveLoopbackInputChannelOffset == settings.WaveInputChannelOffset
            ? null
            : settings.WaveLoopbackInputChannelOffset;
        int? asioLoopback = settings.AsioLoopbackInputChannelOffset == settings.AsioInputChannelOffset
            ? null
            : settings.AsioLoopbackInputChannelOffset;
        return AudioSessionRequestBuilder.Build(
            settings.AudioBackend,
            settings.SampleRate,
            settings.Bits,
            settings.PlaybackChannel,
            settings.WaveInputChannelOffset,
            waveLoopback,
            settings.AsioInputChannelOffset,
            asioLoopback,
            settings.AsioOutputChannelOffset,
            settings.OutputDeviceNumber,
            settings.InputDeviceNumber,
            settings.WasapiCaptureEndpointId,
            settings.WasapiRenderEndpointId,
            settings.AsioDriverName,
            settings.WasapiBufferMilliseconds,
            expectedCaptureSamples: 0);
    }

}
