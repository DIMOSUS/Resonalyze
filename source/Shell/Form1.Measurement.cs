using System.Windows.Forms;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
    private async void Form1_Shown(object? sender, EventArgs e)
    {
        StartStartupAudioWarmup();
        NotifySettingsLoadProblem();
        NotifyHistoryLoadProblem();
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

    private void NotifySettingsLoadProblem()
    {
        if (string.IsNullOrWhiteSpace(measurementSettings.LoadWarning))
        {
            return;
        }

        MessageBox.Show(
            this,
            measurementSettings.LoadWarning,
            "Settings recovery",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void NotifyHistoryLoadProblem()
    {
        if (string.IsNullOrWhiteSpace(measurementHistoryService.LoadWarning))
        {
            return;
        }

        MessageBox.Show(
            this,
            measurementHistoryService.LoadWarning,
            "History recovery",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
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

    // Commit a calibration change (microphone 0°/90° file or the SPL anchor) the
    // moment it happens: into the settings and to disk (debounced, flushed on
    // close), onto the live measurement so the next impulse response stamps the SPL
    // anchor, and onto the plot so a microphone calibration shows at once. None of
    // it needs an Apply-settings click — a completed calibration is not a tentative
    // edit, and losing it because Apply was not pressed is the whole bug here.
    private async void PersistCalibration(MeasurementOptions.CalibrationSelection selection)
    {
        measurementSettings.Measurement.MicrophoneCalibration0DegreesPath =
            selection.MicrophoneCalibration0DegreesPath;
        measurementSettings.Measurement.MicrophoneCalibration90DegreesPath =
            selection.MicrophoneCalibration90DegreesPath;
        measurementSettings.Measurement.SplCalibration = selection.SplCalibration;
        expSweepMeasurement.SplCalibration = selection.SplCalibration;
        RefreshCalibrationConsumers();
        // Persist the calibration itself up front so it survives even if the redraw
        // below fails; the normalized live options are re-saved afterwards.
        ScheduleMeasurementSettingsSave();

        IReadOnlyList<AxisViewport> viewports = CaptureAxisViewports();
        try
        {
            // Normalize Live Spectrum for the calibration change in EVERY mode — drop
            // its stale peak hold, and fall a Silent RTA back to a real excitation once
            // SPL is gone — not only while it is the visible mode; it rebuilds its own
            // model only when visible, so any other mode still refreshes below.
            bool liveSignalChanged = await liveSpectrumController.RefreshCalibrationAsync();

            // If the live signal was normalized (Silent → periodic pink), capture the
            // options so the change reaches disk: a plain schedule serializes the
            // un-captured LiveSpectrum settings and would keep the stale Silent.
            if (liveSignalChanged)
            {
                SaveMeasurementSettings();
            }

            if (CurrentMode != Mode.LiveSpectrum)
            {
                await RefreshCurrentModePlotAsync(viewports);
            }
        }
        catch (Exception exception)
        {
            // The calibration is already saved and applied; only the redraw failed.
            ShowMeasurementError("Failed to redraw after a calibration change.", exception);
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
            () =>
            {
                var options = new MeasurementOptions(audioSessionFactory);
                options.CalibrationChanged += PersistCalibration;
                return options;
            },
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
