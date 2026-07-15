using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverProcessingCoordinatorTests
{
    [Fact]
    public async Task ProcessAsync_ReturnsResultsInSnapshotOrderAndCachesByProcessedKey()
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
    public async Task ProcessAsync_CachesBothSourcesForSameChannelId()
    {
        int processCount = 0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, _) =>
            {
                Interlocked.Increment(ref processCount);
                return source.Apply(chain, sampleRate);
            });
        var left = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 3, 1.0));
        var right = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 11, 1.0));
        long revision = coordinator.Invalidate();
        var leftSnapshot = new VirtualCrossoverProcessingSnapshot(
            revision,
            [new VirtualCrossoverChannelSnapshot(0, left, 48_000, DspChannelChain.Identity)]);
        var rightSnapshot = new VirtualCrossoverProcessingSnapshot(
            revision,
            [new VirtualCrossoverChannelSnapshot(0, right, 48_000, DspChannelChain.Identity)]);

        await coordinator.ProcessAsync(leftSnapshot);
        await coordinator.ProcessAsync(rightSnapshot);
        VirtualCrossoverRenderResult? leftAgain = await coordinator.ProcessAsync(leftSnapshot);

        Assert.NotNull(leftAgain);
        Assert.Equal(3, leftAgain.Channels[0].PeakIndex);
        Assert.Equal(2, processCount);
    }

    [Fact]
    public async Task InvalidateDuringCompletedComputation_DropsResultAtCommitGuard()
    {
        VirtualCrossoverProcessingCoordinator? coordinator = null;
        int processCount = 0;
        coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, _) =>
            {
                Interlocked.Increment(ref processCount);
                Complex[] result = source.Apply(chain, sampleRate);
                ForceRevisionWithoutCancellation(coordinator!, coordinator.CurrentRevision + 1);
                return result;
            });
        using var disposeCoordinator = coordinator;
        var source = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 4, 1.0));
        long oldRevision = coordinator.Invalidate();
        var oldSnapshot = new VirtualCrossoverProcessingSnapshot(
            oldRevision,
            [new VirtualCrossoverChannelSnapshot(0, source, 48_000, DspChannelChain.Identity)]);

        VirtualCrossoverRenderResult? stale = await coordinator.ProcessAsync(oldSnapshot);

        Assert.Null(stale);
        long currentRevision = coordinator.CurrentRevision;
        var currentSnapshot = new VirtualCrossoverProcessingSnapshot(
            currentRevision,
            [new VirtualCrossoverChannelSnapshot(0, source, 48_000, DspChannelChain.Identity)]);
        VirtualCrossoverRenderResult? current = await coordinator.ProcessAsync(currentSnapshot);

        Assert.Null(current);
        Assert.Equal(2, processCount);
    }

    [Fact]
    public async Task ChangedChainForSameChannel_InvalidatesProcessedCache()
    {
        int processCount = 0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, _) =>
            {
                Interlocked.Increment(ref processCount);
                return source.Apply(chain, sampleRate);
            });
        var source = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 5, 1.0));
        long firstRevision = coordinator.Invalidate();
        await coordinator.ProcessAsync(new VirtualCrossoverProcessingSnapshot(
            firstRevision,
            [new VirtualCrossoverChannelSnapshot(0, source, 48_000, DspChannelChain.Identity)]));
        long changedRevision = coordinator.Invalidate();

        VirtualCrossoverRenderResult? changed = await coordinator.ProcessAsync(
            new VirtualCrossoverProcessingSnapshot(
                changedRevision,
                [new VirtualCrossoverChannelSnapshot(
                    0,
                    source,
                    48_000,
                    new DspChannelChain(GainDb: 6))]));

        Assert.NotNull(changed);
        Assert.Equal(2, processCount);
        Assert.InRange(changed.Channels[0].ImpulseResponse[5].Real, 1.994, 1.997);
    }

    [Fact]
    public async Task ChangedSourceForSameChannel_InvalidatesProcessedCache()
    {
        int processCount = 0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, _) =>
            {
                Interlocked.Increment(ref processCount);
                return source.Apply(chain, sampleRate);
            });
        var left = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 3, 1.0));
        var right = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 11, 1.0));
        long leftRevision = coordinator.Invalidate();
        await coordinator.ProcessAsync(new VirtualCrossoverProcessingSnapshot(
            leftRevision,
            [new VirtualCrossoverChannelSnapshot(0, left, 48_000, DspChannelChain.Identity)]));
        long rightRevision = coordinator.Invalidate();

        VirtualCrossoverRenderResult? changed = await coordinator.ProcessAsync(
            new VirtualCrossoverProcessingSnapshot(
                rightRevision,
                [new VirtualCrossoverChannelSnapshot(0, right, 48_000, DspChannelChain.Identity)]));

        Assert.NotNull(changed);
        Assert.Equal(2, processCount);
        Assert.Equal(11, changed.Channels[0].PeakIndex);
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

    private static void ForceRevisionWithoutCancellation(
        VirtualCrossoverProcessingCoordinator coordinator,
        long revision)
    {
        System.Reflection.FieldInfo? revisionField =
            typeof(VirtualCrossoverProcessingCoordinator).GetField(
                "revision",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(revisionField);
        revisionField.SetValue(coordinator, revision);
    }

    private static Complex[] CreateImpulse(int length, int peakIndex, double amplitude)
    {
        var impulse = new Complex[length];
        impulse[peakIndex] = amplitude;
        return impulse;
    }
}
