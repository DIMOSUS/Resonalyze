using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>
/// The review of the two operations that write IIR crossover edges — setCrossover on
/// one channel, tuneJunction on both sides of two blocks — onto a side already cut by
/// a linear-phase FIR crossover. Both are allowed, as the panel allows the same chain,
/// and both have to SAY that the side ends up filtered twice: the assistant only sees
/// the kernel, and the reader should not learn it from a red button afterwards.
/// </summary>
public sealed class AgentFirCrossoverReviewTests
{
    private const string Package = "11111111-1111-1111-1111-111111111111";

    private static readonly CrossoverEdge Lr24At2000 = new(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);

    // B mid (IIR BP 80–2000) into C tweeter, which is cut by a FIR high-pass at 2 kHz
    // and has its IIR crossover off — on both sides unless told otherwise.
    private static AgentSessionSnapshot Session(bool firOnC = true)
    {
        VirtualCrossoverChannelSettings B() => new()
        {
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24),
            LowPassEdge = Lr24At2000
        };
        VirtualCrossoverChannelSettings C()
        {
            if (!firOnC)
            {
                return new VirtualCrossoverChannelSettings
                {
                    CrossoverKind = CrossoverKind.HighPass,
                    HighPassEdge = Lr24At2000
                };
            }

            var design = new FirCrossoverDesign(
                CrossoverKind.HighPass, Lr24At2000, Lr24At2000,
                FirCrossoverMethod.IirMagnitude, FirWindow.Kaiser, 8, 1_023, 96_000);
            return new VirtualCrossoverChannelSettings { Fir = design.Build(), FirDesign = design };
        }

        return new AgentSessionSnapshot(
            [
                new AgentChannelSnapshot("B", AgentChannelSide.Left, B(), true, []),
                new AgentChannelSnapshot("B", AgentChannelSide.Right, B(), true, []),
                new AgentChannelSnapshot("C", AgentChannelSide.Left, C(), true, []),
                new AgentChannelSnapshot("C", AgentChannelSide.Right, C(), true, [])
            ],
            96_000,
            50,
            Package,
            new AgentAutoDelaySettings(0.25, RightHandDrive: false, AdjustGains: false, 1.0, 15.0),
            VirtualCrossoverSpatialAverageMode.Off,
            HybridTicked: false);
    }

    private static AgentProposal Proposal(params AgentOperation[] operations) =>
        new(Package, "summary", [], [], operations, []);

    [Fact]
    public void AJunctionTune_OverAFirCrossover_IsAllowed_AndSaysItFiltersTwice()
    {
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(new TuneJunctionOperation("op-1", "", "left:B-C", null, null, null, null, null)),
            Session()).Verdicts[0];

        Assert.True(verdict.Applicable);
        Assert.Contains("already cut by a linear-phase FIR crossover", verdict.Message);
        // Both sides of the block: the tune writes the edges on both.
        Assert.Contains("C left (HP 2 kHz)", verdict.Message);
        Assert.Contains("C right (HP 2 kHz)", verdict.Message);
    }

    [Fact]
    public void AJunctionTune_OverIirCrossovers_SaysNothingAboutFir()
    {
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(new TuneJunctionOperation("op-1", "", "left:B-C", null, null, null, null, null)),
            Session(firOnC: false)).Verdicts[0];

        Assert.True(verdict.Applicable);
        Assert.DoesNotContain("FIR", verdict.Message);
    }

    [Fact]
    public void ACrossoverSet_OnAFirCutSide_IsAllowed_AndSaysItFiltersTwice()
    {
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(new SetCrossoverOperation(
                "op-1", "C:left", "",
                new AgentCrossover("Off", null, null),
                new AgentCrossover("HighPass", new AgentCrossoverEdge("LinkwitzRiley", 2_000, 24, null), null))),
            Session()).Verdicts[0];

        Assert.True(verdict.Applicable);
        Assert.Contains("already cut by a linear-phase FIR crossover (HP 2 kHz)", verdict.Message);
    }
}
