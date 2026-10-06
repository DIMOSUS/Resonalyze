using System.Drawing;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

public sealed class ToggleCheckBoxTests
{
    public static TheoryData<bool, bool, bool, bool> States => new()
    {
        // ticked, enabled, muted, lit
        { false, true, false, false },
        { true, true, false, true },
        { true, false, false, false },
        { true, true, true, false },
        { false, true, true, false },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void OnlyATickInForceLightsTheFrameAndFill(bool ticked, bool enabled, bool muted, bool lit) => StaTest.Run(() =>
    {
        using var on = new ToggleCheckBox { Checked = true };
        using var toggle = new ToggleCheckBox { ForeColor = UiPalette.Error };
        toggle.Checked = ticked;
        toggle.Enabled = enabled;
        toggle.Muted = muted;

        Assert.Equal(lit, toggle.FlatAppearance.BorderColor == on.FlatAppearance.BorderColor);
        Assert.NotEqual(toggle.ForeColor, toggle.FlatAppearance.BorderColor);
        Assert.Equal(lit, toggle.FlatAppearance.CheckedBackColor == on.FlatAppearance.CheckedBackColor);
        Assert.Equal(!muted, toggle.AutoCheck);
        Assert.Equal(
            ticked ? toggle.FlatAppearance.CheckedBackColor : Color.Empty,
            toggle.FlatAppearance.MouseOverBackColor);
    });

    [Fact]
    public void UnmutingATickedToggleLightsItAgain() => StaTest.Run(() =>
    {
        using var on = new ToggleCheckBox { Checked = true };
        using var toggle = new ToggleCheckBox { Checked = true, Muted = true };

        toggle.Muted = false;

        Assert.Equal(on.FlatAppearance.BorderColor, toggle.FlatAppearance.BorderColor);
        Assert.Equal(on.FlatAppearance.CheckedBackColor, toggle.FlatAppearance.CheckedBackColor);
    });
}
