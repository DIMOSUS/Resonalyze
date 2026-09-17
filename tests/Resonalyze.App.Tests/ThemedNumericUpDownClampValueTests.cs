namespace Resonalyze.App.Tests;

public sealed class ThemedNumericUpDownClampValueTests
{
    private static ThemedNumericUpDown Control(
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
        using ThemedNumericUpDown control = Control();

        // Non-finite values become zero before clamping, so +infinity lands on the minimum.
        Assert.Equal(2m, control.ClampValue(value));
    }

    [Fact]
    public void ANonFiniteValue_ClampsToZeroWhenZeroIsInRange()
    {
        using ThemedNumericUpDown control = Control(minimum: -10, maximum: 10, decimalPlaces: 2);

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
        using ThemedNumericUpDown control = Control();

        // Pre-clamped in double: double.MaxValue does not fit in a decimal.
        Assert.Equal(expected, control.ClampValue(value));
    }

    [Theory]
    [InlineData(1.0, 2.0)]
    [InlineData(-5.0, 2.0)]
    [InlineData(1000.0, 64.0)]
    [InlineData(30.0, 30.0)]
    public void AValueOutsideTheRange_ClampsToTheNearestBound(double value, double expected)
    {
        using ThemedNumericUpDown control = Control();

        Assert.Equal((decimal)expected, control.ClampValue(value));
    }

    [Theory]
    [InlineData(2.0, 2.0)]
    [InlineData(64.0, 64.0)]
    public void TheBoundsThemselves_SurviveUnchanged(double value, double expected)
    {
        using ThemedNumericUpDown control = Control();

        Assert.Equal((decimal)expected, control.ClampValue(value));
    }

    [Theory]
    // Math.Round(decimal, int) is ToEven: 0.125 -> 0.12, 0.375 -> 0.38.
    [InlineData(0.125, 0.12)]
    [InlineData(0.375, 0.38)]
    [InlineData(1.625, 1.62)]
    [InlineData(1.875, 1.88)]
    public void AMidpoint_RoundsToEvenAtTheControlsDecimalPlaces(double value, double expected)
    {
        using ThemedNumericUpDown control = Control(minimum: 0, maximum: 10, decimalPlaces: 2);

        Assert.Equal((decimal)expected, control.ClampValue(value));
    }

    [Fact]
    public void TheResultCarriesNoPrecisionTheControlCannotDisplay()
    {
        using ThemedNumericUpDown control = Control(minimum: 0, maximum: 10, decimalPlaces: 3);

        Assert.Equal(1.235m, control.ClampValue(1.2345678));
    }

    [Fact]
    public void AWholeNumberControl_DropsTheFraction()
    {
        using ThemedNumericUpDown control = Control(minimum: 2, maximum: 64, decimalPlaces: 0);

        Assert.Equal(7m, control.ClampValue(6.7));
        Assert.Equal(6m, control.ClampValue(6.4));
    }
}
