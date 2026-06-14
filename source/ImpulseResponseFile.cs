using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze;

/// <summary>
/// Versioned, human-readable representation of a captured impulse response.
/// </summary>
public sealed class ImpulseResponseFile
{
    public const string CurrentFormat = "resonalyze-impulse-response";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Format { get; set; } = CurrentFormat;
    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset SavedAtUtc { get; set; }
    public int SampleRate { get; set; }
    public int Bits { get; set; }
    public int Octaves { get; set; }
    public double SweepDurationSeconds { get; set; }
    public PlaybackChannel PlayChannel { get; set; }
    [JsonPropertyName("maxMagnitudeIndex")]
    public int PeakIndex { get; set; }
    public double[] RealSamples { get; set; } = Array.Empty<double>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? ImaginarySamples { get; set; }

    public static ImpulseResponseFile Capture(ExpSweepMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        Complex[] impulseResponse = measurement.ImpulseResponse
            ?? throw new InvalidOperationException("There is no impulse response to save.");
        ExponentialSineSweep sweep = measurement.Sweep
            ?? throw new InvalidOperationException("The sweep measurement is not initialized.");

        var realSamples = new double[impulseResponse.Length];
        double[]? imaginarySamples = null;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            Complex sample = impulseResponse[i];
            if (!double.IsFinite(sample.Real) || !double.IsFinite(sample.Imaginary))
            {
                throw new InvalidOperationException(
                    $"Impulse response sample {i} is not a finite number.");
            }

            realSamples[i] = sample.Real;
            if (sample.Imaginary != 0)
            {
                imaginarySamples ??= new double[impulseResponse.Length];
                imaginarySamples[i] = sample.Imaginary;
            }
        }

        return new ImpulseResponseFile
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            SampleRate = measurement.SampleRate,
            Bits = measurement.Bits,
            Octaves = measurement.Octaves,
            SweepDurationSeconds = sweep.RequestedDuration,
            PlayChannel = measurement.PlaybackChannel,
            PeakIndex = measurement.PeakIndex,
            RealSamples = realSamples,
            ImaginarySamples = imaginarySamples
        };
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate();

        await using FileStream stream = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await JsonSerializer.SerializeAsync(
            stream,
            this,
            SerializerOptions,
            cancellationToken);
    }

    public static async Task<ImpulseResponseFile> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        ImpulseResponseFile file = await JsonSerializer.DeserializeAsync<ImpulseResponseFile>(
            stream,
            SerializerOptions,
            cancellationToken)
            ?? throw new InvalidDataException("The impulse response file is empty.");
        file.Validate();
        return file;
    }

    public Complex[] GetImpulseResponse()
    {
        Validate();

        var result = new Complex[RealSamples.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new Complex(
                RealSamples[i],
                ImaginarySamples?[i] ?? 0);
        }
        return result;
    }

    private void Validate()
    {
        if (!string.Equals(Format, CurrentFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported file format '{Format}'.");
        }
        if (Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported impulse response version {Version}.");
        }
        if (SampleRate is < 8_000 or > 768_000)
        {
            throw new InvalidDataException("The sample rate is outside the supported range.");
        }
        if (Bits is not (16 or 24))
        {
            throw new InvalidDataException("Only 16-bit and 24-bit measurements are supported.");
        }
        if (Octaves is < 1 or > 24)
        {
            throw new InvalidDataException("The octave count is outside the supported range.");
        }
        if (!double.IsFinite(SweepDurationSeconds) ||
            SweepDurationSeconds <= 0 ||
            SweepDurationSeconds > 3_600)
        {
            throw new InvalidDataException("The sweep duration is invalid.");
        }
        if (!Enum.IsDefined(PlayChannel))
        {
            throw new InvalidDataException("The playback channel is invalid.");
        }
        if (RealSamples.Length == 0)
        {
            throw new InvalidDataException("The impulse response contains no samples.");
        }
        if (ImaginarySamples != null &&
            ImaginarySamples.Length != RealSamples.Length)
        {
            throw new InvalidDataException(
                "Real and imaginary sample arrays have different lengths.");
        }
        if ((uint)PeakIndex >= (uint)RealSamples.Length)
        {
            throw new InvalidDataException("The peak index is outside the sample array.");
        }

        for (int i = 0; i < RealSamples.Length; i++)
        {
            if (!double.IsFinite(RealSamples[i]) ||
                (ImaginarySamples != null && !double.IsFinite(ImaginarySamples[i])))
            {
                throw new InvalidDataException(
                    $"Impulse response sample {i} is not a finite number.");
            }
        }
    }
}
