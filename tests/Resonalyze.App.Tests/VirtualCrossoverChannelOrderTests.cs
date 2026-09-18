using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>Position decides letter, colour and persisted order; the block owns settings and sources.</summary>
public sealed class VirtualCrossoverChannelOrderTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, Hidden)!.GetValue(target)!;

    private static void Call(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, Hidden)!.Invoke(target, arguments);

    private static List<VirtualCrossoverChannel> Channels(VirtualCrossoverPanel panel) =>
        panel.Session.Channels;

    private static VirtualCrossoverProjectFile Project(VirtualCrossoverPanel panel) =>
        panel.Session.Project;

    private static FlowLayoutPanel ChannelList(VirtualCrossoverPanel panel) =>
        (FlowLayoutPanel)Field(panel, "channelListPanel");

    private static Control ControlOf(VirtualCrossoverPanel panel, VirtualCrossoverChannel channel)
    {
        var map = (System.Collections.IDictionary)Field(panel, "channelControls");
        return (Control)map[channel]!;
    }

    private static Color Accent(VirtualCrossoverPanel panel, VirtualCrossoverChannel channel)
    {
        object control = ControlOf(panel, channel);
        var label = (Label)control.GetType().GetField("labelChannel", Hidden)!.GetValue(control)!;
        return label.ForeColor;
    }

    private static void Move(
        VirtualCrossoverPanel panel, VirtualCrossoverChannel channel, int delta) =>
        Call(panel, "MoveChannel", channel, delta);

    // Bound as applying a project binds; a fresh panel's lists are unrelated objects.
    private static VirtualCrossoverPanel Loaded(int count)
    {
        var panel = new VirtualCrossoverPanel();
        while (Channels(panel).Count < count)
        {
            Call(panel, "AddChannel");
        }

        List<VirtualCrossoverChannel> channels = Channels(panel);
        for (int i = 0; i < channels.Count; i++)
        {
            channels[i].Pair = Project(panel).Pairs[i];
        }

        return panel;
    }

    private static void AssertConsistent(VirtualCrossoverPanel panel)
    {
        List<VirtualCrossoverChannel> channels = Channels(panel);
        FlowLayoutPanel list = ChannelList(panel);
        Assert.Equal(channels.Count, Project(panel).Pairs.Count);
        for (int i = 0; i < channels.Count; i++)
        {
            Assert.Equal(VirtualCrossoverSheet.ChannelName(i), channels[i].Name);
            // The file stores no letter: the pair list order is the block order.
            Assert.Same(channels[i].Pair, Project(panel).Pairs[i]);
            Assert.Equal(i, list.Controls.GetChildIndex(ControlOf(panel, channels[i])));
        }

        list.PerformLayout();
        IEnumerable<int> tops = channels.Select(channel => ControlOf(panel, channel).Top);
        Assert.Equal(tops.OrderBy(top => top), tops);
        Assert.Equal(channels.Count, tops.Distinct().Count());
    }

    [Fact]
    public void MoveChannel_CarriesTheBlocksOwnSettingsAndRewritesOnlyItsPosition()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded(4);
            List<VirtualCrossoverChannel> channels = Channels(panel);
            for (int i = 0; i < channels.Count; i++)
            {
                channels[i].Pair.Left.DelayMs = 10 + i;
            }

            VirtualCrossoverChannel moved = channels[2];
            double carried = moved.Pair.Left.DelayMs;
            AssertConsistent(panel);

            Move(panel, moved, -1);

            Assert.Same(moved, Channels(panel)[1]);
            Assert.Equal("B", moved.Name);
            Assert.Equal(carried, moved.Pair.Left.DelayMs);
            AssertConsistent(panel);
        });
    }

    [Fact]
    public void MoveChannel_TakesTheAccentColourOfTheNewPosition()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded(3);
            VirtualCrossoverChannel first = Channels(panel)[0];
            VirtualCrossoverChannel second = Channels(panel)[1];
            Color firstAccent = Accent(panel, first);
            Color secondAccent = Accent(panel, second);
            Assert.NotEqual(firstAccent, secondAccent);

            Move(panel, second, -1);

            Assert.Equal(firstAccent, Accent(panel, second));
            Assert.Equal(secondAccent, Accent(panel, first));
        });
    }

    [Fact]
    public void MoveChannel_DoesNothingOffEitherEnd()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded(3);
            List<VirtualCrossoverChannel> before = Channels(panel).ToList();

            Move(panel, before[0], -1);
            Move(panel, before[^1], +1);

            Assert.Equal(before, Channels(panel));
            AssertConsistent(panel);
        });
    }

    private static bool Enabled(
        VirtualCrossoverPanel panel, VirtualCrossoverChannel channel, string button)
    {
        object control = ControlOf(panel, channel);
        var arrow = (Button)control.GetType().GetField(button, Hidden)!.GetValue(control)!;
        return arrow.Enabled;
    }

    [Fact]
    public void ChannelOrder_GreysTheArrowsTheEndBlocksHaveNowhereToGoWith()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded(3);
            void AssertEnds()
            {
                List<VirtualCrossoverChannel> channels = Channels(panel);
                Assert.False(Enabled(panel, channels[0], "buttonMoveUp"));
                Assert.True(Enabled(panel, channels[0], "buttonMoveDown"));
                Assert.True(Enabled(panel, channels[1], "buttonMoveUp"));
                Assert.True(Enabled(panel, channels[1], "buttonMoveDown"));
                Assert.True(Enabled(panel, channels[^1], "buttonMoveUp"));
                Assert.False(Enabled(panel, channels[^1], "buttonMoveDown"));
            }

            AssertEnds();
            Move(panel, Channels(panel)[0], +1);
            AssertEnds();
        });
    }

    [Fact]
    public void MoveChannel_OnAPanelWithNoProjectApplied_KeepsTheProjectsOwnPairs()
    {
        // Unbound panel: rebuilding the project list from channels would discard its pairs, so it is permuted by index.
        StaTest.Run(() =>
        {
            using var panel = new VirtualCrossoverPanel();
            List<VirtualCrossoverChannelPairSettings> before = Project(panel).Pairs.ToList();
            Assert.True(Channels(panel).Count >= 2);
            Assert.DoesNotContain(Channels(panel)[0].Pair, before);

            Move(panel, Channels(panel)[1], -1);

            Assert.Equal(before.Count, Project(panel).Pairs.Count);
            Assert.Same(before[1], Project(panel).Pairs[0]);
            Assert.Same(before[0], Project(panel).Pairs[1]);
        });
    }

    [Fact]
    public void ReorderIntoSlots_LeavesTheBlocksTheWizardNeverSawWhereTheyWere()
    {
        string[] all = ["a", "b", "skipped", "c", "d"];
        string[] sorted = ["d", "c", "b", "a"];

        IReadOnlyList<string> result = VirtualCrossoverPanel.ReorderIntoSlots(all, sorted);

        Assert.Equal(["d", "c", "skipped", "b", "a"], result);
    }

    [Fact]
    public void ReorderIntoSlots_IsIdentityWhenNothingMoved()
    {
        string[] all = ["a", "b", "c"];

        Assert.Equal(all, VirtualCrossoverPanel.ReorderIntoSlots(all, all));
    }
}
