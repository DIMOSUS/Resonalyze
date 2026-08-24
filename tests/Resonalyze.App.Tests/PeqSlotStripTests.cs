using System.Drawing;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// A PEQ strip is told apart from its neighbours by its tint, and the fader is
/// most of the strip's area — so the fader has to paint the tint, not something
/// of its own. It painted a black box instead: it cleared its background with its
/// PARENT's colour, correct while the strip's layout was the direct parent, and
/// the fader host put between them for the group-delay readout is
/// Color.Transparent. Clearing with an alpha-0 colour writes black, so every
/// strip — peaking and both shelves alike — carried the same black rectangle and
/// the palette that distinguishes the shapes was invisible where it mattered.
/// </summary>
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
        // Off-screen: the strip has to be SHOWN to paint, and a test has no
        // business flashing a window over whatever the user is doing.
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
        Application.DoEvents();

        Control fader = strip.Controls.Find("fader", searchAllChildren: true)[0];
        using var bitmap = new Bitmap(fader.Width, fader.Height);
        fader.DrawToBitmap(bitmap, new Rectangle(Point.Empty, fader.Size));

        // A column clear of everything the fader draws on top: the groove and its
        // cap run down the middle, the scale ticks and their labels sit to the
        // left of it.
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
