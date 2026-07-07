namespace Resonalyze;

internal static class DarkNumericUpDownExtensions
{
    /// <summary>
    /// Converts a stored double into a value assignable to the control:
    /// non-finite becomes 0, the result is rounded to the control's decimal
    /// places and clamped into its range.
    /// </summary>
    public static decimal ClampValue(this DarkNumericUpDown control, double value)
    {
        if (!double.IsFinite(value))
        {
            value = 0;
        }

        // Pre-clamp in double so the decimal cast cannot overflow.
        value = Math.Clamp(value, (double)control.Minimum, (double)control.Maximum);
        decimal rounded = Math.Round((decimal)value, control.DecimalPlaces);
        return Math.Clamp(rounded, control.Minimum, control.Maximum);
    }
}
