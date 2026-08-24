using System.Drawing;
using System.Reflection;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>
/// The palette's readability, pinned as numbers. A user with poor eyesight
/// measured the theme and reported the two colours that failed (#116): muted text
/// at 2.7:1 on the control surface, where a greyed-out control still shows the
/// value in force, and the accent — which turned out never to be text at all, but
/// the fill under the active tab's own label, at 3.1:1.
///
/// Nothing here judges taste: it only holds the pairs that CARRY TEXT above the
/// WCAG floor (4.5:1 for text, 3:1 for a UI element such as a glyph or a tick
/// line), so a later change of value cannot walk them back under without saying
/// so. Every pair listed is one the app actually paints — a foreground measured
/// against a background it is never drawn on proves nothing, which is precisely
/// how the accent came to look worse than it was. When a light palette arrives it
/// inherits this test unchanged: the roles are the same, only the values move.
///
/// Decorative colours are deliberately absent — gridlines, meter bars, fader
/// grooves and curve colours answer to no threshold.
/// </summary>
public sealed class UiPaletteContrastTests
{
    /// <summary>Text on a background, and the ratio it has to clear.</summary>
    public static TheoryData<string, string, double> Pairs => new()
    {
        // Body text on every surface it is written on.
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.AppBackground), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.DialogBackground), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.DialogSurface), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.ControlSurface), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.InputSurface), 4.5 },
        { nameof(UiPalette.TextSecondary), nameof(UiPalette.AppBackground), 4.5 },
        { nameof(UiPalette.TextSecondary), nameof(UiPalette.DialogBackground), 4.5 },
        { nameof(UiPalette.TextSecondary), nameof(UiPalette.DialogSurface), 4.5 },
        { nameof(UiPalette.TextSecondary), nameof(UiPalette.ControlSurface), 4.5 },

        // The value of a control that cannot be edited right now. This is the row
        // the issue was about: it is text a user has to READ, not a state cue.
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.AppBackground), 4.5 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.DialogBackground), 4.5 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.DialogSurface), 4.5 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.ControlSurface), 4.5 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.InputSurface), 4.5 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.ButtonDisabledBackground), 4.5 },
        { nameof(UiPalette.MeterMutedText), nameof(UiPalette.PlotSurfaceDark), 4.5 },
        { nameof(UiPalette.MeterMutedText), nameof(UiPalette.PlotTrack), 4.5 },

        // Buttons, and the accent FILL under its own label: the dialog's accent
        // button carries TextPrimary, the active mode tab carries TitleBarTextSoft.
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.ButtonBackground), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.ButtonHoverBackground), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.ButtonPressedBackground), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.AccentFill), 4.5 },
        { nameof(UiPalette.TitleBarTextSoft), nameof(UiPalette.AccentFill), 4.5 },
        { nameof(UiPalette.TitleBarTextSoft), nameof(UiPalette.AccentFillPressed), 4.5 },
        { nameof(UiPalette.TitleBarTextBright), nameof(UiPalette.ButtonBackground), 4.5 },

        // The title bar: its own text, and the update notice's link colour.
        { nameof(UiPalette.TitleBarText), nameof(UiPalette.TitleBarBackground), 4.5 },
        { nameof(UiPalette.TitleBarTextSoft), nameof(UiPalette.TitleBarBackground), 4.5 },
        { nameof(UiPalette.AccentBlueSoft), nameof(UiPalette.TitleBarBackground), 4.5 },

        // Status text that means something by its colour.
        { nameof(UiPalette.ErrorSoft), nameof(UiPalette.ControlSurface), 4.5 },
        { nameof(UiPalette.ErrorSoft), nameof(UiPalette.AppBackground), 4.5 },
        { nameof(UiPalette.WarningAmber), nameof(UiPalette.AppBackground), 4.5 },
        { nameof(UiPalette.SuccessGreen), nameof(UiPalette.AppBackground), 4.5 },

        // Axis numbers on the graph surface. These used to be OxyPlot's own black
        // on the same surface — 1.9:1, the worst pair in the app.
        { nameof(UiPalette.GraphAxisText), nameof(UiPalette.GraphSurface), 4.5 },

        // Glyphs and tick lines are UI elements, not text: 3:1.
        { nameof(UiPalette.GraphTickline), nameof(UiPalette.GraphSurface), 3.0 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.DialogSurfaceMuted), 3.0 },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void PaintedTextPairs_ClearTheContrastFloor(
        string foreground, string background, double floor)
    {
        double ratio = ContrastRatio(PaletteColor(foreground), PaletteColor(background));

        Assert.True(
            ratio >= floor,
            $"{foreground} on {background} is {ratio:0.00}:1, under the {floor:0.0}:1 floor.");
    }

    /// <summary>
    /// Raising the disabled colour is only half the fix: it also has to stay
    /// visibly BELOW live text, or "disabled" would be carried by nothing the eye
    /// can catch. The surface and border under such a control say it too, so this
    /// is a floor on the gap rather than on the darkness.
    /// </summary>
    [Fact]
    public void DisabledText_StaysDimmerThanLiveText()
    {
        double disabled = RelativeLuminance(UiPalette.TextDisabled);

        Assert.True(disabled < RelativeLuminance(UiPalette.TextSecondary));
        Assert.True(RelativeLuminance(UiPalette.TextPrimary) / disabled >= 1.5);
    }

    private static Color PaletteColor(string name)
    {
        PropertyInfo? property = typeof(UiPalette).GetProperty(
            name, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(property);
        return (Color)property!.GetValue(null)!;
    }

    // WCAG 2.x: (L1 + 0.05) / (L2 + 0.05) over the sRGB relative luminances.
    private static double ContrastRatio(Color first, Color second)
    {
        double a = RelativeLuminance(first);
        double b = RelativeLuminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double RelativeLuminance(Color color) =>
        (0.2126 * Linearize(color.R)) +
        (0.7152 * Linearize(color.G)) +
        (0.0722 * Linearize(color.B));

    private static double Linearize(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
