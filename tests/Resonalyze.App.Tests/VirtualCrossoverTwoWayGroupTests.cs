using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// A later stage whose group is a two-way of its own — a two-way rear fill, a
/// two-way centre. Its drivers cross each other, so the engine walks that
/// junction before the group is placed, and the result it hands back follows
/// the engine's own sparse-map contract.
/// </summary>
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
    public void SettleWithinGroup_HandsBackTheEnginesSparseMapAndComposesWithoutThrowing()
    {
        // The crash this pins: the engine's override map has NO entry for the
        // channel it chose as the group's reference, because absence means
        // "nothing proposed". Copying the map onto the run with an indexer threw
        // the moment a later group had two drivers — which is exactly the case
        // the multi-way fix had just introduced, one function away from the
        // normalization pass that respects the same contract.
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
                member.Settings.ToChain()))]);
        IReadOnlyList<AlignmentSnapshot> initial = reprocessor.Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        Dictionary<IAlignmentChannel, AlignmentSnapshot> snapshots = members
            .Select((member, index) => (member, snapshot: initial[index]))
            .ToDictionary(item => (IAlignmentChannel)item.member, item => item.snapshot);

        Dictionary<IAlignmentChannel, AlignmentOverride> inner =
            VirtualCrossoverPanel.SettleWithinGroup(
                [.. members.Cast<IAlignmentChannel>()],
                member => ((VirtualCrossoverChannel)member).Settings,
                snapshots,
                reprocessor,
                new System.Text.StringBuilder());

        // The map is sparse: the engine settled the pair and named one of them
        // its reference, which carries no entry. If that ever stops being true
        // the composition below is still right, but the reason for it is gone.
        Assert.NotEmpty(inner);
        Assert.True(
            inner.Count < members.Count,
            "the engine's map is expected to omit the group's own reference");

        // The composition the crash was in. Every member must come out with an
        // override, the absent one reading as an untouched zero.
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        VirtualCrossoverPanel.ApplyInnerSettlement(members, inner, alignment);

        Assert.Equal(2, alignment.Count);
        Assert.All(members, member => Assert.True(alignment.ContainsKey(member)));

        // And the junction the engine just settled survives the copy: the two
        // drivers are still the same distance apart as the walk left them.
        double settledGap =
            alignment[tweeter].DelayMs - alignment[woofer].DelayMs;
        double engineGap =
            inner.GetValueOrDefault(tweeter).DelayMs -
            inner.GetValueOrDefault(woofer).DelayMs;
        Assert.Equal(engineGap, settledGap, 6);
        // The later driver needs the shorter delay, so the gap is negative and
        // of the order of the 0.6 ms head start it was given.
        Assert.InRange(settledGap, -1.2, -0.1);
    }

    [Fact]
    public void ApplyInnerSettlement_ReadsAnAbsentReferenceAsAnUntouchedZero()
    {
        // The same rule stated without an engine run, so the reason survives even
        // if the engine's own behaviour changes.
        var reference = new VirtualCrossoverChannel("A") { SampleRate = Rate };
        var other = new VirtualCrossoverChannel("B") { SampleRate = Rate };
        var inner = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [other] = new AlignmentOverride(1.37, true)
        };
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();

        VirtualCrossoverPanel.ApplyInnerSettlement([reference, other], inner, alignment);

        Assert.Equal(0.0, alignment[reference].DelayMs, 6);
        Assert.False(alignment[reference].InvertPolarity);
        Assert.Equal(1.37, alignment[other].DelayMs, 6);
        Assert.True(alignment[other].InvertPolarity);
    }
}
