using System.Drawing;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The fader must paint the strip's tint: clearing with the Transparent fader host's colour wrote black.</summary>
public sealed class PeqSlotStripTests
{
    [Theory]
    [InlineData(PeqBandType.Peaking)]
    [InlineData(PeqBandType.LowShelf)]
    [InlineData(PeqBandType.HighShelf)]
    [InlineData(PeqBandType.AllPassFirstOrder)]
    [InlineData(PeqBandType.AllPassSecondOrder)]
    public void TheFaderArea_CarriesTheBandTint(PeqBandType type) => StaTest.Run(() =>
    {
        using var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000)
        };
        using var strip = new PeqSlotControl { BandType = type };
        form.ClientSize = strip.Size;
        form.Controls.Add(strip);
        form.Show();
        StaTest.Pump();

        Control fader = strip.Controls.Find("fader", searchAllChildren: true)[0];
        using var bitmap = new Bitmap(fader.Width, fader.Height);
        fader.DrawToBitmap(bitmap, new Rectangle(Point.Empty, fader.Size));

        // A column clear of the groove, cap, ticks and labels.
        Color expected = PeqBandPalette.Strip(type);
        for (int y = fader.Height / 3; y < fader.Height * 2 / 3; y++)
        {
            Color painted = bitmap.GetPixel(fader.Width - 3, y);
            Assert.True(
                painted.ToArgb() == expected.ToArgb(),
                $"{type} fader at y={y} painted {painted.R},{painted.G},{painted.B}, " +
                $"expected the strip tint {expected.R},{expected.G},{expected.B}.");
        }
    });
}
