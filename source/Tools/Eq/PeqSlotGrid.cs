namespace Resonalyze;

/// <summary>
/// The row-major geometry of the PEQ strip grid: which cell a slot index lives
/// in, and which slot index a point falls on while a strip is being dragged.
/// Kept apart from the panel (and from WinForms layout) because the hit test is
/// where an off-by-one silently drops a strip in the wrong place.
/// </summary>
internal static class PeqSlotGrid
{
    /// <summary>The (column, row) of a slot index, filling each row left to right.</summary>
    public static (int Column, int Row) CellOf(int index, int columnCount) =>
        (index % columnCount, index / columnCount);

    /// <summary>
    /// The slot index under a point given in the grid's content coordinates (the
    /// panel's padding already subtracted). Points outside the grid clamp to the
    /// nearest cell, so a drag that strays past an edge still has a target.
    /// </summary>
    public static int IndexAt(
        IReadOnlyList<int> columnWidths,
        IReadOnlyList<int> rowHeights,
        Point point)
    {
        ArgumentNullException.ThrowIfNull(columnWidths);
        ArgumentNullException.ThrowIfNull(rowHeights);

        if (columnWidths.Count == 0 || rowHeights.Count == 0)
        {
            return 0;
        }

        int column = TrackIndexAt(columnWidths, point.X);
        int row = TrackIndexAt(rowHeights, point.Y);
        return row * columnWidths.Count + column;
    }

    // The index of the band containing an offset along one axis, clamped to the
    // first/last band for offsets before or past the track.
    private static int TrackIndexAt(IReadOnlyList<int> sizes, int offset)
    {
        int start = 0;
        for (int index = 0; index < sizes.Count; index++)
        {
            start += sizes[index];
            if (offset < start)
            {
                return Math.Max(index, 0);
            }
        }

        return sizes.Count - 1;
    }
}
