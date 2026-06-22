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
    public const int CurrentVersion = 4;

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
    public SweepMeasurementMode MeasurementMode { get; set; } =
        SweepMeasurementMode.SweepDeconvolution;
    public int SweepDeconvolutionPeakIndex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TransferPeakIndex { get; set; }

    public double[] SweepDeconvolutionRealSamples { get; set; } = Array.Empty<double>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? SweepDeconvolutionImaginarySamples { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? TransferRealSamples { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? TransferImaginarySamples { get; set; }

    public static ImpulseResponseFile Capture(ExpSweepMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        Complex[] sweepImpulseResponse = measurement.SweepDeconvolutionImpulseResponse
            ?? throw new InvalidOperationException("There is no impulse response to save.");
        ExponentialSineSweep sweep = measurement.Sweep
            ?? throw new InvalidOperationException("The sweep measurement is not initialized.");

        (double[] sweepRealSamples, double[]? sweepImaginarySamples) =
            ConvertSamples(sweepImpulseResponse, "Sweep deconvolution impulse response");
        double[]? transferRealSamples = null;
        double[]? transferImaginarySamples = null;
        int? transferPeakIndex = null;
        if (measurement.TransferImpulseResponse is { Length: > 0 } transferImpulseResponse)
        {
            (transferRealSamples, transferImaginarySamples) =
                ConvertSamples(transferImpulseResponse, "Transfer impulse response");
            transferPeakIndex = measurement.TransferPeakIndex;
        }

        return new ImpulseResponseFile
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            SampleRate = measurement.SampleRate,
            Bits = measurement.Bits,
            Octaves = measurement.Octaves,
            SweepDurationSeconds = sweep.ComputedDuration,
            PlayChannel = measurement.PlaybackChannel,
            MeasurementMode = measurement.MeasurementMode,
            SweepDeconvolutionPeakIndex = measurement.SweepDeconvolutionPeakIndex,
            TransferPeakIndex = transferPeakIndex,
            SweepDeconvolutionRealSamples = sweepRealSamples,
            SweepDeconvolutionImaginarySamples = sweepImaginarySamples,
            TransferRealSamples = transferRealSamples,
            TransferImaginarySamples = transferImaginarySamples
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

    public Complex[] GetSweepDeconvolutionImpulseResponse()
    {
        Validate();

        return ToComplexSamples(
            SweepDeconvolutionRealSamples,
            SweepDeconvolutionImaginarySamples);
    }

    public Complex[]? GetTransferImpulseResponse()
    {
        Validate();

        return TransferRealSamples == null
            ? null
            : ToComplexSamples(TransferRealSamples, TransferImaginarySamples);
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
        if (SampleRate is < 44_100 or > 768_000)
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
        if (!Enum.IsDefined(MeasurementMode))
        {
            throw new InvalidDataException("The measurement mode is invalid.");
        }
        if (SweepDeconvolutionRealSamples.Length == 0)
        {
            throw new InvalidDataException(
                "The sweep deconvolution impulse response contains no samples.");
        }
        if (SweepDeconvolutionImaginarySamples != null &&
            SweepDeconvolutionImaginarySamples.Length != SweepDeconvolutionRealSamples.Length)
        {
            throw new InvalidDataException(
                "Sweep deconvolution real and imaginary sample arrays have different lengths.");
        }
        if ((uint)SweepDeconvolutionPeakIndex >= (uint)SweepDeconvolutionRealSamples.Length)
        {
            throw new InvalidDataException(
                "The sweep deconvolution peak index is outside the sample array.");
        }
        if (TransferRealSamples != null &&
            TransferRealSamples.Length == 0)
        {
            throw new InvalidDataException("The transfer impulse response contains no samples.");
        }
        if (TransferImaginarySamples != null &&
            TransferRealSamples != null &&
            TransferImaginarySamples.Length != TransferRealSamples.Length)
        {
            throw new InvalidDataException(
                "Transfer real and imaginary sample arrays have different lengths.");
        }
        if (MeasurementMode == SweepMeasurementMode.LoopbackTransfer &&
            TransferRealSamples == null)
        {
            throw new InvalidDataException(
                "Loopback transfer files must include transfer impulse response samples.");
        }
        if (TransferRealSamples != null &&
            (!TransferPeakIndex.HasValue ||
                (uint)TransferPeakIndex.Value >= (uint)TransferRealSamples.Length))
        {
            throw new InvalidDataException("The transfer peak index is outside the sample array.");
        }

        ValidateSamples(
            SweepDeconvolutionRealSamples,
            SweepDeconvolutionImaginarySamples,
            "Sweep deconvolution impulse response");
        if (TransferRealSamples != null)
        {
            ValidateSamples(
                TransferRealSamples,
                TransferImaginarySamples,
                "Transfer impulse response");
        }
    }

    private static (double[] Real, double[]? Imaginary) ConvertSamples(
        Complex[] samples,
        string label)
    {
        var realSamples = new double[samples.Length];
        double[]? imaginarySamples = null;
        for (int i = 0; i < samples.Length; i++)
        {
            Complex sample = samples[i];
            if (!double.IsFinite(sample.Real) || !double.IsFinite(sample.Imaginary))
            {
                throw new InvalidOperationException(
                    $"{label} sample {i} is not a finite number.");
            }

            realSamples[i] = sample.Real;
            if (sample.Imaginary != 0)
            {
                imaginarySamples ??= new double[samples.Length];
                imaginarySamples[i] = sample.Imaginary;
            }
        }

        return (realSamples, imaginarySamples);
    }

    private static Complex[] ToComplexSamples(
        double[] realSamples,
        double[]? imaginarySamples)
    {
        var result = new Complex[realSamples.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new Complex(realSamples[i], imaginarySamples?[i] ?? 0);
        }

        return result;
    }

    private static void ValidateSamples(
        double[] realSamples,
        double[]? imaginarySamples,
        string label)
    {
        for (int i = 0; i < realSamples.Length; i++)
        {
            if (!double.IsFinite(realSamples[i]) ||
                (imaginarySamples != null && !double.IsFinite(imaginarySamples[i])))
            {
                throw new InvalidDataException($"{label} sample {i} is not a finite number.");
            }
        }
    }
}
