using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>How a spatial average was obtained. Consumers never branch on it; it decides set-consistency rules.</summary>
public enum SpatialAverageMethod
{
    MovingMic,

    /// <summary>Several simultaneous microphones; level tethered to the measurement loopback, unlike a moving mic.</summary>
    MicArray
}

/// <summary>Everything needed to re-render a stored spectrum and to judge set membership.</summary>
/// <remarks>Slope compensation is a curve shaped by frame, window and rate; without the recipe it cannot be corrected. See docs/tech/live-spectrum.md#capture-document.</remarks>
public sealed class LiveCaptureRecipe
{
    public LiveAnalysisMode AnalysisMode { get; set; } = LiveAnalysisMode.Mmm;
    public int SampleRateHz { get; set; }

    public int SequenceLength { get; set; }

    /// <summary>Informational: resolution is set by frame duration; the pipeline uses the two integers.</summary>
    public double FrameMilliseconds { get; set; }

    public WindowType WindowType { get; set; } = WindowType.Rectangular;

    /// <summary>ENBW and main-lobe width in bins, stored so a reader reproduces the capture's numbers, not today's derivation.</summary>
    public double WindowEnbwBins { get; set; }

    public double WindowMainLobeBins { get; set; }

    /// <summary>Array microphone count; zero for a moving microphone.</summary>
    public int MicrophoneCount { get; set; }

    public int OverlapPercent { get; set; }
    public AveragingSpeed AveragingSpeed { get; set; } = AveragingSpeed.Infinite;

    public int AveragedFrameCount { get; set; }

    /// <summary>Frames times hop, seconds.</summary>
    public double IntegratedSeconds { get; set; }

    public NoiseColor NoiseColor { get; set; } = NoiseColor.PinkPeriodic;

    public bool SlopeCompensation { get; set; }

    public MagnitudeScale MagnitudeScale { get; set; } = MagnitudeScale.SoundPressureLevel;

    /// <summary>dB offset to absolute SPL, or null without an SPL anchor (then captures from different sessions cannot mix).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SplAnchorOffsetDb { get; set; }

    public int SmoothingCode { get; set; }

    /// <summary>Hardware protective high-pass at capture time: a sweep divides it out, a reference-free capture carries it.</summary>
    public ProtectiveHighPassKind ProtectiveHighPassKind { get; set; }

    public double ProtectiveHighPassFrequencyHz { get; set; } = 2_000.0;
    public int ProtectiveHighPassSlopeDbPerOctave { get; set; } = 24;

    /// <summary>Whether two captures may share one common offset. Protective high-pass is deliberately not compared (it is per-channel hardware, divided out per capture).</summary>
    public bool MatchesSetOf(LiveCaptureRecipe other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return SampleRateHz == other.SampleRateHz &&
            SequenceLength == other.SequenceLength &&
            WindowType == other.WindowType &&
            NoiseColor == other.NoiseColor &&
            SlopeCompensation == other.SlopeCompensation &&
            MagnitudeScale == other.MagnitudeScale;
    }

    /// <summary>The mismatching field phrased for the user, or null when the recipes agree.</summary>
    internal string? DescribeSetMismatch(LiveCaptureRecipe other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (SampleRateHz != other.SampleRateHz)
        {
            return $"sample rate ({other.SampleRateHz} Hz against {SampleRateHz} Hz)";
        }

        if (SequenceLength != other.SequenceLength)
        {
            return $"frame length ({other.SequenceLength} against {SequenceLength})";
        }

        if (WindowType != other.WindowType)
        {
            return $"analysis window ({other.WindowType} against {WindowType})";
        }

        if (NoiseColor != other.NoiseColor)
        {
            return $"excitation ({other.NoiseColor} against {NoiseColor})";
        }

        if (SlopeCompensation != other.SlopeCompensation)
        {
            return "noise-slope compensation (on in one capture, off in the other)";
        }

        return MagnitudeScale != other.MagnitudeScale
            ? $"magnitude scale ({other.MagnitudeScale} against {MagnitudeScale})"
            : null;
    }
}

public readonly record struct LiveCaptureSetVerdict(bool Coherent, string? Reason)
{
    public static LiveCaptureSetVerdict Ok { get; } = new(true, null);

    public static LiveCaptureSetVerdict No(string reason) => new(false, reason);
}

/// <summary>One reference-free capture: stored FFT bins, recipe, applied corrections and the drawn curve. See docs/tech/live-spectrum.md#capture-document.</summary>
public sealed class LiveCaptureDocument
{
    public const string CurrentFormat = "resonalyze-live-capture";
    public const int CurrentVersion = 1;

    public const int CurvePointCount = 1024;

    /// <summary>Bins above this are dropped; the band integrator's upper clamp still lands above 20 kHz.</summary>
    public const double StoredSpectrumCeilingHz = 24_000.0;

    public const double SilentBinDb = -400.0;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Empty by default so a JSON file without a <c>format</c> property fails the capture gate.</summary>
    public string Format { get; set; } = string.Empty;
    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset SavedAtUtc { get; set; }

    public string Title { get; set; } = string.Empty;

    public SpatialAverageMethod Method { get; set; } = SpatialAverageMethod.MovingMic;

    /// <summary>Analyzer session id, persisted for future set checks; nothing reads it yet.</summary>
    public Guid CaptureSessionId { get; set; }

    public LiveCaptureRecipe Recipe { get; set; } = new();

    /// <summary>Calibration stored as the curve, not an id, so it survives on another machine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VirtualCrossoverCalibrationSettings? Calibration { get; set; }

    /// <summary>Accumulated amplitude, dB per bin from bin 0 (never trimmed: the band integrator addresses bins by index).</summary>
    public double[] SpectrumDb { get; set; } = [];

    public double[] CurveDb { get; set; } = [];

    /// <summary>Log grid bounds; stored because the start moves with frame and window.</summary>
    public double GridStartHz { get; set; }

    public double GridStopHz { get; set; }

    public double[] TiltCompensationDb { get; set; } = [];

    /// <summary>Mic correction per drawn point, subtracted by the pipeline (undo by adding back). Empty when none.</summary>
    public double[] CalibrationCorrectionDb { get; set; } = [];

    /// <summary>Correction is an aggregate of several mic files: undoing stays exact, but replacing it with one curve is wrong.</summary>
    public bool CalibrationIsAggregate { get; set; }

    /// <summary>Protective high-pass divided out of <see cref="CurveDb"/>, dB per point; NaN where unrecoverable. Empty when none.</summary>
    public double[] ProtectiveHighPassCorrectionDb { get; set; } = [];

    /// <summary>Whether these captures may share one common offset, and what to say when not.</summary>
    /// <remarks>Recipe must match, and levels must be comparable (one session or all SPL-anchored). Calibration is not compared. See docs/tech/spatial-average.md#coverage-and-set-verdict.</remarks>
    public static LiveCaptureSetVerdict JudgeSet(
        IReadOnlyList<LiveCaptureDocument> captures)
    {
        ArgumentNullException.ThrowIfNull(captures);
        if (captures.Count == 0)
        {
            return LiveCaptureSetVerdict.No("There are no spatial averages to draw.");
        }

        LiveCaptureDocument first = captures[0];
        // One method per set: mixing would need per-channel offsets, which are the spread detector itself.
        if (captures.Any(capture => capture.Method != first.Method))
        {
            return LiveCaptureSetVerdict.No(
                "Some channels carry a moving-microphone pass and some a microphone " +
                "array. They are levelled differently, so one set cannot hold both — " +
                "pick one method for the project.");
        }

        if (first.Method == SpatialAverageMethod.MicArray)
        {
            return JudgeArraySet(captures, first);
        }

        foreach (LiveCaptureDocument capture in captures)
        {
            if (capture.Recipe.MatchesSetOf(first.Recipe))
            {
                continue;
            }

            string? difference = first.Recipe.DescribeSetMismatch(capture.Recipe);
            return LiveCaptureSetVerdict.No(
                $"The captures were not all taken the same way — they differ in " +
                $"{difference ?? "their analyzer recipe"}. Re-take the odd one with " +
                "the settings the others used.");
        }

        bool oneSession = captures.All(
            capture => capture.CaptureSessionId == first.CaptureSessionId);
        if (oneSession ||
            captures.All(capture => capture.Recipe.SplAnchorOffsetDb.HasValue))
        {
            return LiveCaptureSetVerdict.Ok;
        }

        return LiveCaptureSetVerdict.No(
            "The captures come from more than one analyzer session and not all of " +
            "them carry a dB SPL anchor, so nothing vouches for their levels " +
            "matching. Re-take them in one session, or with an SPL calibration in " +
            "force.");
    }

    /// <summary>Array sets: no recipe or session to match (loopback-tethered); high-pass and array composition are not judged here.</summary>
    private static LiveCaptureSetVerdict JudgeArraySet(
        IReadOnlyList<LiveCaptureDocument> captures,
        LiveCaptureDocument first) => LiveCaptureSetVerdict.Ok;

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Format = CurrentFormat;
        Validate();
        AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, this, SerializerOptions));
    }

    public static LiveCaptureDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        LiveCaptureDocument document =
            JsonSerializer.Deserialize<LiveCaptureDocument>(stream, SerializerOptions)
            ?? throw new InvalidDataException("The capture file is empty.");
        document.Validate();
        return document;
    }

    /// <summary>Reads a capture, or returns false when the file declares another format. A capture that then fails to parse or validate throws.</summary>
    public static bool TryLoad(string path, out LiveCaptureDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        document = null!;
        // Declined on the head marker without deserializing: the shared Load path asks every file, including large IRs.
        if (JsonFormatMarker.Read(path) != CurrentFormat)
        {
            return false;
        }

        LiveCaptureDocument? parsed;
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            parsed = JsonSerializer.Deserialize<LiveCaptureDocument>(stream, SerializerOptions);
        }
        catch (JsonException exception)
        {
            // Marker said ours, so this is a damaged capture; returning false would misroute it to the IR loader.
            throw new InvalidDataException(
                $"The capture file could not be read: {exception.Message}", exception);
        }

        if (parsed == null ||
            !string.Equals(parsed.Format, CurrentFormat, StringComparison.Ordinal))
        {
            return false;
        }

        parsed.Validate();
        document = parsed;
        return true;
    }

    /// <summary>Inverse of <see cref="ToAmplitudeSpectrum"/>: dB per bin from bin 0 up to <see cref="StoredSpectrumCeilingHz"/>.</summary>
    public static double[] StoreSpectrumBins(
        double[] amplitudeSpectrum,
        int sequenceLength,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(amplitudeSpectrum);
        double binWidth = (double)sampleRate / sequenceLength;
        int lastBin = Math.Min(
            amplitudeSpectrum.Length - 1,
            Math.Min(
                sequenceLength / 2,
                (int)Math.Ceiling(StoredSpectrumCeilingHz / binWidth)));
        var stored = new double[lastBin + 1];
        for (int bin = 0; bin <= lastBin; bin++)
        {
            double amplitude = amplitudeSpectrum[bin];
            stored[bin] = amplitude > 0
                ? Math.Max(SilentBinDb, DataHelper.AmplitudeToDecibels(amplitude))
                : SilentBinDb;
        }

        return stored;
    }

    /// <summary>The drawn curve as points on its own grid; NaN levels (below the protective high-pass) are kept.</summary>
    public List<SignalPoint> ToCurvePoints()
    {
        var points = new List<SignalPoint>(CurveDb.Length);
        for (int i = 0; i < CurveDb.Length; i++)
        {
            points.Add(new SignalPoint(FrequencyAt(i), CurveDb[i]));
        }

        return points;
    }

    public double FrequencyAt(int index) =>
        CurveDb.Length < 2 || !(GridStartHz > 0) || !(GridStopHz > GridStartHz)
            ? double.NaN
            : GridStartHz * Math.Pow(
                GridStopHz / GridStartHz, index / (CurveDb.Length - 1.0));

    public double IndexOf(double hz) =>
        CurveDb.Length < 2 || !(hz > 0) ||
        !(GridStartHz > 0) || !(GridStopHz > GridStartHz)
            ? double.NaN
            : Math.Log(hz / GridStartHz) /
                Math.Log(GridStopHz / GridStartHz) * (CurveDb.Length - 1);

    public double[] ToAmplitudeSpectrum()
    {
        var amplitude = new double[SpectrumDb.Length];
        for (int bin = 0; bin < amplitude.Length; bin++)
        {
            amplitude[bin] = SpectrumDb[bin] <= SilentBinDb
                ? 0.0
                : DataHelper.DecibelsToAmplitude(SpectrumDb[bin]);
        }

        return amplitude;
    }

    public void Validate()
    {
        if (!string.Equals(Format, CurrentFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The file is not a Resonalyze live capture.");
        }

        if (Version <= 0 || Version > CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported live-capture version {Version}; this build reads up to " +
                $"{CurrentVersion}.");
        }

        if (Recipe == null)
        {
            throw new InvalidDataException("The capture carries no recipe.");
        }

        if (Recipe.SampleRateHz < 1 || Recipe.SequenceLength < 2)
        {
            throw new InvalidDataException("The capture recipe has no usable frame.");
        }

        if (SpectrumDb is not { Length: > 1 })
        {
            throw new InvalidDataException("The capture carries no spectrum.");
        }

        if (SpectrumDb.Length > Recipe.SequenceLength / 2 + 1)
        {
            throw new InvalidDataException(
                "The capture spectrum holds more bins than its frame can produce.");
        }

        if (CurveDb.Length != CurvePointCount)
        {
            throw new InvalidDataException("The capture curve has the wrong length.");
        }

        if (!(GridStartHz > 0) || !(GridStopHz > GridStartHz))
        {
            throw new InvalidDataException("The capture curve has no usable frequency grid.");
        }

        if (TiltCompensationDb.Length is not 0 && TiltCompensationDb.Length != CurvePointCount)
        {
            throw new InvalidDataException("The stored slope compensation is misaligned.");
        }

        if (CalibrationCorrectionDb.Length is not 0 &&
            CalibrationCorrectionDb.Length != CurvePointCount)
        {
            throw new InvalidDataException("The stored calibration correction is misaligned.");
        }

        if (ProtectiveHighPassCorrectionDb.Length is not 0 &&
            ProtectiveHighPassCorrectionDb.Length != CurvePointCount)
        {
            throw new InvalidDataException(
                "The stored protective high-pass correction is misaligned.");
        }

        // Bins must be finite; the curve may hold NaN where the high-pass compensation could not recover signal.
        if (SpectrumDb.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidDataException("The capture spectrum holds a non-finite level.");
        }

        if (CurveDb.Any(double.IsInfinity))
        {
            throw new InvalidDataException("The capture curve holds an infinite level.");
        }

        Title = Title?.Trim() ?? string.Empty;
        Calibration?.Validate();
    }
}
