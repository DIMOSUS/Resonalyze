using System.Windows.Forms;
using Resonalyze.Dsp;
using static Resonalyze.App.Tests.AutoSetupWizardFixtures;

namespace Resonalyze.App.Tests;

[Trait("Category", "Slow")]
public sealed class VirtualCrossoverAutoSetupGroupTests
{
    [Fact]
    public void ChannelOrderControlsRemainInsideTheClientArea()
    {
        // Ordinary filenames overflow the fixed-width table and clip the Down arrow.
        string[] names =
        [
            "A — f R TWEET.json",
            "B — f RT Mid.json",
            "C — f R MB.json",
            "D — f FRONT Sub.json",
            "E — f REAR Sub.json",
            "F — f R fill.json",
            "G — f R center.json"
        ];
        IReadOnlyList<AutoSetupWizardChannel> channels = ReferenceCar()
            .Select((channel, index) => channel with { Name = names[index] })
            .ToList();

        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, channels);
            dialog.Show();

            Control table = dialog.Controls.Find("tableChannels", searchAllChildren: true).Single();
            Assert.True(
                table.Right < dialog.ClientSize.Width,
                $"channel table ends at {table.Right} in a {dialog.ClientSize.Width}px client");
        });
    }

    // One fit shared by the class; every test that takes it only reads it.
    private static readonly Lazy<(AutoSetupWizardSession Session, CrossoverProposal[] Proposals)> ReferenceFit =
        new(() =>
        {
            AutoSetupWizardSession session = Session(ReferenceCar());
            return (session, Proposals(session));
        });

    [Fact]
    public void Apply_AsksForTheBlocksInTheOrderTheDialogCrossedThem()
    {
        Assert.True(ReferenceFit.Value.Session.ReorderBlocks);
        Assert.Equal([6, 5, 2, 1, 0, 3, 4], ReferenceFit.Value.Session.RequestedChainOrder());
    }

    [Fact]
    public void Apply_AsksForNothingWhenTheUserClearedTheReorder()
    {
        AutoSetupWizardSession session = Session(ReferenceCar());
        session.ReorderBlocks = false;

        Assert.Null(session.RequestedChainOrder());
    }

    [Fact]
    public void MovingTheBassAnchor_MovesTheCeilingOnTheElevationWithIt()
    {
        // The elevation cap follows the lowest bass driver, so reordering must re-open it rather than keep the first cap.
        AutoSetupWizardSession session = Session(
        [
            Channel("quiet sub", VirtualCrossoverAlignmentStage.FrontChain, 20, 50, lowPassHz: 50),
            Channel(
                "loud sub", VirtualCrossoverAlignmentStage.FrontChain, 25, 62, levelDb: 8,
                highPassHz: 50),
            Channel("midbass", VirtualCrossoverAlignmentStage.FrontChain, 60, 900),
            Channel("mid", VirtualCrossoverAlignmentStage.FrontChain, 250, 6_000),
            Channel("tweeter", VirtualCrossoverAlignmentStage.FrontChain, 2_200, 20_000)
        ]);
        Preview(session);
        decimal before = session.ElevationRange.Maximum;
        Assert.True(before <= 1m, $"capped at {before} dB to begin with");

        Assert.True(session.MoveInChain(session.Rows[1], -1));
        Preview(session);

        // The elevation is the AVERAGE over the anchor's assigned passband, which takes in some of the driver's own
        // roll-off, so the cap re-opens by most of the loud sub's 8 dB rather than all of it.
        Assert.True(
            session.ElevationRange.Maximum >= before + 3m,
            $"The elevation is still capped at {session.ElevationRange.Maximum} dB after the driver " +
            $"carrying it moved to the bottom of the chain (was {before} dB).");
    }

    [Fact]
    public void Apply_ReturnsOneProposalPerChannel_InTheOrderTheyWereHandedIn()
    {
        // The panel writes back by position, so proposals come out in INPUT order.
        CrossoverProposal[] proposals = ReferenceFit.Value.Proposals;

        Assert.Equal(ReferenceCar().Count, proposals.Length);
        Assert.All(proposals, Assert.NotNull);
        Assert.Equal(CrossoverKind.HighPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.BandPass, proposals[2].Kind);
    }

    [Fact]
    public void Apply_CrossesTheFrontChainThroughBothSubwoofers()
    {
        CrossoverProposal[] proposals = ReferenceFit.Value.Proposals;

        int[] chain = [6, 5, 2, 1, 0];
        for (int i = 0; i + 1 < chain.Length; i++)
        {
            CrossoverEdge? lowPass = proposals[chain[i]].LowPassEdge;
            CrossoverEdge? highPass = proposals[chain[i + 1]].HighPassEdge;
            Assert.NotNull(lowPass);
            Assert.NotNull(highPass);
            Assert.Equal(lowPass!.Value.FrequencyHz, highPass!.Value.FrequencyHz, 3);
        }

        Assert.Null(proposals[6].HighPassEdge);
        Assert.Equal(CrossoverKind.LowPass, proposals[6].Kind);
    }

    [Fact]
    public void Apply_GivesTheRearAndCentreAProtectiveHighPassAndNoJunction()
    {
        CrossoverProposal[] proposals = ReferenceFit.Value.Proposals;

        foreach (int index in new[] { 3, 4 })
        {
            Assert.Equal(CrossoverKind.HighPass, proposals[index].Kind);
            Assert.Null(proposals[index].LowPassEdge);
            Assert.NotNull(proposals[index].HighPassEdge);
            double measured = CrossoverAutoSetup.EstimateBand(ReferenceCar()[index].MagnitudeDb).LowHz;
            Assert.InRange(proposals[index].HighPassEdge!.Value.FrequencyHz, measured * 1.8, measured * 2.3);
        }
    }

    [Fact]
    public void Apply_CutsALoudRearOntoTheFrontStage()
    {
        CrossoverProposal[] proposals = ReferenceFit.Value.Proposals;

        Assert.InRange(proposals[3].GainDb, -7.5, -4.5);
    }

    [Fact]
    public void Apply_LeavesAQuietGroupWhereItIs()
    {
        List<AutoSetupWizardChannel> channels = ReferenceCar().ToList();
        channels[3] = Channel("D rear", VirtualCrossoverAlignmentStage.Rear, 120, 15_000, levelDb: -6);

        CrossoverProposal[] proposals = Proposals(Session(channels));

        Assert.Equal(0, proposals[3].GainDb);
    }

    [Fact]
    public void Apply_WithNoRearOrCentre_IsOneGroupAndOneChain()
    {
        List<AutoSetupWizardChannel> channels = ReferenceCar()
            .Where(channel => channel.Group == VirtualCrossoverAlignmentStage.FrontChain)
            .ToList();

        CrossoverProposal[] proposals = Proposals(Session(channels));

        Assert.Equal(5, proposals.Length);
        Assert.Equal(4, proposals.Count(proposal => proposal.LowPassEdge is not null));
        Assert.Equal(4, proposals.Count(proposal => proposal.HighPassEdge is not null));
    }

    [Fact]
    public void AGroupOfOne_IsNoChainAndCarriesNoBassElevation()
    {
        AutoSetupWizardSession session = Session(
            [Channel("A", VirtualCrossoverAlignmentStage.Rear, 120, 15_000)]);

        Assert.False(session.SubElevationApplies);
        Assert.Empty(session.Junctions());
        Assert.Equal(VirtualCrossoverAlignmentStage.Rear, session.PrimaryGroup());
        Assert.False(session.MoveInChain(session.Rows[0], +1));
    }
}
