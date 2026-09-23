using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;
using static Resonalyze.App.Tests.VirtualCrossoverLivePanel;

namespace Resonalyze.App.Tests;

/// <summary>
/// The panel's menus and dialogs driven through a live panel: the AI import, the EQ Wizard handoff and its return, the
/// target level, the processor, a channel's goal and the phase gate. The rules have their own tests; these pin the glue
/// between the controls, the dialogs and the types that hold the rules.
/// </summary>
public sealed class VirtualCrossoverPanelDialogWiringTests
{
    [Fact]
    public void TheAiMenu_ImportsTheTickedRows_ShowsThemOnTheCards_AndUndoPutsThemBack() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannel a = live.Session.Channels[0];
        double before = a.SideSettings(rightSide: false).GainDb;
        string reply = "{\"kind\":\"resonalyze.agent-proposal\",\"protocolVersion\":1,\"summary\":\"trim\",\"operations\":[" +
            "{\"id\":\"op-1\",\"op\":\"setGainDb\",\"channelId\":\"A:left\",\"expectedCurrent\":" +
            before.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"proposed\":-4,\"reason\":\"hot\"}]}";
        Func<string?> read = AgentClipboard.ReadText;
        AgentClipboard.ReadText = () => reply;
        try
        {
            live.Click("buttonAi");
            Assert.False(live.MenuItemEnabled("Undo AI import"));
            live.Answer<AgentProposalDialog>(() => live.ClickMenu("Import AI proposal"), dialog =>
            {
                In<Button>(dialog, "buttonApply").PerformClick();
                return true;
            });
        }
        finally
        {
            AgentClipboard.ReadText = read;
        }

        Assert.Equal(-4, a.SideSettings(rightSide: false).GainDb);
        Assert.Equal(-4m, live.Card(a).GainInput.Value);
        Assert.Contains("Applied 1 of 1 proposed change.", Assert.Single(live.Messages));

        live.Click("buttonAi");
        Assert.True(live.MenuItemEnabled("Undo AI import"));
        live.ClickMenu("Undo AI import");

        Assert.Equal(before, a.SideSettings(rightSide: false).GainDb);
        Assert.Equal((decimal)before, live.Card(a).GainInput.Value);
        live.Click("buttonAi");
        Assert.False(live.MenuItemEnabled("Undo AI import"));
    });

    [Fact]
    public void AnUnreadableReply_IsRefusedWithAMessage_AndWritesNothing() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        string fingerprint = live.Panel.ComputeAgentFingerprint();
        Func<string?> read = AgentClipboard.ReadText;
        AgentClipboard.ReadText = () => "no proposal here";
        try
        {
            live.Click("buttonAi");
            live.ClickMenu("Import AI proposal");
        }
        finally
        {
            AgentClipboard.ReadText = read;
        }

        Assert.StartsWith("Virtual DSP: The AI proposal was not imported.", Assert.Single(live.Messages));
        Assert.Equal(fingerprint, live.Panel.ComputeAgentFingerprint());
    });

    [Fact]
    public void CopyForAi_PutsThePackageOnTheClipboard_AndSaysSo() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        string? copied = null;
        Action<string> write = AgentClipboard.WriteText;
        AgentClipboard.WriteText = text => copied = text;
        try
        {
            live.Click("buttonAi");
            live.ClickMenu("Copy for AI");
            live.Wait(() => live.Messages.Count > 0, "copy the package");
        }
        finally
        {
            AgentClipboard.WriteText = write;
        }

        Assert.StartsWith("Copy for AI: AI package copied", Assert.Single(live.Messages));
        Assert.Contains("\"kind\":\"resonalyze.agent-package\"", copied);
    });

    [Fact]
    public void TheDiagnosticsMenu_CopiesTheExcessGroupDelay_AndSaysSo() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        string? copied = null;
        Action<string> write = AgentClipboard.WriteText;
        AgentClipboard.WriteText = text => copied = text;
        try
        {
            live.Click("buttonAi");
            live.ClickMenu("Excess group delay");
            live.Wait(() => live.Messages.Count > 0, "copy the diagnostic");
        }
        finally
        {
            AgentClipboard.WriteText = write;
        }

        Assert.StartsWith("Copy diagnostics for AI: Excess group delay diagnostic copied", Assert.Single(live.Messages));
        Assert.Contains("excessGroupDelay", copied);
    });

    [Fact]
    public void ThePeqMenu_HandsTheChannelToTheWizard_AndTheReturnLandsWithItsLevel() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannel b = live.Session.Channels[1];
        VirtualDspEqHandoffRequest? handed = null;
        live.Panel.EditPeqInWizardRequested = request => handed = request;

        live.Card(b).PeqMenuButton.PerformClick();
        live.ClickMenu("Edit in EQ Wizard");

        VirtualDspEqHandoffRequest request = Assert.IsType<VirtualDspEqHandoffRequest>(handed);
        Assert.Same(b, request.Token.Channel);
        Assert.True(request.Token.WithChain);
        Assert.Equal(live.Session.Project.TargetLevelDb, request.TargetLevelDb);
        Assert.Equal((-120, 60), (request.TargetLevelMinDb, request.TargetLevelMaxDb));
        Assert.NotNull(request.Source.PhaseContext);
        var bank = new EqualizationCurve([new PeqBand(1_000, 2, -3)], -1);

        Assert.True(live.Panel.TryApplyPeqFromWizard(request.Token, bank, request.TargetLevelDb - 4));

        Assert.Equal(request.TargetLevelDb - 4, live.Session.Project.TargetLevelDb);
        Assert.Equal((decimal)(request.TargetLevelDb - 4), live.Find<ThemedNumericUpDown>("numericTargetLevel").Value);
        Assert.Single(b.Settings.PeqBands);
        Assert.Contains("1 bands", live.Card(b).PeqInfoLabel.Text);

        live.Card(b).PeqMenuButton.PerformClick();
        live.ClickMenu("Edit raw in EQ Wizard");
        VirtualDspEqHandoffRequest raw = handed!;
        live.Card(b).PeqMenuButton.PerformClick();
        live.ClickMenu("Clear");

        Assert.False(live.Panel.TryApplyPeqFromWizard(raw.Token, bank, raw.TargetLevelDb));
        Assert.Empty(b.Settings.PeqBands);
    });

    [Fact]
    public void TheLevelField_WritesTheProjectsLevel_AndARedrawFollowsIt() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        ThemedNumericUpDown level = live.Find<ThemedNumericUpDown>("numericTargetLevel");
        Assert.Equal(VirtualCrossoverLimits.TargetLevel, level.FieldRange());

        level.Value = -12m;
        live.Settle();
        string at12 = live.Panel.ComputeAgentFingerprint();
        level.Value = -11m;
        live.Settle();

        Assert.Equal(-11, live.Session.Project.TargetLevelDb);
        Assert.NotEqual(at12, live.Panel.ComputeAgentFingerprint());
    });

    [Fact]
    public void TheProcessorDialog_WritesTheModel_AndADeviceWithoutPhaseControlClearsTheAngle() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannel a = live.Session.Channels[0];

        live.Answer<DspProcessorDialog>(() => live.Click("buttonDspProcessor"), dialog =>
        {
            ThemedComboBox model = In<ThemedComboBox>(dialog, "comboBoxModel");
            model.SelectedItem = DspProcessorCatalog.Preset("helix-dsp-ultra-s");
            In<TextBox>(dialog, "textBoxNotes").Text = "Doors.";
            In<Button>(dialog, "buttonOk").PerformClick();
            return true;
        });

        Assert.Equal("helix-dsp-ultra-s", live.Session.Project.DspProcessorModelId);
        Assert.Equal("Doors.", live.Session.Project.AiNotes);
        Assert.True(live.Card(a).PhaseControlShown);
        live.Card(a).PhaseInput.Value = 45m;
        live.Settle();
        Assert.Equal(45, a.Settings.PhaseRotationDegrees);

        live.Answer<DspProcessorDialog>(() => live.Click("buttonDspProcessor"), dialog =>
        {
            In<ThemedComboBox>(dialog, "comboBoxModel").SelectedItem = DspProcessorCatalog.Preset("amp-panacea-v1-v2");
            In<Button>(dialog, "buttonOk").PerformClick();
            return true;
        });

        Assert.Equal(0, a.Settings.PhaseRotationDegrees);
        Assert.False(live.Card(a).PhaseControlShown);
        Assert.StartsWith("Virtual DSP: 1 channel side had a phase rotation", Assert.Single(live.Messages));
    });

    [Fact]
    public void TheGoalDialog_StatesTheCardsGoal_AndCancelKeepsIt() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannel a = live.Session.Channels[0];

        live.Answer<VirtualCrossoverAcousticGoalDialog>(() => live.Card(a).AcousticGoalButton.PerformClick(), dialog =>
        {
            Assert.False(In<ThemedComboBox>(dialog, "comboBoxHighPassFamily").Enabled);
            In<ThemedComboBox>(dialog, "comboBoxLowPassFamily").SelectedItem =
                CrossoverFamilyChoice.Offered.First(choice => choice.Value == CrossoverFilterFamily.LinkwitzRiley);
            In<Button>(dialog, "buttonOk").PerformClick();
            return true;
        });

        var goal = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24);
        Assert.Equal(goal, a.SideSettings(false).AcousticLowPass);
        Assert.Equal(goal, a.SideSettings(true).AcousticLowPass);
        Assert.Equal("LR24", live.Card(a).AcousticGoalButton.Text);

        live.Answer<VirtualCrossoverAcousticGoalDialog>(() => live.Card(a).AcousticGoalButton.PerformClick(), dialog =>
        {
            In<Button>(dialog, "buttonClear").PerformClick();
            In<Button>(dialog, "buttonCancel").PerformClick();
            return true;
        });

        Assert.Equal(goal, a.Settings.AcousticLowPass);
    });

    [Fact]
    public void TheGateDialog_PreviewsWhileOpen_AndSaveWritesThePinnedGate() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverGatePreview? previewed = null;

        live.Answer<VirtualCrossoverGateDialog>(() => live.Click("buttonPhaseGate"), dialog =>
        {
            In<CheckBox>(dialog, "checkAutoOffset").Checked = false;
            In<ThemedNumericUpDown>(dialog, "numericPlateau").Value = 4m;
            previewed = live.Session.GatePreview;
            In<Button>(dialog, "buttonSave").PerformClick();
            return true;
        });

        Assert.NotNull(previewed);
        Assert.False(previewed!.AutoOffset);
        Assert.Equal(4, previewed.PlateauMs);
        Assert.Null(live.Session.GatePreview);
        Assert.Equal(4, live.Session.Project.PhaseGatePlateauMs);
        Assert.Equal(previewed.OffsetMs, live.Session.Project.PhaseGateFor(live.Session.ActiveSideRight).OffsetMs);

        live.Answer<VirtualCrossoverGateDialog>(() => live.Click("buttonPhaseGate"), dialog =>
        {
            In<ThemedNumericUpDown>(dialog, "numericPlateau").Value = 6m;
            In<Button>(dialog, "buttonCancel").PerformClick();
            return true;
        });

        Assert.Equal(4, live.Session.Project.PhaseGatePlateauMs);
    });
}
