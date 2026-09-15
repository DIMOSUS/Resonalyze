using System.Drawing;

namespace Resonalyze.Ui;

/// <summary>Dark-theme colours named by role. Text pairs are held above WCAG 4.5:1 (3:1 for UI) by <c>UiPaletteContrastTests</c>.</summary>
internal static class UiPalette
{
    public static Color AppBackground => Color.FromArgb(45, 50, 60);
    public static Color DialogBackground => Color.FromArgb(40, 42, 48);
    public static Color DialogSurface => Color.FromArgb(55, 58, 65);
    public static Color DialogSurfaceMuted => Color.FromArgb(62, 65, 73);
    public static Color DialogBorder => Color.FromArgb(100, 105, 115);
    public static Color DialogBorderSoft => Color.FromArgb(90, 94, 104);
    public static Color ButtonBackground => Color.FromArgb(50, 55, 80);
    public static Color ButtonDisabledBackground => Color.FromArgb(55, 60, 70);
    public static Color ButtonPressedBackground => Color.FromArgb(40, 45, 68);
    public static Color ButtonHoverBackground => Color.FromArgb(50, 55, 120);
    public static Color TitleBarBackground => Color.FromArgb(28, 30, 36);
    public static Color TitleBarText => Color.FromArgb(168, 176, 190);
    public static Color TitleBarTextSoft => Color.FromArgb(220, 224, 232);
    public static Color TitleBarTextBright => Color.FromArgb(230, 232, 238);
    // Accent fill (carries white text) is deliberately darker than the mark AccentBlueSoft (sits on dark surfaces). No hover lift: it drops the label under 4.5:1.
    public static Color AccentFill => Color.FromArgb(36, 86, 210);
    public static Color AccentFillPressed => Color.FromArgb(24, 60, 150);
    public static Color AccentBlueSoft => Color.FromArgb(106, 173, 255);
    public static Color AccentBlueSoftHover => Color.FromArgb(150, 210, 255);
    public static Color AccentBlueGlow => Color.FromArgb(196, 228, 255);
    public static Color AccentBlueMuted => Color.FromArgb(54, 58, 68);
    public static Color AccentBlueMutedAlt => Color.FromArgb(150, 32, 22);
    public static Color AccentBlueWarning => Color.FromArgb(196, 43, 28);
    public static Color TextPrimary => Color.White;
    public static Color TextPrimarySoft => Color.FromArgb(220, 225, 235);
    public static Color TextSecondary => Color.FromArgb(185, 190, 200);
    public static Color TextSecondarySoft => Color.FromArgb(190, 195, 205);
    public static Color TextSecondaryAlt => Color.FromArgb(205, 210, 220);
    public static Color TextHighlight => Color.FromArgb(210, 214, 222);
    // Disabled controls still show the value in force, so it must stay readable; the surface swap carries the disabled state.
    public static Color TextDisabled => Color.FromArgb(165, 170, 180);
    public static Color TextBright => Color.FromArgb(235, 237, 240);
    public static Color ControlSurface => Color.FromArgb(55, 60, 72);
    public static Color InputSurface => Color.FromArgb(55, 58, 65);
    public static Color PlotSurfaceDark => Color.FromArgb(38, 42, 52);
    public static Color PlotTrack => Color.FromArgb(24, 28, 36);
    public static Color PlotBorder => Color.FromArgb(78, 84, 98);
    public static Color MeterText => Color.FromArgb(225, 230, 240);
    public static Color MeterMutedText => Color.FromArgb(146, 153, 168);
    public static Color MeterPeakHold => Color.FromArgb(248, 248, 252);
    public static Color MeterLowAccent => Color.FromArgb(88, 182, 255);
    public static Color MeterDimFill => Color.FromArgb(80, 86, 100);
    public static Color MeterTrackInactive => Color.FromArgb(30, 34, 42);
    public static Color MeterBorderInactive => Color.FromArgb(56, 60, 70);
    public static Color MeterGrid => Color.FromArgb(90, 18, 20, 26);
    public static Color MeterBand => Color.FromArgb(127, 12, 14, 18);
    public static Color SuccessGreen => Color.FromArgb(90, 220, 120);
    public static Color SuccessGreenAlt => Color.FromArgb(136, 224, 112);
    public static Color SuccessGreenSoft => Color.FromArgb(170, 220, 95);
    public static Color WarningAmber => Color.FromArgb(255, 190, 80);
    public static Color WarningOrange => Color.FromArgb(255, 196, 76);
    public static Color WarningRed => Color.FromArgb(255, 96, 96);
    // Error text, held above 4.5:1; WarningRed is a fill and answers to no text threshold.
    public static Color ErrorSoft => Color.FromArgb(255, 130, 130);
    public static Color ErrorSoftTint => Color.FromArgb(255, 210, 210);
    public static Color TimeAlignmentFirstArrival => Color.FromArgb(236, 148, 148);
    public static Color TimeAlignmentStrongestPeak => Color.FromArgb(150, 180, 250);
    public static Color TimeAlignmentEnergyOnset => Color.FromArgb(150, 220, 160);

    // OxyPlot defaults are a light theme (labels at 1.9:1 here); PlotModelStyle.ApplyChrome applies these. The Plot* colours above are meter/fader surfaces.
    public static Color GraphSurface => Color.FromArgb(50, 55, 100);
    public static Color GraphAxisText => Color.FromArgb(228, 232, 240);
    public static Color GraphTickline => Color.FromArgb(165, 172, 192);
    // Grid lines are white with alpha: plot backgrounds differ per panel, and alpha lands within 0.02 contrast on all of them.
    // Axis labels and ticks are ink, not alpha.
    public static Color GraphAreaBorder => Color.FromArgb(60, 255, 255, 255);
    public static Color GraphGridlineMajor => Color.FromArgb(32, 255, 255, 255);
    public static Color GraphGridlineMinor => Color.FromArgb(15, 255, 255, 255);
}
