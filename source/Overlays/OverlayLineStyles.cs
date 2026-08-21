using OxyPlot;

namespace Resonalyze;

/// <summary>
/// The one mapping from a stored <see cref="OverlayLineStyle"/> to the OxyPlot
/// <see cref="LineStyle"/> a plot draws it with. Overlays, the EQ Wizard target
/// and the Virtual DSP target all render the same stored value, so they read it
/// through here instead of each keeping a private switch.
/// </summary>
internal static class OverlayLineStyles
{
    public static LineStyle ToOxy(OverlayLineStyle value) => value switch
    {
        OverlayLineStyle.Dash => LineStyle.Dash,
        OverlayLineStyle.Dot => LineStyle.Dot,
        OverlayLineStyle.DashDot => LineStyle.DashDot,
        _ => LineStyle.Solid
    };
}
