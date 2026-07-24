namespace Resonalyze.App.Tests;

/// <summary>
/// The options panels assign settings straight to these controls without
/// pre-clamping them to each control's range, so the control has to absorb an
/// out-of-range value. <see cref="System.Windows.Forms.NumericUpDown"/> throws
/// there; this one is expected to clamp, and several panels depend on it.
/// </summary>
public sealed class DarkNumericUpDownRangeTests
{
    [Fact]
    public void AValueBelowTheMinimum_ClampsInsteadOfThrowing()
    {
        // The Measurements control offers 2..64 while a settings file may still
        // carry the old single-run default of 1, which Init assigns directly.
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
        // Already at the minimum, so nothing changed and nothing is raised.
        Assert.Null(observed);
    }
}
