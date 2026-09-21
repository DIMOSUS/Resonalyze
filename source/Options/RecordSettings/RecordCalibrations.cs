namespace Resonalyze.Options;

internal sealed record RecordCalibrationButton(string Text, Color Color, string ToolTip);

/// <summary>What the calibration half of Record Settings shows: the 0° file, the further calibrations, the one the
/// microphone is read through, and the SPL anchor with whether it still describes the selected input.</summary>
internal sealed record RecordCalibrationView(
    RecordCalibrationButton ZeroDegree,
    bool CanClearZeroDegree,
    string ClearZeroDegreeToolTip,
    string ExtraButtonText,
    bool MicrophoneCalibrationEnabled,
    bool SplEnabled,
    RecordCalibrationButton Spl,
    bool CanClearSpl,
    string ClearSplToolTip,
    string ArrayButtonText);

internal static class RecordCalibrations
{
    public const string ExtraToolTip =
        "Further calibration files, and curves estimated from one of them for " +
        "an angle of incidence. The measurement microphone and every array " +
        "microphone are read through one of them.";

    public static readonly string MicrophoneCalibrationToolTip =
        "The calibration the measurement microphone is read through." +
        Environment.NewLine + Environment.NewLine +
        "A run FREEZES it into the file it writes, so it describes the capsule " +
        "about to record — set it before measuring, not after. The analysis " +
        "views then read a measurement through the curve it was recorded " +
        "with, and Virtual DSP offers it as \"Own (as measured)\".";

    /// <param name="canCalibrateSpl">False when the panel has no audio sessions to capture the calibrator with.</param>
    public static RecordCalibrationView Read(RecordSettingsSession session, bool canCalibrateSpl)
    {
        string? zeroDegreePath = session.MicrophoneCalibration0DegreesPath;
        string? problem = session.ZeroDegreeCalibrationProblem;
        int extraCount = session.AdditionalMicrophoneCalibrations.Count;
        return new RecordCalibrationView(
            new RecordCalibrationButton(
                zeroDegreePath == null ? "Select file..." : Path.GetFileName(zeroDegreePath),
                problem != null ? UiPalette.Error : UiPalette.TextPrimary,
                zeroDegreePath == null ? "No calibration file selected." : problem ?? zeroDegreePath),
            CanClearZeroDegree: zeroDegreePath != null,
            zeroDegreePath == null ? "No calibration file selected." : "Clear selected calibration file.",
            extraCount == 0 ? "Manage..." : $"Manage... ({extraCount})",
            MicrophoneCalibrationEnabled: session.MicrophoneCalibration.Items.Count > 1,
            SplEnabled: canCalibrateSpl,
            SplButton(session, canCalibrateSpl),
            CanClearSpl: session.SplCalibration != null,
            session.SplCalibration == null ? "No SPL calibration." : "Clear the SPL calibration.",
            RecordArrayInputs.ButtonText(session));
    }

    /// <summary>The microphone alone, no loopback: the anchor is measured against an external calibrator.</summary>
    public static AudioSessionRequest SplCaptureRequest(RecordSettingsSession session) =>
        AudioSessionRequestBuilder.Build(
            (AudioBackend)session.Backend.SelectedIndex,
            session.SelectedSampleRate,
            (int)session.Bits.Value,
            session.SelectedPlaybackChannel,
            waveInputChannelOffset: session.SelectedWaveInputOffset,
            waveLoopbackInputChannelOffset: null,
            asioInputChannelOffset: session.SelectedAsioInputOffset,
            asioLoopbackInputChannelOffset: null,
            asioOutputChannelOffset: session.SelectedAsioOutputOffset,
            outputDeviceNumber: session.SelectedPlaybackDeviceNumber,
            inputDeviceNumber: session.SelectedRecordingDeviceNumber,
            wasapiCaptureEndpointId: session.SelectedCaptureEndpoint?.Id,
            wasapiRenderEndpointId: session.SelectedRenderEndpoint?.Id,
            asioDriverName: session.SelectedAsioDriverName,
            bufferMilliseconds: 100,
            expectedCaptureSamples: 0);

    /// <summary>The anchor is pinned to one input; a different backend, device, rate or channel makes it stale.</summary>
    public static bool MatchesSelectedInput(RecordSettingsSession session, SplCalibration calibration)
    {
        var backend = (AudioBackend)session.Backend.SelectedIndex;
        return calibration.MatchesInput(
            backend,
            session.SelectedSampleRate,
            (int)session.Bits.Value,
            backend == AudioBackend.Asio ? session.SelectedAsioInputOffset : session.SelectedWaveInputOffset,
            backend == AudioBackend.Wave ? session.SelectedRecordingDeviceNumber : null,
            session.SelectedCaptureEndpoint?.Id,
            session.SelectedAsioDriverName);
    }

    private static RecordCalibrationButton SplButton(RecordSettingsSession session, bool canCalibrateSpl)
    {
        SplCalibration? calibration = session.SplCalibration;
        if (calibration == null)
        {
            return new RecordCalibrationButton(
                "Calibrate...",
                UiPalette.TextPrimary,
                canCalibrateSpl
                    ? "Measure the offset from a 1 kHz acoustic calibrator so measurements " +
                        "can be shown in dB SPL. Uses the currently selected input."
                    : "SPL calibration is unavailable.");
        }

        bool stale = !MatchesSelectedInput(session, calibration);
        string detail =
            $"Measured {calibration.MeasuredLevelDbFs:0.0} dBFS at " +
            $"{calibration.MeasuredFrequencyHz:0} Hz " +
            $"({calibration.ReferenceLevelDbSpl:0} dB SPL reference).\r\n" +
            $"Offset {calibration.OffsetDb:+0.0;-0.0;0.0} dB · " +
            $"{calibration.CapturedAtUtc.ToLocalTime():g}.";
        if (stale)
        {
            detail += "\r\n⚠ The current input differs from the calibrated one — recalibrate.";
        }

        return new RecordCalibrationButton(
            $"{calibration.ReferenceLevelDbSpl:0} dB · {calibration.OffsetDb:+0.0;-0.0;0.0} dB",
            stale ? UiPalette.Warning : UiPalette.TextPrimary,
            detail);
    }
}
