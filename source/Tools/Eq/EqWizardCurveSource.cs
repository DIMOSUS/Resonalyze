using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

internal enum EqWizardSourceKind
{
    ImpulseResponse,

    OverlaySlot,

    TextCurve,

    /// <summary>A stored spatially averaged magnitude (moving mic or mic array; treated identically downstream).</summary>
    SpatialAverage,

    /// <summary>A Virtual DSP channel side: an IR rendered through the panel's gate and pinned to its calibration.</summary>
    VirtualDspChannel
}

/// <summary>The curve the wizard equalizes. Importing is a SNAPSHOT with no link back to its origin.</summary>
internal sealed record EqWizardCurveSource
{
    public required EqWizardSourceKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public IImpulseMeasurement? Measurement { get; init; }

    /// <summary>Per-frequency 0..1 confidence gating Auto Tune boosts: loopback γ², or a mic array's position agreement.</summary>
    public IReadOnlyList<SignalPoint>? Coherence { get; init; }

    /// <summary>The Virtual DSP gate (offset resolved); null for other sources, which use the wizard's fixed window.</summary>
    public PhaseAnalysisSettings? GateSettings { get; init; }

    /// <summary>The panel's calibration curve this source is pinned to (a curve, not an id: it may be session-only). Null = Off.</summary>
    public CalibrationFile? PinnedCalibration { get; init; }

    public string? PinnedCalibrationName { get; init; }

    /// <summary>
    /// The channel's original measurement; the preview runs the whole chain (bank included) once and gates the result,
    /// since a gate does not commute with a filter. See docs/tech/eq-auto-tuner.md#gated-sources.
    /// </summary>
    public Complex[]? PreviewImpulseResponse { get; init; }

    /// <summary>The chain without its PEQ, into which the edited bank is substituted. Identity for a raw handoff.</summary>
    public DspChannelChain? PreviewChain { get; init; }

    /// <summary>Spatial average that REPLACES the magnitude when present; the measurement still serves the phase view.</summary>
    public LiveCaptureDocument? SpatialAverage { get; init; }

    /// <summary>How the panel read <see cref="SpatialAverage"/>: Off and Own both resolve to no curve, so the mode travels.</summary>
    public SpatialAverageCalibration SpatialAverageCalibration { get; init; } =
        SpatialAverageCalibration.Own;

    /// <summary>Offset (dB) the panel resolved over the whole set; not re-derivable from one channel.</summary>
    public double SpatialAverageOffsetDb { get; init; }

    /// <summary>Magnitude built through a gate. A spatial average is excluded: steady-state, no window (its phase stays gated).</summary>
    public bool IsGated =>
        GateSettings != null && PreviewImpulseResponse != null && SpatialAverage == null;

    /// <summary>
    /// Neighbours, window and τ for the phase view; null without neighbours. Phase uses the Virtual DSP gate while
    /// magnitude keeps the fixed steady-state window.
    /// </summary>
    public EqWizardPhaseContext? PhaseContext { get; init; }

    /// <summary>Uncalibrated oversampled spectrum, re-smoothable like its mode; null when only <see cref="Points"/> exist.</summary>
    public IReadOnlyList<SignalPoint>? RawSpectrum { get; init; }

    /// <summary>Band the raw spectrum measured (stored unmasked, so the break is re-applied to each finished curve).</summary>
    public MeasuredBand RawSpectrumBand { get; init; }

    /// <summary>Correction frozen at capture, on the raw output grid; empty when captured uncalibrated.</summary>
    public IReadOnlyList<double> OwnCalibrationCorrectionDb { get; init; } =
        Array.Empty<double>();

    /// <summary>The stored curve: unsmoothed and carrying the capture's calibration.</summary>
    public IReadOnlyList<SignalPoint> Points { get; init; } = Array.Empty<SignalPoint>();

    /// <summary>For a no-raw curve: the additive per-point correction baked into <see cref="Points"/>; empty otherwise.</summary>
    public IReadOnlyList<double> PointsCalibrationCorrectionDb { get; init; } =
        Array.Empty<double>();

    /// <summary>Smoothing baked into <see cref="Points"/> (<see cref="SpectrumSmoothing"/> code); only 0 may be re-smoothed.</summary>
    public int? CapturedSmoothingCode { get; init; }

    public MagnitudeScale Scale { get; init; } = MagnitudeScale.Relative;

    /// <summary>Rate the curve was MEASURED at, not the rate biquads are realised at (see <see cref="ProcessorProfile"/>).</summary>
    public int? SampleRateHz { get; init; }

    /// <summary>The processor this source is tuned FOR (rate, Q convention); only a Virtual DSP handoff carries one.</summary>
    public DspProcessorProfile? ProcessorProfile { get; init; }

    public AnalysisCurveKind? CurveKind { get; init; }

    /// <summary>Calibration selector applies. See docs/tech/eq-auto-tuner.md#calibration-choice.</summary>
    public bool SupportsCalibration =>
        Kind == EqWizardSourceKind.ImpulseResponse || HasOwnCalibration;

    /// <summary>
    /// The panel pinned a correction: the IR's curve OR the spatial average's mode (Own is a real correction
    /// even when the IR names no file).
    /// </summary>
    public bool PinsCorrection =>
        PinnedCalibration != null ||
        (SpatialAverage != null &&
            SpatialAverageCalibration.Mode != SpatialAverageCalibrationMode.Off);

    /// <summary>Correction aggregated from several mics' files: only Own and Off are exact.</summary>
    public bool CalibrationIsAggregate { get; init; }

    public bool HasOwnCalibration =>
        RawSpectrum != null || PointsCalibrationCorrectionDb.Count > 0;

    /// <summary>Smoothing selector applies. See docs/tech/eq-auto-tuner.md#re-smoothing-imported-curves.</summary>
    public bool SupportsSmoothing =>
        Kind is EqWizardSourceKind.ImpulseResponse or EqWizardSourceKind.VirtualDspChannel ||
        RawSpectrum != null ||
        (CapturedSmoothingCode == 0 && CurveKind == AnalysisCurveKind.InputSpectrum);
}
