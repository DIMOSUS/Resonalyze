using System.Drawing;

namespace Resonalyze.App.Tests;

public sealed class PeqSlotGridTests
{
    // A 4-column grid of 20 px cells over two 30 px rows, standing in for the
    // panel's 16x2 strip grid.
    private static readonly int[] Columns = { 20, 20, 20, 20 };
    private static readonly int[] Rows = { 30, 30 };

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(19, 29, 0)]
    [InlineData(20, 0, 1)]
    [InlineData(79, 29, 3)]
    [InlineData(0, 30, 4)]
    [InlineData(45, 59, 6)]
    public void IndexAt_ReadsTheGridRowMajor(int x, int y, int expected) =>
        Assert.Equal(expected, PeqSlotGrid.IndexAt(Columns, Rows, new Point(x, y)));

    [Fact]
    public void IndexAt_ClampsAPointDraggedPastTheEdges()
    {
        // A drag that strays outside the grid still has to name a target cell,
        // otherwise the strip would jump to slot 1 on every overshoot.
        Assert.Equal(0, PeqSlotGrid.IndexAt(Columns, Rows, new Point(-40, -10)));
        Assert.Equal(7, PeqSlotGrid.IndexAt(Columns, Rows, new Point(400, 200)));
    }

    [Fact]
    public void CellOf_FillsEachRowLeftToRight()
    {
        Assert.Equal((0, 0), PeqSlotGrid.CellOf(0, 16));
        Assert.Equal((15, 0), PeqSlotGrid.CellOf(15, 16));
        Assert.Equal((0, 1), PeqSlotGrid.CellOf(16, 16));
        Assert.Equal((15, 1), PeqSlotGrid.CellOf(31, 16));
    }
}
