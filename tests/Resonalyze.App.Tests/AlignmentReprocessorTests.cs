using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Characterization tests for the shared Auto delay <see cref="AlignmentReprocessor"/>:
/// it returns snapshots in channel order, reuses a channel's processed IR while
/// its chain is unchanged (value-equal chains hit the cache) and re-FFTs only the
/// channels whose override actually moved — the behavior the single-side and
/// stereo runs used to each hand-roll.
/// </summary>
public sealed class AlignmentReprocessorTests
{
    private sealed class FakeChannel : IAlignmentChannel
    {
        public FakeChannel(string name) => Name = name;

        public string Name { get; }
        public int SampleRate => 48_000;
    }

    private static Complex[] Impulse(int peak)
    {
        var ir = new Complex[256];
        ir[peak] = Complex.One;
        return ir;
    }

    private static AlignmentReprocessor Build(params IAlignmentChannel[] channels) =>
        new(
            channels
                .Select((channel, i) => new AlignmentReprocessInput(
                    channel, Impulse(64 + i), 48_000, DspChannelChain.Identity))
                .ToList(),
            cropLength: 128,
            cropPrePeakSamples: 16);

    private static readonly Dictionary<IAlignmentChannel, AlignmentOverride> NoOverrides = new();

    [Fact]
    public void Reprocess_ReturnsProcessedSnapshotsInChannelOrder()
    {
        var a = new FakeChannel("A");
        var b = new FakeChannel("B");
        AlignmentReprocessor reprocessor = Build(a, b);

        IReadOnlyList<AlignmentSnapshot> result = reprocessor.Reprocess(NoOverrides);

        Assert.Equal([a, b], result.Select(snapshot => snapshot.Channel));
        Assert.All(result, snapshot => Assert.NotEmpty(snapshot.ImpulseResponse));
    }

    [Fact]
    public void Reprocess_ReusesCachedResult_ForValueEqualChains()
    {
        AlignmentReprocessor reprocessor = Build(new FakeChannel("A"), new FakeChannel("B"));

        // Each call rebuilds the per-channel chain from scratch (a fresh
        // DspChannelChain instance), so an identical result reference proves the
        // cache matched the chain by value, not by reference.
        IReadOnlyList<AlignmentSnapshot> first = reprocessor.Reprocess(NoOverrides);
        IReadOnlyList<AlignmentSnapshot> second = reprocessor.Reprocess(NoOverrides);

        Assert.Same(first[0].ImpulseResponse, second[0].ImpulseResponse);
        Assert.Same(first[1].ImpulseResponse, second[1].ImpulseResponse);
    }

    [Fact]
    public void Reprocess_RecomputesOnlyTheChannelWhoseOverrideChanged()
    {
        var a = new FakeChannel("A");
        var b = new FakeChannel("B");
        AlignmentReprocessor reprocessor = Build(a, b);

        IReadOnlyList<AlignmentSnapshot> first = reprocessor.Reprocess(NoOverrides);
        IReadOnlyList<AlignmentSnapshot> moved = reprocessor.Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>
            {
                [a] = new AlignmentOverride(DelayMs: 1.0, InvertPolarity: false)
            });

        // A's chain changed, so it is re-FFT'd; B is untouched and served from cache.
        Assert.NotSame(first[0].ImpulseResponse, moved[0].ImpulseResponse);
        Assert.Same(first[1].ImpulseResponse, moved[1].ImpulseResponse);
    }
}
