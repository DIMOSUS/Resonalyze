namespace Resonalyze;

internal static class ThemedNumericUpDownExtensions
{
    /// <summary>Non-finite becomes 0; rounded to the control's decimals and clamped into range.</summary>
    public static decimal ClampValue(this ThemedNumericUpDown control, double value)
    {
        if (!double.IsFinite(value))
        {
            value = 0;
        }

        value = Math.Clamp(value, (double)control.Minimum, (double)control.Maximum);
        decimal rounded = Math.Round((decimal)value, control.DecimalPlaces);
        return Math.Clamp(rounded, control.Minimum, control.Maximum);
    }
}
