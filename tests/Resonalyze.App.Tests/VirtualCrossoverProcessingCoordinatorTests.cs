using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverProcessingCoordinatorTests
{
    [Fact]
    public async Task ProcessAsync_ReturnsResultsInSnapshotOrderAndCachesByChannel()
    {
        int processCount = 0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, cancellationToken) =>
            {
                Interlocked.Increment(ref processCount);
                cancellationToken.ThrowIfCancellationRequested();
                return source.Apply(chain, sampleRate);
            });
        var first = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 3, 1.0));
        var second = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 9, 0.5));
        long revision = coordinator.Invalidate();
        var snapshot = new VirtualCrossoverProcessingSnapshot(
            revision,
            [
                new VirtualCrossoverChannelSnapshot(17, first, 48_000, DspChannelChain.Identity),
                new VirtualCrossoverChannelSnapshot(
                    4,
                    second,
                    48_000,
                    new DspChannelChain(GainDb: 6))
            ]);

        VirtualCrossoverRenderResult? firstRender = await coordinator.ProcessAsync(snapshot);
        VirtualCrossoverRenderResult? cachedRender = await coordinator.ProcessAsync(snapshot);

        Assert.NotNull(firstRender);
        Assert.NotNull(cachedRender);
        Assert.Equal([17, 4], firstRender.Channels.Select(result => result.Id));
        Assert.Equal(3, firstRender.Channels[0].PeakIndex);
        Assert.Equal(9, firstRender.Channels[1].PeakIndex);
        Assert.InRange(firstRender.Channels[1].ImpulseResponse[9].Real, 0.997, 0.999);
        Assert.Equal(2, processCount);
    }

    [Fact]
    public async Task Invalidate_DropsInFlightResultAndDoesNotPopulateCache()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int processCount = 0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, _) =>
            {
                Interlocked.Increment(ref processCount);
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                return source.Apply(chain, sampleRate);
            });
        var source = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 4, 1.0));
        long oldRevision = coordinator.Invalidate();
        var oldSnapshot = new VirtualCrossoverProcessingSnapshot(
            oldRevision,
            [new VirtualCrossoverChannelSnapshot(0, source, 48_000, DspChannelChain.Identity)]);

        Task<VirtualCrossoverRenderResult?> oldTask = coordinator.ProcessAsync(oldSnapshot);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        long newRevision = coordinator.Invalidate();
        release.Set();

        Assert.Null(await oldTask);

        var newSnapshot = new VirtualCrossoverProcessingSnapshot(
            newRevision,
            [new VirtualCrossoverChannelSnapshot(0, source, 48_000, DspChannelChain.Identity)]);
        VirtualCrossoverRenderResult? current = await coordinator.ProcessAsync(newSnapshot);

        Assert.NotNull(current);
        Assert.Equal(newRevision, current.Revision);
        Assert.Equal(2, processCount);
    }

    [Fact]
    public async Task SourceSnapshot_DoesNotObserveLaterMutationOfPanelArray()
    {
        Complex[] panelOwned = CreateImpulse(32, 2, 1.0);
        var source = new VirtualCrossoverSourceSnapshot(panelOwned);
        panelOwned[2] = new Complex(8, 0);
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        long revision = coordinator.Invalidate();

        VirtualCrossoverRenderResult? render = await coordinator.ProcessAsync(
            new VirtualCrossoverProcessingSnapshot(
                revision,
                [new VirtualCrossoverChannelSnapshot(
                    0,
                    source,
                    48_000,
                    DspChannelChain.Identity)]));

        Assert.NotNull(render);
        Assert.InRange(render.Channels[0].ImpulseResponse[2].Real, 0.999, 1.001);
    }

    [Fact]
    public async Task ProcessAsync_ExternalCancellationIsPropagated()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        long revision = coordinator.Invalidate();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.ProcessAsync(
                new VirtualCrossoverProcessingSnapshot(
                    revision,
                    [new VirtualCrossoverChannelSnapshot(
                        0,
                        new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 1, 1.0)),
                        48_000,
                        DspChannelChain.Identity)]),
                cancellation.Token));
    }

    [Fact]
    public async Task ProcessAsync_EmptyCurrentSnapshotReturnsEmptyResult()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        long revision = coordinator.Invalidate();

        VirtualCrossoverRenderResult? render = await coordinator.ProcessAsync(
            new VirtualCrossoverProcessingSnapshot(
                revision,
                Array.Empty<VirtualCrossoverChannelSnapshot>()));

        Assert.NotNull(render);
        Assert.Empty(render.Channels);
    }

    private static Complex[] CreateImpulse(int length, int peakIndex, double amplitude)
    {
        var impulse = new Complex[length];
        impulse[peakIndex] = amplitude;
        return impulse;
    }
}
