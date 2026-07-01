using System.Drawing;

namespace Resonalyze.Ui;

internal static class UiPalette
{
    public static Color AppBackground => Color.FromArgb(45, 50, 60);
    public static Color DialogBackground => Color.FromArgb(40, 42, 48);
    public static Color DialogSurface => Color.FromArgb(55, 58, 65);
    public static Color DialogSurfaceAlt => Color.FromArgb(32, 36, 46);
    public static Color DialogSurfaceMuted => Color.FromArgb(62, 65, 73);
    public static Color DialogBorder => Color.FromArgb(100, 105, 115);
    public static Color DialogBorderSoft => Color.FromArgb(90, 94, 104);
    public static Color DialogBackgroundSoft => Color.FromArgb(50, 50, 50);
    public static Color ButtonBackground => Color.FromArgb(50, 55, 80);
    public static Color ButtonBackgroundWide => Color.FromArgb(50, 55, 100);
    public static Color ButtonDisabledBackground => Color.FromArgb(55, 60, 70);
    public static Color ButtonPressedBackground => Color.FromArgb(40, 45, 68);
    public static Color ButtonHoverBackground => Color.FromArgb(50, 55, 120);
    public static Color TitleBarBackground => Color.FromArgb(28, 30, 36);
    public static Color TitleBarText => Color.FromArgb(168, 176, 190);
    public static Color TitleBarTextSoft => Color.FromArgb(220, 224, 232);
    public static Color TitleBarTextBright => Color.FromArgb(230, 232, 238);
    public static Color AccentBlue => Color.FromArgb(64, 116, 255);
    public static Color AccentBlueHover => Color.FromArgb(78, 130, 255);
    public static Color AccentBlueStrong => Color.FromArgb(36, 86, 210);
    public static Color AccentBlueSoft => Color.FromArgb(106, 173, 255);
    public static Color AccentBlueSoftHover => Color.FromArgb(150, 210, 255);
    public static Color AccentBlueMuted => Color.FromArgb(54, 58, 68);
    public static Color AccentBlueMutedAlt => Color.FromArgb(150, 32, 22);
    public static Color AccentBlueWarning => Color.FromArgb(196, 43, 28);
    public static Color TextPrimary => Color.White;
    public static Color TextPrimarySoft => Color.FromArgb(220, 225, 235);
    public static Color TextSecondary => Color.FromArgb(185, 190, 200);
    public static Color TextSecondarySoft => Color.FromArgb(190, 195, 205);
    public static Color TextSecondaryAlt => Color.FromArgb(205, 210, 220);
    public static Color TextHighlight => Color.FromArgb(210, 214, 222);
    public static Color TextMuted => Color.FromArgb(120, 125, 135);
    public static Color TextBright => Color.FromArgb(235, 237, 240);
    public static Color ControlSurface => Color.FromArgb(55, 60, 72);
    public static Color InputSurface => Color.FromArgb(55, 58, 65);
    public static Color PlotSurface => Color.FromArgb(50, 55, 100);
    public static Color PlotSurfaceMuted => Color.FromArgb(40, 44, 54);
    public static Color PlotSurfaceDark => Color.FromArgb(38, 42, 52);
    public static Color PlotTrack => Color.FromArgb(24, 28, 36);
    public static Color PlotBorder => Color.FromArgb(78, 84, 98);
    public static Color MeterText => Color.FromArgb(225, 230, 240);
    public static Color MeterMutedText => Color.FromArgb(128, 135, 150);
    public static Color MeterPeakHold => Color.FromArgb(248, 248, 252);
    public static Color MeterFullScale => Color.FromArgb(95, 200, 255);
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
    public static Color ErrorSoft => Color.FromArgb(255, 110, 110);
    public static Color ErrorSoftTint => Color.FromArgb(255, 210, 210);
    // Light accents echoing the Time Alignment envelope markers: first arrival red,
    // strongest peak blue.
    public static Color TimeAlignmentFirstArrival => Color.FromArgb(236, 148, 148);
    public static Color TimeAlignmentStrongestPeak => Color.FromArgb(150, 180, 250);
    public static Color TextMutedSoft => Color.FromArgb(165, 170, 180);
}
