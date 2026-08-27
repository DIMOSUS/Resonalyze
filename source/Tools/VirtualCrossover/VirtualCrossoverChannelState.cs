using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The resolved source measurement of one SIDE of a channel pair (null while
/// unresolved), plus that side's interactive processed-IR cache. UI-free: the
/// runtime model owns this, so the algorithmic paths never reach a WinForms
/// control to read a channel's measurement state.
/// </summary>
internal sealed class VirtualCrossoverChannelState
{
    private Complex[]? transferImpulseResponse;

    public Complex[]? TransferImpulseResponse
    {
        get => transferImpulseResponse;
        set
        {
            transferImpulseResponse = value;
            ProcessingSource = value == null
                ? null
                : new VirtualCrossoverSourceSnapshot(value);
        }
    }
    public VirtualCrossoverSourceSnapshot? ProcessingSource { get; private set; }

    /// <summary>
    /// The spatially averaged magnitude attached to this side — a stored capture of
    /// this driver, taken with the DSP bypassed. Null when none is attached.
    /// </summary>
    /// <remarks>
    /// Optional refinement, never the basis of anything: every complex computation
    /// here keeps running on the honest impulse response. This curve only replaces
    /// the MAGNITUDE the hybrid view draws, because a point measurement carries dips
    /// that the average over the listening volume does not, and equalizing those is
    /// the mistake the whole feature exists to avoid.
    /// <para>
    /// It rides on the SIDE rather than the pair: a moving-microphone pass is taken
    /// per driver, and a pair's two drivers are two measurements.
    /// </para>
    /// </remarks>
    public LiveCaptureDocument? SpatialAverage { get; set; }

    /// <summary>
    /// The spatial average the measurement on this side brought with it — the
    /// microphone array it was recorded with — or null when it was recorded with
    /// one microphone.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="SpatialAverage"/> and not instead of it: an attached
    /// moving-microphone pass is a file the user chose, this one arrives with the
    /// measurement, and the project decides which it reads. Cleared with the
    /// measurement, because it IS part of it.
    /// </remarks>
    public LiveCaptureDocument? ArrayCapture { get; set; }

    /// <summary>
    /// The microphone calibration the measurement on this side was READ through,
    /// as its file recorded it; null when the file names none.
    /// </summary>
    /// <remarks>
    /// The panel corrects with one calibration because one microphone usually took
    /// every channel — but which one that was is a fact of each measurement, not of
    /// the project, and the file has carried it since measurements became portable.
    /// This is what the selector's "Own (as measured)" reads. Kept as the settings
    /// rather than the curve so the name travels with it: the selector has to be able
    /// to say what it is applying.
    /// </remarks>
    public VirtualCrossoverCalibrationSettings? MicrophoneCalibration
    {
        get => microphoneCalibration;
        set
        {
            microphoneCalibration = value;
            // Converted once. The redraw asks for it per channel per frame, and
            // rebuilding a three-hundred-point curve on every ask is a cost with
            // nothing to show for it.
            microphoneCalibrationCurve = value?.ToCalibrationFile();
        }
    }

    /// <summary>
    /// <see cref="MicrophoneCalibration"/> as the curve the analysis applies.
    /// </summary>
    public CalibrationFile? MicrophoneCalibrationCurve => microphoneCalibrationCurve;

    private VirtualCrossoverCalibrationSettings? microphoneCalibration;
    private CalibrationFile? microphoneCalibrationCurve;

    /// <summary>
    /// How far apart <see cref="ArrayCapture"/>'s microphones sat at each band of the
    /// shared grid, or null when this side carries no array.
    /// </summary>
    /// <remarks>
    /// Beside the average rather than inside it, because it answers a different
    /// question: the average says what the listening volume measures, the spread says
    /// how much of a claim that is. The EQ Wizard gates its boosts on it — where seven
    /// positions part by more than 20 dB, filling the dip six of them measured helps
    /// the seventh and spends everyone's headroom.
    /// </remarks>
    public double[]? ArraySpreadDb { get; set; }

    /// <summary>
    /// What this side's response actually measured; the whole range by default.
    /// </summary>
    /// <remarks>
    /// Narrowed where a protective high-pass was divided back out, and where the
    /// sweep behind it never reached. Either way the response is zeroed there, and a
    /// gated spectrum of a zero draws the analysis window's leakage — smooth,
    /// plausible, and none of it measured. Curves stop at these edges; sums do not,
    /// because a sum plays wherever any of its channels does.
    /// </remarks>
    public MeasuredBand MeasuredBand { get; set; } = MeasuredBand.Everything;

    /// <summary>
    /// The spatial average this side contributes under <paramref name="mode"/>, or
    /// null when it has none of that family.
    /// </summary>
    public LiveCaptureDocument? SpatialAverageFor(
        VirtualCrossoverSpatialAverageMode mode) =>
        mode switch
        {
            VirtualCrossoverSpatialAverageMode.MicArray => ArrayCapture,
            VirtualCrossoverSpatialAverageMode.MovingMic => SpatialAverage,
            _ => null
        };
    public int TransferPeakIndex { get; set; }
    public int SampleRate { get; set; }

    // The measurement's per-bin coherence (γ²) on the linear FFT grid, when
    // the source carried it. Only the auto-crossover wizard reads it, to
    // discount frequencies the measurement did not trust when reading each
    // driver's usable band; null when the source had none.
    public double[]? TransferCoherence { get; set; }

    // The channel's harmonic distortion (THD, dB vs the fundamental) computed
    // from the source's sweep deconvolution, when it carried one. Only the
    // auto-crossover wizard reads it, to bound each driver by its
    // distortion-clean band (a tweeter's low handover follows its measured
    // distortion knee); null when the source had no sweep deconvolution.
    public IReadOnlyList<SignalPoint>? DistortionCurve { get; set; }

    // The band-limited envelope arrival and gated band level of this
    // side's PROCESSED response, keyed by the processed array's identity
    // and the measured band — the L/R/Δ read-out re-runs on every redraw,
    // and the Hilbert analysis of a full-length IR is far too heavy to
    // repeat when nothing changed. The level rides in the same cache
    // entry: it is measured over the same band from the same response.
    // Latched: the full-band envelope timed the room's modal build-up rather
    // than the direct rise (its upper-half read lands much earlier) — the
    // same detection the alignment engine's cross-side links run, so the
    // read-out can mark the number instead of presenting it as a clean skew.
    public (Complex[] ProcessedIr, double LowHz, double HighHz,
        TimeAlignmentAnalysisResult Result, double? LevelDb, bool Latched)?
        ArrivalCache
    { get; set; }

    // Invalidation counter for in-flight asynchronous source loads: a
    // load captures the revision when it starts (BeginSourceLoad, which
    // also invalidates any OLDER in-flight load into this slot, so the
    // user's latest pick wins regardless of completion order) and may
    // write back only while the revision still matches. Clear() bumps it
    // too: a project import or mono toggle mid-load kills the landing
    // instead of hiding a stale measurement in a slot that was wiped.
    public int SourceRevision { get; private set; }

    public int BeginSourceLoad() => ++SourceRevision;

    public void Clear()
    {
        TransferImpulseResponse = null;
        // The average belongs to the measurement that was here; a slot wiped for a
        // new source must not keep the old driver's curve.
        SpatialAverage = null;
        ArrayCapture = null;
        // It described the measurement that was here, like the array does.
        MicrophoneCalibration = null;
        TransferPeakIndex = 0;
        SampleRate = 0;
        TransferCoherence = null;
        DistortionCurve = null;
        ArrivalCache = null;
        SourceRevision++;
    }
}
