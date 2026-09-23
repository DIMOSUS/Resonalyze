using System.Drawing;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>Position decides letter, colour and persisted order; the block owns settings and sources.</summary>
public sealed class VirtualCrossoverChannelOrderTests
{
    private static VirtualCrossoverSession Session(int count)
    {
        var session = new VirtualCrossoverSession();
        while (session.Project.Pairs.Count < count)
        {
            session.Project.Pairs.Add(new VirtualCrossoverChannelPairSettings());
        }

        for (int index = 0; index < count; index++)
        {
            session.Channels.Add(new VirtualCrossoverChannel(VirtualCrossoverSheet.ChannelName(index))
            {
                Pair = session.Project.Pairs[index]
            });
        }

        return session;
    }

    [Fact]
    public void AMove_CarriesTheBlocksOwnSettings_AndRewritesOnlyItsPosition()
    {
        VirtualCrossoverSession session = Session(4);
        for (int i = 0; i < session.Channels.Count; i++)
        {
            session.Channels[i].Pair.Left.DelayMs = 10 + i;
        }

        VirtualCrossoverChannel moved = session.Channels[2];

        session.Reorder(session.MoveOrder(moved, -1)!);

        Assert.Same(moved, session.Channels[1]);
        Assert.Equal(["A", "B", "C", "D"], session.Channels.Select(channel => channel.Name));
        Assert.Equal(12, moved.Pair.Left.DelayMs);
        Assert.Equal(session.Channels.Select(channel => channel.Pair), session.Project.Pairs);
    }

    [Fact]
    public void NothingMovesOffEitherEnd()
    {
        VirtualCrossoverSession session = Session(3);

        Assert.Null(session.MoveOrder(session.Channels[0], -1));
        Assert.Null(session.MoveOrder(session.Channels[^1], +1));
        Assert.Null(session.MoveOrder(new VirtualCrossoverChannel("X"), +1));
        Assert.Equal([1, 0, 2], session.MoveOrder(session.Channels[0], +1));
    }

    [Fact]
    public void AnUnboundProjectsPairs_ArePermutedByIndex_NotRebuiltFromTheBlocks()
    {
        var session = new VirtualCrossoverSession();
        session.Channels.Add(new VirtualCrossoverChannel("A"));
        session.Channels.Add(new VirtualCrossoverChannel("B"));
        session.Channels.Add(new VirtualCrossoverChannel("C"));
        List<VirtualCrossoverChannelPairSettings> before = session.Project.Pairs.ToList();

        session.Reorder(session.MoveOrder(session.Channels[1], -1)!);

        Assert.Equal([before[1], before[0], before[2]], session.Project.Pairs);
    }

    [Fact]
    public void AnEarlierOrder_IsFoundByIdentity_AndNoneWhenABlockIsGoneOrNothingMoved()
    {
        VirtualCrossoverSession session = Session(3);
        List<VirtualCrossoverChannel> earlier = session.Channels.ToList();

        Assert.Null(session.OrderOf(earlier));
        session.Reorder([2, 0, 1]);
        IReadOnlyList<int> back = Assert.IsAssignableFrom<IReadOnlyList<int>>(session.OrderOf(earlier));
        session.Reorder(back);

        Assert.Equal(earlier, session.Channels);
        Assert.Equal(["A", "B", "C"], session.Channels.Select(channel => channel.Name));
        Assert.Null(session.OrderOf([earlier[0], earlier[1], new VirtualCrossoverChannel("X")]));
        Assert.Null(session.OrderOf(earlier.Take(2).ToList()));
    }

    [Fact]
    public void ReorderIntoSlots_LeavesTheBlocksTheWizardNeverSawWhereTheyWere()
    {
        string[] all = ["a", "b", "skipped", "c", "d"];
        string[] sorted = ["d", "c", "b", "a"];

        Assert.Equal(["d", "c", "skipped", "b", "a"], VirtualCrossoverAutoSetup.ReorderIntoSlots(all, sorted));
    }

    [Fact]
    public void ReorderIntoSlots_IsIdentityWhenNothingMoved()
    {
        string[] all = ["a", "b", "c"];

        Assert.Equal(all, VirtualCrossoverAutoSetup.ReorderIntoSlots(all, all));
    }

    [Fact]
    public void TheArrows_MoveTheCards_WhichTakeTheirPositionsColour_AndGreyWhereABlockCannotGo() => StaTest.Run(() =>
    {
        using var panel = new VirtualCrossoverPanel();
        Find<Button>(panel, "buttonAddChannel").PerformClick();
        List<VirtualCrossoverChannel> blocks = panel.Session.Channels.ToList();
        Color firstAccent = Accent(Cards(panel)[0]);
        Color secondAccent = Accent(Cards(panel)[1]);
        Assert.NotEqual(firstAccent, secondAccent);
        AssertArrows(panel);

        Find<Button>(Cards(panel)[1], "buttonMoveUp").PerformClick();

        Assert.Equal([blocks[1], blocks[0], blocks[2], blocks[3]], panel.Session.Channels);
        Assert.Equal(["A", "B", "C", "D"], Cards(panel).Select(card => card.ChannelName));
        Assert.Equal(firstAccent, Accent(Cards(panel)[0]));
        Assert.Equal(secondAccent, Accent(Cards(panel)[1]));
        AssertArrows(panel);

        Find<Button>(Cards(panel)[^1], "buttonMoveDown").PerformClick();
        Assert.Equal([blocks[1], blocks[0], blocks[2], blocks[3]], panel.Session.Channels);
    });

    private static void AssertArrows(VirtualCrossoverPanel panel)
    {
        List<VirtualCrossoverChannelControl> cards = Cards(panel);
        for (int i = 0; i < cards.Count; i++)
        {
            Assert.Equal(i > 0, Find<Button>(cards[i], "buttonMoveUp").Enabled);
            Assert.Equal(i < cards.Count - 1, Find<Button>(cards[i], "buttonMoveDown").Enabled);
        }
    }

    private static List<VirtualCrossoverChannelControl> Cards(VirtualCrossoverPanel panel) =>
        Find<FlowLayoutPanel>(panel, "channelListPanel").Controls.OfType<VirtualCrossoverChannelControl>().ToList();

    private static Color Accent(Control card) => Find<Label>(card, "labelChannel").ForeColor;

    private static T Find<T>(Control root, string name) where T : Control =>
        (T)root.Controls.Find(name, searchAllChildren: true).First();
}
