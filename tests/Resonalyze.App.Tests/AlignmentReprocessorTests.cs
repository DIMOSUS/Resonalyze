using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

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

        // Each call builds a fresh chain instance, so a reused result proves value-equality caching.
        IReadOnlyList<AlignmentSnapshot> first = reprocessor.Reprocess(NoOverrides);
        IReadOnlyList<AlignmentSnapshot> second = reprocessor.Reprocess(NoOverrides);

        Assert.Same(first[0].ImpulseResponse, second[0].ImpulseResponse);
        Assert.Same(first[1].ImpulseResponse, second[1].ImpulseResponse);
    }

    // The crop must hold the 350 ms window clamp plus arrival spread after the 1/8 pre-peak reserve;
    // 65 536 stays exact at 48/96 kHz so archived results do not move.
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

    // A fixed 65 536 crop leaves 149 ms at 384 kHz, so a 33 Hz junction's 262 ms window would read filter tail.
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
        // ApplyChain pads beyond the crop with synthesized tail; the valid range marks the measured span.
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

        Assert.NotSame(first[0].ImpulseResponse, moved[0].ImpulseResponse);
        Assert.Same(first[1].ImpulseResponse, moved[1].ImpulseResponse);
    }
}
