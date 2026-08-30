using System.Drawing;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// The rounded card the app draws its panels as. Nothing cuts the corners away —
/// they are painted with the colour behind the control — so what the tests below
/// look at is the three colours a card is made of and where each one lands:
/// the colour behind in the corner, the outline on the edge, the surface inside.
/// </summary>
public sealed class RoundedSurfaceTests
{
    private static readonly Color Outside = Color.FromArgb(10, 20, 30);
    private static readonly Color Fill = Color.FromArgb(200, 100, 50);
    private static readonly Color Border = Color.FromArgb(0, 200, 120);

    // A radius stated in 96-DPI pixels has to grow with the display, or it shrinks
    // against the text beside it. 6 logical pixels at 125% is 7.5, which rounds to 8.
    [Theory]
    [InlineData(96, 6)]
    [InlineData(120, 8)]
    [InlineData(144, 9)]
    [InlineData(192, 12)]
    public void ScaleRadius_FollowsTheDisplay(int dpi, int expected) =>
        Assert.Equal(expected, RoundedSurface.ScaleRadius(6, dpi, new Size(400, 300)));

    // Past half the shorter side the four arcs would meet and the path would fold
    // on itself, so a strip only a few pixels tall clamps rather than folds.
    [Fact]
    public void ScaleRadius_StopsAtHalfTheShorterSide() =>
        Assert.Equal(5, RoundedSurface.ScaleRadius(20, 96, new Size(40, 10)));

    [Fact]
    public void ScaleRadius_ZeroStaysSquare() =>
        Assert.Equal(0, RoundedSurface.ScaleRadius(0, 192, new Size(40, 30)));

    [Fact]
    public void Paint_ShowsTheColourBehindInTheCorners()
    {
        using Bitmap surface = PaintSurface(radius: 6);

        Assert.Equal(Outside.ToArgb(), surface.GetPixel(0, 0).ToArgb());
        Assert.Equal(Outside.ToArgb(), surface.GetPixel(39, 0).ToArgb());
        Assert.Equal(Outside.ToArgb(), surface.GetPixel(0, 29).ToArgb());
        Assert.Equal(Outside.ToArgb(), surface.GetPixel(39, 29).ToArgb());
    }

    // One pixel of the outline colour exactly, on the outermost row and column.
    // Anti-aliased GDI+ places a line's colour by coverage, and a half-pixel error
    // in either direction spreads it over two rows at a fraction of its strength —
    // which is what the outline looked like before the pixel offset was set.
    [Fact]
    public void Paint_DrawsTheOutlineOnTheEdgeItself()
    {
        using Bitmap surface = PaintSurface(radius: 6);

        Assert.Equal(Border.ToArgb(), surface.GetPixel(20, 0).ToArgb());
        Assert.Equal(Border.ToArgb(), surface.GetPixel(20, 29).ToArgb());
        Assert.Equal(Border.ToArgb(), surface.GetPixel(0, 15).ToArgb());
        Assert.Equal(Border.ToArgb(), surface.GetPixel(39, 15).ToArgb());
        Assert.Equal(Fill.ToArgb(), surface.GetPixel(20, 1).ToArgb());
        Assert.Equal(Fill.ToArgb(), surface.GetPixel(20, 15).ToArgb());
    }

    // No radius is a square card, not a cut-away one: nothing of the colour behind
    // is left in the corners. Read on the unbordered surface, where the corner is
    // one flat colour rather than whatever coverage the outline's mitre gives it.
    [Fact]
    public void Paint_WithoutARadiusFillsTheCornersToo()
    {
        using Bitmap plain = PaintSurface(radius: 0, border: Color.Transparent);
        using Bitmap outlined = PaintSurface(radius: 0);

        Assert.Equal(Fill.ToArgb(), plain.GetPixel(0, 0).ToArgb());
        Assert.Equal(Fill.ToArgb(), plain.GetPixel(39, 29).ToArgb());
        Assert.NotEqual(Outside.ToArgb(), outlined.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Paint_WithoutABorderLeavesTheSurfaceToTheEdge()
    {
        using Bitmap surface = PaintSurface(radius: 6, border: Color.Transparent);

        Assert.Equal(Fill.ToArgb(), surface.GetPixel(20, 0).ToArgb());
        Assert.Equal(Outside.ToArgb(), surface.GetPixel(0, 0).ToArgb());
    }

    // The corners show what is BEHIND the control, which a transparent parent is
    // not: the walk has to carry on to the first parent that paints something.
    [Fact]
    public void ColorBehind_LooksPastTransparentParents()
    {
        using var form = new Form { BackColor = Outside };
        using var host = new Panel { BackColor = Color.Transparent };
        using var panel = new RoundedPanel { BackColor = Fill };
        form.Controls.Add(host);
        host.Controls.Add(panel);

        Assert.Equal(Outside.ToArgb(), RoundedSurface.ColorBehind(panel).ToArgb());
    }

    // With nothing behind it there is no honest answer, and the surface's own
    // colour is the one that draws square corners rather than a guessed frame.
    [Fact]
    public void ColorBehind_WithoutAParentIsTheControlsOwnColour()
    {
        using var panel = new RoundedPanel { BackColor = Fill };

        Assert.Equal(Fill.ToArgb(), RoundedSurface.ColorBehind(panel).ToArgb());
    }

    // The control end of it: a panel realised in a form paints the form's colour
    // into its corners and its own inside them.
    [Fact]
    public void RoundedPanel_PaintsItsParentsColourIntoTheCorners() =>
        StaTest.Run(() =>
        {
            using var form = new Form
            {
                BackColor = Outside,
                ClientSize = new Size(120, 90),
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000)
            };
            using var panel = new RoundedPanel
            {
                BackColor = Fill,
                BorderColor = Border,
                Bounds = new Rectangle(10, 10, 60, 40)
            };
            form.Controls.Add(panel);
            form.Show();

            using var surface = new Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(surface, new Rectangle(Point.Empty, panel.Size));

            Assert.Equal(Outside.ToArgb(), surface.GetPixel(0, 0).ToArgb());
            Assert.Equal(Border.ToArgb(), surface.GetPixel(30, 0).ToArgb());
            Assert.Equal(Fill.ToArgb(), surface.GetPixel(30, 20).ToArgb());
        });

    private static Bitmap PaintSurface(int radius, Color? border = null)
    {
        var surface = new Bitmap(40, 30);
        using (Graphics graphics = Graphics.FromImage(surface))
        {
            RoundedSurface.Paint(
                graphics,
                new Rectangle(0, 0, 40, 30),
                radius,
                Outside,
                Fill,
                border ?? Border);
        }

        return surface;
    }
}
