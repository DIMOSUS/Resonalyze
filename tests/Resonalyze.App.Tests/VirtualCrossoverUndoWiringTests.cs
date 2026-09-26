using System.Numerics;
using System.Windows.Forms;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;
using static Resonalyze.App.Tests.VirtualCrossoverLivePanel;

namespace Resonalyze.App.Tests;

/// <summary>Undo last Apply / Undo last copy through a live panel: the command's button opens its real dialog, a timer in
/// its loop writes, and the same dialog's Undo is read against the session, the cards and the questions. The rules are
/// <see cref="VirtualCrossoverUndoTests"/>'.</summary>
[Trait("Category", "Slow")]
public sealed class VirtualCrossoverUndoWiringTests
{
    [Fact]
    public void ACopy_IsUndoneFromEitherCopyDialog_AfterAskingAboutLaterChanges() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannel a = live.Session.Channels[0];
        var bell = new PeqBand(1_000, 2, -4);
        a.SideSettings(rightSide: false).PeqBands = [bell];
        string before = live.Panel.ComputeAgentFingerprint();
        ThemedNumericUpDown level = live.Find<ThemedNumericUpDown>("numericTargetLevel");
        decimal levelBefore = level.Value;

        live.Answer<VirtualCrossoverCopySideDialog>(() => live.Click("buttonCopyLeftToRight"), dialog =>
        {
            Assert.False(In<Button>(dialog, "buttonUndo").Enabled);
            In<Button>(dialog, "buttonCopy").PerformClick();
            return true;
        });

        Assert.Equal([bell], a.SideSettings(rightSide: true).PeqBands);
        level.Value = levelBefore - 3;
        live.Settle();

        UndoIn<VirtualCrossoverCopySideDialog>(live, "buttonCopyRightToLeft");

        Assert.Contains("L → R of A, B, C", Assert.Single(live.Messages));
        Assert.Equal([bell], a.SideSettings(rightSide: true).PeqBands);

        live.Answers.Enqueue(DialogResult.Yes);
        UndoIn<VirtualCrossoverCopySideDialog>(live, "buttonCopyLeftToRight");

        Assert.Empty(a.SideSettings(rightSide: true).PeqBands);
        Assert.Equal(levelBefore, level.Value);
        Assert.Equal(before, live.Panel.ComputeAgentFingerprint());
        live.Answer<VirtualCrossoverCopySideDialog>(() => live.Click("buttonCopyLeftToRight"), dialog =>
        {
            Assert.False(In<Button>(dialog, "buttonUndo").Enabled);
            return true;
        });
    });

    [Fact]
    public void AutoDelay_IsUndoneFromItsDialog_TheSceneAndTheCardsIncluded() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannel b = live.Session.Channels[1];
        live.Card(b).DelayInput.Value = 1.5m;
        b.SideSettings(rightSide: true).DelayMs = 1.5;
        live.Settle();
        double scene = live.Session.Project.StereoSceneOffsetMagnitudeMs;
        string before = live.Panel.ComputeAgentFingerprint();

        RunAutoDelay(live, dialog => In<ThemedNumericUpDown>(dialog, "numericSceneOffset").Value = (decimal)scene + 0.1m);

        Assert.NotEqual(1.5, b.SideSettings(rightSide: false).DelayMs);
        Assert.Equal(scene + 0.1, live.Session.Project.StereoSceneOffsetMagnitudeMs, 6);

        UndoIn<VirtualCrossoverAutoDelayDialog>(live, "buttonAutoDelay");

        Assert.Empty(live.Messages);
        Assert.Equal(1.5, b.SideSettings(rightSide: false).DelayMs);
        Assert.Equal(1.5, b.SideSettings(rightSide: true).DelayMs);
        Assert.Equal(1.5m, live.Card(b).DelayInput.Value);
        Assert.Equal(scene, live.Session.Project.StereoSceneOffsetMagnitudeMs);
        Assert.Equal(before, live.Panel.ComputeAgentFingerprint());
    });

    [Fact]
    public void AMisplacedGate_RefusesAutoDelay_ButOffersToUndoTheLastOne() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannel b = live.Session.Channels[1];
        live.Card(b).DelayInput.Value = 1.5m;
        live.Settle();
        RunAutoDelay(live, _ => { });
        PinGate(live, 200);
        live.Answers.Enqueue(DialogResult.Yes);

        NeverOpens<VirtualCrossoverAutoDelayDialog>(live, () => live.Click("buttonAutoDelay"));

        // One question, the offer: the gate is not part of what Undo takes back, so moving it asks nothing more.
        Assert.Single(live.Messages);
        Assert.Empty(live.Answers);
        Assert.Equal(1.5, b.SideSettings(rightSide: false).DelayMs);
    });

    [Fact]
    public void AutoCrossover_IsUndoneFromTheWizard_BlockOrderAndPhaseRotationsIncluded() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel(impulseResponse: Drivers);
        VirtualCrossoverChannel woofer = live.Session.Channels[0];
        ((Button)live.Card(woofer).Controls.Find("buttonMoveDown", searchAllChildren: true).Single()).PerformClick();
        live.Settle();
        List<VirtualCrossoverChannel> order = [.. live.Session.Channels];
        foreach ((VirtualCrossoverChannel channel, bool rightSide) in live.Session.Sides())
        {
            channel.SideSettings(rightSide).PhaseRotationDegrees = 40;
        }

        string before = live.Panel.ComputeAgentFingerprint();

        ApplyWizard(live, () => live.Click("buttonAutoSetup"));

        Assert.Same(woofer, live.Session.Channels[0]);
        Assert.All(live.Session.Sides(), side => Assert.Equal(0, side.Channel.SideSettings(side.RightSide).PhaseRotationDegrees));
        Assert.Contains("6 channel sides", Assert.Single(live.Messages));

        PinGate(live, 200);
        live.Answers.Enqueue(DialogResult.No);
        NeverOpens<VirtualCrossoverAutoSetupDialog>(live, () => live.Click("buttonAutoSetup"));
        // The refusal asked (a report would have left the answer queued), and No leaves the Apply standing.
        Assert.Empty(live.Answers);
        Assert.Same(woofer, live.Session.Channels[0]);
        PinGate(live, null);

        UndoIn<VirtualCrossoverAutoSetupDialog>(live, "buttonAutoSetup");

        Assert.Equal(2, live.Messages.Count);

        Assert.Equal(order, live.Session.Channels);
        Assert.Equal(["A", "B", "C"], live.Session.Channels.Select(channel => channel.Name));
        List<VirtualCrossoverChannelControl> cards =
            [.. live.Find<FlowLayoutPanel>("channelListPanel").Controls.OfType<VirtualCrossoverChannelControl>()];
        Assert.Equal(["A", "B", "C"], cards.Select(card => card.ChannelName));
        Assert.Equal(
            order.Select(channel => (object)channel.Settings.CrossoverKind),
            cards.Select(card => card.CrossoverKindComboBox.SelectedItem));
        Assert.All(live.Session.Sides(), side => Assert.Equal(40, side.Channel.SideSettings(side.RightSide).PhaseRotationDegrees));
        Assert.Equal(before, live.Panel.ComputeAgentFingerprint());
    });

    [Fact]
    public void AnImportsEngineRuns_LeaveNoUndoInTheDialogs() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel(impulseResponse: Drivers);
        var host = (IAgentImportHost)live.Panel;
        VirtualCrossoverChannel a = live.Session.Channels[0];

        ApplyWizard(live, () => Assert.Null(host.OpenAutoSetupWizard()));
        Task delay = host.ApplyAutoDelayAsync(new AutoDelayRunResult(
            [new AutoDelayChannelOutcome(
                a, a.SideSettings(rightSide: false), "A", 0, false, 0, 2.5, true, 0, false,
                null, null, string.Empty, null, string.Empty)],
            Stereo: false,
            new AutoDelayRunRequest(0.25, false, false, 1.0),
            string.Empty,
            new System.Text.StringBuilder()));
        live.Wait(() => delay.IsCompleted, "commit the import's Auto delay");

        Assert.Equal(2.5, a.SideSettings(rightSide: false).DelayMs);
        live.Answer<VirtualCrossoverAutoSetupDialog>(() => live.Click("buttonAutoSetup"), dialog =>
        {
            Assert.False(In<Button>(dialog, "buttonUndo").Enabled);
            return true;
        });
        live.Answer<VirtualCrossoverAutoDelayDialog>(() => live.Click("buttonAutoDelay"), dialog =>
        {
            Assert.False(In<Button>(dialog, "buttonUndo").Enabled);
            return true;
        });
    });

    private static Complex[] Drivers(int index) => index switch
    {
        0 => Driver(35, 600),
        1 => Driver(150, 6_000),
        _ => Driver(1_800, 20_000)
    };

    // Applies the wizard as it opens; Undo is not offered by a first run.
    private static void ApplyWizard(VirtualCrossoverLivePanel live, Action open)
    {
        bool applied = false;
        live.Answer<VirtualCrossoverAutoSetupDialog>(open, dialog =>
        {
            Button apply = In<Button>(dialog, "buttonApply");
            // Apply ranks off the UI thread and is off meanwhile; offered again, it did not land.
            Assert.False(applied && apply.Enabled, "Apply did not land: " + In<Label>(dialog, "labelPreview").Text);
            if (!applied && apply.Enabled)
            {
                Assert.False(In<Button>(dialog, "buttonUndo").Enabled);
                applied = true;
                apply.PerformClick();
            }

            return false;
        });
    }

    // A pin past every arrival: the window opens after the drivers and the gate reads as misplaced; null unpins it.
    private static void PinGate(VirtualCrossoverLivePanel live, double? offsetMs)
    {
        live.Session.Project.PhaseGateFor(rightSide: false).OffsetMs = offsetMs;
        live.Find<CheckBox>("checkBoxShowSum").Checked ^= true;
        live.Settle();
    }

    // A dialog the refusal should have kept shut is closed and fails the test, rather than hanging it.
    private static void NeverOpens<TDialog>(VirtualCrossoverLivePanel live, Action open) where TDialog : Form
    {
        bool opened = false;
        using var guard = new System.Windows.Forms.Timer { Interval = 20 };
        guard.Tick += (_, _) =>
        {
            if (StaTest.OpenForm<TDialog>(form => form.Visible && form.Modal) is { } dialog)
            {
                opened = true;
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            }
        };
        guard.Start();
        open();
        guard.Stop();
        Assert.False(opened, $"{typeof(TDialog).Name} opened.");
    }

    private static void UndoIn<TDialog>(VirtualCrossoverLivePanel live, string button) where TDialog : Form =>
        live.Answer<TDialog>(() => live.Click(button), dialog =>
        {
            Button undo = In<Button>(dialog, "buttonUndo");
            Assert.True(undo.Enabled);
            undo.PerformClick();
            return true;
        });

    private static void RunAutoDelay(VirtualCrossoverLivePanel live, Action<VirtualCrossoverAutoDelayDialog> ask)
    {
        bool ran = false;
        live.Answer<VirtualCrossoverAutoDelayDialog>(() => live.Click("buttonAutoDelay"), dialog =>
        {
            if (!ran)
            {
                ran = true;
                ask(dialog);
                In<Button>(dialog, "buttonRun").PerformClick();
                return false;
            }

            string status = In<Label>(dialog, "labelStatus").Text;
            Assert.DoesNotContain("failed", status, StringComparison.OrdinalIgnoreCase);
            Button apply = In<Button>(dialog, "buttonApply");
            if (!apply.Enabled)
            {
                return false;
            }

            apply.PerformClick();
            return true;
        });
    }

    // A driver's band: second-order skirts at both ends, arriving where the live panel's impulses do.
    private static Complex[] Driver(double lowHz, double highHz)
    {
        var spectrum = new Complex[Length];
        double binHz = (double)SampleRate / Length;
        for (int bin = 1; bin < Length / 2; bin++)
        {
            double hz = bin * binHz;
            double magnitude = 1.0 / Math.Sqrt(1 + Math.Pow(lowHz / hz, 4)) / Math.Sqrt(1 + Math.Pow(hz / highHz, 4));
            Complex value = Complex.FromPolarCoordinates(magnitude, -2.0 * Math.PI * bin * PeakIndex / Length);
            spectrum[bin] = value;
            spectrum[Length - bin] = Complex.Conjugate(value);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return spectrum;
    }
}
