using Resonalyze.Dsp;

namespace Resonalyze.Options;

internal enum SplStatusTone
{
    Default,
    Error,
    Success
}

internal sealed record SplStatus(string Text, SplStatusTone Tone);

/// <summary>What the SPL dialog says while it listens and after, and the anchor a successful listen makes.</summary>
internal static class SplCalibrationReport
{
    public static readonly SplStatus Listening = new("Listening for the calibrator tone…", SplStatusTone.Default);

    public static readonly SplStatus Cancelled = new("Calibration cancelled.", SplStatusTone.Default);

    public static string ReferenceLabel(double levelDbSpl) => $"{levelDbSpl:0} dB SPL";

    public static int Percent(SplCalibrationProgress progress, TimeSpan duration) =>
        (int)Math.Clamp(progress.ElapsedSeconds / duration.TotalSeconds * 100.0, 0, 100);

    public static SplStatus Progress(SplCalibrationProgress progress)
    {
        string tone = progress.Reading.PeakFrequencyHz > 0
            ? $"{progress.Reading.PeakFrequencyHz:0} Hz at {progress.Reading.LevelDbFs:0.0} dBFS"
            : "—";
        string clip = progress.Clipped ? "   ⚠ CLIPPING — lower the input gain" : "";
        return new SplStatus(
            $"Listening…   input peak {progress.InputPeakDbFs:0.0} dBFS\r\n" +
            $"Loudest tone: {tone}\r\n" +
            $"Prominence: {progress.Reading.ProminenceDb:0.0} dB{clip}",
            progress.Clipped ? SplStatusTone.Error : SplStatusTone.Default);
    }

    public static SplStatus OpenFailed(string error) =>
        new($"Could not open the input for calibration:\r\n{error}", SplStatusTone.Error);

    public static SplStatus Succeeded(SplCalibrationCaptureResult result, SplCalibration calibration) =>
        new(
            $"Calibration successful.\r\n" +
            $"{result.Reading.PeakFrequencyHz:0} Hz measured at {result.Reading.LevelDbFs:0.0} dBFS.\r\n" +
            $"Offset {calibration.OffsetDb:+0.0;-0.0;0.0} dB at {calibration.ReferenceLevelDbSpl:0} dB SPL reference.",
            SplStatusTone.Success);

    public static SplStatus Failed(
        SplCalibrationFailure failure,
        SplCalibrationCaptureResult result,
        SplToneCriteria criteria) =>
        new(failure switch
        {
            SplCalibrationFailure.TooFewFrames =>
                "The capture did not run long enough. Check the input device and try again.",
            SplCalibrationFailure.Clipped =>
                $"The input clipped (peak {result.InputPeakDbFs:0.0} dBFS). Lower the input " +
                "gain and calibrate again.",
            SplCalibrationFailure.OffFrequency =>
                $"The loudest tone was at {result.Reading.PeakFrequencyHz:0} Hz, not " +
                $"{criteria.TargetFrequencyHz:0} Hz. Check the calibrator is set to 1 kHz and " +
                "seated on the capsule.",
            SplCalibrationFailure.NoClearPeak =>
                $"No clear {criteria.TargetFrequencyHz:0} Hz tone stood out from the noise " +
                $"(prominence {result.Reading.ProminenceDb:0.0} dB). Seat the calibrator firmly " +
                "and reduce ambient noise.",
            SplCalibrationFailure.Unstable =>
                $"The level was unsteady ({result.LevelStabilityDb:0.0} dB variation). Make sure " +
                "the calibrator is fully seated and held still.",
            SplCalibrationFailure.CaptureOverrun =>
                "The capture dropped frames (processing overload), so the result cannot be " +
                "trusted. Close other work and calibrate again.",
            _ => "Calibration failed."
        }, SplStatusTone.Error);

    /// <summary>Pinned to the input the listen used, so a later run on another input does not trust it.</summary>
    public static SplCalibration Calibration(
        AudioSessionRequest request,
        SplCalibrationCaptureResult result,
        double referenceLevelDbSpl,
        SplToneCriteria criteria,
        DateTimeOffset capturedAtUtc) =>
        new()
        {
            ReferenceLevelDbSpl = referenceLevelDbSpl,
            MeasuredLevelDbFs = result.Reading.LevelDbFs,
            ReferenceFrequencyHz = criteria.TargetFrequencyHz,
            MeasuredFrequencyHz = result.Reading.PeakFrequencyHz,
            CapturedAtUtc = capturedAtUtc,
            Backend = request.Backend,
            SampleRate = request.SampleRate,
            Bits = request.BitsPerSample,
            MicrophoneChannelOffset = request.Routing.MicrophoneChannel,
            InputDeviceNumber = request.Backend == AudioBackend.Wave
                ? request.WaveInputDeviceNumber
                : null,
            WasapiCaptureEndpointId = request.WasapiCaptureEndpointId,
            AsioDriverName = request.AsioDriverName
        };
}
