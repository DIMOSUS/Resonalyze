using Resonalyze.Dsp;

namespace Resonalyze;

public sealed record SweepMeasurementConfiguration(
    SweepSignalConfiguration Signal,
    SweepAudioConfiguration Audio,
    SweepAveragingConfiguration Averaging,
    ProtectiveHighPassConfiguration? ProtectiveHighPass = null);

public enum ProtectiveHighPassKind
{
    Off,
    Butterworth,
    LinkwitzRiley
}

/// <summary>High-pass in the external DSP after the loopback tap; divided out of the transfer IR.</summary>
public sealed record ProtectiveHighPassConfiguration(
    ProtectiveHighPassKind Kind = ProtectiveHighPassKind.Off,
    double FrequencyHz = 2_000.0,
    int SlopeDbPerOctave = 24)
{
    public const double MaximumCompensationBoostDb = 40.0;

    public static ProtectiveHighPassConfiguration Off { get; } = new();

    public bool Enabled => Kind != ProtectiveHighPassKind.Off;

    public static ProtectiveHighPassConfiguration Normalize(
        ProtectiveHighPassConfiguration? configuration)
    {
        if (configuration == null || !Enum.IsDefined(configuration.Kind))
        {
            return Off;
        }

        double frequencyHz = double.IsFinite(configuration.FrequencyHz)
            ? Math.Clamp(configuration.FrequencyHz, 10.0, 20_000.0)
            : 2_000.0;
        if (configuration.Kind == ProtectiveHighPassKind.Off)
        {
            return new ProtectiveHighPassConfiguration(
                ProtectiveHighPassKind.Off,
                frequencyHz,
                NormalizeSlope(
                    ProtectiveHighPassKind.Butterworth,
                    configuration.SlopeDbPerOctave));
        }

        return new ProtectiveHighPassConfiguration(
            configuration.Kind,
            frequencyHz,
            NormalizeSlope(configuration.Kind, configuration.SlopeDbPerOctave));
    }

    /// <summary>Low edge a loopback transfer carries after this filter was divided out; null filter (unknown) masks nothing. See docs/tech/sweep-measurement.md#measured-band.</summary>
    public static double LowestMeasuredFrequencyHz(
        ProtectiveHighPassConfiguration? measurementFilter,
        int sampleRate) =>
        measurementFilter is { Enabled: true } filter && sampleRate > 0
            ? ProtectiveHighPassCompensation.LowestRecoverableFrequencyHz(
                filter.ToEdge(),
                sampleRate,
                MaximumCompensationBoostDb)
            : 0.0;

    public CrossoverEdge ToEdge()
    {
        if (!Enabled)
        {
            throw new InvalidOperationException(
                "An off protective high-pass has no crossover edge.");
        }

        CrossoverFilterFamily family = Kind switch
        {
            ProtectiveHighPassKind.Butterworth => CrossoverFilterFamily.Butterworth,
            ProtectiveHighPassKind.LinkwitzRiley => CrossoverFilterFamily.LinkwitzRiley,
            _ => throw new InvalidOperationException(
                "An off protective high-pass has no crossover edge.")
        };
        return new CrossoverEdge(family, FrequencyHz, SlopeDbPerOctave);
    }

    public static IReadOnlyList<int> SupportedSlopes(ProtectiveHighPassKind kind) =>
        kind switch
        {
            ProtectiveHighPassKind.LinkwitzRiley =>
                CrossoverFilter.SupportedSlopes(CrossoverFilterFamily.LinkwitzRiley),
            _ => CrossoverFilter.SupportedSlopes(CrossoverFilterFamily.Butterworth)
        };

    private static int NormalizeSlope(ProtectiveHighPassKind kind, int slope)
    {
        IReadOnlyList<int> supported = SupportedSlopes(kind);
        return supported.Contains(slope) ? slope : 24;
    }
}

public sealed record SweepSignalConfiguration(
    double LowFrequencyHz,
    double HighFrequencyHz,
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
    int WasapiBufferMilliseconds = 100,
    IReadOnlyList<int>? WaveArrayInputChannelOffsets = null,
    IReadOnlyList<int>? AsioArrayInputChannelOffsets = null);

public sealed record SweepAveragingConfiguration(int RunCount = 1);
