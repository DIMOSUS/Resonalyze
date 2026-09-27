using System.Drawing;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Series;

namespace Resonalyze.App.Tests;

// DrawToBitmap takes the GDI+ path on any machine; a headless runner never paints an off-screen window.
public sealed class AcceleratedPlotViewTests
{
    private static readonly Color Surface = Color.FromArgb(40, 44, 90);

    [Fact]
    public void DrawToBitmap_DrawsThePlotAndReportsTheFrame()
    {
        using Form form = ShownForm(out AcceleratedPlotView view);
        int frames = 0;
        view.FrameRendered += (_, _) => frames++;
        view.Model = LineModel();

        using var bitmap = new Bitmap(view.Width, view.Height);
        view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, view.Size));

        Assert.True(frames > 0);
        Assert.Equal(Surface.ToArgb(), bitmap.GetPixel(5, 5).ToArgb());
        Assert.Contains(
            Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(bitmap.Width / 2, y)),
            pixel => pixel.R > 200 && pixel.G < 80 && pixel.B < 80);
    }

    [Fact]
    public void TurningTheGpuOff_LeavesAPlotThatStillDraws()
    {
        using Form form = ShownForm(out AcceleratedPlotView view);
        view.Model = LineModel();

        view.GpuAllowed = false;

        Assert.False(view.IsAccelerated);
        using var bitmap = new Bitmap(view.Width, view.Height);
        view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, view.Size));
        Assert.Equal(Surface.ToArgb(), bitmap.GetPixel(5, 5).ToArgb());
    }

    private static PlotModel LineModel()
    {
        var model = new PlotModel { Background = OxyColor.FromRgb(Surface.R, Surface.G, Surface.B) };
        var line = new LineSeries { Color = OxyColors.Red, StrokeThickness = 6 };
        line.Points.Add(new DataPoint(0, 0));
        line.Points.Add(new DataPoint(1, 1));
        model.Series.Add(line);
        return model;
    }

    private static Form ShownForm(out AcceleratedPlotView view)
    {
        view = new AcceleratedPlotView { Dock = DockStyle.Fill };
        var form = new Form
        {
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ClientSize = new Size(400, 300)
        };
        form.Controls.Add(view);
        form.Show();
        return form;
    }
}
