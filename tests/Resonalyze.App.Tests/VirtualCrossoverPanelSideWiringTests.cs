using System.Windows.Forms;
using Resonalyze.Dsp;
using static Resonalyze.App.Tests.VirtualCrossoverPanelWiringTests;

namespace Resonalyze.App.Tests;

/// <summary>
/// The side selector and the scale both sides share, through the live panel of
/// <see cref="VirtualCrossoverPanelWiringTests"/>, whose right side plays 6 dB below the left.
/// </summary>
[Trait("Category", "Slow")]
public sealed class VirtualCrossoverPanelSideWiringTests
{
    [Fact]
    public void TheSideSelector_DrawsTheSideItNames_AndEveryBlockFollowsIt()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            double left = live.LevelDb("A", 100);

            live.ShowRight();

            Assert.Equal(20 * Math.Log10(RightAmplitude), live.LevelDb("A", 100) - left, 1);
            Assert.Contains("Sum L", live.MainTitles());
            live.Click("buttonAddChannel");
            Assert.All(live.Session.Channels, channel => Assert.True(channel.ActiveRight));
            Assert.Throws<InvalidOperationException>(() => live.Session.Channels[0].ActiveRight = false);
        });
    }

    [Fact]
    public void TheGateWarning_JudgesTheShownSidesGate()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            // Both pins far past every arrival: whichever side is judged, its window misses the channels.
            live.Session.Project.PhaseGateLeft.OffsetMs = 300;
            live.Session.Project.PhaseGateRight.OffsetMs = 300;
            live.Redraw();

            Assert.StartsWith("⚠ L gate at", live.Warning);

            live.ShowRight();

            Assert.StartsWith("⚠ R gate at", live.Warning);
        });
    }

    [Fact]
    public void OwnCalibration_ReadsTheShownSidesOwnFile()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            live.Session.Channels[0].PhysicalSideState(true).MicrophoneCalibration = FlatCalibration(6);
            live.Panel.ConfigureCalibration(_ => null, []);
            live.ShowRight();
            double off = live.LevelDb("A", 100);
            double uncalibrated = live.LevelDb("B", 1_000);

            live.SelectCalibration(VirtualCrossoverCalibrationSelection.OwnId);

            Assert.Equal(6, Math.Abs(live.LevelDb("A", 100) - off), 1);
            Assert.Equal(uncalibrated, live.LevelDb("B", 1_000), 3);
        });
    }

    // The right side plays 6 dB lower; without the Sum (whose dashed opposite curve spans both) each side alone scales differently.
    [Fact]
    public void BothSides_AreDrawnOnOneScale()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            live.Set<CheckBox>("checkBoxShowSum", box => box.Checked = false);
            live.ShowRight();
            live.ShowLeft();
            var left = live.AxisRanges();

            live.ShowRight();

            Assert.Equal(left, live.AxisRanges());
        });
    }

    // Raised while hidden, the right side is re-read once edits pause: the left view's axis follows it up without a visit.
    [Fact]
    public void AHiddenSideRaisedWhileHidden_WidensTheScaleOnceEditsPause()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            live.Set<CheckBox>("checkBoxShowSum", box => box.Checked = false);
            double before = live.AxisRanges().Value.High;

            foreach (VirtualCrossoverChannel channel in live.Session.Channels)
            {
                channel.SideSettings(true).GainDb = 18;
            }

            live.Redraw();
            live.WaitFor(() => live.AxisRanges().Value.High >= before + 10, "take in the raised hidden side");
        });
    }

    // Emptied while shown, the left side keeps the scale the right side is drawn on.
    [Fact]
    public void AnEmptiedShownSide_KeepsTheOtherSidesScale()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            live.Set<CheckBox>("checkBoxShowSum", box => box.Checked = false);
            live.ShowRight();
            live.ShowLeft();
            double withLeft = live.AxisRanges().Value.High;

            foreach (VirtualCrossoverChannel channel in live.Session.Channels)
            {
                channel.PhysicalSideState(false).Clear();
            }

            live.Redraw();
            var emptied = live.AxisRanges().Value;
            live.ShowRight();

            Assert.Equal(live.AxisRanges().Value, emptied);
            Assert.True(emptied.High < withLeft, $"the emptied left side still holds the scale at {emptied.High} dB");
        });
    }

    // The louder left side, once emptied, must stop holding the right side's axis up.
    [Fact]
    public void AnEmptiedHiddenSide_StopsWideningTheScale()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            live.Set<CheckBox>("checkBoxShowSum", box => box.Checked = false);
            live.ShowRight();
            double shared = live.AxisRanges().Value.High;

            foreach (VirtualCrossoverChannel channel in live.Session.Channels)
            {
                channel.PhysicalSideState(false).Clear();
            }

            live.Redraw();
            live.WaitFor(() => live.AxisRanges().Value.High < shared, "drop the emptied side's range");
        });
    }

    private static VirtualCrossoverCalibrationSettings FlatCalibration(double correctionDb) =>
        VirtualCrossoverCalibrationSettings.From(
            CalibrationFile.FromPoints(
                [new CalibrationPoint(20.0, correctionDb), new CalibrationPoint(20_000.0, correctionDb)],
                "flat"),
            $"flat {correctionDb:0.#}",
            null);
}
