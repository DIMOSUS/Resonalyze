using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Resonalyze;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAutoSetupGroupTests
{
    private const double SampleRate = 48_000;

    private static List<SignalPoint> BandCurve(double lowHz, double highHz, double levelDb = 0)
    {
        var points = new List<SignalPoint>();
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 512))
        {
            double y = levelDb;
            if (frequency < lowHz)
            {
                y -= 24.0 * Math.Log2(lowHz / frequency);
            }
            else if (frequency > highHz)
            {
                y -= 24.0 * Math.Log2(frequency / highHz);
            }

            points.Add(new SignalPoint(frequency, y));
        }

        return points;
    }

    private static AutoSetupWizardChannel Channel(
        string name,
        VirtualCrossoverAlignmentStage group,
        double lowHz,
        double highHz,
        double levelDb = 0,
        double? highPassHz = null,
        double? lowPassHz = null)
    {
        List<SignalPoint> curve = BandCurve(lowHz, highHz, levelDb);
        return new AutoSetupWizardChannel(
            name,
            Color.White,
            group,
            curve,
            null,
            null,
            CrossoverAutoSetup.EstimateBand(curve),
            highPassHz,
            lowPassHz,
            null);
    }

    // Handed in panel order; both subs measure the same, so only their preset corners order them.
    private static IReadOnlyList<AutoSetupWizardChannel> ReferenceCar() =>
    [
        Channel("A tweeter", VirtualCrossoverAlignmentStage.FrontChain, 2_200, 20_000),
        Channel("B mid", VirtualCrossoverAlignmentStage.FrontChain, 250, 6_000),
        Channel("C midbass", VirtualCrossoverAlignmentStage.FrontChain, 60, 900),
        Channel("D rear", VirtualCrossoverAlignmentStage.Rear, 120, 15_000, levelDb: 6),
        Channel("E center", VirtualCrossoverAlignmentStage.Center, 200, 18_000),
        Channel(
            "F front sub", VirtualCrossoverAlignmentStage.FrontChain, 20, 300,
            highPassHz: 50, lowPassHz: 110),
        Channel(
            "G rear sub", VirtualCrossoverAlignmentStage.FrontChain, 20, 300, lowPassHz: 50)
    ];

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

            var table = (TableLayoutPanel)typeof(VirtualCrossoverAutoSetupDialog)
                .GetField("tableChannels", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            Assert.True(
                table.Right < dialog.ClientSize.Width,
                $"channel table ends at {table.Right} in a {dialog.ClientSize.Width}px client");
        });
    }

    // Without IRs Apply finishes synchronously; fixtures must be unambiguous or a confirmation dialog blocks.
    private static IReadOnlyList<CrossoverProposal> Apply(
        IReadOnlyList<AutoSetupWizardChannel> channels)
    {
        IReadOnlyList<CrossoverProposal>? result = null;
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, channels);
            typeof(VirtualCrossoverAutoSetupDialog)
                .GetMethod("ApplyClick", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(dialog, [null, EventArgs.Empty]);
            result = dialog.Result;
        });

        Assert.NotNull(result);
        return result!;
    }

    private static IReadOnlyList<int>? ChainOrder(
        IReadOnlyList<AutoSetupWizardChannel> channels,
        bool reorder)
    {
        IReadOnlyList<int>? order = null;
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, channels);
            var box = (CheckBox)typeof(VirtualCrossoverAutoSetupDialog)
                .GetField("reorderBlocks", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            box.Checked = reorder;
            typeof(VirtualCrossoverAutoSetupDialog)
                .GetMethod("ApplyClick", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(dialog, [null, EventArgs.Empty]);
            Assert.NotNull(dialog.Result);
            order = dialog.ChainOrder;
        });

        return order;
    }

    [Fact]
    public void Apply_AsksForTheBlocksInTheOrderTheDialogCrossedThem()
    {
        Assert.Equal([6, 5, 2, 1, 0, 3, 4], ChainOrder(ReferenceCar(), reorder: true));
    }

    [Fact]
    public void Apply_AsksForNothingWhenTheUserClearedTheReorder()
    {
        Assert.Null(ChainOrder(ReferenceCar(), reorder: false));
    }

    [Fact]
    public void MovingTheBassAnchor_MovesTheCeilingOnTheElevationWithIt()
    {
        // The elevation cap follows the lowest bass driver, so reordering must re-open it rather than keep the first cap.
        var channels = new List<AutoSetupWizardChannel>
        {
            Channel("quiet sub", VirtualCrossoverAlignmentStage.FrontChain, 20, 50, lowPassHz: 50),
            Channel(
                "loud sub", VirtualCrossoverAlignmentStage.FrontChain, 25, 62, levelDb: 8,
                highPassHz: 50),
            Channel("midbass", VirtualCrossoverAlignmentStage.FrontChain, 60, 900),
            Channel("mid", VirtualCrossoverAlignmentStage.FrontChain, 250, 6_000),
            Channel("tweeter", VirtualCrossoverAlignmentStage.FrontChain, 2_200, 20_000)
        };

        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, channels);
            // The elevation ceiling comes off the fit, and the fit is off the UI thread now.
            StaTest.Settle(dialog.PendingPreview);

            var field = (DarkNumericUpDown)typeof(VirtualCrossoverAutoSetupDialog)
                .GetField("subElevation", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            Assert.True(field.Maximum <= 1m, $"capped at {field.Maximum} dB to begin with");
            decimal before = field.Maximum;

            var rows = (System.Collections.IList)typeof(VirtualCrossoverAutoSetupDialog)
                .GetField("rows", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            typeof(VirtualCrossoverAutoSetupDialog)
                .GetMethod("MoveInChain", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(dialog, [rows[1]!, -1]);
            StaTest.Settle(dialog.PendingPreview);

            // The cap re-opens by most of the loud sub's 8 dB, not all of it: the elevation is the AVERAGE over the
            // anchor's assigned passband, and that passband reaches from the measured band edge up to the junction,
            // so it takes in some of the driver's own roll-off. What this test is about is that the cap follows the
            // anchor at all, which a fixed threshold stated the long way round.
            Assert.True(
                field.Maximum >= before + 3m,
                $"The elevation is still capped at {field.Maximum} dB after the driver " +
                $"carrying it moved to the bottom of the chain (was {before} dB).");
        });
    }

    [Fact]
    public void Apply_ReturnsOneProposalPerChannel_InTheOrderTheyWereHandedIn()
    {
        // The panel writes back by position, so proposals come out in INPUT order.
        IReadOnlyList<AutoSetupWizardChannel> channels = ReferenceCar();

        IReadOnlyList<CrossoverProposal> proposals = Apply(channels);

        Assert.Equal(channels.Count, proposals.Count);
        Assert.All(proposals, Assert.NotNull);
        Assert.Equal(CrossoverKind.HighPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.BandPass, proposals[2].Kind);
    }

    [Fact]
    public void Apply_CrossesTheFrontChainThroughBothSubwoofers()
    {
        IReadOnlyList<CrossoverProposal> proposals = Apply(ReferenceCar());

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
        IReadOnlyList<CrossoverProposal> proposals = Apply(ReferenceCar());

        foreach (int index in new[] { 3, 4 })
        {
            Assert.Equal(CrossoverKind.HighPass, proposals[index].Kind);
            Assert.Null(proposals[index].LowPassEdge);
            Assert.NotNull(proposals[index].HighPassEdge);
        }

        foreach (int index in new[] { 3, 4 })
        {
            double measured = CrossoverAutoSetup
                .EstimateBand(ReferenceCar()[index].MagnitudeDb).LowHz;
            Assert.InRange(
                proposals[index].HighPassEdge!.Value.FrequencyHz,
                measured * 1.8,
                measured * 2.3);
        }

        // The rear's 200 Hz protective corner coincides with the mid handover; exclusion is asserted by chain pairing, not by frequency.
    }

    [Fact]
    public void Apply_CutsALoudRearOntoTheFrontStage()
    {
        IReadOnlyList<CrossoverProposal> proposals = Apply(ReferenceCar());

        Assert.InRange(proposals[3].GainDb, -7.5, -4.5);
    }

    [Fact]
    public void Apply_LeavesAQuietGroupWhereItIs()
    {
        List<AutoSetupWizardChannel> channels = ReferenceCar().ToList();
        channels[3] = Channel(
            "D rear", VirtualCrossoverAlignmentStage.Rear, 120, 15_000, levelDb: -6);

        IReadOnlyList<CrossoverProposal> proposals = Apply(channels);

        Assert.Equal(0, proposals[3].GainDb);
    }

    [Fact]
    public void Apply_WithNoRearOrCentre_IsOneGroupAndOneChain()
    {
        List<AutoSetupWizardChannel> channels = ReferenceCar()
            .Where(channel => channel.Group == VirtualCrossoverAlignmentStage.FrontChain)
            .ToList();

        IReadOnlyList<CrossoverProposal> proposals = Apply(channels);

        Assert.Equal(5, proposals.Count);
        Assert.Equal(
            4,
            proposals.Count(proposal => proposal.LowPassEdge is not null));
        Assert.Equal(
            4,
            proposals.Count(proposal => proposal.HighPassEdge is not null));
    }
}
