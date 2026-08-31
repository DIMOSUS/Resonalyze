using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// Moving a channel block. Everything the block's POSITION decides has to move
/// with it — the letter, the plot colour, the order the project persists — and
/// everything the block OWNS has to stay: its settings, its sources, the
/// measurements hanging off them.
/// </summary>
public sealed class VirtualCrossoverChannelOrderTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, Hidden)!.GetValue(target)!;

    private static void Call(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, Hidden)!.Invoke(target, arguments);

    private static List<VirtualCrossoverChannel> Channels(VirtualCrossoverPanel panel) =>
        (List<VirtualCrossoverChannel>)Field(panel, "channels");

    private static VirtualCrossoverProjectFile Project(VirtualCrossoverPanel panel) =>
        (VirtualCrossoverProjectFile)Field(panel, "project");

    private static FlowLayoutPanel ChannelList(VirtualCrossoverPanel panel) =>
        (FlowLayoutPanel)Field(panel, "channelListPanel");

    // The control bound to a channel, from the panel's own map.
    private static Control ControlOf(VirtualCrossoverPanel panel, VirtualCrossoverChannel channel)
    {
        var map = (System.Collections.IDictionary)Field(panel, "channelControls");
        return (Control)map[channel]!;
    }

    // The control takes an accent but does not hand it back; the header label it
    // paints with it is the honest place to read it from.
    private static Color Accent(VirtualCrossoverPanel panel, VirtualCrossoverChannel channel)
    {
        object control = ControlOf(panel, channel);
        var label = (Label)control.GetType().GetField("labelChannel", Hidden)!.GetValue(control)!;
        return label.ForeColor;
    }

    private static void Move(
        VirtualCrossoverPanel panel, VirtualCrossoverChannel channel, int delta) =>
        Call(panel, "MoveChannel", channel, delta);

    // A panel with the given number of blocks, bound to the project's pairs the
    // way applying a project binds them — the state every arrow press in the
    // field happens in. Without that binding the two lists are unrelated objects,
    // which is what a freshly constructed panel holds and its own test below.
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
            // The persisted order IS the block order: the file stores no letter,
            // so the pair list is the only thing that remembers it.
            Assert.Same(channels[i].Pair, Project(panel).Pairs[i]);
            Assert.Equal(i, list.Controls.GetChildIndex(ControlOf(panel, channels[i])));
        }

        // The child index is only the mechanism. What the user sees is where the
        // flow panel actually puts each block, and a reorder that got that
        // backwards would satisfy every assertion above.
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
            // A mark the block owns, so it can be followed across the move.
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

            // The colour belongs to the slot, not the channel: the block that
            // moved up takes the colour of the row it moved into and the one it
            // displaced takes the other, so a curve stays traceable to the block
            // sitting at that position.
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

            // Right as the list is built, and right again after a block moves:
            // the state is positional, so the block that WAS at the top has to be
            // handed its up-arrow back when it stops being there.
            AssertEnds();
            Move(panel, Channels(panel)[0], +1);
            AssertEnds();
        });
    }

    [Fact]
    public void MoveChannel_OnAPanelWithNoProjectApplied_KeepsTheProjectsOwnPairs()
    {
        // A freshly constructed panel holds channels whose pairs are their own and
        // a project holding unrelated default ones; the two are bound only when a
        // project is applied. Rebuilding the project's list out of the channels
        // there would quietly throw the project's pairs away, so the list is
        // permuted by the same indices instead.
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
        // Auto crossover only ever sees the enabled channels that resolved a
        // source. The rest have no place in a chain it worked out, so they keep
        // the slot they had and the sorted ones fill the slots around them.
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
