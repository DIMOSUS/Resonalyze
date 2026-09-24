using System.Drawing;
using System.Reflection;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>
/// Pins text-carrying pairs above WCAG (4.5:1 text, 3:1 UI elements) in EVERY theme; only pairs the app paints.
/// A second theme therefore costs no new test: it is measured by the same table.
/// </summary>
public sealed class UiPaletteContrastTests
{
    private static readonly UiThemePalette[] Themes = [UiThemePalette.Dark, UiThemePalette.Light];

    public static TheoryData<string, string, string, double> Pairs
    {
        get
        {
            (string Foreground, string Background, double Floor)[] pairs =
            [
                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.AppBackground), 4.5),
                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.ShellSurface), 4.5),
                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.PanelSurface), 4.5),
                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.PanelSurfaceDeep), 4.5),
                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.DialogBackground), 4.5),
                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.SunkenSurface), 4.5),
                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.InputSurface), 4.5),
                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.ControlSurface), 4.5),
                (nameof(UiThemePalette.TextValue), nameof(UiThemePalette.PanelSurfaceDeep), 4.5),
                (nameof(UiThemePalette.TextDefault), nameof(UiThemePalette.AppBackground), 4.5),
                (nameof(UiThemePalette.TextDefault), nameof(UiThemePalette.PanelSurface), 4.5),
                (nameof(UiThemePalette.TextDefault), nameof(UiThemePalette.ControlSurface), 4.5),
                (nameof(UiThemePalette.TextSecondary), nameof(UiThemePalette.AppBackground), 4.5),
                (nameof(UiThemePalette.TextSecondary), nameof(UiThemePalette.DialogBackground), 4.5),
                (nameof(UiThemePalette.TextSecondary), nameof(UiThemePalette.InputSurface), 4.5),
                (nameof(UiThemePalette.TextSecondary), nameof(UiThemePalette.ControlSurface), 4.5),
                (nameof(UiThemePalette.TextMuted), nameof(UiThemePalette.AppBackground), 4.5),
                (nameof(UiThemePalette.TextMuted), nameof(UiThemePalette.PanelSurface), 4.5),
                (nameof(UiThemePalette.TextAccent), nameof(UiThemePalette.AppBackground), 4.5),
                (nameof(UiThemePalette.TextAccent), nameof(UiThemePalette.ShellSurface), 4.5),

                // Disabled controls still show the value in force: text to READ, not a state cue.
                (nameof(UiThemePalette.TextDisabled), nameof(UiThemePalette.AppBackground), 4.5),
                (nameof(UiThemePalette.TextDisabled), nameof(UiThemePalette.DialogBackground), 4.5),
                (nameof(UiThemePalette.TextDisabled), nameof(UiThemePalette.InputSurface), 4.5),
                (nameof(UiThemePalette.TextDisabled), nameof(UiThemePalette.ControlSurface), 4.5),
                (nameof(UiThemePalette.TextDisabled), nameof(UiThemePalette.ButtonDisabledBackground), 4.5),
                (nameof(UiThemePalette.MeterMutedText), nameof(UiThemePalette.MeterSurface), 4.5),
                (nameof(UiThemePalette.MeterMutedText), nameof(UiThemePalette.MeterTrack), 4.5),
                (nameof(UiThemePalette.MeterText), nameof(UiThemePalette.MeterSurface), 4.5),

                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.ButtonBackground), 4.5),
                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.ButtonHoverBackground), 4.5),
                (nameof(UiThemePalette.TextPrimary), nameof(UiThemePalette.ButtonPressedBackground), 4.5),
                (nameof(UiThemePalette.TextOnAccent), nameof(UiThemePalette.AccentFill), 4.5),
                (nameof(UiThemePalette.TextOnAccent), nameof(UiThemePalette.AccentFillPressed), 4.5),
                (nameof(UiThemePalette.TitleBarTextActive), nameof(UiThemePalette.ButtonBackground), 4.5),

                (nameof(UiThemePalette.TitleBarText), nameof(UiThemePalette.TitleBarBackground), 4.5),
                (nameof(UiThemePalette.TitleBarTextActive), nameof(UiThemePalette.TitleBarBackground), 4.5),
                (nameof(UiThemePalette.TitleBarTextActive), nameof(UiThemePalette.TitleBarButtonFill), 4.5),

                (nameof(UiThemePalette.Error), nameof(UiThemePalette.ControlSurface), 4.5),
                (nameof(UiThemePalette.Error), nameof(UiThemePalette.AppBackground), 4.5),
                (nameof(UiThemePalette.Warning), nameof(UiThemePalette.AppBackground), 4.5),
                (nameof(UiThemePalette.Warning), nameof(UiThemePalette.ShellSurface), 4.5),
                (nameof(UiThemePalette.BandLockedHeaderText), nameof(UiThemePalette.BandLockedHeader), 4.5),
                (nameof(UiThemePalette.TextSecondary), nameof(UiThemePalette.SideLeftFill), 4.5),
                (nameof(UiThemePalette.TextSecondary), nameof(UiThemePalette.SideRightFill), 4.5),
                (nameof(UiThemePalette.TextOnAccent), nameof(UiThemePalette.SideLeftFillSelected), 4.5),
                (nameof(UiThemePalette.TextOnAccent), nameof(UiThemePalette.SideRightFillSelected), 4.5),
                (nameof(UiThemePalette.SideLeftBorder), nameof(UiThemePalette.ShellSurface), 3.0),
                (nameof(UiThemePalette.SideRightBorder), nameof(UiThemePalette.ShellSurface), 3.0),
                (nameof(UiThemePalette.Success), nameof(UiThemePalette.AppBackground), 4.5),
                (nameof(UiThemePalette.Success), nameof(UiThemePalette.PanelSurface), 4.5),

                (nameof(UiThemePalette.GraphAxisText), nameof(UiThemePalette.GraphSurface), 4.5),
                (nameof(UiThemePalette.GraphAxisText), nameof(UiThemePalette.GraphSurfaceMuted), 4.5),

                (nameof(UiThemePalette.GraphTickline), nameof(UiThemePalette.GraphSurface), 3.0),
                (nameof(UiThemePalette.Warning), nameof(UiThemePalette.BandPeakingStrip), 3.0),
                (nameof(UiThemePalette.Warning), nameof(UiThemePalette.BandPeakingStripSelected), 3.0),
                (nameof(UiThemePalette.Warning), nameof(UiThemePalette.BandLowShelfStrip), 3.0),
                (nameof(UiThemePalette.Warning), nameof(UiThemePalette.BandLowShelfStripSelected), 3.0),
                (nameof(UiThemePalette.Warning), nameof(UiThemePalette.BandHighShelfStrip), 3.0),
                (nameof(UiThemePalette.Warning), nameof(UiThemePalette.BandHighShelfStripSelected), 3.0),
                (nameof(UiThemePalette.Warning), nameof(UiThemePalette.BandAllPassStrip), 3.0),
                (nameof(UiThemePalette.Warning), nameof(UiThemePalette.BandAllPassStripSelected), 3.0),
                (nameof(UiThemePalette.AccentMark), nameof(UiThemePalette.AppBackground), 3.0),
                (nameof(UiThemePalette.AccentMark), nameof(UiThemePalette.TitleBarBackground), 3.0),
                (nameof(UiThemePalette.TextDisabled), nameof(UiThemePalette.ButtonBackground), 3.0),
            ];

            var data = new TheoryData<string, string, string, double>();
            foreach (UiThemePalette theme in Themes)
            {
                foreach ((string foreground, string background, double floor) in pairs)
                {
                    data.Add(theme.Theme.ToString(), foreground, background, floor);
                }
            }

            return data;
        }
    }

    // A curve is only readable if it separates from the surface it is drawn on; 3:1 is the graphical-object floor.
    public static TheoryData<string, string, string> CurvesOnSurfaces
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (UiThemePalette theme in Themes)
            {
                foreach (string curve in RoleNames(name =>
                    name.StartsWith("Curve", StringComparison.Ordinal) ||
                    name.StartsWith("Marker", StringComparison.Ordinal)))
                {
                    data.Add(theme.Theme.ToString(), curve, nameof(UiThemePalette.GraphSurface));
                    data.Add(theme.Theme.ToString(), curve, nameof(UiThemePalette.GraphSurfaceMuted));
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void PaintedTextPairs_ClearTheContrastFloor(
        string theme, string foreground, string background, double floor)
    {
        UiThemePalette palette = Palette(theme);
        double ratio = ContrastRatio(RoleColor(palette, foreground), RoleColor(palette, background));

        Assert.True(
            ratio >= floor,
            $"{theme}: {foreground} on {background} is {ratio:0.00}:1, under the {floor:0.0}:1 floor.");
    }

    // 3:1 is the floor for a graphical object. The dark curve set predates it and is the owner's tuning - the
    // weakest (the third harmonic, violet on deep blue) reads 1.58:1 - so dark is held where it stands instead.
    private const double DarkCurveFloor = 1.55;
    private const double CurveFloor = 3.0;

    [Theory]
    [MemberData(nameof(CurvesOnSurfaces))]
    public void EveryCurve_SeparatesFromThePlotSurface(string theme, string curve, string surface)
    {
        UiThemePalette palette = Palette(theme);
        double floor = theme == nameof(UiTheme.Dark) ? DarkCurveFloor : CurveFloor;
        double ratio = ContrastRatio(RoleColor(palette, curve), RoleColor(palette, surface));

        Assert.True(
            ratio >= floor,
            $"{theme}: {curve} on {surface} is {ratio:0.00}:1, under the {floor:0.00}:1 floor.");
    }

    [Theory]
    [InlineData(nameof(UiTheme.Dark))]
    [InlineData(nameof(UiTheme.Light))]
    public void EveryVirtualDspChannel_SeparatesFromThePlotSurface(string theme)
    {
        UiThemePalette palette = Palette(theme);
        for (int i = 0; i < palette.ChannelCurves.Count; i++)
        {
            double ratio = ContrastRatio(palette.ChannelCurves[i], palette.GraphSurface);
            Assert.True(
                ratio >= (theme == nameof(UiTheme.Dark) ? DarkCurveFloor : CurveFloor),
                $"{theme}: channel curve {i} is {ratio:0.00}:1 on the plot surface.");
        }
    }

    // The colour a free overlay slot starts in is a curve colour the moment the slot is captured into.
    [Theory]
    [InlineData(nameof(UiTheme.Dark))]
    [InlineData(nameof(UiTheme.Light))]
    public void EveryOverlaySlotDefault_SeparatesFromThePlotSurface(string theme)
    {
        UiThemePalette palette = Palette(theme);

        Assert.Equal(OverlayFile.MaximumSlotCount, palette.OverlaySlotDefaults.Count);
        for (int i = 0; i < palette.OverlaySlotDefaults.Count; i++)
        {
            double ratio = ContrastRatio(palette.OverlaySlotDefaults[i], palette.GraphSurface);
            Assert.True(
                ratio >= CurveFloor,
                $"{theme}: overlay slot {i + 1} is {ratio:0.00}:1 on the plot surface.");
        }
    }

    [Theory]
    [InlineData(nameof(UiTheme.Dark))]
    [InlineData(nameof(UiTheme.Light))]
    public void DisabledText_ReadsFainterThanLiveText(string theme)
    {
        UiThemePalette palette = Palette(theme);
        double disabled = ContrastRatio(palette.TextDisabled, palette.AppBackground);

        Assert.True(disabled < ContrastRatio(palette.TextSecondary, palette.AppBackground));
        Assert.True(ContrastRatio(palette.TextPrimary, palette.AppBackground) / disabled >= 1.5);
    }

    [Theory]
    [InlineData(nameof(UiTheme.Dark))]
    [InlineData(nameof(UiTheme.Light))]
    public void TheTextLadder_DescendsFromPrimaryToMuted(string theme)
    {
        UiThemePalette palette = Palette(theme);
        double[] ladder =
        [
            ContrastRatio(palette.TextPrimary, palette.AppBackground),
            ContrastRatio(palette.TextDefault, palette.AppBackground),
            ContrastRatio(palette.TextSecondary, palette.AppBackground),
            ContrastRatio(palette.TextMuted, palette.AppBackground)
        ];

        for (int i = 1; i < ladder.Length; i++)
        {
            Assert.True(
                ladder[i] < ladder[i - 1],
                $"{theme}: step {i} of the text ladder does not fall ({ladder[i - 1]:0.00} then {ladder[i]:0.00}).");
        }
    }

    [Fact]
    public void TheFacade_ExposesEveryRole()
    {
        foreach (PropertyInfo role in typeof(UiThemePalette)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != nameof(UiThemePalette.Theme)))
        {
            PropertyInfo? exposed = typeof(UiPalette).GetProperty(
                role.Name, BindingFlags.Public | BindingFlags.Static);
            Assert.True(exposed != null, $"UiPalette does not expose {role.Name}.");
            Assert.Equal(role.PropertyType, exposed!.PropertyType);
        }
    }

    [Fact]
    public void TheLightTheme_AnswersEveryRoleDifferently()
    {
        List<string> unanswered = [];
        foreach (PropertyInfo role in typeof(UiThemePalette)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(Color)))
        {
            var dark = (Color)role.GetValue(UiThemePalette.Dark)!;
            var light = (Color)role.GetValue(UiThemePalette.Light)!;
            if (dark == light)
            {
                unanswered.Add(role.Name);
            }
        }

        // TextOnAccent rides an accent fill that does not change; the other two are written into user files.
        Assert.Equal(
            [
                nameof(UiThemePalette.AccentFill),
                nameof(UiThemePalette.AccentFillPressed),
                nameof(UiThemePalette.CursorOutline),
                nameof(UiThemePalette.TextOnAccent)
            ],
            unanswered.Order(StringComparer.Ordinal));
    }

    private static IEnumerable<string> RoleNames(Func<string, bool> predicate) =>
        typeof(UiThemePalette)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(Color) && predicate(property.Name))
            .Select(property => property.Name);

    private static UiThemePalette Palette(string theme) =>
        theme == nameof(UiTheme.Light) ? UiThemePalette.Light : UiThemePalette.Dark;

    private static Color RoleColor(UiThemePalette palette, string role)
    {
        PropertyInfo? property = typeof(UiThemePalette).GetProperty(
            role, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return (Color)property!.GetValue(palette)!;
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
