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
        public FakeChannel(string name, int sampleRate = 48_000)
        {
            Name = name;
            SampleRate = sampleRate;
        }

        public string Name { get; }
        public int SampleRate { get; }
        public int ProcessorSampleRate => SampleRate;
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
                    channel, Impulse(64 + i), 48_000, 48_000, DspChannelChain.Identity))
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

    // The crop is sized by TIME: after the 1/8 pre-peak reserve it must
    // still hold the longest window the band sizing can ask for (the 350 ms
    // clamp) plus the channels' arrival/delay spread. The base 65_536 stays
    // EXACT at the archived fleet's 48/96 kHz — results there may not move —
    // and doubles at the rates where the fixed length used to truncate a
    // sub junction's window.
    [Theory]
    [InlineData(44_100, 65_536)]
    [InlineData(48_000, 65_536)]
    [InlineData(96_000, 65_536)]
    [InlineData(176_400, 131_072)]
    [InlineData(192_000, 131_072)]
    [InlineData(352_800, 262_144)]
    [InlineData(384_000, 262_144)]
    public void SearchCrop_HoldsTheLongestAlignmentWindowAtEveryRate(
        int sampleRate, int expectedLength)
    {
        int length = AlignmentReprocessor.SearchCropLength(sampleRate);
        int prePeak = AlignmentReprocessor.SearchCropPrePeakSamples(sampleRate);

        Assert.Equal(expectedLength, length);
        Assert.Equal(length / 8, prePeak);
        double afterReserveMs = (length - prePeak) * 1_000.0 / sampleRate;
        Assert.True(
            afterReserveMs >=
                VirtualCrossoverAnalysis.MaximumAlignmentGateMs + 175.0,
            $"only {afterReserveMs:0} ms left after the pre-peak reserve " +
            $"at {sampleRate} Hz");
    }

    // The regression the sizing exists for, end to end through the
    // production constructor: at 384 kHz a front cropped to the pre-peak
    // reserve must keep the full 350 ms window clamp of MEASURED record
    // after it. The old fixed crop left 149 ms there — a 33 Hz junction's
    // 262 ms window read synthesized filter tail instead of the room.
    [Fact]
    public void Reprocess_At384kHz_TheCropHoldsAFullLowBandWindow()
    {
        const int Rate = 384_000;
        const int PeakSample = 100_000;
        var source = new Complex[524_288];
        source[PeakSample] = Complex.One;
        var channel = new FakeChannel("SUB", Rate);
        var reprocessor = new AlignmentReprocessor(
            [new AlignmentReprocessInput(
                channel, source, Rate, Rate, DspChannelChain.Identity)]);

        AlignmentSnapshot snapshot = reprocessor.Reprocess(NoOverrides)[0];

        int windowSamples = (int)Math.Round(
            VirtualCrossoverAnalysis.MaximumAlignmentGateMs / 1_000.0 * Rate);
        // The MEASURED span inside the processed record is the crop's length
        // (ApplyChain pads beyond it with synthesized filter tail, which no
        // window may mistake for the room) — the valid range is what says so.
        Assert.Equal(
            AlignmentReprocessor.SearchCropLength(Rate),
            snapshot.ValidRange.EndSample);
        Assert.Equal(
            AlignmentReprocessor.SearchCropPrePeakSamples(Rate),
            snapshot.PeakIndex);
        Assert.True(
            snapshot.ValidRange.EndSample - snapshot.PeakIndex >= windowSamples,
            $"{snapshot.ValidRange.EndSample - snapshot.PeakIndex} measured " +
            $"samples after the front cannot hold a {windowSamples}-sample window");
        // The discriminating figure: the fixed base crop could not.
        Assert.True(
            AlignmentReprocessor.BaseSearchCropLength -
                AlignmentReprocessor.BaseSearchCropLength / 8 < windowSamples);
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
