using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverProcessingPipelineTests
{
    [Fact]
    public async Task ProcessAsync_ReturnsResultsInSnapshotOrder()
    {
        Complex[] first = CreateImpulse(32, 3, 1.0);
        Complex[] second = CreateImpulse(32, 9, 0.5);
        VirtualCrossoverProcessingInput[] inputs =
        [
            new(17, first, 48_000, DspChannelChain.Identity),
            new(4, second, 48_000, new DspChannelChain(GainDb: 6))
        ];

        IReadOnlyList<VirtualCrossoverProcessingResult> results =
            await VirtualCrossoverProcessingPipeline.ProcessAsync(inputs);

        Assert.Equal([17, 4], results.Select(result => result.Id));
        Assert.Equal(3, results[0].PeakIndex);
        Assert.Equal(9, results[1].PeakIndex);
        Assert.InRange(results[1].ImpulseResponse[9].Real, 0.997, 0.999);
    }

    [Fact]
    public async Task ProcessAsync_EmptySnapshotCompletesWithoutWorkerState()
    {
        IReadOnlyList<VirtualCrossoverProcessingResult> results =
            await VirtualCrossoverProcessingPipeline.ProcessAsync(
                Array.Empty<VirtualCrossoverProcessingInput>());

        Assert.Empty(results);
    }

    private static Complex[] CreateImpulse(
        int length,
        int peakIndex,
        double amplitude)
    {
        var impulse = new Complex[length];
        impulse[peakIndex] = amplitude;
        return impulse;
    }
}
