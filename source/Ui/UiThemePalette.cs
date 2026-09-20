using System.Drawing;

namespace Resonalyze.Ui;

/// <summary>One theme: every colour the application paints with, named by the role it plays.
/// Roles are <c>required</c>, so a role cannot be added to one theme without being answered in the other.</summary>
/// <remarks>Text pairs are held above WCAG 4.5:1 (3:1 for UI) in both themes by <c>UiPaletteContrastTests</c>.</remarks>
internal sealed class UiThemePalette
{
    public required UiTheme Theme { get; init; }

    // Surfaces, from the window inwards: the form, a docked mode panel, a card on it, a sunken read-out strip.
    public required Color AppBackground { get; init; }
    public required Color ShellSurface { get; init; }
    public required Color PanelSurface { get; init; }
    public required Color PanelSurfaceDeep { get; init; }
    public required Color DialogBackground { get; init; }
    public required Color SunkenSurface { get; init; }
    public required Color InputSurface { get; init; }
    public required Color ControlSurface { get; init; }
    public required Color RowHighlightSurface { get; init; }
    public required Color TitleBarBackground { get; init; }

    public required Color Border { get; init; }
    public required Color BorderSoft { get; init; }
    public required Color BorderMuted { get; init; }

    public required Color ButtonBackground { get; init; }
    public required Color ButtonHoverBackground { get; init; }
    public required Color ButtonPressedBackground { get; init; }
    public required Color ButtonDisabledBackground { get; init; }
    // Accent fill carries TextOnAccent, white in both themes; no hover lift, it drops the label under 4.5:1.
    public required Color AccentFill { get; init; }
    public required Color AccentFillPressed { get; init; }
    public required Color ToggleCheckedFill { get; init; }

    // The text ladder, brightest read-out down to a hint. TextOnAccent is the only one that ignores the theme.
    public required Color TextOnAccent { get; init; }
    public required Color TextPrimary { get; init; }
    public required Color TextBright { get; init; }
    public required Color TextValue { get; init; }
    public required Color TextDefault { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color TextMuted { get; init; }
    // Disabled controls still show the value in force, so it must stay readable; the surface swap carries the state.
    public required Color TextDisabled { get; init; }
    public required Color TextAccent { get; init; }

    public required Color AccentMark { get; init; }
    public required Color AccentMarkHover { get; init; }
    public required Color AccentGlow { get; init; }

    public required Color Success { get; init; }
    public required Color Warning { get; init; }
    public required Color Danger { get; init; }
    public required Color Error { get; init; }
    public required Color ErrorTint { get; init; }

    public required Color TitleBarText { get; init; }
    public required Color TitleBarTextActive { get; init; }
    public required Color TitleBarButtonFill { get; init; }
    public required Color UpdateBadgeFill { get; init; }
    public required Color UpdateBadgeFillDim { get; init; }

    public required Color MeterSurface { get; init; }
    public required Color MeterTrack { get; init; }
    public required Color MeterTrackInactive { get; init; }
    public required Color MeterBorder { get; init; }
    public required Color MeterBorderInactive { get; init; }
    public required Color MeterDimFill { get; init; }
    public required Color MeterPeakHold { get; init; }
    public required Color MeterText { get; init; }
    public required Color MeterMutedText { get; init; }
    public required Color MeterGrid { get; init; }
    public required Color MeterBand { get; init; }

    public required Color FaderCapTop { get; init; }
    public required Color FaderCapBottom { get; init; }
    public required Color FaderCapHoverTop { get; init; }
    public required Color FaderCapHoverBottom { get; init; }
    public required Color FaderCapPressedTop { get; init; }
    public required Color FaderCapPressedBottom { get; init; }
    public required Color FaderTick { get; init; }

    // Curves and the marks on them. A curve says WHICH quantity it is, so its hue survives the theme; only the
    // lightness moves, because the same ink has to read on a dark plot and on a white one.
    public required Color CurveNeutral { get; init; }
    public required Color CurveMuted { get; init; }
    public required Color CurveHarmonic2 { get; init; }
    public required Color CurveHarmonic3 { get; init; }
    public required Color CurveHarmonic4 { get; init; }
    public required Color CurveMinimumPhase { get; init; }
    public required Color CurveExcessPhase { get; init; }
    public required Color CurveEnvelope { get; init; }
    public required Color CurveStep { get; init; }
    public required Color CurveArrayAverage { get; init; }
    public required Color CurveArrayMicrophone { get; init; }
    public required Color CurveArraySpread { get; init; }
    public required Color CurveFallback { get; init; }
    public required Color CurveTarget { get; init; }
    public required Color CurveSource { get; init; }
    public required Color CurveSourcePlusEq { get; init; }
    public required Color CurveEqBank { get; init; }
    public required Color CurveKernel { get; init; }
    public required Color CurvePhase { get; init; }
    public required Color CurveLiveTransfer { get; init; }
    public required Color CurveLiveInput { get; init; }
    public required Color CurvePeakHold { get; init; }
    public required Color CurveCoherence { get; init; }
    public required Color CurveCompare { get; init; }
    public required Color CurveAboveTarget { get; init; }
    public required Color CurveBelowTarget { get; init; }
    public required Color CurveBandOverlay { get; init; }
    public required Color CurveWindowFill { get; init; }
    public required Color CurveWindowGuide { get; init; }
    public required Color CurveWindowMarker { get; init; }
    public required Color CurveChainA { get; init; }
    public required Color CurveChainB { get; init; }
    public required Color CurveChainC { get; init; }
    public required Color CurveChainD { get; init; }
    public required Color CurveZoneFront { get; init; }
    public required Color CurveZoneRear { get; init; }
    public required Color CurveZoneCentre { get; init; }
    public required Color CurveZoneSub { get; init; }
    /// <summary>The colour a free overlay slot starts in, one per slot. A slot that has been captured carries its
    /// own colour in its file and is not touched by the theme.</summary>
    public required IReadOnlyList<Color> OverlaySlotDefaults { get; init; }

    /// <summary>One entry per Virtual DSP channel, in the order the channels are created.</summary>
    public required IReadOnlyList<Color> ChannelCurves { get; init; }

    public required Color MarkerArrival { get; init; }
    public required Color MarkerPeak { get; init; }

    // Controls drawn ON the plot (zoom buttons, the zoom box and its read-out) are ink over the surface, so they
    // carry their own alpha and swap ends with the theme.
    public required Color PlotOverlayFill { get; init; }
    public required Color PlotOverlayStroke { get; init; }
    public required Color PlotOverlayFillHovered { get; init; }
    public required Color PlotOverlayStrokeHovered { get; init; }
    public required Color PlotZoomBoxFill { get; init; }
    public required Color PlotZoomBoxStroke { get; init; }
    public required Color PlotZoomLabelFill { get; init; }
    public required Color PlotZoomLabelStroke { get; init; }
    public required Color PlotLegendBackground { get; init; }

    public required Color WaterfallSurface { get; init; }
    public required Color WaterfallOutline { get; init; }
    public required Color WaterfallMarker { get; init; }
    public required Color WaterfallLabel { get; init; }

    // Band shape reads as a wash of its own hue over the strip; the level says whether the strip is a tile,
    // a row, or the selected row.
    public required Color BandLowShelfStrip { get; init; }
    public required Color BandLowShelfStripSelected { get; init; }
    public required Color BandLowShelfTile { get; init; }
    public required Color BandHighShelfStrip { get; init; }
    public required Color BandHighShelfStripSelected { get; init; }
    public required Color BandHighShelfTile { get; init; }
    public required Color BandAllPassStrip { get; init; }
    public required Color BandAllPassStripSelected { get; init; }
    public required Color BandAllPassTile { get; init; }
    public required Color BandPeakingStrip { get; init; }
    public required Color BandPeakingStripSelected { get; init; }
    public required Color BandPeakingTile { get; init; }

    // The drag cursor is drawn over whatever it hovers, so it keeps its own outline in both themes. The two
    // curve defaults are written into user FILES at creation: the value is fixed there, but which value a new
    // curve starts from follows the theme it was created in.
    public required Color CursorOutline { get; init; }
    public required Color CurveOverlayDefault { get; init; }
    public required Color CurveTargetDefault { get; init; }

    // OxyPlot defaults are a light theme; PlotModelStyle.ApplyChrome replaces them with these.
    public required Color MarkerFirstArrival { get; init; }
    public required Color MarkerStrongestPeak { get; init; }
    public required Color MarkerEnergyOnset { get; init; }

    public required Color GraphSurface { get; init; }
    public required Color GraphSurfaceMuted { get; init; }
    public required Color GraphAxisText { get; init; }
    public required Color GraphTickline { get; init; }
    // Grid and border are ink WITH ALPHA: they lie on the plot surface and must read the same on every one of them.
    public required Color GraphAreaBorder { get; init; }
    public required Color GraphGridlineMajor { get; init; }
    public required Color GraphGridlineMinor { get; init; }

    public static UiThemePalette Dark { get; } = new()
    {
        Theme = UiTheme.Dark,

        AppBackground = Color.FromArgb(45, 50, 60),
        ShellSurface = Color.FromArgb(40, 44, 54),
        PanelSurface = Color.FromArgb(46, 50, 62),
        PanelSurfaceDeep = Color.FromArgb(20, 22, 30),
        DialogBackground = Color.FromArgb(40, 42, 48),
        SunkenSurface = Color.FromArgb(33, 36, 45),
        InputSurface = Color.FromArgb(55, 58, 65),
        ControlSurface = Color.FromArgb(55, 60, 72),
        RowHighlightSurface = Color.FromArgb(54, 58, 68),
        TitleBarBackground = Color.FromArgb(28, 30, 36),

        Border = Color.FromArgb(100, 105, 115),
        BorderSoft = Color.FromArgb(90, 94, 104),
        BorderMuted = Color.FromArgb(70, 76, 92),

        ButtonBackground = Color.FromArgb(50, 55, 80),
        ButtonHoverBackground = Color.FromArgb(50, 55, 120),
        ButtonPressedBackground = Color.FromArgb(40, 45, 68),
        ButtonDisabledBackground = Color.FromArgb(55, 60, 70),
        AccentFill = Color.FromArgb(36, 86, 210),
        AccentFillPressed = Color.FromArgb(24, 60, 150),
        ToggleCheckedFill = Color.FromArgb(80, 100, 140),

        TextOnAccent = Color.White,
        TextPrimary = Color.White,
        TextBright = Color.FromArgb(235, 237, 240),
        TextValue = Color.FromArgb(225, 228, 235),
        TextDefault = Color.FromArgb(210, 214, 222),
        TextSecondary = Color.FromArgb(185, 190, 200),
        TextMuted = Color.FromArgb(165, 170, 180),
        TextDisabled = Color.FromArgb(165, 170, 180),
        TextAccent = Color.FromArgb(150, 170, 205),

        AccentMark = Color.FromArgb(106, 173, 255),
        AccentMarkHover = Color.FromArgb(150, 210, 255),
        AccentGlow = Color.FromArgb(196, 228, 255),

        Success = Color.FromArgb(96, 210, 120),
        Warning = Color.FromArgb(255, 190, 80),
        Danger = Color.FromArgb(255, 96, 96),
        Error = Color.FromArgb(255, 130, 130),
        ErrorTint = Color.FromArgb(255, 210, 210),

        TitleBarText = Color.FromArgb(168, 176, 190),
        TitleBarTextActive = Color.FromArgb(225, 228, 235),
        TitleBarButtonFill = Color.FromArgb(54, 58, 68),
        UpdateBadgeFill = Color.FromArgb(196, 43, 28),
        UpdateBadgeFillDim = Color.FromArgb(150, 32, 22),

        MeterSurface = Color.FromArgb(38, 42, 52),
        MeterTrack = Color.FromArgb(24, 28, 36),
        MeterTrackInactive = Color.FromArgb(30, 34, 42),
        MeterBorder = Color.FromArgb(78, 84, 98),
        MeterBorderInactive = Color.FromArgb(56, 60, 70),
        MeterDimFill = Color.FromArgb(80, 86, 100),
        MeterPeakHold = Color.FromArgb(248, 248, 252),
        MeterText = Color.FromArgb(225, 230, 240),
        MeterMutedText = Color.FromArgb(146, 153, 168),
        MeterGrid = Color.FromArgb(90, 18, 20, 26),
        MeterBand = Color.FromArgb(127, 12, 14, 18),

        FaderCapTop = Color.FromArgb(48, 52, 62),
        FaderCapBottom = Color.FromArgb(34, 37, 45),
        FaderCapHoverTop = Color.FromArgb(72, 80, 112),
        FaderCapHoverBottom = Color.FromArgb(44, 50, 74),
        FaderCapPressedTop = Color.FromArgb(58, 64, 84),
        FaderCapPressedBottom = Color.FromArgb(36, 40, 54),
        FaderTick = Color.FromArgb(110, 150, 160, 175),

        CurveNeutral = Color.White,
        CurveMuted = Color.FromArgb(128, 128, 128),
        CurveHarmonic2 = Color.FromArgb(255, 64, 0),
        CurveHarmonic3 = Color.FromArgb(128, 64, 127),
        CurveHarmonic4 = Color.FromArgb(1, 64, 254),
        CurveMinimumPhase = Color.FromArgb(0, 200, 255),
        CurveExcessPhase = Color.FromArgb(130, 220, 90),
        CurveEnvelope = Color.FromArgb(255, 210, 80),
        CurveStep = Color.FromArgb(120, 200, 255),
        CurveArrayAverage = Color.FromArgb(120, 230, 190),
        CurveArrayMicrophone = Color.FromArgb(70, 130, 115),
        CurveArraySpread = Color.FromArgb(200, 140, 220),
        CurveFallback = Color.FromArgb(255, 127, 0),
        CurveTarget = Color.FromArgb(230, 184, 0),
        CurveSource = Color.FromArgb(180, 190, 205),
        CurveSourcePlusEq = Color.FromArgb(0, 209, 255),
        CurveEqBank = Color.FromArgb(198, 152, 255),
        CurveKernel = Color.FromArgb(90, 180, 255),
        CurvePhase = Color.FromArgb(210, 140, 255),
        CurveLiveTransfer = Color.FromArgb(255, 0, 127),
        CurveLiveInput = Color.FromArgb(80, 170, 255),
        CurvePeakHold = Color.FromArgb(255, 196, 0),
        CurveCoherence = Color.FromArgb(90, 200, 140),
        CurveCompare = Color.FromArgb(80, 210, 255),
        CurveAboveTarget = Color.FromArgb(232, 80, 80),
        CurveBelowTarget = Color.FromArgb(64, 176, 255),
        CurveBandOverlay = Color.FromArgb(255, 170, 40),
        CurveWindowFill = Color.FromArgb(50, 210, 120),
        CurveWindowGuide = Color.FromArgb(80, 150, 255),
        CurveWindowMarker = Color.FromArgb(255, 210, 70),
        CurveChainA = Color.FromArgb(79, 195, 247),
        CurveChainB = Color.FromArgb(200, 130, 255),
        CurveChainC = Color.FromArgb(124, 213, 124),
        CurveChainD = Color.FromArgb(255, 169, 79),
        CurveZoneFront = Color.FromArgb(86, 156, 255),
        CurveZoneRear = Color.FromArgb(255, 150, 64),
        CurveZoneCentre = Color.FromArgb(96, 210, 120),
        CurveZoneSub = Color.FromArgb(200, 130, 255),
        ChannelCurves =
        [
            Color.FromArgb(86, 156, 255),
            Color.FromArgb(255, 150, 64),
            Color.FromArgb(96, 210, 120),
            Color.FromArgb(200, 130, 255),
            Color.FromArgb(80, 210, 220),
            Color.FromArgb(240, 100, 140),
            Color.FromArgb(210, 200, 90),
            Color.FromArgb(140, 200, 90),
            Color.FromArgb(230, 120, 90),
            Color.FromArgb(150, 175, 215),
            Color.FromArgb(215, 180, 140),
            Color.FromArgb(90, 180, 175)
        ],

        OverlaySlotDefaults =
        [
            Color.FromArgb(255, 130, 70),
            Color.FromArgb(95, 190, 255),
            Color.FromArgb(120, 220, 120),
            Color.FromArgb(235, 150, 235),
            Color.FromArgb(245, 215, 90),
            Color.FromArgb(255, 115, 115),
            Color.FromArgb(120, 230, 220),
            Color.FromArgb(190, 165, 255),
            Color.FromArgb(205, 230, 125),
            Color.FromArgb(255, 175, 205),
            Color.FromArgb(175, 200, 235),
            Color.FromArgb(228, 192, 145)
        ],

        MarkerArrival = Color.FromArgb(130, 220, 90),
        MarkerPeak = Color.FromArgb(150, 170, 205),

        PlotOverlayFill = Color.FromArgb(70, 0, 0, 0),
        PlotOverlayStroke = Color.FromArgb(120, 255, 255, 255),
        PlotOverlayFillHovered = Color.FromArgb(150, 0, 0, 0),
        PlotOverlayStrokeHovered = Color.FromArgb(230, 255, 255, 255),
        PlotZoomBoxFill = Color.FromArgb(60, 255, 215, 0),
        PlotZoomBoxStroke = Color.FromArgb(220, 255, 215, 0),
        PlotZoomLabelFill = Color.FromArgb(220, 0, 0, 0),
        PlotZoomLabelStroke = Color.FromArgb(140, 255, 255, 255),
        PlotLegendBackground = Color.FromArgb(120, 40, 44, 54),

        WaterfallSurface = Color.FromArgb(30, 0, 50),
        WaterfallOutline = Color.FromArgb(0, 0, 0),
        WaterfallMarker = Color.FromArgb(0, 127, 32),
        WaterfallLabel = Color.FromArgb(0, 255, 255),

        BandLowShelfStrip = Color.FromArgb(58, 50, 45),
        BandLowShelfStripSelected = Color.FromArgb(78, 66, 58),
        BandLowShelfTile = Color.FromArgb(32, 27, 24),
        BandHighShelfStrip = Color.FromArgb(40, 56, 62),
        BandHighShelfStripSelected = Color.FromArgb(52, 76, 86),
        BandHighShelfTile = Color.FromArgb(22, 31, 35),
        BandAllPassStrip = Color.FromArgb(53, 47, 64),
        BandAllPassStripSelected = Color.FromArgb(71, 62, 92),
        BandAllPassTile = Color.FromArgb(29, 26, 36),
        BandPeakingStrip = Color.FromArgb(44, 50, 60),
        BandPeakingStripSelected = Color.FromArgb(58, 66, 86),
        BandPeakingTile = Color.FromArgb(25, 28, 34),

        CursorOutline = Color.FromArgb(8, 10, 14),
        CurveOverlayDefault = Color.FromArgb(230, 184, 0),
        CurveTargetDefault = Color.FromArgb(55, 200, 160),

        MarkerFirstArrival = Color.FromArgb(255, 96, 96),
        MarkerStrongestPeak = Color.FromArgb(140, 170, 255),
        MarkerEnergyOnset = Color.FromArgb(96, 200, 120),

        GraphSurface = Color.FromArgb(50, 55, 100),
        GraphSurfaceMuted = Color.FromArgb(32, 36, 46),
        GraphAxisText = Color.FromArgb(228, 232, 240),
        GraphTickline = Color.FromArgb(165, 172, 192),
        GraphAreaBorder = Color.FromArgb(60, 255, 255, 255),
        GraphGridlineMajor = Color.FromArgb(32, 255, 255, 255),
        GraphGridlineMinor = Color.FromArgb(15, 255, 255, 255)
    };

    public static UiThemePalette Light { get; } = new()
    {
        Theme = UiTheme.Light,

        AppBackground = Color.FromArgb(238, 240, 245),
        ShellSurface = Color.FromArgb(230, 233, 240),
        PanelSurface = Color.FromArgb(224, 228, 236),
        PanelSurfaceDeep = Color.FromArgb(208, 213, 224),
        DialogBackground = Color.FromArgb(240, 242, 246),
        SunkenSurface = Color.FromArgb(230, 233, 239),
        InputSurface = Color.FromArgb(252, 253, 255),
        ControlSurface = Color.FromArgb(252, 253, 255),
        RowHighlightSurface = Color.FromArgb(214, 222, 238),
        TitleBarBackground = Color.FromArgb(214, 219, 230),

        Border = Color.FromArgb(128, 136, 150),
        BorderSoft = Color.FromArgb(158, 166, 180),
        BorderMuted = Color.FromArgb(180, 188, 202),

        ButtonBackground = Color.FromArgb(214, 222, 240),
        ButtonHoverBackground = Color.FromArgb(196, 210, 242),
        ButtonPressedBackground = Color.FromArgb(178, 194, 232),
        ButtonDisabledBackground = Color.FromArgb(222, 225, 232),
        AccentFill = Color.FromArgb(36, 86, 210),
        AccentFillPressed = Color.FromArgb(24, 60, 150),
        ToggleCheckedFill = Color.FromArgb(150, 176, 226),

        TextOnAccent = Color.White,
        TextPrimary = Color.FromArgb(18, 21, 28),
        TextBright = Color.FromArgb(24, 28, 36),
        TextValue = Color.FromArgb(32, 37, 46),
        TextDefault = Color.FromArgb(42, 47, 58),
        TextSecondary = Color.FromArgb(74, 81, 94),
        TextMuted = Color.FromArgb(88, 95, 108),
        TextDisabled = Color.FromArgb(92, 99, 112),
        TextAccent = Color.FromArgb(24, 78, 152),

        AccentMark = Color.FromArgb(22, 82, 178),
        AccentMarkHover = Color.FromArgb(12, 60, 140),
        AccentGlow = Color.FromArgb(8, 44, 110),

        Success = Color.FromArgb(16, 104, 50),
        Warning = Color.FromArgb(140, 92, 0),
        Danger = Color.FromArgb(198, 40, 40),
        Error = Color.FromArgb(176, 28, 32),
        ErrorTint = Color.FromArgb(128, 16, 20),

        TitleBarText = Color.FromArgb(78, 85, 98),
        TitleBarTextActive = Color.FromArgb(24, 28, 36),
        TitleBarButtonFill = Color.FromArgb(206, 212, 224),
        UpdateBadgeFill = Color.FromArgb(178, 34, 22),
        UpdateBadgeFillDim = Color.FromArgb(214, 118, 106),

        MeterSurface = Color.FromArgb(226, 230, 238),
        MeterTrack = Color.FromArgb(206, 211, 222),
        MeterTrackInactive = Color.FromArgb(214, 218, 228),
        MeterBorder = Color.FromArgb(150, 158, 172),
        MeterBorderInactive = Color.FromArgb(186, 192, 204),
        MeterDimFill = Color.FromArgb(168, 175, 190),
        MeterPeakHold = Color.FromArgb(28, 32, 40),
        MeterText = Color.FromArgb(32, 37, 46),
        MeterMutedText = Color.FromArgb(82, 89, 102),
        MeterGrid = Color.FromArgb(90, 120, 126, 140),
        MeterBand = Color.FromArgb(127, 196, 202, 214),

        FaderCapTop = Color.FromArgb(250, 251, 253),
        FaderCapBottom = Color.FromArgb(214, 219, 230),
        FaderCapHoverTop = Color.FromArgb(232, 240, 255),
        FaderCapHoverBottom = Color.FromArgb(186, 202, 236),
        FaderCapPressedTop = Color.FromArgb(214, 222, 240),
        FaderCapPressedBottom = Color.FromArgb(176, 186, 206),
        FaderTick = Color.FromArgb(110, 70, 78, 92),

        CurveNeutral = Color.FromArgb(24, 28, 36),
        CurveMuted = Color.FromArgb(110, 116, 128),
        CurveHarmonic2 = Color.FromArgb(198, 44, 0),
        CurveHarmonic3 = Color.FromArgb(122, 40, 120),
        CurveHarmonic4 = Color.FromArgb(24, 52, 196),
        CurveMinimumPhase = Color.FromArgb(0, 108, 160),
        CurveExcessPhase = Color.FromArgb(58, 130, 30),
        CurveEnvelope = Color.FromArgb(160, 116, 0),
        CurveStep = Color.FromArgb(20, 102, 178),
        CurveArrayAverage = Color.FromArgb(10, 128, 98),
        CurveArrayMicrophone = Color.FromArgb(54, 108, 94),
        CurveArraySpread = Color.FromArgb(132, 60, 162),
        CurveFallback = Color.FromArgb(186, 86, 0),
        CurveTarget = Color.FromArgb(148, 108, 0),
        CurveSource = Color.FromArgb(88, 96, 112),
        CurveSourcePlusEq = Color.FromArgb(0, 108, 158),
        CurveEqBank = Color.FromArgb(104, 52, 168),
        CurveKernel = Color.FromArgb(16, 90, 174),
        CurvePhase = Color.FromArgb(122, 48, 174),
        CurveLiveTransfer = Color.FromArgb(196, 0, 98),
        CurveLiveInput = Color.FromArgb(20, 96, 180),
        CurvePeakHold = Color.FromArgb(158, 118, 0),
        CurveCoherence = Color.FromArgb(24, 118, 72),
        CurveCompare = Color.FromArgb(0, 110, 164),
        CurveAboveTarget = Color.FromArgb(196, 52, 52),
        CurveBelowTarget = Color.FromArgb(24, 104, 186),
        CurveBandOverlay = Color.FromArgb(176, 104, 0),
        CurveWindowFill = Color.FromArgb(22, 124, 66),
        CurveWindowGuide = Color.FromArgb(26, 86, 178),
        CurveWindowMarker = Color.FromArgb(158, 114, 0),
        CurveChainA = Color.FromArgb(14, 106, 158),
        CurveChainB = Color.FromArgb(128, 44, 186),
        CurveChainC = Color.FromArgb(34, 120, 34),
        CurveChainD = Color.FromArgb(178, 92, 0),
        CurveZoneFront = Color.FromArgb(20, 86, 190),
        CurveZoneRear = Color.FromArgb(176, 84, 0),
        CurveZoneCentre = Color.FromArgb(16, 120, 52),
        CurveZoneSub = Color.FromArgb(122, 44, 168),
        ChannelCurves =
        [
            Color.FromArgb(20, 86, 190),
            Color.FromArgb(176, 84, 0),
            Color.FromArgb(16, 120, 52),
            Color.FromArgb(122, 44, 168),
            Color.FromArgb(0, 110, 120),
            Color.FromArgb(176, 32, 86),
            Color.FromArgb(128, 108, 0),
            Color.FromArgb(70, 120, 20),
            Color.FromArgb(168, 60, 20),
            Color.FromArgb(60, 84, 140),
            Color.FromArgb(140, 96, 44),
            Color.FromArgb(16, 110, 104)
        ],

        OverlaySlotDefaults =
        [
            Color.FromArgb(188, 70, 0),
            Color.FromArgb(20, 95, 180),
            Color.FromArgb(16, 118, 52),
            Color.FromArgb(148, 45, 148),
            Color.FromArgb(128, 98, 0),
            Color.FromArgb(188, 30, 45),
            Color.FromArgb(0, 112, 112),
            Color.FromArgb(92, 58, 188),
            Color.FromArgb(92, 112, 0),
            Color.FromArgb(172, 40, 98),
            Color.FromArgb(58, 84, 140),
            Color.FromArgb(136, 84, 28)
        ],

        MarkerArrival = Color.FromArgb(46, 118, 28),
        MarkerPeak = Color.FromArgb(40, 76, 132),

        PlotOverlayFill = Color.FromArgb(80, 255, 255, 255),
        PlotOverlayStroke = Color.FromArgb(130, 0, 0, 0),
        PlotOverlayFillHovered = Color.FromArgb(170, 255, 255, 255),
        PlotOverlayStrokeHovered = Color.FromArgb(230, 0, 0, 0),
        PlotZoomBoxFill = Color.FromArgb(60, 210, 150, 0),
        PlotZoomBoxStroke = Color.FromArgb(220, 166, 116, 0),
        PlotZoomLabelFill = Color.FromArgb(230, 255, 255, 255),
        PlotZoomLabelStroke = Color.FromArgb(150, 0, 0, 0),
        PlotLegendBackground = Color.FromArgb(150, 236, 238, 242),

        WaterfallSurface = Color.FromArgb(250, 248, 252),
        WaterfallOutline = Color.FromArgb(64, 64, 74),
        WaterfallMarker = Color.FromArgb(0, 110, 28),
        WaterfallLabel = Color.FromArgb(0, 104, 116),

        BandLowShelfStrip = Color.FromArgb(246, 238, 228),
        BandLowShelfStripSelected = Color.FromArgb(250, 226, 200),
        BandLowShelfTile = Color.FromArgb(240, 232, 222),
        BandHighShelfStrip = Color.FromArgb(228, 240, 246),
        BandHighShelfStripSelected = Color.FromArgb(202, 230, 244),
        BandHighShelfTile = Color.FromArgb(224, 236, 242),
        BandAllPassStrip = Color.FromArgb(238, 232, 248),
        BandAllPassStripSelected = Color.FromArgb(224, 212, 248),
        BandAllPassTile = Color.FromArgb(232, 226, 244),
        BandPeakingStrip = Color.FromArgb(232, 236, 244),
        BandPeakingStripSelected = Color.FromArgb(210, 222, 242),
        BandPeakingTile = Color.FromArgb(226, 230, 238),

        CursorOutline = Color.FromArgb(8, 10, 14),
        CurveOverlayDefault = Color.FromArgb(150, 104, 0),
        CurveTargetDefault = Color.FromArgb(0, 118, 92),

        MarkerFirstArrival = Color.FromArgb(186, 46, 46),
        MarkerStrongestPeak = Color.FromArgb(40, 70, 190),
        MarkerEnergyOnset = Color.FromArgb(24, 116, 60),

        GraphSurface = Color.FromArgb(252, 252, 255),
        GraphSurfaceMuted = Color.FromArgb(244, 246, 250),
        GraphAxisText = Color.FromArgb(32, 37, 46),
        GraphTickline = Color.FromArgb(88, 95, 110),
        GraphAreaBorder = Color.FromArgb(70, 0, 0, 0),
        GraphGridlineMajor = Color.FromArgb(38, 0, 0, 0),
        GraphGridlineMinor = Color.FromArgb(18, 0, 0, 0)
    };
}
