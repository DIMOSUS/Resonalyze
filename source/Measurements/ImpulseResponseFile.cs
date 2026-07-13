using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.History;

namespace Resonalyze;

/// <summary>
/// Versioned, human-readable representation of a captured impulse response.
/// </summary>
public sealed class ImpulseResponseFile
{
    public const string CurrentFormat = "resonalyze-impulse-response";
    public const int CurrentVersion = 6;

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
    public int AverageRunCount { get; set; } = 1;
    public int AcceptedAverageRunCount { get; set; } = 1;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AudioSessionFileEntry? AudioSession { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TransferPeakIndex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LevelSnapshotFileEntry? MicrophoneLevels { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LevelSnapshotFileEntry? LoopbackLevels { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PreviewFrequencyResponseFileEntry? PreviewFrequencyResponse { get; set; }

    public double[] SweepDeconvolutionRealSamples { get; set; } = Array.Empty<double>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? SweepDeconvolutionImaginarySamples { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? TransferRealSamples { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? TransferImaginarySamples { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? TransferCoherence { get; set; }

    public static ImpulseResponseFile Capture(ExpSweepMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        MeasurementImpulseResponse sweepDeconvolution = measurement.SweepDeconvolution
            ?? throw new InvalidOperationException("There is no impulse response to save.");
        MeasurementImpulseResponse? transfer = measurement.Transfer;
        Complex[] sweepImpulseResponse = sweepDeconvolution.ImpulseResponse;
        ExponentialSineSweep sweep = measurement.Sweep
            ?? throw new InvalidOperationException("The sweep measurement is not initialized.");

        (double[] sweepRealSamples, double[]? sweepImaginarySamples) =
            ConvertSamples(sweepImpulseResponse, "Sweep deconvolution impulse response");
        double[]? transferRealSamples = null;
        double[]? transferImaginarySamples = null;
        int? transferPeakIndex = null;
        if (transfer is { ImpulseResponse.Length: > 0 })
        {
            (transferRealSamples, transferImaginarySamples) =
                ConvertSamples(transfer.ImpulseResponse, "Transfer impulse response");
            transferPeakIndex = transfer.PeakIndex;
        }
        InputLevelMeterSnapshot levels = measurement.CurrentLevels;
        LevelSnapshotFileEntry? microphoneLevels =
            CreateLevelSnapshotFileEntry(levels.Microphone);
        LevelSnapshotFileEntry? loopbackLevels =
            CreateLevelSnapshotFileEntry(levels.Loopback);

        return new ImpulseResponseFile
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            SampleRate = measurement.SampleRate,
            Bits = measurement.Bits,
            Octaves = measurement.Octaves,
            SweepDurationSeconds = sweep.ComputedDuration,
            PlayChannel = measurement.PlaybackChannel,
            MeasurementMode = measurement.MeasurementMode,
            SweepDeconvolutionPeakIndex = sweepDeconvolution.PeakIndex,
            AverageRunCount = measurement.AverageRunCount,
            AcceptedAverageRunCount = measurement.AcceptedAverageRunCount,
            AudioSession = CreateAudioSessionFileEntry(
                measurement.LastAudioSessionDiagnostics,
                measurement.SampleRate,
                measurement.Bits),
            TransferPeakIndex = transferPeakIndex,
            MicrophoneLevels = microphoneLevels,
            LoopbackLevels = loopbackLevels,
            PreviewFrequencyResponse = CreatePreviewFileEntry(
                MeasurementHistoryPreviewBuilder.Build(
                    sweepImpulseResponse,
                    sweepDeconvolution.PeakIndex,
                    measurement.SampleRate,
                    measurement.MeasurementMode,
                    transfer?.ImpulseResponse,
                    transferPeakIndex)),
            SweepDeconvolutionRealSamples = sweepRealSamples,
            SweepDeconvolutionImaginarySamples = sweepImaginarySamples,
            TransferRealSamples = transferRealSamples,
            TransferImaginarySamples = transferImaginarySamples,
            TransferCoherence = measurement.TransferCoherence?.ToArray()
        };
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate();

        // Write to a sibling temp file first: creating the target directly would
        // truncate it before writing, so a failure mid-write (crash, full disk)
        // destroys the previously saved measurement. The final move is atomic.
        string tempPath = path + ".tmp";
        try
        {
            await using (FileStream stream = new(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    this,
                    SerializerOptions,
                    cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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

    internal InputLevelMeterSnapshot GetMeterSnapshot()
    {
        Validate();

        return new InputLevelMeterSnapshot(
            ToMeterEntry(MicrophoneLevels),
            ToMeterEntry(LoopbackLevels));
    }

    internal MeasurementHistoryPreview? ToPreview()
    {
        if (PreviewFrequencyResponse == null)
        {
            return null;
        }

        return new MeasurementHistoryPreview
        {
            Window = PreviewFrequencyResponse.Window,
            LeftTukeyWindow = PreviewFrequencyResponse.LeftTukeyWindow,
            RightTukeyWindow = PreviewFrequencyResponse.RightTukeyWindow,
            SmoothingInverseOctaves = PreviewFrequencyResponse.SmoothingInverseOctaves,
            Frequencies = PreviewFrequencyResponse.Frequencies.ToArray(),
            MagnitudesDb = PreviewFrequencyResponse.MagnitudesDb.ToArray()
        };
    }

    private void Validate()
    {
        if (!string.Equals(Format, CurrentFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported file format '{Format}'.");
        }
        if (Version is < 4 or > CurrentVersion)
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
        if (AverageRunCount < 1 || AcceptedAverageRunCount < 1)
        {
            throw new InvalidDataException("The averaging run counts are invalid.");
        }
        if (AcceptedAverageRunCount > AverageRunCount)
        {
            throw new InvalidDataException("Accepted averaging runs exceed requested runs.");
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
        if (TransferCoherence != null)
        {
            // The pipeline produces exactly N/2 + 1 coherence bins for a
            // transfer IR of length N; anything else would draw the curve on a
            // wrong frequency grid, because the FFT length is reconstructed
            // from the coherence itself.
            if (TransferRealSamples == null)
            {
                throw new InvalidDataException(
                    "Transfer coherence requires transfer impulse response samples.");
            }
            if (TransferCoherence.Length != TransferRealSamples.Length / 2 + 1)
            {
                throw new InvalidDataException(
                    "Transfer coherence length does not match the transfer impulse response " +
                    $"({TransferCoherence.Length} bins for {TransferRealSamples.Length} samples).");
            }

            for (int i = 0; i < TransferCoherence.Length; i++)
            {
                double value = TransferCoherence[i];
                if (!double.IsFinite(value) || value < 0 || value > 1)
                {
                    throw new InvalidDataException(
                        $"Transfer coherence sample {i} is outside the valid range.");
                }
            }
        }

        ValidateLevelEntry(MicrophoneLevels, nameof(MicrophoneLevels));
        ValidateLevelEntry(LoopbackLevels, nameof(LoopbackLevels));
        ValidatePreview(PreviewFrequencyResponse);
        ValidateAudioSession(AudioSession);
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

    internal static LevelSnapshotFileEntry? CreateLevelSnapshotFileEntry(InputLevelMeterEntry entry)
    {
        if (!entry.Available)
        {
            return null;
        }

        return new LevelSnapshotFileEntry
        {
            PeakDbFs = entry.PeakDbFs,
            RmsDbFs = entry.RmsDbFs,
            Clipped = entry.Clipped,
            FullScaleReference = entry.FullScaleReference
        };
    }

    internal static PreviewFrequencyResponseFileEntry? CreatePreviewFileEntry(
        MeasurementHistoryPreview? preview)
    {
        if (preview == null)
        {
            return null;
        }

        int count = Math.Min(preview.Frequencies.Length, preview.MagnitudesDb.Length);
        List<double> frequencies = [];
        List<double> magnitudesDb = [];
        for (int i = 0; i < count; i++)
        {
            double frequency = preview.Frequencies[i];
            double magnitudeDb = preview.MagnitudesDb[i];
            if (!double.IsFinite(frequency) ||
                !double.IsFinite(magnitudeDb) ||
                frequency <= 0)
            {
                continue;
            }

            frequencies.Add(frequency);
            magnitudesDb.Add(magnitudeDb);
        }

        if (frequencies.Count == 0)
        {
            return null;
        }

        return new PreviewFrequencyResponseFileEntry
        {
            Window = preview.Window,
            LeftTukeyWindow = preview.LeftTukeyWindow,
            RightTukeyWindow = preview.RightTukeyWindow,
            SmoothingInverseOctaves = preview.SmoothingInverseOctaves,
            Frequencies = frequencies.ToArray(),
            MagnitudesDb = magnitudesDb.ToArray()
        };
    }

    internal static AudioSessionFileEntry? CreateAudioSessionFileEntry(
        AudioSessionDiagnostics? diagnostics,
        int analysisSampleRate,
        int analysisBits)
    {
        if (diagnostics == null)
        {
            return null;
        }

        return new AudioSessionFileEntry
        {
            Backend = diagnostics.Backend,
            CaptureEndpointId = diagnostics.CaptureEndpointId,
            RenderEndpointId = diagnostics.RenderEndpointId,
            ShareMode = diagnostics.Backend.Contains("Exclusive", StringComparison.Ordinal)
                ? "Exclusive"
                : "Shared",
            CaptureFormat = diagnostics.CaptureFormat.ToString(),
            RenderFormat = diagnostics.RenderFormat.ToString(),
            CaptureSampleRate = diagnostics.CaptureFormat.SampleRate,
            RenderSampleRate = diagnostics.RenderFormat.SampleRate,
            AnalysisSampleRate = analysisSampleRate,
            FormatConversionOccurred =
                diagnostics.Backend.Contains("Shared", StringComparison.Ordinal) &&
                (diagnostics.RenderFormat.SampleRate != analysisSampleRate ||
                    diagnostics.RenderFormat.Encoding != AudioSampleEncoding.Pcm ||
                    diagnostics.RenderFormat.BitsPerSample != analysisBits),
            RequestedBufferMilliseconds = diagnostics.RequestedBufferMilliseconds,
            ActualBufferFrames = diagnostics.ActualBufferFrames,
            CapturePackets = diagnostics.CapturePackets,
            RenderCallbacks = diagnostics.RenderCallbacks,
            Discontinuities = diagnostics.Discontinuities,
            SilentPackets = diagnostics.SilentPackets,
            TimestampErrors = diagnostics.TimestampErrors,
            CaptureOverruns = diagnostics.CaptureOverruns,
            RenderUnderruns = diagnostics.RenderUnderruns
        };
    }

    private static void ValidateAudioSession(AudioSessionFileEntry? session)
    {
        if (session == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.Backend) ||
            string.IsNullOrWhiteSpace(session.CaptureEndpointId) ||
            string.IsNullOrWhiteSpace(session.RenderEndpointId))
        {
            throw new InvalidDataException("Audio session endpoint metadata is incomplete.");
        }
        if (session.CaptureSampleRate <= 0 || session.RenderSampleRate <= 0 ||
            session.AnalysisSampleRate <= 0 || session.RequestedBufferMilliseconds <= 0 ||
            session.ActualBufferFrames <= 0)
        {
            throw new InvalidDataException("Audio session format metadata is invalid.");
        }
        if (session.CapturePackets < 0 || session.RenderCallbacks < 0 ||
            session.Discontinuities < 0 || session.SilentPackets < 0 ||
            session.TimestampErrors < 0 || session.CaptureOverruns < 0 ||
            session.RenderUnderruns < 0)
        {
            throw new InvalidDataException("Audio session diagnostics cannot be negative.");
        }
    }

    private static InputLevelMeterEntry ToMeterEntry(LevelSnapshotFileEntry? entry)
    {
        if (entry == null)
        {
            return InputLevelMeterEntry.Unavailable;
        }

        return new InputLevelMeterEntry(
            true,
            entry.PeakDbFs,
            entry.RmsDbFs,
            entry.Clipped,
            entry.FullScaleReference);
    }

    private static void ValidateLevelEntry(LevelSnapshotFileEntry? entry, string label)
    {
        if (entry == null)
        {
            return;
        }

        if (!double.IsFinite(entry.PeakDbFs) || !double.IsFinite(entry.RmsDbFs))
        {
            throw new InvalidDataException($"{label} contains a non-finite level value.");
        }
    }

    private static void ValidatePreview(PreviewFrequencyResponseFileEntry? preview)
    {
        if (preview == null)
        {
            return;
        }

        if (preview.Window <= 0 ||
            preview.LeftTukeyWindow < 0 ||
            preview.RightTukeyWindow < 0 ||
            preview.SmoothingInverseOctaves < 0)
        {
            throw new InvalidDataException("Preview frequency-response settings are invalid.");
        }

        if (preview.Frequencies.Length != preview.MagnitudesDb.Length)
        {
            throw new InvalidDataException(
                "Preview frequency-response arrays have different lengths.");
        }

        for (int i = 0; i < preview.Frequencies.Length; i++)
        {
            if (!double.IsFinite(preview.Frequencies[i]) ||
                !double.IsFinite(preview.MagnitudesDb[i]))
            {
                throw new InvalidDataException(
                    $"Preview frequency-response sample {i} is not a finite number.");
            }
        }
    }

    public sealed class LevelSnapshotFileEntry
    {
        public double PeakDbFs { get; set; }
        public double RmsDbFs { get; set; }
        public bool Clipped { get; set; }
        public bool FullScaleReference { get; set; }
    }

    public sealed class PreviewFrequencyResponseFileEntry
    {
        public int Window { get; set; }
        public int LeftTukeyWindow { get; set; }
        public int RightTukeyWindow { get; set; }
        public int SmoothingInverseOctaves { get; set; }
        public double[] Frequencies { get; set; } = Array.Empty<double>();
        public double[] MagnitudesDb { get; set; } = Array.Empty<double>();
    }

    public sealed class AudioSessionFileEntry
    {
        public string Backend { get; set; } = string.Empty;
        public string CaptureEndpointId { get; set; } = string.Empty;
        public string RenderEndpointId { get; set; } = string.Empty;
        public string ShareMode { get; set; } = string.Empty;
        public string CaptureFormat { get; set; } = string.Empty;
        public string RenderFormat { get; set; } = string.Empty;
        public int CaptureSampleRate { get; set; }
        public int RenderSampleRate { get; set; }
        public int AnalysisSampleRate { get; set; }
        public bool FormatConversionOccurred { get; set; }
        public int RequestedBufferMilliseconds { get; set; }
        public int ActualBufferFrames { get; set; }
        public long CapturePackets { get; set; }
        public long RenderCallbacks { get; set; }
        public long Discontinuities { get; set; }
        public long SilentPackets { get; set; }
        public long TimestampErrors { get; set; }
        public long CaptureOverruns { get; set; }
        public long RenderUnderruns { get; set; }
    }
}
