using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverProcessingCoordinatorTests
{
    [Fact]
    public void ChainCacheKey_ComparesIndependentPeqCurvesByValue()
    {
        DspChannelChain first = CreatePeqChain(-2.0, -4.0);
        DspChannelChain sameValues = CreatePeqChain(-2.0, -4.0);
        DspChannelChain changedBand = CreatePeqChain(-2.0, -3.5);
        DspChannelChain changedPreamp = CreatePeqChain(-1.5, -4.0);

        var firstKey = new DspChannelChainCacheKey(first);
        var sameKey = new DspChannelChainCacheKey(sameValues);

        Assert.Equal(firstKey, sameKey);
        Assert.Equal(firstKey.GetHashCode(), sameKey.GetHashCode());
        Assert.NotEqual(firstKey, new DspChannelChainCacheKey(changedBand));
        Assert.NotEqual(firstKey, new DspChannelChainCacheKey(changedPreamp));
    }

    [Fact]
    public void AlignmentCacheKey_ComparesIndependentPeqChainsByValue()
    {
        Complex[] source = CreateImpulse(32, 3, 1.0);
        DspChannelChain first = CreatePeqChain(-2.0, -4.0);
        DspChannelChain sameValues = CreatePeqChain(-2.0, -4.0);

        object firstKey = CreateAlignmentCacheKey(source, first);
        object sameKey = CreateAlignmentCacheKey(source, sameValues);

        Assert.Equal(firstKey, sameKey);
        Assert.Equal(firstKey.GetHashCode(), sameKey.GetHashCode());
    }

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
    public async Task ProcessAsync_CachesIndependentPhysicalSideSlots()
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
            [new VirtualCrossoverChannelSnapshot(
                0,
                new ProcessingSlotId(0, false),
                left,
                48_000,
                DspChannelChain.Identity)]);
        var rightSnapshot = new VirtualCrossoverProcessingSnapshot(
            revision,
            [new VirtualCrossoverChannelSnapshot(
                0,
                new ProcessingSlotId(0, true),
                right,
                48_000,
                DspChannelChain.Identity)]);

        await coordinator.ProcessAsync(leftSnapshot);
        await coordinator.ProcessAsync(rightSnapshot);
        VirtualCrossoverRenderResult? leftAgain = await coordinator.ProcessAsync(leftSnapshot);

        Assert.NotNull(leftAgain);
        Assert.Equal(3, leftAgain.Channels[0].PeakIndex);
        Assert.Equal(2, processCount);
    }

    [Fact]
    public async Task ProcessAsync_ReplacesOldConfigurationForSameSlot()
    {
        int processCount = 0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, _) =>
            {
                Interlocked.Increment(ref processCount);
                return source.Apply(chain, sampleRate);
            });
        var source = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 3, 1.0));
        var slot = new ProcessingSlotId(0, false);
        long revision = coordinator.Invalidate();
        var original = new VirtualCrossoverProcessingSnapshot(
            revision,
            [new VirtualCrossoverChannelSnapshot(
                0, slot, source, 48_000, DspChannelChain.Identity)]);
        var changed = new VirtualCrossoverProcessingSnapshot(
            revision,
            [new VirtualCrossoverChannelSnapshot(
                0, slot, source, 48_000, new DspChannelChain(GainDb: 6))]);

        await coordinator.ProcessAsync(original);
        await coordinator.ProcessAsync(changed);
        await coordinator.ProcessAsync(original);

        Assert.Equal(3, processCount);
    }

    [Fact]
    public async Task RunAuxiliaryAsync_InvalidateCancelsWork()
    {
        using var entered = new ManualResetEventSlim();
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        long revision = coordinator.Invalidate();
        Task<object?> work = coordinator.RunAuxiliaryAsync(
            revision,
            cancellationToken =>
            {
                entered.Set();
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
                return new object();
            });

        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        coordinator.Invalidate();

        Assert.Null(await work);
    }

    [Fact]
    public async Task ProcessAsync_SameRevisionAllowsConcurrentConsumers()
    {
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, cancellationToken) =>
            {
                entered.Signal();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                cancellationToken.ThrowIfCancellationRequested();
                return source.Apply(chain, sampleRate);
            });
        long revision = coordinator.Invalidate();
        VirtualCrossoverProcessingSnapshot left = CreateSnapshot(
            revision, 0, false, 3);
        VirtualCrossoverProcessingSnapshot right = CreateSnapshot(
            revision, 0, true, 11);

        Task<VirtualCrossoverRenderResult?> leftTask = coordinator.ProcessAsync(left);
        Task<VirtualCrossoverRenderResult?> rightTask = coordinator.ProcessAsync(right);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        release.Set();

        Assert.NotNull(await leftTask);
        Assert.NotNull(await rightTask);
    }

    [Fact]
    public async Task ProcessAsync_InvalidateCancelsAllConcurrentConsumers()
    {
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, cancellationToken) =>
            {
                entered.Signal();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                cancellationToken.ThrowIfCancellationRequested();
                return source.Apply(chain, sampleRate);
            });
        long revision = coordinator.Invalidate();

        Task<VirtualCrossoverRenderResult?> leftTask = coordinator.ProcessAsync(
            CreateSnapshot(revision, 0, false, 3));
        Task<VirtualCrossoverRenderResult?> rightTask = coordinator.ProcessAsync(
            CreateSnapshot(revision, 0, true, 11));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        coordinator.Invalidate();
        release.Set();

        Assert.Null(await leftTask);
        Assert.Null(await rightTask);
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
                ForceRevisionWithoutCancellation(
                    coordinator!,
                    coordinator!.CurrentRevision + 1);
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

    private static VirtualCrossoverProcessingSnapshot CreateSnapshot(
        long revision,
        int channelIndex,
        bool rightSide,
        int peakIndex) =>
        new(
            revision,
            [new VirtualCrossoverChannelSnapshot(
                channelIndex,
                new ProcessingSlotId(channelIndex, rightSide),
                new VirtualCrossoverSourceSnapshot(CreateImpulse(32, peakIndex, 1.0)),
                48_000,
                DspChannelChain.Identity)]);

    private static Complex[] CreateImpulse(int length, int peakIndex, double amplitude)
    {
        var impulse = new Complex[length];
        impulse[peakIndex] = amplitude;
        return impulse;
    }

    private static DspChannelChain CreatePeqChain(double preampDb, double bandGainDb) =>
        new(
            GainDb: 1.5,
            DelayMs: 2.25,
            InvertPolarity: true,
            Peq: new EqualizationCurve(
                [new PeqBand(1_000, 1.4, bandGainDb)],
                preampDb));

    private static object CreateAlignmentCacheKey(
        Complex[] source,
        DspChannelChain chain)
    {
        Type keyType = typeof(VirtualCrossoverPanel).GetNestedType(
            "AlignmentProcessingCacheKey",
            System.Reflection.BindingFlags.NonPublic)!;
        return Activator.CreateInstance(keyType, source, 48_000, chain)!;
    }
}
