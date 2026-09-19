using System.Windows.Forms;
using Resonalyze.Options;

using Resonalyze.Dsp;

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

    // A mode switch stops what the mode being left runs.
    private async Task StopRunningForModeSwitchAsync()
    {
        if (expSweepMeasurement.InProgress)
        {
            await expSweepMeasurement.AbortAsync();
        }
        if (timeAlignmentController.InProgress)
        {
            await timeAlignmentController.AbortAsync();
        }

        bool liveCaptureWasRunning = liveSpectrumSession.InProgress;
        await liveSpectrumController.AbortAsync();
        if (liveCaptureWasRunning)
        {
            // Only when a capture actually stopped: runs on every mode switch, and a needless refresh opens an AsioOut per tab click.
            RefreshOpenMeasurementSettingsDevice();
        }
    }

    // Held from the Record press to the run's completion, so no load lands under a sweep.
    private AnalyzerDocument.Request? runRequest;

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
            if (!liveSpectrumSession.InProgress)
            {
                ResetLiveSplViewOnlyDisplayForRun();
                await startupAudioWarmup.WaitAsync();
                // Another tab may have been chosen while the device warmed up; a run starts in its own.
                if (!liveSpectrumSession.InProgress && CurrentMode != Mode.LiveSpectrum)
                {
                    await SelectModeAsync(ModeTab.LiveSpectrum);
                }
            }

            await liveSpectrumController.ToggleAsync();
            // Pay the settings panel's device refresh deferred while the capture owned the device.
            RefreshOpenMeasurementSettingsDevice();
            return;
        }

        if (liveSpectrumSession.InProgress)
        {
            await liveSpectrumController.AbortAsync();
        }

        if (expSweepMeasurement.InProgress)
        {
            await expSweepMeasurement.AbortAsync();
        }
        else if (!analyzerDocument.IsBusy)
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
            if (expSweepMeasurement.InProgress || analyzerDocument.IsBusy)
            {
                // A second click during the warm-up would Init a running measurement.
                return;
            }

            PrepareSweepMeasurementForRun();
            // After Prepare, so the anchor prediction reads this run's input configuration.
            ResetSplViewOnlyDisplayForRun();
            // A run replaces the open measurement from its first sample; completion installs the new one.
            analyzerDocument.Clear();
            runRequest = analyzerDocument.TryAcquire();
            EnterMeasurementRunningState();
            _ = expSweepMeasurement.RunAsync();
        }
    }

    // A user stop reports success; a failure is the device's and must not reset the UI silently.
    private void ShowLiveSpectrumFailure(Exception error)
    {
        if (IsDisposed || closingInProgress)
        {
            return;
        }

        MessageBox.Show(
            this,
            $"The live measurement failed.\r\n\r\n{error.Message}",
            "Live Spectrum",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    // Live mirror of ResetSplViewOnlyDisplayForRun; also used when a running analyzer loses calibration. The signal is left alone.
    private void ResetLiveSplViewOnlyDisplayForRun()
    {
        // MMM is never view-only and always renders band-power dB SPL.
        if (!liveSpectrumSession.Display.SplViewOnly)
        {
            return;
        }

        viewSettings.LiveSpectrum.MagnitudeScale = Dsp.MagnitudeScale.Relative;
        SaveMeasurementSettings();
        // An open panel must follow, or its next apply writes SPL back.
        dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
            panel => panel.ForceSplScaleOff());
    }

    // Stays in dB SPL only if the run ahead has an SPL calibration for its input; otherwise new curves would be born hidden.
    // Not gated on the previous measurement's anchor.
    private void ResetSplViewOnlyDisplayForRun()
    {
        if (viewSettings.FrequencyResponse.MagnitudeScale !=
                Dsp.MagnitudeScale.SoundPressureLevel ||
            expSweepMeasurement.NextRunHasSplAnchor)
        {
            return;
        }

        viewSettings.FrequencyResponse.MagnitudeScale = Dsp.MagnitudeScale.Relative;
        SaveMeasurementSettings();
        // An open panel must follow, or its next apply writes SPL back.
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.ForceRelativeScale());
    }

    // Settings from the separate-loopback-device era: loopback selection was reset, the user must pick a channel again.
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

    // A completed calibration commits immediately (settings, disk, measurement, plot) without Apply.
    private async void PersistCalibration(MeasurementOptions.CalibrationSelection selection)
    {
        // Mic curves cannot reach a capture already taken (frozen on the accumulation); only the SPL anchor may drop a peak hold.
        bool splAnchorMoved = !ReferenceEquals(
            measurementSettings.Measurement.SplCalibration, selection.SplCalibration);
        measurementSettings.Measurement.MicrophoneCalibration0DegreesPath =
            selection.MicrophoneCalibration0DegreesPath;
        measurementSettings.Measurement.AdditionalMicrophoneCalibrations =
            selection.AdditionalMicrophoneCalibrations.ToList();
        measurementSettings.Measurement.SplCalibration = selection.SplCalibration;
        expSweepMeasurement.SplCalibration = selection.SplCalibration;
        RefreshCalibrationConsumers();
        // A running analyzer losing its anchor in dB SPL would draw nothing, so drop to relative. Idle view-only SPL is legitimate.
        if (liveSpectrumSession.InProgress)
        {
            ResetLiveSplViewOnlyDisplayForRun();
        }
        // Persist first so it survives a failed redraw.
        ScheduleMeasurementSettingsSave();

        try
        {
            // The anchor changes level mapping, so refresh the analyzer in every mode; mic curve changes must not drop a valid peak hold.
            if (splAnchorMoved)
            {
                liveSpectrumController.RefreshCalibration();
            }

            if (CurrentMode != Mode.LiveSpectrum)
            {
                await RefreshCurrentModePlotAsync();
            }
        }
        catch (Exception exception)
        {
            ShowMeasurementError("Failed to redraw after a calibration change.", exception);
        }
    }

    // All but the audio-backend group apply as edited, into settings only; PrepareSweepMeasurementForRun pushes them before the next sweep.
    private async void ApplySweepSettingsLive(MeasurementOptions dialog)
    {
        sweepSettingsApplyPending = true;
        if (applyingSweepSettings)
        {
            // An apply is in flight; the loop re-reads the panel after it.
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

    private void RefreshOpenMeasurementSettingsDevice()
    {
        if (liveSpectrumSession.InProgress || expSweepMeasurement.InProgress)
        {
            return;
        }

        dockedMeasurementSettingsHost.InvokeIfOpen<MeasurementOptions>(
            panel => panel.RefreshAudioDeviceView());
    }

    /// <summary>Stops a live capture and refreshes an open Record Settings panel; no-op (no ASIO probe) when nothing ran.</summary>
    private async Task StopLiveCaptureAsync()
    {
        if (!liveSpectrumSession.InProgress && !liveSpectrumController.TimerEnabled)
        {
            return;
        }

        await liveSpectrumController.AbortAsync();
        RefreshOpenMeasurementSettingsDevice();
    }

    private async Task ApplySweepSettingsAsync(MeasurementOptions dialog)
    {
        AudioSessionRequest requestBefore =
            CreateAudioWarmupRequest(measurementSettings.Measurement);
        dialog.ApplySweepSettings(measurementSettings.Measurement);
        RefreshCalibrationConsumers();
        // The settings just edited are not read back from expSweepMeasurement.
        SaveMeasurementSettings();

        // Before the early return: the protective high-pass is not an audio setting but the live analyzer divides it out, without a restart.
        liveSpectrumSession.SetProtectiveHighPass(measurementSettings.Measurement);

        AudioSessionRequest request =
            CreateAudioWarmupRequest(measurementSettings.Measurement);
        if (request == requestBefore)
        {
            // Only the sweep definition moved; no device reopen.
            return;
        }

        await ApplyMeasurementConfigurationToControllersAsync();
        if (liveSpectrumSession.InProgress || expSweepMeasurement.InProgress)
        {
            return;
        }

        try
        {
            await audioSessionFactory.WarmUpAsync(request, CancellationToken.None);
        }
        catch
        {
            // Best effort: a driver refusing pre-open must not interrupt a sweep edit.
        }
    }

    private async void buttonRecordOpt_Click(object sender, EventArgs e)
    {
        if (dockedMeasurementSettingsHost.IsOpen)
        {
            dockedMeasurementSettingsHost.Close();
            return;
        }

        // The panel opens the device (ASIO rates come from a probe); a driver busy with the warm-up answers only its open rate.
        await startupAudioWarmup.WaitAsync();
        if (IsDisposed || dockedMeasurementSettingsHost.IsOpen)
        {
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
                    if (!liveSpectrumSession.InProgress &&
                        !expSweepMeasurement.InProgress)
                    {
                        await audioSessionFactory.WarmUpAsync(
                            CreateAudioWarmupRequest(measurementSettings.Measurement),
                            CancellationToken.None);

                        // Refresh the panel's device snapshot after reconfigure. Only in this branch: while the live spectrum owns the driver,
                        // probing would open a second AsioOut and get a short rate list. Stopping the capture refreshes it later.
                        dialog.RefreshAudioDeviceView();
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
                liveSpectrumSession.InProgress)
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
        }
    }

    // Loopback equal to the mic channel means no loopback, so the warm-up opens only needed channels.
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
