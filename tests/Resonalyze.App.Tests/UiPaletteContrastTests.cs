using System.Drawing;
using System.Reflection;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>
/// Pins text-carrying pairs above WCAG (4.5:1 text, 3:1 UI elements); only pairs the app actually paints.
/// Decorative colours (gridlines, meters, curves) are deliberately absent.
/// </summary>
public sealed class UiPaletteContrastTests
{
    public static TheoryData<string, string, double> Pairs => new()
    {
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.AppBackground), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.DialogBackground), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.DialogSurface), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.ControlSurface), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.InputSurface), 4.5 },
        { nameof(UiPalette.TextSecondary), nameof(UiPalette.AppBackground), 4.5 },
        { nameof(UiPalette.TextSecondary), nameof(UiPalette.DialogBackground), 4.5 },
        { nameof(UiPalette.TextSecondary), nameof(UiPalette.DialogSurface), 4.5 },
        { nameof(UiPalette.TextSecondary), nameof(UiPalette.ControlSurface), 4.5 },

        // Disabled controls still show the value in force: text to READ, not a state cue.
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.AppBackground), 4.5 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.DialogBackground), 4.5 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.DialogSurface), 4.5 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.ControlSurface), 4.5 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.InputSurface), 4.5 },
        { nameof(UiPalette.TextDisabled), nameof(UiPalette.ButtonDisabledBackground), 4.5 },
        { nameof(UiPalette.MeterMutedText), nameof(UiPalette.PlotSurfaceDark), 4.5 },
        { nameof(UiPalette.MeterMutedText), nameof(UiPalette.PlotTrack), 4.5 },

        { nameof(UiPalette.TextPrimary), nameof(UiPalette.ButtonBackground), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.ButtonHoverBackground), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.ButtonPressedBackground), 4.5 },
        { nameof(UiPalette.TextPrimary), nameof(UiPalette.AccentFill), 4.5 },
        { nameof(UiPalette.TitleBarTextSoft), nameof(UiPalette.AccentFill), 4.5 },
        { nameof(UiPalette.TitleBarTextSoft), nameof(UiPalette.AccentFillPressed), 4.5 },
        { nameof(UiPalette.TitleBarTextBright), nameof(UiPalette.ButtonBackground), 4.5 },

        { nameof(UiPalette.TitleBarText), nameof(UiPalette.TitleBarBackground), 4.5 },
        { nameof(UiPalette.TitleBarTextSoft), nameof(UiPalette.TitleBarBackground), 4.5 },
        { nameof(UiPalette.AccentBlueSoft), nameof(UiPalette.TitleBarBackground), 4.5 },

        { nameof(UiPalette.ErrorSoft), nameof(UiPalette.ControlSurface), 4.5 },
        { nameof(UiPalette.ErrorSoft), nameof(UiPalette.AppBackground), 4.5 },
        { nameof(UiPalette.WarningAmber), nameof(UiPalette.AppBackground), 4.5 },
        { nameof(UiPalette.SuccessGreen), nameof(UiPalette.AppBackground), 4.5 },

        { nameof(UiPalette.GraphAxisText), nameof(UiPalette.GraphSurface), 4.5 },

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

    // WCAG 2.x: (L1 + 0.05) / (L2 + 0.05) over sRGB relative luminances.
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
