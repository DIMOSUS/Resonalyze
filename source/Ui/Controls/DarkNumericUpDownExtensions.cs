namespace Resonalyze;

internal static class ThemedNumericUpDownExtensions
{
    /// <summary>Non-finite becomes 0; rounded to the control's decimals and clamped into range.</summary>
    public static decimal ClampValue(this ThemedNumericUpDown control, double value) =>
        control.FieldRange().Clamp(value);

    public static NumericFieldRange FieldRange(this ThemedNumericUpDown control) =>
        new(control.Minimum, control.Maximum, control.DecimalPlaces);

    /// <summary>Bounds and decimals from the model that owns them; the held value is clamped as the bounds move.</summary>
    public static void ApplyFieldRange(this ThemedNumericUpDown control, NumericFieldRange range)
    {
        control.DecimalPlaces = range.Decimals;
        control.Minimum = range.Minimum;
        control.Maximum = range.Maximum;
    }
}
