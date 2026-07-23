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
    TextCurve
}

/// <summary>
/// How the microphone correction is applied to a source curve. This is the wizard's own
/// choice, not the measurement layer's <see cref="MicrophoneCalibrationMode"/>: an
/// imported curve can additionally re-use the correction frozen into it at capture time,
/// which has no meaning for a live measurement.
/// </summary>
internal enum EqWizardCalibrationMode
{
    Off,
    Degrees0,
    Degrees90,

    /// <summary>The correction the curve was captured with, stored alongside it.</summary>
    Own
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
    /// its FR is computed; an imported curve can only be re-calibrated when its
    /// uncalibrated raw form was stored — otherwise the correction is already baked in
    /// and applying another would double it.
    /// </summary>
    public bool SupportsCalibration =>
        Kind == EqWizardSourceKind.ImpulseResponse || RawSpectrum != null;

    /// <summary>
    /// Whether the smoothing selector applies. Same rule: smoothing a curve that is
    /// already smoothed compounds it, so it is offered only where the unsmoothed
    /// reference exists.
    /// </summary>
    public bool SupportsSmoothing => SupportsCalibration;
}
