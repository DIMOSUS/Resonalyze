using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze;

/// <summary>
/// Versioned, human-readable representation of a captured impulse response.
/// </summary>
public sealed class ImpulseResponseFile
{
    public const string CurrentFormat = "resonalyze-impulse-response";
    public const int CurrentVersion = 7;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        // An array microphone's curve carries NaN where the sweep never reached —
        // a load-bearing value, not a defect: the band was not measured, and the
        // one thing it must not become is a very low level an equalizer would try
        // to fill. The same setting the live-capture format uses for the same
        // reason; it only affects non-finite values, so every ordinary number is
        // written exactly as before.
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
    // Legacy field: the sweep used to be defined by an octave count with the top
    // pinned to Nyquist. Kept ONLY so pre-band files deserialize; the band is now
    // stored explicitly in LowFrequencyHz/HighFrequencyHz (see ResolveSweepBand).
    public int Octaves { get; set; }
    public double LowFrequencyHz { get; set; }
    public double HighFrequencyHz { get; set; }
    // The band the sweep actually swept, which is wider than the requested one
    // and is what the harmonic geometry of this IR is keyed to. Written since the
    // band-based generator; absent files fall back through ResolveAchievedSweepBand.
    public double AchievedLowFrequencyHz { get; set; }
    public double AchievedHighFrequencyHz { get; set; }
    // The band the sweep excited at FULL amplitude, which is what the measurement may
    // be READ over; the achieved pair above is what it reaches, guard bands included,
    // and the harmonic geometry needs that one. Zero in a file written before this was
    // recorded, where the reader falls back to the achieved band.
    public double MeasuredLowFrequencyHz { get; set; }
    public double MeasuredHighFrequencyHz { get; set; }
    public double SweepDurationSeconds { get; set; }
    public PlaybackChannel PlayChannel { get; set; }
    public SweepMeasurementMode MeasurementMode { get; set; } =
        SweepMeasurementMode.SweepDeconvolution;

    /// <summary>
    /// What this result's arrival time is referenced to. Absent from files
    /// written before sweeps could be imported, and the default is right for
    /// every one of them: they were all measured against their own loopback.
    /// </summary>
    public TimingReference TimingReference { get; set; } =
        TimingReference.SynchronizedLoopback;
    public int SweepDeconvolutionPeakIndex { get; set; }
    public int AverageRunCount { get; set; } = 1;
    public int AcceptedAverageRunCount { get; set; } = 1;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AudioSessionFileEntry? AudioSession { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TransferPeakIndex { get; set; }

    // The SPL calibration in effect when the measurement ran. Its microphone-side
    // offset, combined with this file's own loopback levels, is what later places
    // the response on an SPL axis.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SplCalibration? SplCalibration { get; set; }

    /// <summary>
    /// The protective high-pass that was divided out of the transfer impulse
    /// response, or null when the file predates this record.
    /// </summary>
    /// <remarks>
    /// Null and <see cref="ProtectiveHighPassKind.Off"/> are DIFFERENT answers, which
    /// is why this is a nullable entry rather than three scalars: "no filter" can be
    /// checked against a reference-free capture that carries one, while "not recorded"
    /// cannot, and silently treating the second as the first would pass a tweeter
    /// whose two measurements sit a whole filter slope apart.
    /// <para>
    /// The setting itself lives in the application's measurement options, so until
    /// now nothing tied a saved impulse response to the filter it was corrected for.
    /// Deliberately NOT a format version bump: the field is additive and optional, and
    /// bumping would make every file this build writes unreadable to older ones for
    /// the sake of metadata they would ignore anyway.
    /// </para>
    /// </remarks>
    public ProtectiveHighPassFileEntry? ProtectiveHighPass { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LevelSnapshotFileEntry? MicrophoneLevels { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LevelSnapshotFileEntry? LoopbackLevels { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PreviewFrequencyResponseFileEntry? PreviewFrequencyResponse { get; set; }

    /// <summary>
    /// The microphone calibration in force when this response was measured, as a
    /// CURVE rather than as the name of a file only this machine has.
    /// </summary>
    /// <remarks>
    /// A measurement is not portable without it. The impulse response is raw — no
    /// calibration is ever baked into one — so a recipient who does not have the
    /// author's calibration file sees a different curve from the author's, and
    /// nothing in the file used to say so. Carried the same way a Virtual DSP
    /// session carries its own (see <c>VirtualCrossoverCalibrationSettings</c>):
    /// the curve is the truth and the name is a hint, because two machines' lists
    /// mint their own ids.
    /// <para>
    /// Additive and optional, like <see cref="ProtectiveHighPass"/>, and for the
    /// same reason: bumping the version would make every file this build writes
    /// unreadable to older ones over metadata they would ignore.
    /// </para>
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VirtualCrossoverCalibrationSettings? MicrophoneCalibration { get; set; }

    /// <summary>
    /// The spatially averaged microphones recorded alongside this measurement,
    /// or null when it was made with one microphone.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArrayMicrophonesFileEntry? ArrayMicrophones { get; set; }

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

        // Stamp the calibration frozen onto this result at run time (not the current
        // configured one), and only when it belongs to this measurement's own input.
        // A calibration left over from a different device/rate/bits/channel would
        // otherwise be trusted on reload (loaded files skip the live match) and show
        // a confidently wrong dB SPL offset.
        SplCalibration? splCalibration =
            measurement.MeasurementSplCalibration is { } anchor && measurement.InputMatches(anchor)
                ? anchor
                : null;

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
            // The sweep that produced this IR, which for a re-saved measurement is
            // longer than the one rebuilt on load if it outran the generation cap.
            SweepDurationSeconds = measurement.AchievedSweepDurationSeconds,
            PlayChannel = measurement.PlaybackChannel,
            MeasurementMode = measurement.MeasurementMode,
            TimingReference = measurement.TimingReference,
            SweepDeconvolutionPeakIndex = sweepDeconvolution.PeakIndex,
            AverageRunCount = measurement.AverageRunCount,
            AcceptedAverageRunCount = measurement.AcceptedAverageRunCount,
            SplCalibration = splCalibration,
            // The filter that belongs to THIS result — snapshotted at run start, or
            // carried in from the file it was loaded from — never the app's current
            // setting. Recorded whether or not it is enabled, because "Off" is an
            // answer a later consistency check can use and a missing record is not;
            // null stays null, so re-saving a response measured before this existed
            // does not invent a filter for it.
            ProtectiveHighPass = measurement.MeasurementProtectiveHighPass is { } filter
                ? ProtectiveHighPassFileEntry.From(filter)
                : null,
            AudioSession = CreateAudioSessionFileEntry(
                measurement.LastAudioSessionDiagnostics,
                measurement.SampleRate,
                measurement.Bits),
            TransferPeakIndex = transferPeakIndex,
            MicrophoneLevels = microphoneLevels,
            LoopbackLevels = loopbackLevels,
            MicrophoneCalibration = measurement.MeasurementMicrophoneCalibration,
            ArrayMicrophones = ArrayMicrophonesFileEntry.From(measurement.ArrayMicrophones),
            PreviewFrequencyResponse = CreatePreviewFileEntry(
                MeasurementHistoryPreviewBuilder.Build(
                    sweepImpulseResponse,
                    sweepDeconvolution.PeakIndex,
                    measurement.SampleRate,
                    measurement.MeasurementMode,
                    transfer?.ImpulseResponse,
                    transferPeakIndex,
                    MeasuredBand.Resolve(
                        measurement.MeasurementProtectiveHighPass,
                        measurement.MeasuredLowFrequencyHz,
                        measurement.MeasuredHighFrequencyHz,
                        measurement.SampleRate))),
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

    /// <summary>
    /// The sweep band that was REQUESTED. Pre-band files carry only an octave
    /// count, whose sweep ran from Nyquist / 2^octaves up to Nyquist; for those
    /// there was no separate request, so the band they swept is returned.
    /// </summary>
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

    /// <summary>
    /// The band the sweep ACTUALLY swept — the one harmonic geometry is keyed to,
    /// which is wider than the request by the guard bands the fades live in.
    /// </summary>
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
            // Written by the band-based generator before the achieved band was
            // stored: it is deterministic, so re-derive rather than mistake the
            // request for what was swept.
            ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(
                lowFrequencyHz,
                highFrequencyHz,
                sweepDurationSeconds,
                sampleRate);
            return spec.IsValid
                ? (spec.LowFrequencyHz, spec.HighFrequencyHz)
                : (lowFrequencyHz, highFrequencyHz);
        }
        // Pre-band file: the octave count describes the swept band directly.
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
            // A gap is a legitimate value here — it is how a band the sweep never
            // reached is recorded — so only an infinity is refused.
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

    /// <summary>
    /// The protective high-pass configured in the user's own DSP between the sound
    /// card output and the loudspeaker, as it stood when this response was measured.
    /// </summary>
    public sealed class ProtectiveHighPassFileEntry
    {
        public ProtectiveHighPassKind Kind { get; set; }
        public double FrequencyHz { get; set; } = 2_000.0;
        public int SlopeDbPerOctave { get; set; } = 24;

        /// <summary>The configuration this record stands for.</summary>
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

    /// <summary>
    /// One microphone's contribution to this measurement's spatial average.
    /// </summary>
    /// <param name="LevelsDb">
    /// The steady-state transfer level on the grid, RAW: the protective high-pass
    /// is divided out, the microphone calibration is NOT applied. Storing it
    /// uncalibrated is what lets a reader change the calibration, and what lets
    /// the frequency-response view's own calibration switch mean something for
    /// these curves too.
    /// </param>
    public sealed class ArrayMicrophoneFileEntry
    {
        public int ChannelOffset { get; set; }

        /// <summary>
        /// Whether this is the microphone that also produced the impulse response
        /// — the one the others are levelled onto.
        /// </summary>
        public bool IsMeasurementMicrophone { get; set; }

        /// <summary>What the user called the position, if anything.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Note { get; set; }

        /// <summary>How many of the measurement's runs this microphone survived.</summary>
        public int AcceptedRunCount { get; set; }

        public double[] LevelsDb { get; set; } = Array.Empty<double>();

        /// <summary>
        /// This microphone's own calibration curve, or null when it was recorded
        /// uncalibrated. Per microphone because an array is not required to be one
        /// model of capsule.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public VirtualCrossoverCalibrationSettings? Calibration { get; set; }
    }

    /// <summary>
    /// The measurement's spatial-average microphones and the grid their curves
    /// live on.
    /// </summary>
    /// <remarks>
    /// The grid is stored rather than assumed even though it is a constant today.
    /// A stored curve outlives the code that wrote it, and a grid that silently
    /// changed under a reader would shift every level in frequency while still
    /// looking like a perfectly ordinary response.
    /// </remarks>
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
        /// The stored curves, or none when they are not on the grid this build reads.
        /// </summary>
        /// <remarks>
        /// The endpoints travel with the file for exactly this check, and it was the
        /// one thing missing: a file whose grid ran from somewhere else would have
        /// been read band for band on this one, shifting every position in FREQUENCY
        /// with nothing to show for it. The band COUNT is checked further down, where
        /// the curves are placed; the ends have to be checked here, because by then
        /// they are gone.
        /// <para>
        /// None rather than a refusal to open the file: the impulse response beside
        /// the array is perfectly readable, and the tools that wanted the array say
        /// out loud when a channel is drawn from its point measurement instead.
        /// </para>
        /// </remarks>
        internal IReadOnlyList<ArrayMicrophoneCurve> ToCurves()
        {
            IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
            return SameFrequency(GridStartHz, grid[0]) && SameFrequency(GridStopHz, grid[^1])
                ? BuildCurves()
                : [];
        }

        // The two constructions of one logarithmic grid differ in their last ULPs
        // (20 against 20.000000000000004), so this asks whether they are the same
        // grid rather than the same double.
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
