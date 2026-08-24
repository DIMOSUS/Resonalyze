using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// A collection that runs beside no other (xUnit honours
/// <c>DisableParallelization</c> against every other collection, not just
/// within this one). The tests below park real pool threads and wait for them
/// to meet; sharing the pool with the rest of the suite is what let a busy
/// runner turn that rendezvous into a timeout.
/// </summary>
[CollectionDefinition(ThreadPoolSensitive.Name, DisableParallelization = true)]
public sealed class ThreadPoolSensitive
{
    internal const string Name = "Thread pool sensitive";
}

[Collection(ThreadPoolSensitive.Name)]
public sealed class VirtualCrossoverProcessingCoordinatorTests
{
    // A net under the rendezvous below, not a schedule: the threads meet as
    // soon as the pool has one to give them, so anything approaching this is a
    // hang worth failing on.
    private static readonly TimeSpan RendezvousTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Holds the thread pool below its own minimum while a test parks worker
    /// threads, so its work items get threads created on demand rather than
    /// injected about one per second. The collection above is what keeps the
    /// rest of the suite off this pool; this covers what isolation cannot — a
    /// runner whose core count, and with it the pool's minimum, is smaller than
    /// the number of threads one test parks. Note that a minimum is a threshold
    /// and not an allocation: it only holds because nothing else is running.
    /// <para>
    /// Measured against this coordinator on a 16-core machine with 96 pool
    /// threads parked: the two consumers never met inside five seconds, and met
    /// in 0.5 s with this held.
    /// </para>
    /// </summary>
    private sealed class PoolHeadroom : IDisposable
    {
        private readonly int workers;
        private readonly int completionPorts;

        internal PoolHeadroom(int parking)
        {
            ThreadPool.GetMinThreads(out workers, out completionPorts);
            ThreadPool.GetMaxThreads(out int maximumWorkers, out _);
            ThreadPool.GetAvailableThreads(out int availableWorkers, out _);
            int busy = maximumWorkers - availableWorkers;
            int headroom = parking + 1;
            ThreadPool.SetMinThreads(
                Math.Min(maximumWorkers, Math.Max(workers, busy + headroom)),
                completionPorts);
        }

        public void Dispose() =>
            ThreadPool.SetMinThreads(workers, completionPorts);
    }

    [Fact]
    public void ChannelSnapshot_PreservesEveryChainStage()
    {
        // The snapshot deep-copies the chain to detach the PEQ's mutable band list from
        // the UI thread. Copying member by member silently drops any stage the copy
        // forgets — and an optional record parameter means the compiler never says a
        // word. Assert on the whole chain, so the next stage added cannot regress here.
        var chain = new DspChannelChain(
            GainDb: -3,
            DelayMs: 0.5,
            InvertPolarity: true,
            Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)),
            Peq: new EqualizationCurve([new PeqBand(1_000, 1.0, -2.0)], -1.0),
            AllPass: new AllPassSpec(AllPassType.SecondOrder, 90, 2.5));

        var snapshot = new VirtualCrossoverChannelSnapshot(
            1, new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 3, 1.0)), 48_000, chain);

        // The PEQ is deliberately a fresh instance and EqualizationCurve has no value
        // equality, so compare everything else wholesale — that is what has to survive
        // the copy, including stages added later — and the PEQ by content.
        Assert.Equal(chain with { Peq = null }, snapshot.Chain with { Peq = null });
        Assert.Equal(chain.Peq!.PreampDb, snapshot.Chain.Peq!.PreampDb);
        Assert.Equal(chain.Peq.Bands, snapshot.Chain.Peq.Bands);
        Assert.NotSame(chain.Peq, snapshot.Chain.Peq);
    }

    [Fact]
    public void SourceSnapshot_RunsTheChainOverTheHeadOfALongMeasurement()
    {
        // A sweep's transfer IR is written full length — 524288 samples, ~12 s, with the
        // arrival in the first thousand and the rest at the noise floor. That length is
        // exactly 2^19, so the filter tail tips the FFT to 2^20 and every side costs a
        // million-point transform to feed curves that read 32k samples around the arrival.
        // The snapshot keeps the head; the processed response must shrink with it.
        var full = new Complex[524_288];
        full[260] = 1.0;
        for (int i = 261; i < full.Length; i++)
        {
            full[i] = 1e-4 * Math.Sin(i * 0.01); // decay/noise nothing reads
        }

        var snapshot = new VirtualCrossoverSourceSnapshot(full);
        Complex[] processed = snapshot.Apply(
            new DspChannelChain(
                Crossover: new CrossoverSpec(
                    CrossoverKind.HighPass,
                    HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24))),
            48_000);

        // 65536 head + the filter tail, rounded up — not the 1048576 the full record forced.
        Assert.True(
            processed.Length <= 131_072,
            $"Processed length {processed.Length}: the head crop did not take.");
        // The arrival still sits where it did: truncation starts at sample 0, so no index,
        // no inter-channel timing and no absolute gate offset moves.
        Assert.InRange(VirtualCrossoverAnalysis.FindPeakIndex(processed), 200, 400);
    }

    [Fact]
    public void SourceSnapshot_KeepsAMeasurementWhoseArrivalWouldNotFitTheHead()
    {
        // The guard: an arrival late enough that the head would cut into what the curves
        // read keeps its full record rather than being quietly truncated into it.
        var late = new Complex[524_288];
        late[60_000] = 1.0;

        var snapshot = new VirtualCrossoverSourceSnapshot(late);
        Complex[] processed = snapshot.Apply(new DspChannelChain(GainDb: 1), 48_000);

        Assert.True(
            processed.Length >= 524_288,
            $"Processed length {processed.Length}: a late arrival must not be cropped away.");
    }

    [Fact]
    public void ChainCacheKey_SeesTheAllPass()
    {
        // The key is written by hand, so a stage it forgets never fails to compile — it
        // just makes the coordinator serve a stale render: the user turns the all-pass
        // and the plot does not move.
        var baseline = new DspChannelChain(
            AllPass: new AllPassSpec(AllPassType.SecondOrder, 90, 2.5));
        var key = new DspChannelChainCacheKey(baseline);

        Assert.Equal(key, new DspChannelChainCacheKey(baseline));
        Assert.NotEqual(
            key,
            new DspChannelChainCacheKey(
                baseline with { AllPass = new AllPassSpec(AllPassType.FirstOrder, 90, 2.5) }));
        Assert.NotEqual(
            key,
            new DspChannelChainCacheKey(
                baseline with { AllPass = new AllPassSpec(AllPassType.SecondOrder, 120, 2.5) }));
        Assert.NotEqual(
            key,
            new DspChannelChainCacheKey(
                baseline with { AllPass = new AllPassSpec(AllPassType.SecondOrder, 90, 1.0) }));
    }

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
        using var pool = new PoolHeadroom(parking: 2);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int processCount = 0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, _) =>
            {
                Interlocked.Increment(ref processCount);
                entered.Set();
                Assert.True(release.Wait(RendezvousTimeout));
                return source.Apply(chain, sampleRate);
            });
        var source = new VirtualCrossoverSourceSnapshot(CreateImpulse(32, 4, 1.0));
        long oldRevision = coordinator.Invalidate();
        var oldSnapshot = new VirtualCrossoverProcessingSnapshot(
            oldRevision,
            [new VirtualCrossoverChannelSnapshot(0, source, 48_000, DspChannelChain.Identity)]);

        Task<VirtualCrossoverRenderResult?> oldTask = coordinator.ProcessAsync(oldSnapshot);
        Assert.True(entered.Wait(RendezvousTimeout));
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
        using var pool = new PoolHeadroom(parking: 2);
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

        Assert.True(entered.Wait(RendezvousTimeout));
        coordinator.Invalidate();

        Assert.Null(await work);
    }

    [Fact]
    public async Task ProcessAsync_SameRevisionAllowsConcurrentConsumers()
    {
        using var pool = new PoolHeadroom(parking: 3);
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, cancellationToken) =>
            {
                entered.Signal();
                Assert.True(release.Wait(RendezvousTimeout));
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
        Assert.True(entered.Wait(RendezvousTimeout));
        release.Set();

        Assert.NotNull(await leftTask);
        Assert.NotNull(await rightTask);
    }

    [Fact]
    public async Task ProcessAsync_InvalidateCancelsAllConcurrentConsumers()
    {
        using var pool = new PoolHeadroom(parking: 3);
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, cancellationToken) =>
            {
                entered.Signal();
                Assert.True(release.Wait(RendezvousTimeout));
                cancellationToken.ThrowIfCancellationRequested();
                return source.Apply(chain, sampleRate);
            });
        long revision = coordinator.Invalidate();

        Task<VirtualCrossoverRenderResult?> leftTask = coordinator.ProcessAsync(
            CreateSnapshot(revision, 0, false, 3));
        Task<VirtualCrossoverRenderResult?> rightTask = coordinator.ProcessAsync(
            CreateSnapshot(revision, 0, true, 11));
        Assert.True(entered.Wait(RendezvousTimeout));
        coordinator.Invalidate();
        release.Set();

        Assert.Null(await leftTask);
        Assert.Null(await rightTask);
    }

    [Fact]
    public async Task ProcessAsync_CancellationReportedByNullDropsTheRenderSilently()
    {
        // The production delegate reports a superseded render by RETURNING
        // NULL rather than throwing (see ProcessChannel): every delay edit
        // supersedes one, and an exception thrown out of the parallel body
        // stops a Just My Code debugger even though this method catches it.
        // The render must be dropped just as thoroughly, and no exception may
        // escape or be raised on the way.
        using var pool = new PoolHeadroom(parking: 3);
        using var entered = new CountdownEvent(1);
        using var release = new ManualResetEventSlim();
        int thrown = 0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, cancellationToken) =>
            {
                entered.Signal();
                Assert.True(release.Wait(RendezvousTimeout));
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                Interlocked.Increment(ref thrown);
                return source.Apply(chain, sampleRate);
            });
        long revision = coordinator.Invalidate();

        Task<VirtualCrossoverRenderResult?> render = coordinator.ProcessAsync(
            CreateSnapshot(revision, 0, false, 3));
        Assert.True(entered.Wait(RendezvousTimeout));
        coordinator.Invalidate();
        release.Set();

        Assert.Null(await render);
        Assert.Equal(0, thrown);

        // ...and nothing partial was committed: the next render for the same
        // channel recomputes rather than serving a cached half-result.
        release.Reset();
        entered.Reset(1);
        long next = coordinator.CurrentRevision;
        Task<VirtualCrossoverRenderResult?> second = coordinator.ProcessAsync(
            CreateSnapshot(next, 0, false, 3));
        Assert.True(entered.Wait(RendezvousTimeout));
        release.Set();

        VirtualCrossoverRenderResult? result = await second;
        Assert.NotNull(result);
        Assert.Equal(1, thrown);
        Assert.InRange(result.Channels[0].ImpulseResponse[3].Real, 0.999, 1.001);
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
    public async Task ProcessAsync_ExternalCancellationDuringTheWorkIsPropagated()
    {
        // The discriminating case: cancelling BEFORE the call is caught by the
        // entry guard, so a token cancelled mid-computation is what actually
        // tests the contract. The silent path cannot tell an external
        // cancellation from a revision one — the delegate returns null for
        // both — so without an explicit re-check this call would quietly
        // answer null and the caller would never learn its own token fired.
        using var pool = new PoolHeadroom(parking: 3);
        using var entered = new CountdownEvent(1);
        using var release = new ManualResetEventSlim();
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, cancellationToken) =>
            {
                entered.Signal();
                Assert.True(release.Wait(RendezvousTimeout));
                return cancellationToken.IsCancellationRequested
                    ? null
                    : source.Apply(chain, sampleRate);
            });
        long liveRevision = coordinator.Invalidate();
        using var midFlight = new CancellationTokenSource();

        Task<VirtualCrossoverRenderResult?> render = coordinator.ProcessAsync(
            CreateSnapshot(liveRevision, 0, false, 3), midFlight.Token);
        Assert.True(entered.Wait(RendezvousTimeout));
        midFlight.Cancel();
        release.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render);
    }

    [Fact]
    public async Task RunAuxiliaryAsync_ExternalCancellationDuringTheWorkIsPropagated()
    {
        // Same contract on the auxiliary path, where an operation following
        // the new convention also reports cancellation by returning null.
        using var pool = new PoolHeadroom(parking: 3);
        using var entered = new CountdownEvent(1);
        using var release = new ManualResetEventSlim();
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        long revision = coordinator.Invalidate();
        using var midFlight = new CancellationTokenSource();

        Task<object?> work = coordinator.RunAuxiliaryAsync<object>(
            revision,
            token =>
            {
                entered.Signal();
                Assert.True(release.Wait(RendezvousTimeout));
                return token.IsCancellationRequested ? null : new object();
            },
            midFlight.Token);
        Assert.True(entered.Wait(RendezvousTimeout));
        midFlight.Cancel();
        release.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
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
}
