using System.Drawing;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>Corners are painted with the colour behind the control, not cut away.</summary>
public sealed class RoundedSurfaceTests
{
    private static readonly Color Outside = Color.FromArgb(10, 20, 30);
    private static readonly Color Fill = Color.FromArgb(200, 100, 50);
    private static readonly Color Border = Color.FromArgb(0, 200, 120);

    // 6 logical px at 125% is 7.5, which rounds to 8.
    [Theory]
    [InlineData(96, 6)]
    [InlineData(120, 8)]
    [InlineData(144, 9)]
    [InlineData(192, 12)]
    public void ScaleRadius_FollowsTheDisplay(int dpi, int expected) =>
        Assert.Equal(expected, RoundedSurface.ScaleRadius(6, dpi, new Size(400, 300)));

    // Past half the shorter side the arcs would meet and the path fold.
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

    // Anti-aliased GDI+ spreads a half-pixel-off line over two rows; hence the pixel offset.
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

    [Fact]
    public void ColorBehind_WithoutAParentIsTheControlsOwnColour()
    {
        using var panel = new RoundedPanel { BackColor = Fill };

        Assert.Equal(Fill.ToArgb(), RoundedSurface.ColorBehind(panel).ToArgb());
    }

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
