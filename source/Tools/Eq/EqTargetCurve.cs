namespace Resonalyze;

/// <summary>
/// The EQ target curve as one value: the shape plus how it is drawn. The EQ
/// Wizard owns it and persists it in <see cref="MeasurementSettingsFile.
/// EqWizardSettings"/>; the Virtual DSP tool borrows the same definition to draw
/// the target over its predicted sum, and can edit it back through the host.
/// One definition, two plots — a target tuned in either place is THE target.
/// </summary>
/// <remarks>
/// The LEVEL the curve is anchored at is deliberately not part of this. A target
/// shape is relative dB; where it sits belongs to the plot it is drawn on, and
/// the two plots have unrelated level references (the wizard's source curve, the
/// Virtual DSP transfer-function dB). Each side keeps its own anchor.
/// <para>
/// Tolerance, deviation mode and smoothing are carried unchanged rather than
/// used by every consumer: the Virtual DSP plot draws the line alone, but it
/// opens the shared settings dialog, which shows those fields, and passing them
/// through keeps them from being silently reset by a visit to that dialog.
/// </para>
/// </remarks>
internal sealed record EqTargetCurve(
    TargetPreset Preset,
    TargetCurveSpec Spec,
    double ToleranceDb,
    TargetDeviationMode DeviationMode,
    Color Color,
    double StrokeThickness,
    OverlayLineStyle LineStyle,
    int SmoothingInverseOctaves);
