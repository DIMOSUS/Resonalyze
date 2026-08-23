using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Where an EQ Wizard source curve came from.</summary>
internal enum EqWizardSourceKind
{
    /// <summary>An impulse response (file or history entry); its FR is computed here.</summary>
    ImpulseResponse,

    /// <summary>A curve imported from a captured overlay slot.</summary>
    OverlaySlot,

    /// <summary>A curve imported from a text file.</summary>
    TextCurve,

    /// <summary>
    /// One Virtual DSP channel side handed over for PEQ editing. An impulse response
    /// like <see cref="ImpulseResponse"/>, but rendered through the gate the Virtual
    /// DSP plot draws with (<see cref="EqWizardCurveSource.GateSettings"/>) and pinned
    /// to that panel's microphone calibration, so the wizard shows the very curve the
    /// user just left.
    /// </summary>
    VirtualDspChannel
}

/// <summary>
/// The curve the EQ Wizard equalizes, decoupled from where it was picked. An impulse
/// response still computes its own frequency response (so window, smoothing and
/// calibration all apply); an imported curve is a finished response and carries only what
/// was stored with it.
/// </summary>
/// <remarks>
/// Importing is a SNAPSHOT: nothing here points back at the overlay slot, history entry
/// or file it came from, so later edits there cannot change a tune in progress.
/// </remarks>
internal sealed record EqWizardCurveSource
{
    public required EqWizardSourceKind Kind { get; init; }

    /// <summary>Short name for the source button.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Full description for the tooltip (path, slot, units, rate).</summary>
    public required string Description { get; init; }

    // --- impulse-response sources -------------------------------------------------

    /// <summary>The measurement whose FR is computed; null for an imported curve.</summary>
    public IImpulseMeasurement? Measurement { get; init; }

    /// <summary>
    /// Per-frequency coherence (γ²) gating Auto Tune boosts. Present only for a
    /// loopback-transfer impulse response; imported curves never carry it.
    /// </summary>
    public IReadOnlyList<SignalPoint>? Coherence { get; init; }

    // --- Virtual DSP channel sources ----------------------------------------------

    /// <summary>
    /// The gate the source's FR is computed through, offset already resolved — the same
    /// <see cref="PhaseAnalysisSettings"/> the Virtual DSP magnitude view uses, so the
    /// wizard's source curve is the one that plot draws. Null for every other source;
    /// those use the wizard's own fixed analysis window.
    /// </summary>
    public PhaseAnalysisSettings? GateSettings { get; init; }

    /// <summary>
    /// The microphone calibration the Virtual DSP panel renders with, which this source
    /// is pinned to: the wizard applies it but disables its selector, because a PEQ
    /// fitted under one correction and summed under another would break the very
    /// identity the handoff promises. The curve itself rather than an id: the panel may
    /// be drawing with a curve its session carries, which the wizard's own list cannot
    /// resolve. Null (Off) is a real value. Meaningless for other kinds.
    /// </summary>
    public CalibrationFile? PinnedCalibration { get; init; }

    /// <summary>What the selector shows for <see cref="PinnedCalibration"/>.</summary>
    public string? PinnedCalibrationName { get; init; }

    /// <summary>
    /// The channel's ORIGINAL measurement, before any of its chain — what the corrected
    /// preview is built from.
    /// </summary>
    /// <remarks>
    /// A gate does not commute with a filter, so "the source curve plus the filter's
    /// ideal magnitude" is NOT what the panel draws once the bank rings longer than the
    /// window: a 6 ms gate cannot resolve a Q 5 band at 100 Hz, and the two readings part
    /// by several dB there. The preview therefore runs the WHOLE chain — the edited bank
    /// included — through one <see cref="VirtualCrossoverAnalysis.ApplyChain"/> and gates
    /// the result, exactly as the panel does. One pass, from the original measurement:
    /// re-filtering the already-bypassed response a second time would pad and wrap twice
    /// and no longer match.
    /// </remarks>
    public Complex[]? PreviewImpulseResponse { get; init; }

    /// <summary>
    /// The channel's chain with the PEQ left out — the edited bank is substituted into it
    /// for each preview. Identity for a raw handoff, which is measured without the chain.
    /// </summary>
    public DspChannelChain? PreviewChain { get; init; }

    /// <summary>Whether this source's curves are built through a gate, not the wizard's own window.</summary>
    public bool IsGated => GateSettings != null && PreviewImpulseResponse != null;

    // --- imported curve sources ---------------------------------------------------

    /// <summary>
    /// The uncalibrated oversampled spectrum stored with a captured curve, re-smoothable
    /// exactly like the mode it came from. Null when the curve has no raw form (text
    /// import, a calculated or legacy slot) — then only <see cref="Points"/> exist.
    /// </summary>
    public IReadOnlyList<SignalPoint>? RawSpectrum { get; init; }

    /// <summary>
    /// The microphone correction frozen at capture time, on the raw curve's output grid.
    /// Empty when the curve was captured without calibration.
    /// </summary>
    public IReadOnlyList<double> OwnCalibrationCorrectionDb { get; init; } =
        Array.Empty<double>();

    /// <summary>
    /// The finished curve as stored: unsmoothed and already carrying whatever calibration
    /// was applied at capture. Used directly when there is no raw form.
    /// </summary>
    public IReadOnlyList<SignalPoint> Points { get; init; } = Array.Empty<SignalPoint>();

    /// <summary>
    /// For a curve with NO raw form: the microphone correction baked into
    /// <see cref="Points"/>, one value per point. These modes (a dB SPL RTA or FR) apply
    /// the correction additively per frequency, so undoing this and applying another is
    /// exact even without a raw spectrum — which is what lets the calibration selector work
    /// here. Empty when the curve has a raw form (the correction travels on the raw grid
    /// instead) or when the source never declared one (a text import, a legacy slot).
    /// </summary>
    public IReadOnlyList<double> PointsCalibrationCorrectionDb { get; init; } =
        Array.Empty<double>();

    /// <summary>
    /// The display smoothing already baked into <see cref="Points"/>, in the
    /// <see cref="SpectrumSmoothing"/> encoding; null when the source never declared it.
    /// Only a curve captured unsmoothed (0) may be smoothed here — re-smoothing an already
    /// smoothed curve compounds it.
    /// </summary>
    public int? CapturedSmoothingCode { get; init; }

    /// <summary>The unit the curve is in, which drives the plot's dB axis.</summary>
    public MagnitudeScale Scale { get; init; } = MagnitudeScale.Relative;

    /// <summary>
    /// The rate the curve was measured at, when known. The wizard realizes its biquads at
    /// this rate; null means the user must state it (a foreign text file).
    /// </summary>
    public int? SampleRateHz { get; init; }

    /// <summary>What the curve is, when the source declared it.</summary>
    public AnalysisCurveKind? CurveKind { get; init; }

    /// <summary>
    /// Whether the calibration selector applies. An impulse response is calibrated while
    /// its FR is computed; an imported curve can be re-calibrated when its uncalibrated raw
    /// form was stored, or — for the no-raw modes that correct additively per frequency (a
    /// dB SPL RTA or FR) — when the correction baked into its points travelled with it.
    /// Without either, the correction is already fused into the numbers and applying
    /// another would double it. A Virtual DSP channel is deliberately absent: its
    /// correction is applied, but it is the DSP panel's choice
    /// (<see cref="PinnedCalibration"/>) and its selector stays disabled here.
    /// </summary>
    public bool SupportsCalibration =>
        Kind == EqWizardSourceKind.ImpulseResponse || HasOwnCalibration;

    /// <summary>
    /// Whether the curve carries the correction it was captured with, so "own" can
    /// reproduce it — either on the raw grid or frozen onto its own points.
    /// </summary>
    public bool HasOwnCalibration =>
        RawSpectrum != null || PointsCalibrationCorrectionDb.Count > 0;

    /// <summary>
    /// Whether the smoothing selector applies: where an unsmoothed reference exists (an
    /// impulse response, a stored raw spectrum), or where the curve itself was captured
    /// unsmoothed and so IS one. Smoothing an already smoothed curve compounds it, so a
    /// curve captured under smoothing — or one that never said (a text import, a legacy
    /// slot) — is left alone.
    /// </summary>
    /// <remarks>
    /// A no-raw curve additionally has to be an RTA. Re-smoothing must reproduce the
    /// analyzer that drew the curve, not merely look similar, because the result feeds Auto
    /// Tune; only the RTA's smoothing is a replayable second pass over the band levels it
    /// stored (see <see cref="DataHelper.SmoothBandLevels"/>). A dB SPL SWEEP smooths
    /// linear amplitude inside its Lanczos resampling, which cannot be replayed from the
    /// finished curve — that one needs its raw spectrum kept, so it stays unsmoothable here
    /// rather than being smoothed by a near-enough algorithm.
    /// </remarks>
    public bool SupportsSmoothing =>
        Kind is EqWizardSourceKind.ImpulseResponse or EqWizardSourceKind.VirtualDspChannel ||
        RawSpectrum != null ||
        (CapturedSmoothingCode == 0 && CurveKind == AnalysisCurveKind.InputSpectrum);
}
