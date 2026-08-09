using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The tint that tells the three band shapes apart at a glance in a bank of up to
/// 32 strips. Shared by the strips and by the add tile's zones, so the colour that
/// offers a shape is the colour the filter then carries.
/// </summary>
/// <remarks>
/// The shift is in hue, not in brightness: all three sit at the same weight against
/// the panel, so a bank does not read as some filters being more important than
/// others. The direction follows how the two shelves are heard — the low shelf
/// warm, the high shelf cool — which is also the way round they are stacked on the
/// add tile.
/// </remarks>
internal static class PeqBandPalette
{
    /// <summary>The strip's own background.</summary>
    public static Color Strip(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => Color.FromArgb(58, 50, 45),
        PeqBandType.HighShelf => Color.FromArgb(40, 56, 62),
        _ => Color.FromArgb(44, 50, 60)
    };

    /// <summary>The same strip while it is the highlighted band.</summary>
    public static Color SelectedStrip(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => Color.FromArgb(78, 66, 58),
        PeqBandType.HighShelf => Color.FromArgb(52, 76, 86),
        _ => Color.FromArgb(58, 66, 86)
    };

    /// <summary>
    /// The wash behind an add-tile zone: the strip colour, dimmed most of the way
    /// to the panel behind it. The zone is an empty slot, not a filter, so it only
    /// hints at the colour the filter would have.
    /// </summary>
    public static Color TileZone(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => Color.FromArgb(32, 27, 24),
        PeqBandType.HighShelf => Color.FromArgb(22, 31, 35),
        _ => Color.FromArgb(25, 28, 34)
    };
}
