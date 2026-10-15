using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>A burst of input events costs one redraw once the queue drains, so the plot still gets painted between
/// them; a 1000 Hz mouse delivered more events than ~6 ms redraws fit in and the frame starved.</summary>
public sealed class EqWizardRedrawCoalescingTests
{
    [Fact]
    public void AWheelBurstOnAStripRedrawsOnce_WithTheLastValueOnThePlot() => StaTest.Run(() =>
    {
        using var live = new EqWizardPanelWiringTests.LivePanel();
        live.Invoke("AddBand", PeqBandType.Peaking);
        PeqSlotControl strip = live.Strips[0];
        int before = live.Panel.RedrawCount;

        // No pumping between the edits, as a burst of posted events arrives.
        for (int i = 1; i <= 20; i++)
        {
            strip.GainInput.Value = -i * 0.5m;
        }

        Assert.Equal(before, live.Panel.RedrawCount);
        Assert.Equal(-10.0, live.Session.Bank.Bands[0].GainDb);

        // SetTimer rounds the 1 ms up to the timer's resolution, so the tick is waited for, not pumped for.
        for (int attempt = 0; attempt < 200 && live.Panel.RedrawCount == before; attempt++)
        {
            StaTest.Pump();
            Thread.Sleep(5);
        }

        live.Settle();

        Assert.Equal(before + 1, live.Panel.RedrawCount);
        Assert.Contains(
            live.Plot.Series.OfType<OxyPlot.Series.LineSeries>(),
            series => series.Title == "EQ" && series.Points.Min(point => point.Y) < -9);
    });

    [Fact]
    public void AViewToggleStillRedrawsAtOnce() => StaTest.Run(() =>
    {
        using var live = new EqWizardPanelWiringTests.LivePanel();
        int before = live.Panel.RedrawCount;

        live.Control<CheckBox>("checkBoxEqCurve").Checked = false;

        Assert.Equal(before + 1, live.Panel.RedrawCount);
    });
}
