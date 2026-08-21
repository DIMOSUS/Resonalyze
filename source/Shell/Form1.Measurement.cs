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
                ResetLiveSplViewOnlyDisplayForRun();
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
            // After Prepare, so the anchor prediction reads the input configuration
            // this run will actually use.
            ResetSplViewOnlyDisplayForRun();
            EnterMeasurementRunningState();
            _ = expSweepMeasurement.RunAsync();
        }
    }

    // The Live Spectrum mirror of ResetSplViewOnlyDisplayForRun below: an analyzer
    // started while the display is view-only SPL would draw no curves at all, so
    // drop the display to relative first. Also called when a RUNNING analyzer loses
    // its calibration, where staying in SPL would blank it. The signal is left
    // alone — it follows the analysis mode, not the scale.
    private void ResetLiveSplViewOnlyDisplayForRun()
    {
        if (plotModelFactory.EffectiveLiveSpectrumScale !=
                Dsp.MagnitudeScale.SoundPressureLevel ||
            plotModelFactory.LiveSplOffsetDb.HasValue)
        {
            return;
        }

        liveSpectrumOptions.MagnitudeScale = Dsp.MagnitudeScale.Relative;
        SaveMeasurementSettings();
        // An open panel must follow the reset, or its next apply-on-change would
        // write SPL right back into the options.
        dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
            panel => panel.ForceSplScaleOff());
    }

    // A sweep started in dB SPL only stays there when the RUN AHEAD can supply SPL —
    // i.e. an SPL calibration is configured for the input it will use, so the fresh
    // measurement comes up in dB SPL directly. Without one the new curves would be
    // born hidden (view-only shows overlays only), so the display drops to dBr/dBc.
    // Deliberately NOT gated on the previous measurement's anchor: that one is
    // irrelevant the moment a new run starts.
    private void ResetSplViewOnlyDisplayForRun()
    {
        if (frequencyResponseOptions.MagnitudeScale !=
                Dsp.MagnitudeScale.SoundPressureLevel ||
            expSweepMeasurement.NextRunHasSplAnchor)
        {
            return;
        }

        frequencyResponseOptions.MagnitudeScale = Dsp.MagnitudeScale.Relative;
        SaveMeasurementSettings();
        // An open panel must follow the reset, or its next apply-on-change would
        // write SPL right back into the options.
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.ForceRelativeScale());
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

    // Commit a calibration change (the microphone's files or the SPL anchor) the
    // moment it happens: into the settings and to disk (debounced, flushed on
    // close), onto the live measurement so the next impulse response stamps the SPL
    // anchor, and onto the plot so a microphone calibration shows at once. None of
    // it needs an Apply-settings click — a completed calibration is not a tentative
    // edit, and losing it because Apply was not pressed is the whole bug here.
    private async void PersistCalibration(MeasurementOptions.CalibrationSelection selection)
    {
        measurementSettings.Measurement.MicrophoneCalibration0DegreesPath =
            selection.MicrophoneCalibration0DegreesPath;
        measurementSettings.Measurement.AdditionalMicrophoneCalibrations =
            selection.AdditionalMicrophoneCalibrations.ToList();
        measurementSettings.Measurement.SplCalibration = selection.SplCalibration;
        expSweepMeasurement.SplCalibration = selection.SplCalibration;
        RefreshCalibrationConsumers();
        // Losing the SPL anchor while the analyzer RUNS in dB SPL would leave it
        // drawing nothing (view-only suppresses live curves), so drop the display to
        // relative first — the calibration refresh below then restarts the capture on
        // a scale its signal fits. An IDLE view-only SPL display is legitimate (it
        // shows overlays) and is left alone. The open live panel's amber state
        // follows the new calibration either way.
        if (liveSpectrumController.InProgress)
        {
            ResetLiveSplViewOnlyDisplayForRun();
        }
        dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
            panel => panel.RefreshAvailability(
                plotModelFactory.LiveSplOffsetDb.HasValue,
                liveSpectrumController.HasDisplayableCurve,
                liveSpectrumController.HasConfiguredLoopback));
        // Persist the calibration itself up front so it survives even if the redraw
        // below fails.
        ScheduleMeasurementSettingsSave();

        IReadOnlyList<AxisViewport> viewports = CaptureAxisViewports();
        try
        {
            // Refresh Live Spectrum for the calibration change in EVERY mode — drop
            // its now-stale peak hold — not only while it is the visible mode; it
            // rebuilds its own model only when visible, so any other mode still
            // refreshes below. The capture and its signal are untouched: they
            // follow the analysis mode, which a calibration cannot change.
            liveSpectrumController.RefreshCalibration();

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

    // Everything in the measurement options except the audio-backend group takes
    // effect as it is edited — the Apply button is left to the backend alone. The
    // edits land in the settings only; PrepareSweepMeasurementForRun pushes them
    // into the measurement before the next sweep, so tweaking the band cannot
    // discard the measurement currently on screen.
    private async void ApplySweepSettingsLive(MeasurementOptions dialog)
    {
        sweepSettingsApplyPending = true;
        if (applyingSweepSettings)
        {
            // An apply is in flight (it may be reopening the device); the loop
            // below re-reads the panel once it finishes.
            return;
        }

        applyingSweepSettings = true;
        try
        {
            while (sweepSettingsApplyPending && !IsDisposed && !dialog.IsDisposed)
            {
                sweepSettingsApplyPending = false;
                await ApplySweepSettingsAsync(dialog);
            }
        }
        catch (Exception exception)
        {
            ShowMeasurementError("Failed to apply the measurement settings.", exception);
        }
        finally
        {
            applyingSweepSettings = false;
        }
    }

    private async Task ApplySweepSettingsAsync(MeasurementOptions dialog)
    {
        AudioSessionRequest requestBefore =
            CreateAudioWarmupRequest(measurementSettings.Measurement);
        dialog.ApplySweepSettings(measurementSettings.Measurement);
        // Captures the other option groups and keeps the measurement settings
        // just edited (they are not read back from expSweepMeasurement here).
        SaveMeasurementSettings();

        AudioSessionRequest request =
            CreateAudioWarmupRequest(measurementSettings.Measurement);
        if (request == requestBefore)
        {
            // Only the sweep definition moved; the audio session is unchanged, so
            // there is nothing to reconfigure and no reason to reopen the device.
            return;
        }

        await ApplyMeasurementConfigurationToControllersAsync();
        if (liveSpectrumController.InProgress || expSweepMeasurement.InProgress)
        {
            return;
        }

        try
        {
            await audioSessionFactory.WarmUpAsync(request, CancellationToken.None);
        }
        catch
        {
            // Best effort, like the startup warm-up: the user edited a sweep
            // setting, not the device, so a driver that refuses to pre-open must
            // not interrupt them. Starting a measurement still reports it.
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
                options.SweepSettingsChanged += () => ApplySweepSettingsLive(options);
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
