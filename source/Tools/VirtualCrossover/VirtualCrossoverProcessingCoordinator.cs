using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Owns Virtual DSP redraw revisions, processed-response caching and background
/// scheduling. The panel supplies a UI-thread snapshot and applies a result only
/// when this coordinator confirms that the snapshot is still current.
/// </summary>
internal sealed class VirtualCrossoverProcessingCoordinator : IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<ProcessingSlotId, CacheEntry> cache = new();
    private readonly Func<VirtualCrossoverSourceSnapshot, DspChannelChain, int,
        CancellationToken, Complex[]> processChannel;
    private CancellationTokenSource revisionCancellation = new();
    private long revision;
    private bool disposed;

    public VirtualCrossoverProcessingCoordinator()
        : this(ProcessChannel)
    {
    }

    internal VirtualCrossoverProcessingCoordinator(
        Func<VirtualCrossoverSourceSnapshot, DspChannelChain, int,
            CancellationToken, Complex[]> processChannel)
    {
        this.processChannel = processChannel ?? throw new ArgumentNullException(nameof(processChannel));
    }

    public long CurrentRevision
    {
        get
        {
            lock (sync)
            {
                return revision;
            }
        }
    }

    /// <summary>
    /// Makes the current computation stale and requests cancellation. The DSP
    /// primitive itself is not interruptible, so a running FFT may finish, but
    /// its result can no longer enter the cache or reach the view.
    /// </summary>
    public long Invalidate()
    {
        CancellationTokenSource revisionToCancel;
        long newRevision;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            newRevision = ++revision;
            revisionToCancel = revisionCancellation;
            revisionCancellation = new CancellationTokenSource();
        }
        Cancel(revisionToCancel);
        revisionToCancel.Dispose();
        return newRevision;
    }

    public bool IsCurrent(long candidateRevision)
    {
        lock (sync)
        {
            return !disposed && candidateRevision == revision;
        }
    }

    public async Task<VirtualCrossoverRenderResult?> ProcessAsync(
        VirtualCrossoverProcessingSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        CancellationTokenSource processingCancellation;
        var results = new VirtualCrossoverProcessedChannel?[snapshot.Channels.Count];
        var misses = new List<PendingChannel>();
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (snapshot.Revision != revision)
            {
                return null;
            }

            processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                revisionCancellation.Token);

            for (int index = 0; index < snapshot.Channels.Count; index++)
            {
                VirtualCrossoverChannelSnapshot channel = snapshot.Channels[index];
                var key = new CacheKey(channel.Source, channel.SampleRate, channel.Chain);
                if (cache.TryGetValue(channel.SlotId, out CacheEntry? entry) &&
                    entry.Key.Equals(key))
                {
                    results[index] = new VirtualCrossoverProcessedChannel(
                        channel.Id,
                        entry.ImpulseResponse,
                        entry.PeakIndex,
                        VirtualCrossoverAnalysis.ChainValidRange(
                            channel.Source.SampleCount,
                            channel.Chain,
                            channel.SampleRate,
                            entry.ImpulseResponse.Length));
                }
                else
                {
                    misses.Add(new PendingChannel(index, channel, key));
                }
            }
        }
        try
        {
            if (misses.Count > 0)
            {
                await Task.Run(() =>
                {
                    // Tracy zones live here, on the worker threads, because a
                    // zone must begin and end synchronously on one thread — the
                    // panel's async callers cannot carry one across their
                    // awaits. The outer zone times the whole batch on the
                    // scheduling worker; the per-channel zones time each
                    // chain's FFTs on whichever pool thread runs it.
                    using var _ = AppProfiler.Zone("VirtualDSP.ProcessChannels");
                    Parallel.For(
                        0,
                        misses.Count,
                        new ParallelOptions
                        {
                            CancellationToken = processingCancellation.Token
                        },
                        index =>
                        {
                            using var __ = AppProfiler.Zone("VirtualDSP.ProcessChannel");
                            PendingChannel pending = misses[index];
                            Complex[] response = processChannel(
                                pending.Channel.Source,
                                pending.Channel.Chain,
                                pending.Channel.SampleRate,
                                processingCancellation.Token);
                            processingCancellation.Token.ThrowIfCancellationRequested();
                            results[pending.ResultIndex] = new VirtualCrossoverProcessedChannel(
                                pending.Channel.Id,
                                response,
                                VirtualCrossoverAnalysis.FindPeakIndex(response),
                                VirtualCrossoverAnalysis.ChainValidRange(
                                    pending.Channel.Source.SampleCount,
                                    pending.Channel.Chain,
                                    pending.Channel.SampleRate,
                                    response.Length));
                        });
                }, processingCancellation.Token);
            }

            lock (sync)
            {
                if (disposed || snapshot.Revision != revision ||
                    processingCancellation.IsCancellationRequested)
                {
                    return null;
                }

                foreach (PendingChannel pending in misses)
                {
                    VirtualCrossoverProcessedChannel result = results[pending.ResultIndex]!;
                    cache[pending.Channel.SlotId] = new CacheEntry(
                        pending.Key,
                        result.ImpulseResponse,
                        result.PeakIndex);
                }

                return new VirtualCrossoverRenderResult(
                    snapshot.Revision,
                    results.Select(result => result!).ToArray());
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            processingCancellation.Dispose();
        }
    }

    public async Task<T?> RunAuxiliaryAsync<T>(
        long candidateRevision,
        Func<CancellationToken, T> operation,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(operation);

        CancellationTokenSource linked;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (candidateRevision != revision)
            {
                return null;
            }
            linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                revisionCancellation.Token);
        }

        using (linked)
        {
            try
            {
                T result = await Task.Run(() => operation(linked.Token), linked.Token);
                return IsCurrent(candidateRevision) ? result : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
    }

    public void Dispose()
    {
        CancellationTokenSource revisionToCancel;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cache.Clear();
            revisionToCancel = revisionCancellation;
        }
        Cancel(revisionToCancel);
        revisionToCancel.Dispose();
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation that owned this source completed between the state
            // transition and the out-of-lock cancellation request.
        }
    }

    private static Complex[] ProcessChannel(
        VirtualCrossoverSourceSnapshot source,
        DspChannelChain chain,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Complex[] response = source.Apply(chain, sampleRate);
        cancellationToken.ThrowIfCancellationRequested();
        return response;
    }

    private sealed record PendingChannel(
        int ResultIndex,
        VirtualCrossoverChannelSnapshot Channel,
        CacheKey Key);

    private sealed record CacheEntry(
        CacheKey Key,
        Complex[] ImpulseResponse,
        int PeakIndex);

    private sealed class CacheKey : IEquatable<CacheKey>
    {
        private readonly VirtualCrossoverSourceSnapshot source;
        private readonly int sampleRate;
        private readonly DspChannelChainCacheKey chain;

        public CacheKey(
            VirtualCrossoverSourceSnapshot source,
            int sampleRate,
            DspChannelChain chain)
        {
            this.source = source;
            this.sampleRate = sampleRate;
            this.chain = new DspChannelChainCacheKey(chain);
        }

        public bool Equals(CacheKey? other) =>
            other != null &&
            ReferenceEquals(source, other.source) &&
            sampleRate == other.sampleRate &&
            chain.Equals(other.chain);

        public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(source, sampleRate, chain);
        }
    }
}

internal sealed class DspChannelChainCacheKey : IEquatable<DspChannelChainCacheKey>
{
    private readonly double gainDb;
    private readonly double delayMs;
    private readonly bool invertPolarity;
    private readonly CrossoverSpec? crossover;
    private readonly AllPassSpec? allPass;
    private readonly double peqPreampDb;
    private readonly PeqBand[] peqBands;

    // Every stage of the chain must be represented here. This key exists only because
    // EqualizationCurve is a plain class with reference equality, so it cannot simply
    // defer to the record's own equality — which means each new stage has to be added
    // by hand, and one that is forgotten does not fail to compile: it just makes the
    // coordinator serve a stale render forever.
    public DspChannelChainCacheKey(DspChannelChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        gainDb = chain.GainDb;
        delayMs = chain.DelayMs;
        invertPolarity = chain.InvertPolarity;
        crossover = chain.Crossover;
        allPass = chain.AllPass;
        peqPreampDb = chain.Peq?.PreampDb ?? 0;
        peqBands = chain.Peq?.Bands.ToArray() ?? Array.Empty<PeqBand>();
    }

    public bool Equals(DspChannelChainCacheKey? other) =>
        other != null &&
        gainDb == other.gainDb &&
        delayMs == other.delayMs &&
        invertPolarity == other.invertPolarity &&
        EqualityComparer<CrossoverSpec?>.Default.Equals(crossover, other.crossover) &&
        EqualityComparer<AllPassSpec?>.Default.Equals(allPass, other.allPass) &&
        peqPreampDb == other.peqPreampDb &&
        peqBands.SequenceEqual(other.peqBands);

    public override bool Equals(object? obj) =>
        obj is DspChannelChainCacheKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(gainDb);
        hash.Add(delayMs);
        hash.Add(invertPolarity);
        hash.Add(crossover);
        hash.Add(allPass);
        hash.Add(peqPreampDb);
        foreach (PeqBand band in peqBands)
        {
            hash.Add(band);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Write-once source owned by the processing layer. Construction copies the
/// panel's measurement array once, when the source is loaded, so background
/// work never observes later mutation of panel-owned data — and keeps only the
/// head of it (see <see cref="RenderCropLength"/>).
/// </summary>
internal sealed class VirtualCrossoverSourceSnapshot
{
    /// <summary>
    /// How much of a measured transfer IR the chain is run over.
    /// <para>
    /// A sweep writes the whole record — 524288 samples, ~12 s, of which the last three
    /// quarters sit at the noise floor (measured: −67 dBFS against the peak) while the
    /// arrival lands inside the first thousand. Running the chain over all of it costs a
    /// 1048576-point FFT per side: the length is exactly 2^19, so ANY filter-tail padding
    /// tips <c>NextPowerOfTwo</c> to 2^20 — 82 ms and 16 MB per side, against 10 ms and
    /// 2 MB over the head alone.
    /// </para>
    /// <para>
    /// Nothing downstream reads past it: the magnitude window's analysis length is clamped
    /// to 32768 by <c>GetOversampledLength</c>, and the phase gate is a few hundred samples
    /// zero-padded to its own fixed FFT. Verified against three real cabin measurements —
    /// the magnitude and phase curves come out identical to 0.00000 dB and 0.00000°.
    /// </para>
    /// </summary>
    private const int RenderCropLength = 65_536;

    /// <summary>
    /// The most any curve reads after the arrival — the magnitude's clamped analysis
    /// length. A measurement whose arrival sits so late that this would not fit keeps its
    /// full length rather than being quietly cut short.
    /// </summary>
    private const int RenderCropPostPeakSamples = 32_768;

    private readonly Complex[] impulseResponse;

    public VirtualCrossoverSourceSnapshot(Complex[] impulseResponse)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException("The impulse response is empty.", nameof(impulseResponse));
        }
        this.impulseResponse = TakeHead(impulseResponse);
    }

    public Complex[] Apply(DspChannelChain chain, int sampleRate) =>
        VirtualCrossoverAnalysis.ApplyChain(impulseResponse, chain, sampleRate);

    /// <summary>
    /// The source measurement's length: with a processed response's length
    /// and its chain, enough to recover the ApplyChain valid range without
    /// re-running the chain (see
    /// <see cref="VirtualCrossoverAnalysis.ChainValidRange"/>) — the cache
    /// keeps processed arrays only.
    /// </summary>
    public int SampleCount => impulseResponse.Length;

    // Truncation from sample 0 — deliberately NOT a window centred on the arrival. Every
    // channel keeps its own peak index, the channels keep their relative timing, and the
    // user's absolute gate offset still points where they put it. A per-channel crop
    // offset breaks all three at once, which is why the auto-delay search has to share one
    // offset across channels (VirtualCrossoverAnalysis.CropSharedDirectSoundWindow);
    // starting at 0 sidesteps the question entirely.
    private static Complex[] TakeHead(Complex[] impulseResponse)
    {
        if (impulseResponse.Length <= RenderCropLength)
        {
            return impulseResponse.ToArray();
        }

        int peakIndex = VirtualCrossoverAnalysis.FindPeakIndex(impulseResponse);
        if (peakIndex + RenderCropPostPeakSamples > RenderCropLength)
        {
            return impulseResponse.ToArray();
        }

        var head = new Complex[RenderCropLength];
        Array.Copy(impulseResponse, head, RenderCropLength);
        return head;
    }
}

internal sealed class VirtualCrossoverChannelSnapshot
{
    public VirtualCrossoverChannelSnapshot(
        int id,
        VirtualCrossoverSourceSnapshot source,
        int sampleRate,
        DspChannelChain chain)
        : this(id, new ProcessingSlotId(id, false), source, sampleRate, chain)
    {
    }

    public VirtualCrossoverChannelSnapshot(
        int id,
        ProcessingSlotId slotId,
        VirtualCrossoverSourceSnapshot source,
        int sampleRate,
        DspChannelChain chain)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chain);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        Id = id;
        SlotId = slotId;
        Source = source;
        SampleRate = sampleRate;
        // Only the PEQ needs detaching — its band list is mutable and the UI thread may
        // edit it while this snapshot is processed in the background. Everything else is
        // immutable, so `with` carries it across untouched. Copying member by member (as
        // this once did) silently drops any stage the copy forgets, and an optional
        // record parameter means the compiler never complains: that is exactly how the
        // all-pass came to be a no-op on the whole processed-channel path.
        Chain = chain.Peq == null
            ? chain
            : chain with { Peq = new EqualizationCurve(chain.Peq.Bands, chain.Peq.PreampDb) };
    }

    public int Id { get; }
    public ProcessingSlotId SlotId { get; }
    public VirtualCrossoverSourceSnapshot Source { get; }
    public int SampleRate { get; }
    public DspChannelChain Chain { get; }
}

internal readonly record struct ProcessingSlotId(int ChannelIndex, bool RightSide);

internal sealed class VirtualCrossoverProcessingSnapshot
{
    private readonly VirtualCrossoverChannelSnapshot[] channels;

    public VirtualCrossoverProcessingSnapshot(
        long revision,
        IEnumerable<VirtualCrossoverChannelSnapshot> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        Revision = revision;
        this.channels = channels.ToArray();
    }

    public long Revision { get; }
    public IReadOnlyList<VirtualCrossoverChannelSnapshot> Channels => channels;
}

internal sealed record VirtualCrossoverProcessedChannel(
    int Id,
    Complex[] ImpulseResponse,
    int PeakIndex,
    ValidSampleRange ValidRange);

internal sealed record VirtualCrossoverRenderResult(
    long Revision,
    IReadOnlyList<VirtualCrossoverProcessedChannel> Channels);
