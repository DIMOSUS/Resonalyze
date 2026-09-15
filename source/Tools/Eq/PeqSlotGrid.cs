namespace Resonalyze;

/// <summary>Row-major strip grid geometry, kept apart from WinForms so the off-by-one-prone hit test is testable.</summary>
internal static class PeqSlotGrid
{
    public static (int Column, int Row) CellOf(int index, int columnCount) =>
        (index % columnCount, index / columnCount);

    /// <summary>Point in content coordinates (padding subtracted); outside points clamp to the nearest cell.</summary>
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
