using OxyPlot;

namespace Resonalyze;

/// <summary>The colour each Virtual DSP curve takes, by role: a block's colour follows its list position.</summary>
internal static class VirtualCrossoverColors
{
    public static OxyColor Sum => UiPalette.CurveNeutral.ToOxy();

    public static OxyColor Loss => VirtualCrossoverAcousticPlot.LossAxisColor;

    public static OxyColor Channel(int position) => UiPalette.ChannelCurves[position].ToOxy();

    /// <summary>The same colour for a control: a block's accent, a wizard row.</summary>
    public static Color ChannelAccent(int position) => UiPalette.ChannelCurves[position];

    // Semantic: in the Groups view a line IS a zone.
    public static OxyColor Group(VirtualCrossoverZone zone) => zone switch
    {
        VirtualCrossoverZone.Rear => UiPalette.CurveZoneRear.ToOxy(),
        VirtualCrossoverZone.Center => UiPalette.CurveZoneCentre.ToOxy(),
        VirtualCrossoverZone.Sub => UiPalette.CurveZoneSub.ToOxy(),
        _ => UiPalette.CurveZoneFront.ToOxy()
    };
}
