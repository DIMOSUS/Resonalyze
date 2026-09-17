using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Band-shape tints shared by strips and add-tile zones: hue shifts at equal weight; both all-pass orders share violet.</summary>
internal static class PeqBandPalette
{
    public static Color Strip(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => UiPalette.BandLowShelfStrip,
        PeqBandType.HighShelf => UiPalette.BandHighShelfStrip,
        PeqBandType.AllPassFirstOrder or PeqBandType.AllPassSecondOrder =>
            UiPalette.BandAllPassStrip,
        _ => UiPalette.BandPeakingStrip
    };

    public static Color SelectedStrip(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => UiPalette.BandLowShelfStripSelected,
        PeqBandType.HighShelf => UiPalette.BandHighShelfStripSelected,
        PeqBandType.AllPassFirstOrder or PeqBandType.AllPassSecondOrder =>
            UiPalette.BandAllPassStripSelected,
        _ => UiPalette.BandPeakingStripSelected
    };

    public static Color TileZone(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => UiPalette.BandLowShelfTile,
        PeqBandType.HighShelf => UiPalette.BandHighShelfTile,
        PeqBandType.AllPassFirstOrder or PeqBandType.AllPassSecondOrder =>
            UiPalette.BandAllPassTile,
        _ => UiPalette.BandPeakingTile
    };
}
