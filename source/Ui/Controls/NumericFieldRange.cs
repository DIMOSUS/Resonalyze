namespace Resonalyze;

/// <summary>
/// The range and decimals of a numeric field, and the rules a <see cref="ThemedNumericUpDown"/> applies to what it is
/// given, without the control, so a model holds exactly what its field would show.
/// </summary>
internal readonly record struct NumericFieldRange(decimal Minimum, decimal Maximum, int Decimals)
{
    /// <summary>The smallest difference the field shows: one unit of its last decimal.</summary>
    public decimal Step => new(1, 0, 0, false, (byte)Decimals);

    /// <summary>Whether the field shows the value without clamping it; its decimals are not asked.</summary>
    public bool Includes(double value) =>
        double.IsFinite(value) && value >= (double)Minimum && value <= (double)Maximum;

    /// <summary>A value written by code: non-finite becomes 0, clamped, rounded to the decimals (to even), clamped again.</summary>
    public decimal Clamp(double value)
    {
        if (!double.IsFinite(value))
        {
            value = 0;
        }

        value = Math.Clamp(value, (double)Minimum, (double)Maximum);
        decimal rounded = Math.Round((decimal)value, Decimals);
        return Math.Clamp(rounded, Minimum, Maximum);
    }

    /// <summary>A value assigned to the field itself (typed, stepped, set): rounded half away from zero, then clamped.</summary>
    public decimal Assign(decimal value) =>
        Contain(decimal.Round(value, Decimals, MidpointRounding.AwayFromZero));

    /// <summary>A held value when the bounds move: clamped only, since it is already at the field's decimals.</summary>
    public decimal Contain(decimal value) => Math.Min(Maximum, Math.Max(Minimum, value));

    /// <summary>A new lower bound; an upper bound below it follows it up, as the control's own does.</summary>
    public NumericFieldRange WithMinimum(decimal minimum) =>
        this with { Minimum = minimum, Maximum = Math.Max(Maximum, minimum) };

    /// <summary>A new upper bound; a lower bound above it follows it down, as the control's own does.</summary>
    public NumericFieldRange WithMaximum(decimal maximum) =>
        this with { Maximum = maximum, Minimum = Math.Min(Minimum, maximum) };
}
