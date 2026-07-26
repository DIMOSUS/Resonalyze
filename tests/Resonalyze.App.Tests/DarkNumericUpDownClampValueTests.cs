namespace Resonalyze.App.Tests;

/// <summary>
/// <c>ClampValue</c> is the single conversion from a stored double to a value
/// assignable to a control, after five separate helpers were collapsed into it.
/// Two of those five rounded and three did not, and one reached
/// <c>(decimal)Math.Round(value, ...)</c> with no finiteness guard — which
/// throws <see cref="OverflowException"/> on NaN or infinity. These pin the
/// behaviour every call site now inherits.
/// </summary>
public sealed class DarkNumericUpDownClampValueTests
{
    private static DarkNumericUpDown Control(
        decimal minimum = 2,
        decimal maximum = 64,
        int decimalPlaces = 0) =>
        new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = minimum,
            DecimalPlaces = decimalPlaces
        };

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANonFiniteValue_BecomesZeroClampedIntoRange_RatherThanThrowing(double value)
    {
        using DarkNumericUpDown control = Control();

        // Deliberately NOT the maximum for +infinity: the guard replaces any
        // non-finite value with zero first, and zero then clamps up to the
        // minimum. Surprising, but it is what every call site now gets, so it
        // is pinned rather than assumed.
        Assert.Equal(2m, control.ClampValue(value));
    }

    [Fact]
    public void ANonFiniteValue_ClampsToZeroWhenZeroIsInRange()
    {
        using DarkNumericUpDown control = Control(minimum: -10, maximum: 10, decimalPlaces: 2);

        Assert.Equal(0m, control.ClampValue(double.NaN));
        Assert.Equal(0m, control.ClampValue(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(double.MaxValue, 64)]
    [InlineData(double.MinValue, 2)]
    [InlineData(1e30, 64)]
    [InlineData(-1e30, 2)]
    public void AFiniteValueBeyondDecimalRange_ClampsInsteadOfOverflowing(
        double value,
        int expected)
    {
        using DarkNumericUpDown control = Control();

        // The pre-clamp happens in double precisely so the decimal cast cannot
        // overflow — double.MaxValue does not fit in a decimal.
        Assert.Equal(expected, control.ClampValue(value));
    }

    [Theory]
    [InlineData(1.0, 2.0)]
    [InlineData(-5.0, 2.0)]
    [InlineData(1000.0, 64.0)]
    [InlineData(30.0, 30.0)]
    public void AValueOutsideTheRange_ClampsToTheNearestBound(double value, double expected)
    {
        using DarkNumericUpDown control = Control();

        Assert.Equal((decimal)expected, control.ClampValue(value));
    }

    [Theory]
    [InlineData(2.0, 2.0)]
    [InlineData(64.0, 64.0)]
    public void TheBoundsThemselves_SurviveUnchanged(double value, double expected)
    {
        using DarkNumericUpDown control = Control();

        Assert.Equal((decimal)expected, control.ClampValue(value));
    }

    [Theory]
    // Exact binary fractions, so the double -> decimal cast is not itself the
    // thing under test. Math.Round(decimal, int) is MidpointRounding.ToEven:
    // 0.125 -> 0.12 (2 is even), 0.375 -> 0.38 (8 is even).
    [InlineData(0.125, 0.12)]
    [InlineData(0.375, 0.38)]
    [InlineData(1.625, 1.62)]
    [InlineData(1.875, 1.88)]
    public void AMidpoint_RoundsToEvenAtTheControlsDecimalPlaces(double value, double expected)
    {
        using DarkNumericUpDown control = Control(minimum: 0, maximum: 10, decimalPlaces: 2);

        Assert.Equal((decimal)expected, control.ClampValue(value));
    }

    [Fact]
    public void TheResultCarriesNoPrecisionTheControlCannotDisplay()
    {
        using DarkNumericUpDown control = Control(minimum: 0, maximum: 10, decimalPlaces: 3);

        // The gate offset and tau fields are 3 decimal places; a stored value
        // finer than that used to survive in Value while the control displayed
        // the rounded one, so reading the field back disagreed with the screen.
        Assert.Equal(1.235m, control.ClampValue(1.2345678));
    }

    [Fact]
    public void AWholeNumberControl_DropsTheFraction()
    {
        using DarkNumericUpDown control = Control(minimum: 2, maximum: 64, decimalPlaces: 0);

        Assert.Equal(7m, control.ClampValue(6.7));
        Assert.Equal(6m, control.ClampValue(6.4));
    }
}
