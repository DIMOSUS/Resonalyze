using System.Drawing;
using OxyPlot;

namespace Resonalyze;

internal static class OxyColorConversions
{
    /// <summary>The palette speaks System.Drawing; OxyPlot does not. Alpha is carried through.</summary>
    public static OxyColor ToOxy(this Color color) =>
        OxyColor.FromArgb(color.A, color.R, color.G, color.B);
}
