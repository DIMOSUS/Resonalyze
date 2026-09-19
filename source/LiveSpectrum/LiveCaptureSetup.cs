using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The live analyzer as a plot reads it: the frame it analyses, whether it has a reference, the input an SPL anchor
/// must match, and what the current accumulation was frozen with when its run began.
/// </summary>
/// <remarks>
/// Read once per build from <see cref="NoiseMeasurement.Setup"/>. <c>Init</c>, the options and every run change it,
/// so it is never kept past the build that read it.
/// </remarks>
internal sealed record LiveCaptureSetup(
    int SampleRate,
    int SequenceLength,
    int HopSize,
    WindowType WindowType,
    double WindowEnbwBins,
    double WindowMainLobeBins,
    bool IsMicOnly,
    MeasurementInputIdentity Input,
    Guid CaptureSessionId,
    ProtectiveHighPassConfiguration ProtectiveHighPass,
    CalibrationFile? MicrophoneCalibration,
    string MicrophoneCalibrationName);
