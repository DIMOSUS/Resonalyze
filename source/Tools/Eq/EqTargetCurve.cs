namespace Resonalyze;

/// <summary>
/// The target shared by the EQ Wizard (owner, persisted) and Virtual DSP. The anchoring LEVEL is not part of it:
/// each plot has its own reference. Tolerance/deviation/smoothing ride along so the shared dialog does not reset them.
/// </summary>
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
    /// <summary>Replaces non-finite numbers and undefined enums from disk, which the settings dialog cannot take.</summary>
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
                // Only ImportedTargetCurve builds one (already sanitised); dropping it would turn a house curve back into a preset.
                Imported = Spec.Imported
            },
            Finite(ToleranceDb, DefaultToleranceDb),
            Defined(DeviationMode, TargetDeviationMode.Deviation),
            Color,
            Finite(StrokeThickness, DefaultStrokeThickness),
            Defined(LineStyle, OverlayLineStyle.Dash),
            SmoothingInverseOctaves);
    }

    private const double DefaultToleranceDb = 3;
    private const double DefaultStrokeThickness = 2;

    private static double Finite(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    private static T Defined<T>(T value, T fallback)
        where T : struct, Enum =>
        Enum.IsDefined(value) ? value : fallback;
}
