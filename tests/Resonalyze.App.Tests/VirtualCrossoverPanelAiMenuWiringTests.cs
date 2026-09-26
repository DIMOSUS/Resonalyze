using System.Windows.Forms;
using Resonalyze.Integration.AgentBridge;
using static Resonalyze.App.Tests.VirtualCrossoverLivePanel;

namespace Resonalyze.App.Tests;

/// <summary>The panel's AI menu driven through a live panel, the clipboard swapped for a stub. Beside
/// <see cref="VirtualCrossoverPanelDialogWiringTests"/> so the two run in parallel.</summary>
[Collection(AgentClipboardUsers.Name)]
[Trait("Category", "Slow")]
public sealed class VirtualCrossoverPanelAiMenuWiringTests
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
        double level = live.Session.Project.TargetLevelDb;
        CheckBox hybrid = live.Find<CheckBox>("checkBoxHybrid");
        bool ticked = hybrid.Checked;
        hybrid.Checked = !ticked;
        live.Find<ThemedNumericUpDown>("numericTargetLevel").Value = (decimal)level - 7;

        live.Click("buttonAi");
        Assert.True(live.MenuItemEnabled("Undo AI import"));
        live.ClickMenu("Undo AI import");

        Assert.Equal(before, a.SideSettings(rightSide: false).GainDb);
        Assert.Equal((decimal)before, live.Card(a).GainInput.Value);
        Assert.Equal(ticked, hybrid.Checked);
        Assert.Equal(level, live.Session.Project.TargetLevelDb);
        Assert.Equal((decimal)level, live.Find<ThemedNumericUpDown>("numericTargetLevel").Value);
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
}
