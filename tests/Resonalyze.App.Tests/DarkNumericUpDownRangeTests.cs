namespace Resonalyze.App.Tests;

/// <summary>Panels assign settings without pre-clamping; NumericUpDown throws, this control must clamp.</summary>
public sealed class DarkNumericUpDownRangeTests
{
    [Fact]
    public void AValueBelowTheMinimum_ClampsInsteadOfThrowing()
    {
        using var control = new DarkNumericUpDown
        {
            Minimum = 2,
            Maximum = 64,
            Value = 2
        };

        control.Value = 1;

        Assert.Equal(2m, control.Value);
    }

    [Fact]
    public void AValueAboveTheMaximum_ClampsInsteadOfThrowing()
    {
        using var control = new DarkNumericUpDown
        {
            Minimum = 2,
            Maximum = 64,
            Value = 2
        };

        control.Value = 1000;

        Assert.Equal(64m, control.Value);
    }

    [Fact]
    public void ClampingReportsTheValueThatWasKept()
    {
        using var control = new DarkNumericUpDown
        {
            Minimum = 20,
            Maximum = 20_000,
            Value = 20
        };
        decimal? observed = null;
        control.ValueChanged += (_, _) => observed = control.Value;

        control.Value = 5;

        Assert.Equal(20m, control.Value);
        Assert.Null(observed);
    }
}
