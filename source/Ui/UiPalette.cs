using System.Drawing;

namespace Resonalyze.Ui;

/// <summary>
/// Every colour the app paints itself with. The values below are one theme — the
/// dark one — and the names are the roles, so a second theme means new values
/// here rather than new call sites: a fill stays a fill and disabled text stays
/// disabled text whatever the background under them becomes.
/// Foreground/background pairs that carry text are held above the WCAG contrast
/// thresholds (4.5:1 for text, 3:1 for a UI element) by
/// <c>UiPaletteContrastTests</c>, which fails if a value drifts back under them.
/// </summary>
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
    // Accent as a FILL and accent as a MARK pull in opposite directions, so they
    // are different colours: a fill carries near-white text ON it and has to be
    // dark enough for that, while a link, a focus border or a selection marker
    // sits ON a dark surface and has to be light enough to show against it. The
    // fill is here; the mark is AccentBlueSoft just below.
    // The fill used to be the vivid (64,116,255), which left the active mode tab's
    // own label at 3.1:1 — this deeper blue reads it at 4.8:1, and it is the same
    // blue the dropdowns and the history list already filled a selection with.
    // A filled accent gets no hover lift: a blue light enough to read as one puts
    // the label back under 4.5:1, and the active tab is where the pointer already
    // is.
    public static Color AccentFill => Color.FromArgb(36, 86, 210);
    public static Color AccentFillPressed => Color.FromArgb(24, 60, 150);
    public static Color AccentBlueSoft => Color.FromArgb(106, 173, 255);
    public static Color AccentBlueSoftHover => Color.FromArgb(150, 210, 255);
    // The bright end of the title bar update notice pulse: pale enough to read as a
    // glow against the near-black bar, still blue enough to stay the link colour.
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
    // The colour of a control that cannot be edited right now — which in this app
    // is not the same as a control with nothing to say: a greyed-out rate, gate or
    // gain still shows the value in force, and that value has to stay READABLE.
    // At the old (120,125,135) it measured 2.7:1 on the control surface and users
    // reported working by guessing where a number should be (#116). The state is
    // carried by the surface and border underneath as well (DarkComboBox and
    // DarkNumericUpDown swap their background), so the text does not have to
    // disappear to say "disabled".
    public static Color TextDisabled => Color.FromArgb(165, 170, 180);
    public static Color TextBright => Color.FromArgb(235, 237, 240);
    public static Color ControlSurface => Color.FromArgb(55, 60, 72);
    public static Color InputSurface => Color.FromArgb(55, 58, 65);
    public static Color PlotSurfaceDark => Color.FromArgb(38, 42, 52);
    public static Color PlotTrack => Color.FromArgb(24, 28, 36);
    public static Color PlotBorder => Color.FromArgb(78, 84, 98);
    public static Color MeterText => Color.FromArgb(225, 230, 240);
    // Names the input meter greys out when a channel is not available — the same
    // "disabled but still informative" text as TextDisabled, and it was under the
    // threshold for the same reason (4.0:1 on the meter surface, now 5.0:1).
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
    // Error TEXT — the Time Alignment status lines, the fader's bottom label — so
    // it is held above 4.5:1 on the surfaces it is written on; at (255,110,110) it
    // measured 4.1:1 on the control surface. WarningRed stays where it is: it fills
    // meter bars and fader grooves, and a fill answers to no text threshold.
    public static Color ErrorSoft => Color.FromArgb(255, 130, 130);
    public static Color ErrorSoftTint => Color.FromArgb(255, 210, 210);
    // Light accents echoing the Time Alignment envelope markers: first arrival red,
    // strongest peak blue.
    public static Color TimeAlignmentFirstArrival => Color.FromArgb(236, 148, 148);
    public static Color TimeAlignmentStrongestPeak => Color.FromArgb(150, 180, 250);

    // Chrome of the OxyPlot graphs — the surface a plot is drawn on and the axis
    // furniture on top of it. OxyPlot's own defaults are a light theme (black tick
    // lines, a black-alpha grid, a black plot-area border, and axis labels that
    // follow PlotModel.TextColor, itself black), so a model that says nothing
    // renders its numbers at 1.9:1 on this surface. PlotModelStyle.ApplyChrome is
    // what says otherwise; these are the values it says.
    // Note the Plot* colours above are NOT these: they are the input meter and
    // fader surfaces, which only look like plots.
    public static Color GraphSurface => Color.FromArgb(50, 55, 100);
    public static Color GraphAxisText => Color.FromArgb(228, 232, 240);
    public static Color GraphTickline => Color.FromArgb(165, 172, 192);
    // The lines drawn ON the surface are WHITE WITH ALPHA, not fixed colours, for
    // the same reason OxyPlot's own defaults are black with alpha: GraphSurface is
    // not the only plot background in the app. Virtual DSP's two views sit on
    // (40,44,80) and the option previews on (32,36,46), both from their own
    // designers, so one fixed grid colour reads at a different strength in every
    // panel — measured 1.33:1 on the main plots against 1.59:1 on Virtual DSP,
    // which is exactly how it looked: washed out in one place and loud in the
    // other. As alpha the grid lands within 0.02 of itself on all four surfaces.
    // The strength is chosen against the curves: the grid has to be followable to
    // the axis and must not compete with a trace crossing it (2.0:1 and 1.5:1
    // passes were both rejected on real measurements as foreground).
    // Axis labels and tick lines are deliberately NOT alpha — they are ink, they
    // belong with the text, and a darker panel making them clearer is only good.
    public static Color GraphAreaBorder => Color.FromArgb(60, 255, 255, 255);
    public static Color GraphGridlineMajor => Color.FromArgb(32, 255, 255, 255);
    public static Color GraphGridlineMinor => Color.FromArgb(15, 255, 255, 255);
}
