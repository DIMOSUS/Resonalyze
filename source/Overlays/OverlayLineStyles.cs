using OxyPlot;

namespace Resonalyze;

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
