namespace Resonalyze;

public sealed record SweepMeasurementConfiguration(
    SweepSignalConfiguration Signal,
    SweepAudioConfiguration Audio,
    SweepAveragingConfiguration Averaging);

public sealed record SweepSignalConfiguration(
    int Octaves,
    int SampleRate,
    int Bits,
    double RequestedDurationSeconds,
    PlaybackChannel PlaybackChannel);

public sealed record SweepAudioConfiguration(
    AudioBackend Backend = AudioBackend.Wave,
    int OutputDeviceNumber = -1,
    int InputDeviceNumber = -1,
    int WaveInputChannelOffset = 0,
    int? WaveLoopbackInputChannelOffset = null,
    string? AsioDriverName = null,
    int AsioInputChannelOffset = 0,
    int? AsioLoopbackInputChannelOffset = null,
    int AsioOutputChannelOffset = 0,
    string? WasapiCaptureEndpointId = null,
    string? WasapiRenderEndpointId = null,
    string? WasapiCaptureEndpointName = null,
    string? WasapiRenderEndpointName = null,
    int WasapiBufferMilliseconds = 100);

public sealed record SweepAveragingConfiguration(
    int RunCount = 1,
    bool ConfirmEachRun = false);
