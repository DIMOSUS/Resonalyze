using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverTwoWayGroupTests
{
    private const int Rate = 48_000;

    private static VirtualCrossoverChannel Driver(
        string name,
        CrossoverKind kind,
        double cornerHz,
        double arrivalMs)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = Rate };
        channel.Pair.Zone = VirtualCrossoverZone.Rear;
        channel.Settings.CrossoverKind = kind;
        var edge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, cornerHz, 24);
        if (kind == CrossoverKind.LowPass)
        {
            channel.Settings.LowPassEdge = edge;
        }
        else
        {
            channel.Settings.HighPassEdge = edge;
        }

        var impulse = new Complex[16_384];
        impulse[2_048 + (int)Math.Round(arrivalMs * Rate / 1_000.0)] = Complex.One;
        channel.TransferImpulseResponse = impulse;
        return channel;
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void SettleWithinGroup_HandsBackTheEnginesSparseMapAndComposesWithoutThrowing()
    {
        // The engine's override map omits its reference channel; copying it with an indexer threw for two-driver later groups.
        VirtualCrossoverChannel woofer =
            Driver("R1", CrossoverKind.LowPass, 300, arrivalMs: 0.0);
        VirtualCrossoverChannel tweeter =
            Driver("R2", CrossoverKind.HighPass, 300, arrivalMs: 0.6);
        List<VirtualCrossoverChannel> members = [woofer, tweeter];

        var reprocessor = new AlignmentReprocessor(
            [.. members.Select(member => new AlignmentReprocessInput(
                member,
                member.TransferImpulseResponse!,
                Rate,
                Rate,
                member.Settings.ToChain(member.Pair.Zone)))]);
        IReadOnlyList<AlignmentSnapshot> initial = reprocessor.Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        Dictionary<IAlignmentChannel, AlignmentSnapshot> snapshots = members
            .Select((member, index) => (member, snapshot: initial[index]))
            .ToDictionary(item => (IAlignmentChannel)item.member, item => item.snapshot);

        Dictionary<IAlignmentChannel, AlignmentOverride> inner =
            StagedGroupPlacement.SettleWithinGroup(
                members,
                snapshots,
                reprocessor,
                new System.Text.StringBuilder());

        Assert.NotEmpty(inner);
        Assert.True(
            inner.Count < members.Count,
            "the engine's map is expected to omit the group's own reference");

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        StagedGroupPlacement.ApplyInnerSettlement(members, inner, alignment);

        Assert.Equal(2, alignment.Count);
        Assert.All(members, member => Assert.True(alignment.ContainsKey(member)));

        double settledGap =
            alignment[tweeter].DelayMs - alignment[woofer].DelayMs;
        double engineGap =
            inner.GetValueOrDefault(tweeter).DelayMs -
            inner.GetValueOrDefault(woofer).DelayMs;
        Assert.Equal(engineGap, settledGap, 6);
        Assert.InRange(settledGap, -1.2, -0.1);
    }

    [Fact]
    public void ApplyInnerSettlement_ReadsAnAbsentReferenceAsAnUntouchedZero()
    {
        var reference = new VirtualCrossoverChannel("A") { SampleRate = Rate };
        var other = new VirtualCrossoverChannel("B") { SampleRate = Rate };
        var inner = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [other] = new AlignmentOverride(1.37, true)
        };
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();

        StagedGroupPlacement.ApplyInnerSettlement([reference, other], inner, alignment);

        Assert.Equal(0.0, alignment[reference].DelayMs, 6);
        Assert.False(alignment[reference].InvertPolarity);
        Assert.Equal(1.37, alignment[other].DelayMs, 6);
        Assert.True(alignment[other].InvertPolarity);
    }
}
