using System.Drawing;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>Asserts relative geometry (no overlaps, last radio inside the panel), which holds at any DPI scale.</summary>
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

            // SetOptions persists the remembered choice, not the checkbox, so unchecking alone would re-apply SPL.
            panel.ForceSplScaleOff();

            var applied = new LiveSpectrumOptions();
            panel.SetOptions(applied);
            Assert.Equal(MagnitudeScale.Relative, applied.MagnitudeScale);
        });
    }

    /// <summary><see cref="MicrophoneCalibrationComboHelper.Configure"/> enables a box holding more than one entry, so disabling at construction does not last.</summary>
    [Fact]
    public void TheCalibrationReadOutStaysReadOnlyWhenTheListIsRebuilt() =>
        StaTest.Run(() =>
        {
            using LiveSpectrumOpt panel = CreatePanel();
            var combo = (ThemedComboBox)Find(panel, "comboCalibration");
            Assert.False(combo.Enabled);

            panel.ShowCalibration("90° capsule 2");

            Assert.False(combo.Enabled);
            Assert.Equal("90° capsule 2", combo.SelectedItem);
            Assert.Single(combo.Items);
        });

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

    // An AutoSize control's designer size is only a hint.
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
