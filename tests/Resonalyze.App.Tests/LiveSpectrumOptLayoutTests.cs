using System.Drawing;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>
/// The mode row of the Live Spectrum options. It held two radio buttons laid out by
/// hand at designer coordinates; MMM made it three, which meant moving the other two.
/// A row that fits at 96 DPI and collides at 150% is the classic form of that
/// mistake, so what is asserted here is the RELATIVE geometry — no overlaps, and the
/// last one inside the panel — which holds at any scale the whole row scales by.
/// </summary>
public sealed class LiveSpectrumOptLayoutTests
{
    [Fact]
    public void TheThreeModeRadiosNeitherOverlapNorRunOffThePanel() =>
        StaTest.Run(() =>
        {
            using LiveSpectrumOpt panel = CreatePanel();

            RadioButton transfer = Radio(panel, "radioModeTransfer");
            RadioButton rta = Radio(panel, "radioModeRta");
            RadioButton mmm = Radio(panel, "radioModeMmm");

            // The label the row starts after; running under it reads as a broken row
            // just as surely as running over the next radio.
            Control mode = Find(panel, "labelAnalysisMode");
            Assert.True(
                mode.Right <= transfer.Left,
                $"Mode label ends at {mode.Right}, Transfer starts at {transfer.Left}");

            AssertPrecedes(transfer, rta);
            AssertPrecedes(rta, mmm);

            int required = mmm.Left + Preferred(mmm);
            Assert.True(
                required <= panel.ClientSize.Width,
                $"the row needs {required} px, the panel is {panel.ClientSize.Width} px wide");
        });

    [Fact]
    public void SelectingMmmPinsTheSettingsASpatialAverageIsOnlyValidUnder() =>
        StaTest.Run(() =>
        {
            using LiveSpectrumOpt panel = CreatePanel();
            Radio(panel, "radioModeMmm").Checked = true;

            var spl = (CheckBox)Find(panel, "checkSpl");
            var tilt = (CheckBox)Find(panel, "checkTilt");

            // Forced ON and non-interactive. AutoCheck is how this panel locks a
            // control while keeping it readable, so it is what "pinned" means here.
            Assert.True(spl.Checked);
            Assert.False(spl.AutoCheck);
            Assert.True(tilt.Checked);
            Assert.False(tilt.AutoCheck);
            Assert.False(Find(panel, "averagingComboBox").Enabled);
            Assert.False(Find(panel, "comboSmoothingInverseOctaves").Enabled);
            Assert.False(Find(panel, "signalTypeComboBox").Enabled);
        });

    [Fact]
    public void LeavingMmmGivesTheUsersOwnChoicesBack()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            MagnitudeScale = MagnitudeScale.Relative,
            CompensateNoiseTilt = false,
            AveragingSpeed = AveragingSpeed.Slow,
            SmoothingInverseOctaves = 6
        };

        StaTest.Run(() =>
        {
            using LiveSpectrumOpt panel = CreatePanel(options);
            Radio(panel, "radioModeMmm").Checked = true;
            Radio(panel, "radioModeRta").Checked = true;

            var applied = new LiveSpectrumOptions();
            panel.SetOptions(applied);

            // A trip through MMM must not rewrite what the user picked for the RTA:
            // the pins are the mode's, not the operator's.
            Assert.Equal(LiveAnalysisMode.Rta, applied.AnalysisMode);
            Assert.Equal(MagnitudeScale.Relative, applied.MagnitudeScale);
            Assert.False(applied.CompensateNoiseTilt);
            Assert.Equal(AveragingSpeed.Slow, applied.AveragingSpeed);
            Assert.Equal(6, applied.SmoothingInverseOctaves);
        });
    }

    [Fact]
    public void ForceSplScaleOffAlsoClearsTheRememberedChoice()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };

        StaTest.Run(() =>
        {
            using LiveSpectrumOpt panel = CreatePanel(options);

            // The host calls this when it drops a view-only SPL display for a run,
            // and its own comment says the panel must not write SPL back on its next
            // apply. SetOptions persists the remembered choice, not the checkbox, so
            // unchecking alone would have left the old value to be re-applied.
            panel.ForceSplScaleOff();

            var applied = new LiveSpectrumOptions();
            panel.SetOptions(applied);
            Assert.Equal(MagnitudeScale.Relative, applied.MagnitudeScale);
        });
    }

    private static LiveSpectrumOpt CreatePanel(LiveSpectrumOptions? options = null)
    {
        var panel = new LiveSpectrumOpt();
        panel.Init(
            options ?? new LiveSpectrumOptions(),
            [],
            isSplAvailable: true,
            hasLiveCurve: false,
            hasTransferReference: true,
            sampleRateHz: 48_000);
        panel.CreateControl();
        panel.PerformLayout();
        return panel;
    }

    private static void AssertPrecedes(Control left, Control right) =>
        Assert.True(
            left.Left + Preferred(left) <= right.Left,
            $"{left.Name} ends at {left.Left + Preferred(left)}, " +
            $"{right.Name} starts at {right.Left}");

    // The designer size of an AutoSize control is only a hint; what it will really
    // occupy is what it asks for with the font actually in effect.
    private static int Preferred(Control control) =>
        Math.Max(control.Width, control.GetPreferredSize(Size.Empty).Width);

    private static RadioButton Radio(Control root, string name) =>
        (RadioButton)Find(root, name);

    private static Control Find(Control root, string name)
    {
        Control[] found = root.Controls.Find(name, searchAllChildren: true);
        Assert.True(found.Length == 1, $"expected exactly one {name}, found {found.Length}");
        return found[0];
    }
}
