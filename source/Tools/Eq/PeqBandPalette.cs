using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Band-shape tints shared by strips and add-tile zones: hue shifts at equal weight; both all-pass orders share violet.</summary>
internal static class PeqBandPalette
{
    public static Color Strip(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => Color.FromArgb(58, 50, 45),
        PeqBandType.HighShelf => Color.FromArgb(40, 56, 62),
        PeqBandType.AllPassFirstOrder or PeqBandType.AllPassSecondOrder =>
            Color.FromArgb(53, 47, 64),
        _ => Color.FromArgb(44, 50, 60)
    };

    public static Color SelectedStrip(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => Color.FromArgb(78, 66, 58),
        PeqBandType.HighShelf => Color.FromArgb(52, 76, 86),
        PeqBandType.AllPassFirstOrder or PeqBandType.AllPassSecondOrder =>
            Color.FromArgb(71, 62, 92),
        _ => Color.FromArgb(58, 66, 86)
    };

    public static Color TileZone(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => Color.FromArgb(32, 27, 24),
        PeqBandType.HighShelf => Color.FromArgb(22, 31, 35),
        PeqBandType.AllPassFirstOrder or PeqBandType.AllPassSecondOrder =>
            Color.FromArgb(29, 26, 36),
        _ => Color.FromArgb(25, 28, 34)
    };
}
