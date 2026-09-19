using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze;

public sealed class ImpulseResponseFile
{
    public const string CurrentFormat = "resonalyze-impulse-response";
    // v8: sample arrays as base64 float32. Later versions are refused before deserializing. See docs/tech/sweep-measurement.md#impulse-response-file-format.
    public const int CurrentVersion = 8;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        // Array curves carry NaN where the sweep never reached; it must not become a low level an EQ would fill.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
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
    // Legacy octave count (top pinned to Nyquist), kept only so pre-band files deserialize; see ResolveSweepBand.
    public int Octaves { get; set; }
    public double LowFrequencyHz { get; set; }
    public double HighFrequencyHz { get; set; }
    // Band actually swept (wider than requested); harmonic geometry is keyed to it. Missing: ResolveAchievedSweepBand.
    public double AchievedLowFrequencyHz { get; set; }
    public double AchievedHighFrequencyHz { get; set; }
    // Full-amplitude band the measurement may be read over; zero in older files (reader falls back to achieved).
    public double MeasuredLowFrequencyHz { get; set; }
    public double MeasuredHighFrequencyHz { get; set; }
    // Unlike SavedAtUtc, never re-stamped by a save; default in older files (reader falls back to the save stamp).
    public DateTimeOffset MeasuredAtUtc { get; set; }
    public double SweepDurationSeconds { get; set; }
    public PlaybackChannel PlayChannel { get; set; }
    public SweepMeasurementMode MeasurementMode { get; set; } =
        SweepMeasurementMode.SweepDeconvolution;

    /// <summary>Default is right for files predating import: all were loopback-referenced.</summary>
    public TimingReference TimingReference { get; set; } =
        TimingReference.SynchronizedLoopback;
    public int SweepDeconvolutionPeakIndex { get; set; }
    public int AverageRunCount { get; set; } = 1;
    public int AcceptedAverageRunCount { get; set; } = 1;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AudioSessionFileEntry? AudioSession { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TransferPeakIndex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SplCalibration? SplCalibration { get; set; }

    /// <summary>Protective high-pass divided out of the transfer IR; null (not recorded) differs from Off.</summary>
    /// <remarks>Additive, so no version bump: older builds would reject files over metadata they ignore.</remarks>
    public ProtectiveHighPassFileEntry? ProtectiveHighPass { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LevelSnapshotFileEntry? MicrophoneLevels { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LevelSnapshotFileEntry? LoopbackLevels { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PreviewFrequencyResponseFileEntry? PreviewFrequencyResponse { get; set; }

    /// <summary>Mic calibration as a curve, so the raw IR is portable; the name is only a hint (ids differ per machine).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VirtualCrossoverCalibrationSettings? MicrophoneCalibration { get; set; }

    /// <summary>Null for a single-microphone measurement.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArrayMicrophonesFileEntry? ArrayMicrophones { get; set; }

    // The bulk of the file: base64 float32 LE; pre-v8 number arrays still read. Doubles in memory either way.
    [JsonConverter(typeof(Float32SampleArrayJsonConverter))]
    public double[] SweepDeconvolutionRealSamples { get; set; } = Array.Empty<double>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(Float32SampleArrayJsonConverter))]
    public double[]? SweepDeconvolutionImaginarySamples { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(Float32SampleArrayJsonConverter))]
    public double[]? TransferRealSamples { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(Float32SampleArrayJsonConverter))]
    public double[]? TransferImaginarySamples { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(Float32SampleArrayJsonConverter))]
    public double[]? TransferCoherence { get; set; }

    internal static ImpulseResponseFile From(MeasurementResult measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        MeasurementImpulseResponse sweepDeconvolution = measurement.SweepDeconvolution;
        MeasurementImpulseResponse? transfer = measurement.Transfer;
        Complex[] sweepImpulseResponse = sweepDeconvolution.ImpulseResponse;

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

        return new ImpulseResponseFile
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            SampleRate = measurement.SampleRate,
            Bits = measurement.Bits,
            LowFrequencyHz = measurement.LowFrequencyHz,
            HighFrequencyHz = measurement.HighFrequencyHz,
            AchievedLowFrequencyHz = measurement.AchievedLowFrequencyHz,
            AchievedHighFrequencyHz = measurement.AchievedHighFrequencyHz,
            MeasuredLowFrequencyHz = measurement.MeasuredLowFrequencyHz,
            MeasuredHighFrequencyHz = measurement.MeasuredHighFrequencyHz,
            MeasuredAtUtc = measurement.MeasuredAtUtc,
            // May exceed the length rebuilt on load if it outran the generation cap.
            SweepDurationSeconds = measurement.SweepSampleDurationSeconds,
            PlayChannel = measurement.PlaybackChannel,
            MeasurementMode = measurement.MeasurementMode,
            TimingReference = measurement.TimingReference,
            SweepDeconvolutionPeakIndex = sweepDeconvolution.PeakIndex,
            AverageRunCount = measurement.AverageRunCount,
            AcceptedAverageRunCount = measurement.AcceptedAverageRunCount,
            SplCalibration = measurement.SplCalibration,
            // This result's filter, never the current setting; Off is recorded, null stays null.
            ProtectiveHighPass = measurement.ProtectiveHighPass is { } filter
                ? ProtectiveHighPassFileEntry.From(filter)
                : null,
            AudioSession = measurement.AudioSession,
            TransferPeakIndex = transferPeakIndex,
            MicrophoneLevels = CreateLevelSnapshotFileEntry(measurement.Levels.Microphone),
            LoopbackLevels = CreateLevelSnapshotFileEntry(measurement.Levels.Loopback),
            MicrophoneCalibration = measurement.MicrophoneCalibration,
            ArrayMicrophones = ArrayMicrophonesFileEntry.From(measurement.ArrayMicrophones),
            PreviewFrequencyResponse = CreatePreviewFileEntry(
                MeasurementHistoryPreviewBuilder.Build(measurement)),
            SweepDeconvolutionRealSamples = sweepRealSamples,
            SweepDeconvolutionImaginarySamples = sweepImaginarySamples,
            TransferRealSamples = transferRealSamples,
            TransferImaginarySamples = transferImaginarySamples,
            TransferCoherence = measurement.TransferCoherence?.ToArray()
        };
    }

    /// <summary>The result this file holds, read as it was measured; older files fill the bands they never wrote.</summary>
    internal MeasurementResult ToResult()
    {
        (double lowHz, double highHz) = ResolveSweepBand();
        (double achievedLowHz, double achievedHighHz) = ResolveAchievedSweepBand();
        (double measuredLowHz, double measuredHighHz) = MeasurementResult.ResolveMeasuredBand(
            MeasuredLowFrequencyHz,
            MeasuredHighFrequencyHz,
            achievedLowHz,
            achievedHighHz);
        Complex[]? transfer = GetTransferImpulseResponse();
        int averageRunCount = Math.Clamp(AverageRunCount, 1, 64);
        return new MeasurementResult
        {
            SampleRate = SampleRate,
            Bits = Bits,
            PlaybackChannel = Enum.IsDefined(PlayChannel) ? PlayChannel : PlaybackChannel.Mono,
            LowFrequencyHz = lowHz,
            HighFrequencyHz = highHz,
            AchievedLowFrequencyHz = achievedLowHz,
            AchievedHighFrequencyHz = achievedHighHz,
            MeasuredLowFrequencyHz = measuredLowHz,
            MeasuredHighFrequencyHz = measuredHighHz,
            SweepDurationSeconds = SweepDurationSeconds,
            // Never re-stamped: a file saved today was still measured whenever it was measured.
            MeasuredAtUtc = MeasuredAtUtc > DateTimeOffset.UnixEpoch ? MeasuredAtUtc : SavedAtUtc,
            MeasurementMode = MeasurementMode,
            TimingReference = TimingReference,
            SweepDeconvolution = new MeasurementImpulseResponse(
                GetSweepDeconvolutionImpulseResponse(),
                SweepDeconvolutionPeakIndex),
            Transfer = transfer != null
                ? new MeasurementImpulseResponse(transfer, TransferPeakIndex ?? -1)
                : null,
            TransferCoherence = TransferCoherence?.ToArray(),
            AverageRunCount = averageRunCount,
            AcceptedAverageRunCount = Math.Clamp(AcceptedAverageRunCount, 1, averageRunCount),
            Levels = GetMeterSnapshot(),
            SplCalibration = SplCalibration,
            MicrophoneCalibration = MicrophoneCalibration,
            ProtectiveHighPass = ProtectiveHighPass?.ToConfiguration(),
            ArrayMicrophones = ArrayMicrophones?.ToCurves() ?? [],
            AudioSession = AudioSession
        }.Validated();
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate();

        // Temp file then atomic move: writing the target directly truncates it before a possible failure.
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

        // Refuse a future version before deserializing, or its new representation reads as a parse error (v7 on v8 base64).
        (string? format, int? version) = JsonFormatMarker.ReadWithVersion(path);
        if (string.Equals(format, CurrentFormat, StringComparison.Ordinal) &&
            version is > CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported impulse response version {version}.");
        }

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

    /// <summary>Requested band; pre-band files return their swept band Nyquist / 2^octaves .. Nyquist.</summary>
    public (double LowHz, double HighHz) ResolveSweepBand() =>
        ResolveSweepBand(LowFrequencyHz, HighFrequencyHz, Octaves, SampleRate);

    internal static (double LowHz, double HighHz) ResolveSweepBand(
        double lowFrequencyHz,
        double highFrequencyHz,
        int octaves,
        int sampleRate)
    {
        if (lowFrequencyHz > 0 && highFrequencyHz > lowFrequencyHz)
        {
            return (lowFrequencyHz, highFrequencyHz);
        }
        double nyquist = sampleRate / 2.0;
        double octaveSpan = octaves > 0 ? octaves : 12;
        return (nyquist / Math.Pow(2.0, octaveSpan), nyquist);
    }

    /// <summary>Band actually swept, including the fade guard bands; harmonic geometry is keyed to it.</summary>
    public (double LowHz, double HighHz) ResolveAchievedSweepBand() =>
        ResolveAchievedSweepBand(
            AchievedLowFrequencyHz,
            AchievedHighFrequencyHz,
            LowFrequencyHz,
            HighFrequencyHz,
            Octaves,
            SampleRate,
            SweepDurationSeconds);

    internal static (double LowHz, double HighHz) ResolveAchievedSweepBand(
        double achievedLowFrequencyHz,
        double achievedHighFrequencyHz,
        double lowFrequencyHz,
        double highFrequencyHz,
        int octaves,
        int sampleRate,
        double sweepDurationSeconds)
    {
        if (achievedLowFrequencyHz > 0 && achievedHighFrequencyHz > achievedLowFrequencyHz)
        {
            return (achievedLowFrequencyHz, achievedHighFrequencyHz);
        }
        if (lowFrequencyHz > 0 && highFrequencyHz > lowFrequencyHz)
        {
            // Band-generator file without stored achieved band: the generator is deterministic, so re-derive.
            ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(
                lowFrequencyHz,
                highFrequencyHz,
                sweepDurationSeconds,
                sampleRate);
            return spec.IsValid
                ? (spec.LowFrequencyHz, spec.HighFrequencyHz)
                : (lowFrequencyHz, highFrequencyHz);
        }
        return ResolveSweepBand(lowFrequencyHz, highFrequencyHz, octaves, sampleRate);
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
        if (LowFrequencyHz > 0 || HighFrequencyHz > 0)
        {
            if (!double.IsFinite(LowFrequencyHz) ||
                !double.IsFinite(HighFrequencyHz) ||
                LowFrequencyHz <= 0 ||
                HighFrequencyHz <= LowFrequencyHz ||
                HighFrequencyHz > SampleRate / 2.0 * (1.0 + 1e-3))
            {
                throw new InvalidDataException("The sweep frequency band is invalid.");
            }
        }
        else if (Octaves is < 1 or > 24)
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
        if (!Enum.IsDefined(TimingReference))
        {
            throw new InvalidDataException("The timing reference is invalid.");
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
            // FFT length is reconstructed from coherence, so exactly N/2 + 1 bins are required.
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
        MicrophoneCalibration?.Validate();
        ValidateArrayMicrophones(ArrayMicrophones);
        ValidateAudioSession(AudioSession);
        SplCalibration?.Validate();
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

    private static void ValidateArrayMicrophones(ArrayMicrophonesFileEntry? array)
    {
        if (array == null)
        {
            return;
        }
        if (!double.IsFinite(array.GridStartHz) ||
            !double.IsFinite(array.GridStopHz) ||
            array.GridStartHz <= 0 ||
            array.GridStopHz <= array.GridStartHz)
        {
            throw new InvalidDataException("The array microphone grid is invalid.");
        }
        if (array.Microphones.Count == 0)
        {
            throw new InvalidDataException("The array microphone set is empty.");
        }

        foreach (ArrayMicrophoneFileEntry microphone in array.Microphones)
        {
            if (microphone.ChannelOffset < 0)
            {
                throw new InvalidDataException("An array microphone channel is negative.");
            }
            if (microphone.LevelsDb.Length < 2)
            {
                throw new InvalidDataException("An array microphone curve is too short.");
            }
            if (microphone.AcceptedRunCount < 0)
            {
                throw new InvalidDataException(
                    "An array microphone accepted-run count is negative.");
            }
            // NaN gaps are legitimate (unswept bands); only infinity is refused.
            foreach (double level in microphone.LevelsDb)
            {
                if (double.IsInfinity(level))
                {
                    throw new InvalidDataException(
                        "An array microphone curve contains an infinite level.");
                }
            }

            microphone.Calibration?.Validate();
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

    /// <summary>Protective high-pass in the user's DSP between sound card and loudspeaker, as measured.</summary>
    public sealed class ProtectiveHighPassFileEntry
    {
        public ProtectiveHighPassKind Kind { get; set; }
        public double FrequencyHz { get; set; } = 2_000.0;
        public int SlopeDbPerOctave { get; set; } = 24;

        public ProtectiveHighPassConfiguration ToConfiguration() =>
            new(Kind, FrequencyHz, SlopeDbPerOctave);

        public static ProtectiveHighPassFileEntry From(
            ProtectiveHighPassConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            return new ProtectiveHighPassFileEntry
            {
                Kind = configuration.Kind,
                FrequencyHz = configuration.FrequencyHz,
                SlopeDbPerOctave = configuration.SlopeDbPerOctave
            };
        }
    }

    /// <summary>One microphone of the spatial average.</summary>
    /// <param name="LevelsDb">Steady-state level on the grid, RAW: high-pass divided out, mic calibration NOT applied (so readers can change it).</param>
    public sealed class ArrayMicrophoneFileEntry
    {
        public int ChannelOffset { get; set; }

        /// <summary>The microphone that produced the IR; others are levelled onto it.</summary>
        public bool IsMeasurementMicrophone { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Note { get; set; }

        public int AcceptedRunCount { get; set; }

        public double[] LevelsDb { get; set; } = Array.Empty<double>();

        /// <summary>Per microphone: an array need not be one capsule model. Null when uncalibrated.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public VirtualCrossoverCalibrationSettings? Calibration { get; set; }
    }

    /// <summary>Array microphones and their grid; the grid is stored because curves outlive the code that wrote them.</summary>
    public sealed class ArrayMicrophonesFileEntry
    {
        public double GridStartHz { get; set; }
        public double GridStopHz { get; set; }
        public List<ArrayMicrophoneFileEntry> Microphones { get; set; } = [];

        internal static ArrayMicrophonesFileEntry? From(
            IReadOnlyList<ArrayMicrophoneCurve> microphones)
        {
            if (microphones.Count == 0)
            {
                return null;
            }

            IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
            return new ArrayMicrophonesFileEntry
            {
                GridStartHz = grid[0],
                GridStopHz = grid[^1],
                Microphones = microphones
                    .Select(microphone => new ArrayMicrophoneFileEntry
                    {
                        ChannelOffset = microphone.ChannelOffset,
                        IsMeasurementMicrophone = microphone.IsMeasurementMicrophone,
                        Note = microphone.Note,
                        AcceptedRunCount = microphone.AcceptedRuns,
                        LevelsDb = microphone.LevelsDb.ToArray(),
                        Calibration = microphone.Calibration
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// Curves, or none when the stored grid endpoints differ from this build's (would shift levels in frequency).
        /// None rather than refusing the file: the IR beside it is readable.
        /// </summary>
        internal IReadOnlyList<ArrayMicrophoneCurve> ToCurves()
        {
            IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
            return SameFrequency(GridStartHz, grid[0]) && SameFrequency(GridStopHz, grid[^1])
                ? BuildCurves()
                : [];
        }

        // Two constructions of one log grid differ in the last ULPs (20 vs 20.000000000000004).
        private static bool SameFrequency(double stored, double expected) =>
            Math.Abs(stored - expected) <= 1e-6 * expected;

        private IReadOnlyList<ArrayMicrophoneCurve> BuildCurves() =>
            Microphones
                .Select(microphone => new ArrayMicrophoneCurve(
                    microphone.ChannelOffset,
                    microphone.IsMeasurementMicrophone,
                    microphone.LevelsDb.ToArray(),
                    microphone.AcceptedRunCount)
                {
                    Note = microphone.Note,
                    Calibration = microphone.Calibration
                })
                .ToList();
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
