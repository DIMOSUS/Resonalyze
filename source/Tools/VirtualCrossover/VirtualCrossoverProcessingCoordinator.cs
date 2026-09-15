using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Redraw revisions, processed-response cache and background scheduling; results apply only while their snapshot is current.
/// See docs/tech/virtual-dsp-analysis.md#processing-coordinator.</summary>
internal sealed class VirtualCrossoverProcessingCoordinator : IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<ProcessingSlotId, CacheEntry> cache = new();
    private readonly Func<VirtualCrossoverSourceSnapshot, DspChannelChain, int, int,
        CancellationToken, Complex[]?> processChannel;
    private CancellationTokenSource revisionCancellation = new();
    private long revision;
    private bool disposed;

    public VirtualCrossoverProcessingCoordinator()
        : this(ProcessChannel)
    {
    }

    internal VirtualCrossoverProcessingCoordinator(
        Func<VirtualCrossoverSourceSnapshot, DspChannelChain, int, int,
            CancellationToken, Complex[]?> processChannel)
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

    /// <summary>Makes the current computation stale. A running FFT may finish, but its result never enters the cache or view.</summary>
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
                var key = new CacheKey(
                    channel.Source,
                    channel.SampleRate,
                    channel.ProcessorSampleRate,
                    channel.Chain);
                if (cache.TryGetValue(channel.SlotId, out CacheEntry? entry) &&
                    entry.Key.Equals(key))
                {
                    results[index] = new VirtualCrossoverProcessedChannel(
                        channel.Id,
                        entry.ImpulseResponse,
                        entry.PeakIndex,
                        channel.SampleRate,
                        VirtualCrossoverAnalysis.ChainValidRange(
                            channel.Source.SampleCount,
                            channel.Chain,
                            channel.SampleRate,
                            channel.ProcessorSampleRate,
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
            // External cancellation still throws; only revision-driven cancellation is silent.
            cancellationToken.ThrowIfCancellationRequested();
            if (misses.Count > 0)
            {
                await Task.Run(() =>
                {
                    // Tracy zones must begin and end on one thread, so they live on the workers.
                    using var _ = AppProfiler.Zone("VirtualDSP.ProcessChannels");
                    // Cancelled batches Stop the loop instead of throwing across the TPL boundary; abandoned slots stay null.
                    Parallel.For(
                        0,
                        misses.Count,
                        (index, state) =>
                        {
                            using var __ = AppProfiler.Zone("VirtualDSP.ProcessChannel");
                            if (processingCancellation.IsCancellationRequested)
                            {
                                state.Stop();
                                return;
                            }

                            PendingChannel pending = misses[index];
                            Complex[]? response = processChannel(
                                pending.Channel.Source,
                                pending.Channel.Chain,
                                pending.Channel.SampleRate,
                                pending.Channel.ProcessorSampleRate,
                                processingCancellation.Token);
                            if (response == null)
                            {
                                state.Stop();
                                return;
                            }

                            results[pending.ResultIndex] = new VirtualCrossoverProcessedChannel(
                                pending.Channel.Id,
                                response,
                                VirtualCrossoverAnalysis.FindPeakIndex(response),
                                pending.Channel.SampleRate,
                                VirtualCrossoverAnalysis.ChainValidRange(
                                    pending.Channel.Source.SampleCount,
                                    pending.Channel.Chain,
                                    pending.Channel.SampleRate,
                                    pending.Channel.ProcessorSampleRate,
                                    response.Length));
                        });
                });
            }

            // Re-checked after the work: a null cannot tell external from revision-driven cancellation.
            cancellationToken.ThrowIfCancellationRequested();

            lock (sync)
            {
                if (disposed || snapshot.Revision != revision ||
                    processingCancellation.IsCancellationRequested)
                {
                    return null;
                }

                // Published only when every slot is filled.
                foreach (VirtualCrossoverProcessedChannel? result in results)
                {
                    if (result == null)
                    {
                        return null;
                    }
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
        catch (AggregateException aggregate) when (
            aggregate.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
            // A delegate that threw for the caller's token surfaces as cancellation, not an aggregate.
            cancellationToken.ThrowIfCancellationRequested();
            // Backstop: delegates may still throw for cancellation.
            return null;
        }
        finally
        {
            processingCancellation.Dispose();
        }
    }

    /// <summary>Runs one auxiliary computation; null when superseded (operations report cancellation by returning null).</summary>
    public async Task<T?> RunAuxiliaryAsync<T>(
        long candidateRevision,
        Func<CancellationToken, T?> operation,
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
                T? result = await Task.Run(() => operation(linked.Token));
                cancellationToken.ThrowIfCancellationRequested();
                return result != null && IsCurrent(candidateRevision) ? result : null;
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
        }
    }

    // Returns null on cancellation: exceptions crossing the TPL boundary stop a Just My Code debugger on every edit.
    private static Complex[]? ProcessChannel(
        VirtualCrossoverSourceSnapshot source,
        DspChannelChain chain,
        int sampleRate,
        int processorSampleRate,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        Complex[] response = source.Apply(chain, sampleRate, processorSampleRate);
        return cancellationToken.IsCancellationRequested ? null : response;
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
        private readonly int processorSampleRate;
        private readonly DspChannelChainCacheKey chain;

        public CacheKey(
            VirtualCrossoverSourceSnapshot source,
            int sampleRate,
            int processorSampleRate,
            DspChannelChain chain)
        {
            this.source = source;
            this.sampleRate = sampleRate;
            // Same chain at another processing rate is another filter.
            this.processorSampleRate = processorSampleRate;
            this.chain = new DspChannelChainCacheKey(chain);
        }

        public bool Equals(CacheKey? other) =>
            other != null &&
            ReferenceEquals(source, other.source) &&
            sampleRate == other.sampleRate &&
            processorSampleRate == other.processorSampleRate &&
            chain.Equals(other.chain);

        public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(source, sampleRate, processorSampleRate, chain);
        }
    }
}

internal sealed class DspChannelChainCacheKey : IEquatable<DspChannelChainCacheKey>
{
    private readonly double gainDb;
    private readonly double delayMs;
    private readonly bool invertPolarity;
    private readonly CrossoverSpec? crossover;
    private readonly double peqPreampDb;
    private readonly PeqBand[] peqBands;
    private readonly PhaseRotationSpec phaseRotation;
    // By reference: the same loaded instance is the same filter.
    private readonly FirFilter? fir;

    // Every chain stage must be listed by hand (EqualizationCurve has reference equality); a forgotten stage serves stale renders.
    public DspChannelChainCacheKey(DspChannelChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        gainDb = chain.GainDb;
        delayMs = chain.DelayMs;
        invertPolarity = chain.InvertPolarity;
        crossover = chain.Crossover;
        peqPreampDb = chain.Peq?.PreampDb ?? 0;
        peqBands = chain.Peq?.Bands.ToArray() ?? Array.Empty<PeqBand>();
        phaseRotation = chain.PhaseRotation;
        fir = chain.Fir;
    }

    public bool Equals(DspChannelChainCacheKey? other) =>
        other != null &&
        gainDb == other.gainDb &&
        delayMs == other.delayMs &&
        invertPolarity == other.invertPolarity &&
        EqualityComparer<CrossoverSpec?>.Default.Equals(crossover, other.crossover) &&
        peqPreampDb == other.peqPreampDb &&
        phaseRotation == other.phaseRotation &&
        ReferenceEquals(fir, other.fir) &&
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
        hash.Add(peqPreampDb);
        hash.Add(phaseRotation);
        hash.Add(fir);
        foreach (PeqBand band in peqBands)
        {
            hash.Add(band);
        }
        return hash.ToHashCode();
    }
}

/// <summary>Write-once copy of the panel's measurement, cropped to its head.</summary>
internal sealed class VirtualCrossoverSourceSnapshot
{
    /// <summary>Head of the transfer IR the chain runs over; nothing downstream reads further and a full 2^19 sweep costs a 2^20 FFT.
    /// See docs/tech/virtual-dsp-analysis.md#render-crop.</summary>
    private const int RenderCropLength = 65_536;

    /// <summary>Magnitude's clamped analysis length; a later arrival keeps the full record.</summary>
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

    public Complex[] Apply(DspChannelChain chain, int sampleRate, int processorSampleRate) =>
        VirtualCrossoverAnalysis.ApplyChain(
            impulseResponse, chain, sampleRate, processorSampleRate);

    /// <summary>The cropped measurement, for callers that must run ApplyChain over the same input (EQ Wizard handoff). Never mutate.</summary>
    public Complex[] CroppedImpulseResponse => impulseResponse;

    /// <summary>Source length, enough with the chain to recover the valid range (<see cref="VirtualCrossoverAnalysis.ChainValidRange"/>).</summary>
    public int SampleCount => impulseResponse.Length;

    // Truncated from sample 0, not around the arrival: peak indices, relative timing and the absolute gate offset all survive.
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
        int processorSampleRate,
        DspChannelChain chain)
        : this(
            id,
            new ProcessingSlotId(id, false),
            source,
            sampleRate,
            processorSampleRate,
            chain)
    {
    }

    public VirtualCrossoverChannelSnapshot(
        int id,
        ProcessingSlotId slotId,
        VirtualCrossoverSourceSnapshot source,
        int sampleRate,
        int processorSampleRate,
        DspChannelChain chain)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chain);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (processorSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processorSampleRate));
        }

        Id = id;
        SlotId = slotId;
        Source = source;
        SampleRate = sampleRate;
        ProcessorSampleRate = processorSampleRate;
        // Only the mutable PEQ is detached; `with` carries every other stage (member-wise copying once dropped a stage).
        Chain = chain.Peq == null
            ? chain
            : chain with { Peq = new EqualizationCurve(chain.Peq.Bands, chain.Peq.PreampDb) };
    }

    public int Id { get; }
    public ProcessingSlotId SlotId { get; }
    public VirtualCrossoverSourceSnapshot Source { get; }
    /// <summary>The measurement's rate.</summary>
    public int SampleRate { get; }

    /// <summary>The rate the simulated processor realizes <see cref="Chain"/> at.</summary>
    public int ProcessorSampleRate { get; }

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

// Rate the response was processed at; never read it off the live channel (see ProcessedChannel).
internal sealed record VirtualCrossoverProcessedChannel(
    int Id,
    Complex[] ImpulseResponse,
    int PeakIndex,
    int SampleRate,
    ValidSampleRange ValidRange);

internal sealed record VirtualCrossoverRenderResult(
    long Revision,
    IReadOnlyList<VirtualCrossoverProcessedChannel> Channels);
