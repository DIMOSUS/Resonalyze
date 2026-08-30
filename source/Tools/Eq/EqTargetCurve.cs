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
    int SmoothingInverseOctaves)
{
    /// <summary>
    /// The same target with anything a stored file can hold but the UI cannot
    /// take replaced by its default. Every target that arrives from disk goes
    /// through this, because a target is not only drawn: it also fills the
    /// settings dialog, where a non-finite number throws on the decimal cast
    /// that clamps it into an input, and an enum value outside the list leaves a
    /// combo box with no selection to read back. Both are reachable — the
    /// session and settings files allow named floating-point literals, and the
    /// enum converter accepts numbers as well as names.
    /// </summary>
    public EqTargetCurve Normalized()
    {
        TargetCurveSpec flat = TargetCurveSpec.FromPreset(TargetPreset.Flat);
        return new EqTargetCurve(
            Defined(Preset, TargetPreset.Flat),
            new TargetCurveSpec(
                Finite(Spec.TiltDbPerOctave, flat.TiltDbPerOctave),
                Finite(Spec.BassShelfGainDb, flat.BassShelfGainDb),
                Finite(Spec.BassShelfFrequencyHz, flat.BassShelfFrequencyHz),
                Finite(Spec.BassShelfWidthOctaves, flat.BassShelfWidthOctaves),
                Finite(Spec.TrebleShelfGainDb, flat.TrebleShelfGainDb),
                Finite(Spec.TrebleShelfFrequencyHz, flat.TrebleShelfFrequencyHz),
                Finite(Spec.TrebleShelfWidthOctaves, flat.TrebleShelfWidthOctaves),
                Finite(Spec.PresenceGainDb, flat.PresenceGainDb),
                Finite(Spec.PresenceFrequencyHz, flat.PresenceFrequencyHz),
                Finite(Spec.PresenceWidthOctaves, flat.PresenceWidthOctaves))
            {
                // An imported shape needs no repair here: nothing can build one
                // except ImportedTargetCurve, which drops what it cannot use and
                // refuses to exist at all below two points. Dropping it instead
                // would silently turn a user's house curve back into a preset.
                Imported = Spec.Imported
            },
            Finite(ToleranceDb, DefaultToleranceDb),
            Defined(DeviationMode, TargetDeviationMode.Deviation),
            Color,
            Finite(StrokeThickness, DefaultStrokeThickness),
            Defined(LineStyle, OverlayLineStyle.Dash),
            SmoothingInverseOctaves);
    }

    // What a field the UI cannot take falls back to; the stored defaults.
    private const double DefaultToleranceDb = 3;
    private const double DefaultStrokeThickness = 2;

    private static double Finite(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    private static T Defined<T>(T value, T fallback)
        where T : struct, Enum =>
        Enum.IsDefined(value) ? value : fallback;
}
