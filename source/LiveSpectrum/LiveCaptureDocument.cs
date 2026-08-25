using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// How a spatially averaged magnitude curve was obtained.
/// </summary>
/// <remarks>
/// The consumers of such a curve — the Virtual DSP hybrid view and the EQ Wizard —
/// are deliberately blind to this: a spatial average is a spatial average, and the
/// arithmetic that puts a channel's DSP chain on top of it is identical whichever
/// way the positions were sampled. The method is recorded so a set can be checked
/// for consistency and so a future microphone array does not need a second document
/// type; it is NOT something the hybrid maths branches on.
/// </remarks>
public enum SpatialAverageMethod
{
    /// <summary>One microphone walked through the listening volume.</summary>
    MovingMic
}

/// <summary>
/// Everything needed to re-run the analyzer's display pipeline over a stored
/// spectrum, and to judge whether two captures belong in the same set.
/// </summary>
/// <remarks>
/// This is not provenance decoration. The slope compensation a reference-free
/// capture carries is a CURVE, not a slope, and its shape depends on the frame
/// length, the window and the rate together: the same "compensation off" is worth
/// −13.3 dB at 20 Hz on a 16384-frame Hann capture at 96 kHz and −7.1 dB on a
/// 32768-frame rectangular one. A stored curve whose recipe is missing therefore
/// cannot be corrected, only guessed at — which is how a smooth, plausible-looking
/// bass tilt gets equalized out of a system that never had it.
/// </remarks>
public sealed class LiveCaptureRecipe
{
    public LiveAnalysisMode AnalysisMode { get; set; } = LiveAnalysisMode.Mmm;
    public int SampleRateHz { get; set; }

    /// <summary>Analysis frame length in samples.</summary>
    public int SequenceLength { get; set; }

    /// <summary>
    /// The frame length in milliseconds — <see cref="SequenceLength"/> over
    /// <see cref="SampleRateHz"/>, stored because it, not the sample count, is what
    /// sets the resolution: a rectangular window resolves 2/T hertz whatever the
    /// rate. Written for the reader's benefit; the pipeline uses the two integers.
    /// </summary>
    public double FrameMilliseconds { get; set; }

    public WindowType WindowType { get; set; } = WindowType.Rectangular;

    /// <summary>
    /// The window's equivalent noise bandwidth and main-lobe width, in bins. Both
    /// are derivable from <see cref="WindowType"/>, and both are stored anyway: the
    /// band integrator divides by the first and widens its band to the second, so a
    /// reader that reproduces the curve must use the very numbers the capture did,
    /// not whatever the current code would derive.
    /// </summary>
    public double WindowEnbwBins { get; set; }

    public double WindowMainLobeBins { get; set; }

    public int OverlapPercent { get; set; }
    public AveragingSpeed AveragingSpeed { get; set; } = AveragingSpeed.Infinite;

    /// <summary>Analysis frames integrated into the stored spectrum.</summary>
    public int AveragedFrameCount { get; set; }

    /// <summary>
    /// How long the pass ran, in seconds — frames times the hop. The honest measure
    /// of a moving-microphone capture: a spatial average over a path walked in ten
    /// seconds is a different measurement from the same path walked in ninety.
    /// </summary>
    public double IntegratedSeconds { get; set; }

    public NoiseColor NoiseColor { get; set; } = NoiseColor.PinkPeriodic;

    /// <summary>Whether the excitation's own spectral tilt was compensated.</summary>
    public bool SlopeCompensation { get; set; }

    public MagnitudeScale MagnitudeScale { get; set; } = MagnitudeScale.SoundPressureLevel;

    /// <summary>
    /// The dB offset that lifted the curve to absolute SPL, or null when the capture
    /// had no SPL anchor. Null is a perfectly good capture: the band-power rendering
    /// is what a spatial average needs, and a whole set is levelled against the
    /// impulse responses by one common offset later. It only means captures from
    /// different analyzer sessions cannot be mixed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SplAnchorOffsetDb { get; set; }

    /// <summary>Display smoothing baked into <see cref="LiveCaptureDocument.CurveDb"/>.</summary>
    public int SmoothingCode { get; set; }

    // --- the protective high-pass, which lives in the user's own DSP -------------

    /// <summary>
    /// The protective high-pass configured between the sound card output and the
    /// loudspeaker at capture time, mirrored from the measurement settings.
    /// </summary>
    /// <remarks>
    /// It matters because it is in the HARDWARE, ahead of everything: a sweep has it
    /// divided back out of the transfer impulse response, while a reference-free
    /// capture carries it. Compared against a tweeter's own impulse response, an
    /// uncompensated capture is low by the whole slope of the filter — 24 dB at an
    /// octave below a 2 kHz / 24 dB per octave corner.
    /// </remarks>
    public ProtectiveHighPassKind ProtectiveHighPassKind { get; set; }

    public double ProtectiveHighPassFrequencyHz { get; set; } = 2_000.0;
    public int ProtectiveHighPassSlopeDbPerOctave { get; set; } = 24;

    /// <summary>
    /// Whether two captures may be levelled by one common offset: the same analyzer
    /// configuration, the same excitation and the same corrections.
    /// </summary>
    /// <remarks>
    /// The rule lives with the recipe that defines it, but nothing enforces it yet —
    /// the consumer that checks a whole set against the impulse responses is not
    /// built. Kept here rather than deferred because the fields that must agree are
    /// exactly what this format knows, and the tests beside it are where that
    /// knowledge is written down.
    /// <para>
    /// The protective high-pass is deliberately NOT compared. It describes the
    /// CHANNEL's own hardware path — a tweeter has one and a subwoofer does not —
    /// and each capture has its own divided back out, so two channels filtered
    /// differently are still on the same footing afterwards. Comparing it rejected a
    /// perfectly good seven-channel set for the one difference that was physically
    /// correct.
    /// </para>
    /// </remarks>
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
}

/// <summary>
/// One reference-free capture, stored whole: the accumulated spectrum it was drawn
/// from, the recipe that turns that spectrum back into the curve, the corrections
/// already applied, and the curve as drawn.
/// </summary>
/// <remarks>
/// One payload, several containers — a standalone file today, an overlay slot and a
/// Virtual DSP channel attachment next — so there is one parser, one validator and
/// one version story rather than three that drift.
/// <para>
/// The raw form is stored as FFT BINS, not as the drawn curve. A dB SPL trace is a
/// band-power integral over a fixed 1/12-octave band, clamped to where a whole band
/// fits inside the resolved spectrum; re-gridding the drawn curve would hold its
/// lowest band down to 20 Hz and invent a bass tail the analyzer never measured. The
/// bins are what the pipeline consumes, so they are what makes a capture
/// re-renderable at another smoothing or under another calibration.
/// </para>
/// <para>
/// The corrections are stored BOTH as recipe fields and as the applied arrays, for
/// the same reason an overlay freezes its calibration per drawn point: a reader that
/// cannot reproduce the pipeline can still undo exactly what was applied.
/// </para>
/// </remarks>
public sealed class LiveCaptureDocument
{
    public const string CurrentFormat = "resonalyze-live-capture";
    public const int CurrentVersion = 1;

    /// <summary>Points on the drawn curve, and on each applied-correction array.</summary>
    public const int CurvePointCount = 1024;

    /// <summary>
    /// Bins above this are dropped: nothing above the audible band is ever read, and
    /// at 96 kHz and up they are most of the array. The cut never moves the curve —
    /// the band integrator's upper clamp still lands above 20 kHz.
    /// </summary>
    public const double StoredSpectrumCeilingHz = 24_000.0;

    /// <summary>Level written for a bin with no energy at all, in dB.</summary>
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

    /// <summary>
    /// The format marker. Empty by default, deliberately: a JSON file that simply
    /// has no <c>format</c> property must FAIL the "is this a capture?" gate, and a
    /// default of <see cref="CurrentFormat"/> let every such file through it to die
    /// later on a confusing complaint about its recipe or curve length.
    /// </summary>
    public string Format { get; set; } = string.Empty;
    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset SavedAtUtc { get; set; }

    /// <summary>What the capture is of — a channel name, typically.</summary>
    public string Title { get; set; } = string.Empty;

    public SpatialAverageMethod Method { get; set; } = SpatialAverageMethod.MovingMic;

    /// <summary>
    /// The analyzer configuration this capture was taken under. Recorded so a set can
    /// later be checked for having been taken in one session; nothing reads it yet,
    /// and it is persisted now because a capture that did not record it could never
    /// be checked afterwards. See NoiseMeasurement.CaptureSessionId.
    /// </summary>
    public Guid CaptureSessionId { get; set; }

    public LiveCaptureRecipe Recipe { get; set; } = new();

    /// <summary>
    /// The microphone calibration in force, as the curve rather than an id — the same
    /// call the Virtual DSP session makes, and for the same reason: an id means
    /// nothing on a machine that has never seen that file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VirtualCrossoverCalibrationSettings? Calibration { get; set; }

    /// <summary>
    /// The accumulated amplitude spectrum, in dB per FFT bin, indexed from bin 0
    /// (DC, always silent) up to <see cref="StoredSpectrumCeilingHz"/>. Indexing from
    /// zero is deliberate: the band integrator addresses bins by index, so a trimmed
    /// low end would silently shift every band.
    /// </summary>
    public double[] SpectrumDb { get; set; } = [];

    /// <summary>The curve as drawn, in dB, on the grid below.</summary>
    public double[] CurveDb { get; set; } = [];

    /// <summary>
    /// First and last frequency of the drawn grid, which is logarithmic between them.
    /// Stored rather than assumed: the band integrator bounds the grid to where a
    /// whole band fits inside the resolved spectrum, so the start moves with the
    /// frame and the window.
    /// </summary>
    public double GridStartHz { get; set; }

    public double GridStopHz { get; set; }

    /// <summary>
    /// The slope compensation baked into <see cref="CurveDb"/>, per drawn point.
    /// Empty when none was applied.
    /// </summary>
    public double[] TiltCompensationDb { get; set; } = [];

    /// <summary>
    /// The microphone correction in force at each drawn point, in the same sign
    /// convention as <see cref="CalibrationFile.GetDecibelCorrection"/> and as an
    /// overlay's frozen correction — the pipeline SUBTRACTS it from the band level,
    /// so undoing it means adding it back. Empty when no calibration was in force.
    /// </summary>
    public double[] CalibrationCorrectionDb { get; set; } = [];

    /// <summary>
    /// The protective high-pass divided back out of <see cref="CurveDb"/>, per drawn
    /// point, in dB — NaN where the filter took the signal below what could be
    /// recovered. Empty when no such filter was configured.
    /// </summary>
    /// <remarks>
    /// A reference-free capture carries that filter; a swept impulse response has it
    /// removed. Without this the two measurements of one tweeter sit a whole filter
    /// slope apart, which is precisely the smooth, plausible discrepancy this format
    /// exists to keep out. Stored applied, like the corrections above, so a reader
    /// can undo it exactly.
    /// </remarks>
    public double[] ProtectiveHighPassCorrectionDb { get; set; } = [];

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // The writer stamps what it is writing, so no construction site has to
        // remember to — and the reader can then demand the marker explicitly rather
        // than letting a default stand in for a file that never carried one.
        Format = CurrentFormat;
        Validate();
        // Written aside and renamed into place, so an interrupted save leaves the
        // previous capture intact rather than a truncated one.
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

    /// <summary>
    /// Reads <paramref name="path"/> as a capture, or reports that it is not one.
    /// </summary>
    /// <remarks>
    /// The question "whose file is this?" is answered by the file, so a capture opened
    /// through the shared Load button can be routed to the mode it belongs to instead
    /// of being refused for not being an impulse response. Only the FORMAT decides
    /// that: a file that says it is a capture and then fails validation throws, since
    /// a corrupt capture is a real error and must not be silently offered to the next
    /// reader as something else.
    /// </remarks>
    public static bool TryLoad(string path, out LiveCaptureDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        document = null!;
        LiveCaptureDocument? parsed;
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            parsed = JsonSerializer.Deserialize<LiveCaptureDocument>(stream, SerializerOptions);
        }
        catch (JsonException)
        {
            // Not JSON we can read at all, so certainly not ours to claim.
            return false;
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

    /// <summary>
    /// The accumulated amplitude spectrum as this format stores it: dB per bin, from
    /// bin 0 up to <see cref="StoredSpectrumCeilingHz"/>.
    /// </summary>
    /// <remarks>
    /// Indexing starts at DC and is never trimmed at the low end: the band integrator
    /// addresses bins by index, so dropping the first ones would shift every band it
    /// reads. It lives here rather than beside its caller because it is the inverse of
    /// <see cref="ToAmplitudeSpectrum"/> — the two define the storage between them,
    /// and a test that re-implemented this half was already checking a copy of the
    /// rule instead of the rule.
    /// </remarks>
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

    /// <summary>
    /// The stored spectrum as linear amplitude per bin, ready for the band
    /// integrator — the inverse of <see cref="StoreSpectrumBins"/>.
    /// </summary>
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

        // A capture with no bins cannot be re-rendered at another smoothing or under
        // another calibration, which is the whole point of storing one.
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

        // Each applied correction is either absent or aligned one-to-one with the
        // drawn curve; a mismatched length would silently offset every point.
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

        // The BINS must all be real numbers — they are the measurement. The drawn
        // curve may legitimately hold NaN: where the protective high-pass took the
        // signal below what the compensation can recover there is nothing to plot,
        // and a break in the line is the honest answer where a very negative level
        // would be a fabricated one.
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
