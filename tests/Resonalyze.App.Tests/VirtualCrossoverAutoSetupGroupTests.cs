using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Resonalyze;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The crossover wizard driven end to end over a grouped installation: the whole
/// point of the grouping is what comes back out of Apply, and the split, the
/// chain order and the per-group levelling only meet there.
/// </summary>
public sealed class VirtualCrossoverAutoSetupGroupTests
{
    private const double SampleRate = 48_000;

    // A synthetic driver: flat inside the band, 24 dB/octave off each edge.
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

    // The reference installation's shape, handed in the panel's order rather than
    // any sensible one: three front ways, two subwoofers under them, a rear fill
    // and a centre. Both subs measure the same because they are the same driver;
    // only the corners already set on them say which plays lower.
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

    // Apply's handler is async void and, with no impulse responses to rank
    // against, finishes inside the call — the ranked path is the one that awaits.
    // Every fixture here must leave the order unambiguous (corners on the subs):
    // an ambiguous one puts up the confirmation dialog, which nothing here can
    // answer.
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

    // The order the wizard asks the panel to put its blocks into, or null when
    // the user cleared the checkbox.
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
        // Init indices, group by group: the front chain from the sub the corners
        // put lowest up to the tweeter, then the rear, then the centre. Nothing
        // like the order they were handed in, which is the point.
        Assert.Equal([6, 5, 2, 1, 0, 3, 4], ChainOrder(ReferenceCar(), reorder: true));
    }

    [Fact]
    public void Apply_AsksForNothingWhenTheUserClearedTheReorder()
    {
        // The proposal still applies; only the blocks are left alone.
        Assert.Null(ChainOrder(ReferenceCar(), reorder: false));
    }

    [Fact]
    public void MovingTheBassAnchor_MovesTheCeilingOnTheElevationWithIt()
    {
        // The elevation is measured at the chain's LOWEST bass driver, and the
        // arrows can change which one that is. Here the two subwoofers differ by
        // 8 dB: with the quiet one at the bottom there is no elevation to offer
        // and the field is capped at zero, and if that cap were read once and
        // kept, swapping them could never open it again — the user would be
        // locked out of an elevation the measurement now supports.
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

            var field = (DarkNumericUpDown)typeof(VirtualCrossoverAutoSetupDialog)
                .GetField("subElevation", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            // Not exactly zero: the quiet sub averages a hair over the reference.
            Assert.True(field.Maximum <= 1m, $"capped at {field.Maximum} dB to begin with");

            // Bring the loud sub to the bottom of the chain: it is the anchor now.
            var rows = (System.Collections.IList)typeof(VirtualCrossoverAutoSetupDialog)
                .GetField("rows", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            typeof(VirtualCrossoverAutoSetupDialog)
                .GetMethod("MoveInChain", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(dialog, [rows[1]!, -1]);

            Assert.True(
                field.Maximum >= 7m,
                $"The elevation is still capped at {field.Maximum} dB after the driver " +
                "carrying it moved to the bottom of the chain.");
        });
    }

    [Fact]
    public void Apply_ReturnsOneProposalPerChannel_InTheOrderTheyWereHandedIn()
    {
        // The dialog reorders its rows into chain order inside each group; the
        // panel writes the result back by position, so what comes out must be in
        // the INPUT order however the rows were shuffled to get there.
        IReadOnlyList<AutoSetupWizardChannel> channels = ReferenceCar();

        IReadOnlyList<CrossoverProposal> proposals = Apply(channels);

        Assert.Equal(channels.Count, proposals.Count);
        Assert.All(proposals, Assert.NotNull);
        // Index 0 is the tweeter, the top of the front chain: a high-pass and
        // nothing above it. Index 2 is the midbass, in the middle of that chain.
        Assert.Equal(CrossoverKind.HighPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.BandPass, proposals[2].Kind);
    }

    [Fact]
    public void Apply_CrossesTheFrontChainThroughBothSubwoofers()
    {
        IReadOnlyList<CrossoverProposal> proposals = Apply(ReferenceCar());

        // Chain order: rear sub (6), front sub (5), midbass (2), mid (1),
        // tweeter (0) — the two subs put in that order by their corners alone.
        int[] chain = [6, 5, 2, 1, 0];
        for (int i = 0; i + 1 < chain.Length; i++)
        {
            CrossoverEdge? lowPass = proposals[chain[i]].LowPassEdge;
            CrossoverEdge? highPass = proposals[chain[i + 1]].HighPassEdge;
            Assert.NotNull(lowPass);
            Assert.NotNull(highPass);
            Assert.Equal(lowPass!.Value.FrequencyHz, highPass!.Value.FrequencyHz, 3);
        }

        // And the bottom of the chain is the sub whose corner says it plays
        // lowest, which is not the one that came first in the input.
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

        // Their corners come from their own measured band, not from a handover:
        // an octave over where each of them starts playing.
        foreach (int index in new[] { 3, 4 })
        {
            double measured = CrossoverAutoSetup
                .EstimateBand(ReferenceCar()[index].MagnitudeDb).LowHz;
            Assert.InRange(
                proposals[index].HighPassEdge!.Value.FrequencyHz,
                measured * 1.8,
                measured * 2.3);
        }

        // What says they are not in the chain is that the chain pairs up without
        // them — asserted where the chain is walked, above — and NOT that their
        // corners differ from its junctions. Two unrelated filters are perfectly
        // free to land on the same frequency, and these two do: the rear's
        // protective corner and the midbass-to-midrange handover are both 200 Hz.
    }

    [Fact]
    public void Apply_CutsALoudRearOntoTheFrontStage()
    {
        // The rear measures 6 dB hotter than the front. Left alone it would be
        // applied at its raw level, which is not a starting point anybody wants.
        IReadOnlyList<CrossoverProposal> proposals = Apply(ReferenceCar());

        Assert.InRange(proposals[3].GainDb, -7.5, -4.5);
    }

    [Fact]
    public void Apply_LeavesAQuietGroupWhereItIs()
    {
        // Cut-only: the same rear measured 6 dB UNDER the front is not boosted up
        // to meet it.
        List<AutoSetupWizardChannel> channels = ReferenceCar().ToList();
        channels[3] = Channel(
            "D rear", VirtualCrossoverAlignmentStage.Rear, 120, 15_000, levelDb: -6);

        IReadOnlyList<CrossoverProposal> proposals = Apply(channels);

        Assert.Equal(0, proposals[3].GainDb);
    }

    [Fact]
    public void Apply_WithNoRearOrCentre_IsOneGroupAndOneChain()
    {
        // The front-only car, which is what every project was before zones: one
        // group, so nothing is levelled onto anything and the chain is the whole
        // system exactly as it always was.
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
