using System.Drawing;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>A channel block driven through its fields and the host's setters: each read-out must be what its reader
/// makes of the values the block holds. The readers have their own tests; these pin the binding.</summary>
public sealed class VirtualCrossoverChannelControlWiringTests
{
    public static TheoryData<CrossoverKind, CrossoverFilterFamily, CrossoverFilterFamily> Roles
    {
        get
        {
            var data = new TheoryData<CrossoverKind, CrossoverFilterFamily, CrossoverFilterFamily>();
            foreach (CrossoverKind kind in new[] { CrossoverKind.Off, CrossoverKind.HighPass, CrossoverKind.LowPass, CrossoverKind.BandPass })
            {
                data.Add(kind, CrossoverFilterFamily.Chebyshev, CrossoverFilterFamily.Butterworth);
                data.Add(kind, CrossoverFilterFamily.Bessel, CrossoverFilterFamily.Chebyshev);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void TheRoleAndTheFamilies_GreyOutWhatTheAvailabilityRules(
        CrossoverKind kind, CrossoverFilterFamily highPass, CrossoverFilterFamily lowPass)
    {
        using var block = new Block();
        block.Control.HighPassFamilyComboBox.SelectedItem = highPass;
        block.Control.LowPassFamilyComboBox.SelectedItem = lowPass;
        block.Control.CrossoverKindComboBox.SelectedItem = kind;

        AssertAvailability(block, VirtualCrossoverChannelAvailability.Of(kind, highPass, lowPass));

        block.Control.HighPassFamilyComboBox.SelectedItem = lowPass;
        AssertAvailability(block, VirtualCrossoverChannelAvailability.Of(kind, lowPass, lowPass));
    }

    [Fact]
    public void ACentreZone_ForcesAndLocksMono_AndLeavingItFreesTheBox()
    {
        using var block = new Block();

        block.Control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Center;
        Assert.True(block.Control.MonoCheckBox.Checked);
        Assert.False(block.Control.MonoCheckBox.Enabled);

        block.Control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Rear;
        Assert.True(block.Control.MonoCheckBox.Enabled);
        Assert.True(block.Control.MonoCheckBox.Checked);
    }

    [Fact]
    public void TheGainAndThePreamp_ReachTheTotal()
    {
        using var block = new Block();

        block.Control.GainInput.Value = -2.5m;
        Assert.Equal(VirtualCrossoverChannelTotalGain.Text(-2.5, 0), block.Control.TotalGainLabel.Text);
        block.Control.PeqPreampDb = -4.5;
        Assert.Equal(VirtualCrossoverChannelTotalGain.Text(-2.5, -4.5), block.Control.TotalGainLabel.Text);
        block.Control.RunBatchUpdate(() => block.Control.GainInput.Value = 3m);
        Assert.Equal(VirtualCrossoverChannelTotalGain.Text(3, -4.5), block.Control.TotalGainLabel.Text);
    }

    [Fact]
    public void TheDelay_ReachesItsTooltip_WhicheverArrivesFirst()
    {
        using var block = new Block(tooltips: false);
        block.Control.DelayInput.Value = 1.25m;
        block.ApplyTooltips();
        Assert.Equal(block.Wrapped(VirtualCrossoverChannelDelayReadout.Tooltip(1.25)), block.Tip(block.Control.DelayInput));

        block.Control.DelayInput.Value = 3.5m;
        Assert.Equal(block.Wrapped(VirtualCrossoverChannelDelayReadout.Tooltip(3.5)), block.Tip(block.Control.DelayInput));

        block.Control.RunBatchUpdate(() => block.Control.DelayInput.Value = 0.5m);
        Assert.Equal(block.Wrapped(VirtualCrossoverChannelDelayReadout.Tooltip(0.5)), block.Tip(block.Control.DelayInput));
    }

    [Fact]
    public void TheFirRow_ShowsWhatItsReaderMakesOfTheKernelTheRateAndTheRole()
    {
        using var block = new Block();
        var design = new FirCrossoverDesign(
            CrossoverKind.HighPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_500, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24),
            FirCrossoverMethod.IirMagnitude,
            FirWindow.Kaiser,
            8,
            255,
            48_000);
        FirFilter designed = design.Build();
        var file = new FirFilter([0.1, 1.0, 0.1], 44_100);
        AssertFir(block, null, null, null);

        block.Control.SetFir(designed, "ignored", design);
        AssertFir(block, designed, null, design);
        block.Control.ProcessorSampleRateHz = 96_000;
        AssertFir(block, designed, null, design);
        block.Control.CrossoverKindComboBox.SelectedItem = CrossoverKind.LowPass;
        AssertFir(block, designed, null, design);

        block.Control.SetFir(file, "  ");
        AssertFir(block, file, null, null);
        block.Control.SetFir(file, "room.txt", design);
        AssertFir(block, file, "room.txt", design);
        block.Control.SetFir(null, "room.txt", design);
        AssertFir(block, null, null, null);
    }

    [Fact]
    public void ThePhaseRow_ShowsWhatItsReaderMakesOfTheAngleTheZoneTheEdgesAndTheRate()
    {
        using var block = new Block();
        block.Control.PhaseControlShown = true;
        block.Control.HighPassFrequencyInput.Value = 5_000m;
        block.Control.LowPassFrequencyInput.Value = 800m;
        AssertPhase(block);

        block.Control.PhaseInput.Value = 90m;
        AssertPhase(block);
        block.Control.HighPassFrequencyInput.Value = 12_000m;
        AssertPhase(block);
        block.Control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Sub;
        AssertPhase(block);
        block.Control.LowPassFrequencyInput.Value = 60m;
        AssertPhase(block);
        block.Control.ProcessorSampleRateHz = 44_100;
        AssertPhase(block);
        block.Control.RunBatchUpdate(() => block.Control.PhaseInput.Value = 7m);
        AssertPhase(block);
    }

    [Fact]
    public void TheGoalButton_ShowsItsReadout()
    {
        using var block = new Block();
        var lr24 = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24);
        var bw18 = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18);

        foreach ((JunctionAcousticTarget? high, JunctionAcousticTarget? low, bool highRuns, bool lowRuns) in
            new (JunctionAcousticTarget?, JunctionAcousticTarget?, bool, bool)[]
            {
                (lr24, bw18, true, true), (lr24, bw18, false, true), (lr24, null, true, false), (null, null, true, true)
            })
        {
            block.Control.SetAcousticGoal(high, low, highRuns, lowRuns);
            VirtualCrossoverChannelGoalReadout expected = VirtualCrossoverChannelGoalReadout.Read(high, low, highRuns, lowRuns);
            Assert.Equal(expected.Text, block.Control.AcousticGoalButton.Text);
            Assert.Equal(expected.Color, block.Control.AcousticGoalButton.ForeColor);
            Assert.Equal(block.Wrapped(expected.Tooltip), block.Tip(block.Control.AcousticGoalButton));
        }
    }

    [Fact]
    public void TheAverageButton_ShowsItsReadout_AndATooltipSetBeforeTheHostArrives()
    {
        using var block = new Block(tooltips: false);
        Button button = block.Find<Button>("buttonSpatialAverage");
        block.Control.SetSpatialAverage("front seats", 45, true, VirtualCrossoverSpatialAverageMode.MovingMic);
        block.ApplyTooltips();
        VirtualCrossoverChannelAverageReadout attached = VirtualCrossoverChannelAverageReadout.Read(
            "front seats", 45, true, VirtualCrossoverSpatialAverageMode.MovingMic, null);
        Assert.Equal((attached.Text, attached.Color), (button.Text, button.ForeColor));
        Assert.Equal(block.Wrapped(attached.Tooltip), block.Tip(button));

        block.Control.SetSpatialAverage("front seats", null, false, VirtualCrossoverSpatialAverageMode.MovingMic);
        VirtualCrossoverChannelAverageReadout missing = VirtualCrossoverChannelAverageReadout.Read(
            "front seats", null, false, VirtualCrossoverSpatialAverageMode.MovingMic, null);
        Assert.Equal((missing.Text, missing.Color), (button.Text, button.ForeColor));
        Assert.Equal(block.Wrapped(missing.Tooltip), block.Tip(button));
    }

    [Fact]
    public void TheAccent_ColoursTheNameAndBothCurveToggles()
    {
        using var block = new Block();
        Color accent = VirtualCrossoverColors.ChannelAccent(2);

        block.Control.SetAccentColor(accent);

        Assert.Equal(accent, block.Find<Label>("labelChannel").ForeColor);
        Assert.Equal(accent, block.Control.ShowProcessedCheckBox.ForeColor);
        Assert.Equal(
            VirtualCrossoverColors.ChannelAccentFaded(accent, block.Control.BackColor),
            block.Control.ShowRawCheckBox.ForeColor);
    }

    private static void AssertAvailability(Block block, VirtualCrossoverChannelAvailability expected)
    {
        VirtualCrossoverChannelControl control = block.Control;
        Assert.Equal(
            (expected.HighPass, expected.HighPass, expected.HighPass, expected.HighPassRipple),
            (control.HighPassFrequencyInput.Enabled, control.HighPassFamilyComboBox.Enabled,
                control.HighPassSlopeComboBox.Enabled, control.HighPassRippleInput.Enabled));
        Assert.Equal(
            (expected.LowPass, expected.LowPass, expected.LowPass, expected.LowPassRipple),
            (control.LowPassFrequencyInput.Enabled, control.LowPassFamilyComboBox.Enabled,
                control.LowPassSlopeComboBox.Enabled, control.LowPassRippleInput.Enabled));
        Assert.Equal(!expected.HighPass, block.Find<Label>("labelHighPass").ForeColor == UiPalette.TextDisabled);
        Assert.Equal(!expected.LowPass, block.Find<Label>("labelLowPass").ForeColor == UiPalette.TextDisabled);
    }

    private static void AssertFir(Block block, FirFilter? kernel, string? name, FirCrossoverDesign? design)
    {
        VirtualCrossoverChannelControl control = block.Control;
        VirtualCrossoverChannelFirReadout expected = VirtualCrossoverChannelFirReadout.Read(
            kernel, name, design, control.ProcessorSampleRateHz, control.SelectedCrossoverKind);
        Assert.Equal((expected.ButtonText, expected.ButtonColor), (control.FirButton.Text, control.FirButton.ForeColor));
        Assert.Equal((expected.Info, expected.InfoColor), (control.FirInfoLabel.Text, control.FirInfoLabel.ForeColor));
        Assert.Equal(block.Wrapped(expected.ButtonTip), block.Tip(control.FirButton));
        Assert.Equal(block.Wrapped(expected.InfoTip), block.Tip(control.FirInfoLabel));
        Assert.Equal(
            VirtualCrossoverChannelFirReadout.ConflictOf(kernel, design, control.ProcessorSampleRateHz, control.SelectedCrossoverKind),
            control.FirConflict);
    }

    private static void AssertPhase(Block block)
    {
        VirtualCrossoverChannelControl control = block.Control;
        double reference = VirtualCrossoverChannelPhaseReadout.ReferenceHz(
            control.SelectedZone, (double)control.HighPassFrequencyInput.Value, (double)control.LowPassFrequencyInput.Value);
        double degrees = (double)control.PhaseInput.Value;
        VirtualCrossoverChannelPhaseReadout expected =
            VirtualCrossoverChannelPhaseReadout.Read(degrees, reference, control.ProcessorSampleRateHz);
        Assert.Equal((expected.Text, expected.Color), (control.PhaseInfoLabel.Text, control.PhaseInfoLabel.ForeColor));
        Assert.Equal(
            block.Wrapped(VirtualCrossoverChannelPhaseReadout.Tooltip(degrees, reference, control.ProcessorSampleRateHz)),
            block.Tip(control.PhaseInput));
    }

    private sealed class Block : IDisposable
    {
        private readonly WrappingToolTip toolTip = new();

        public Block(bool tooltips = true)
        {
            if (tooltips)
            {
                ApplyTooltips();
            }
        }

        public VirtualCrossoverChannelControl Control { get; } = new();

        public void ApplyTooltips() => Control.ApplyTooltips(toolTip);

        public string Tip(Control control) => toolTip.GetToolTip(control) ?? string.Empty;

        public string Wrapped(string text) => ToolTipTextWrapper.Wrap(text) ?? string.Empty;

        public T Find<T>(string name) where T : Control =>
            (T)Control.Controls.Find(name, searchAllChildren: true).Single();

        public void Dispose()
        {
            Control.Dispose();
            toolTip.Dispose();
        }
    }
}
