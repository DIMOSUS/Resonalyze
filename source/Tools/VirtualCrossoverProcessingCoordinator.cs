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
    private readonly Dictionary<CacheKey, CacheEntry> cache = new();
    private readonly Func<VirtualCrossoverSourceSnapshot, DspChannelChain, int,
        CancellationToken, Complex[]> processChannel;
    private CancellationTokenSource? activeProcessing;
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
        CancellationTokenSource? processingToCancel;
        long newRevision;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            newRevision = ++revision;
            processingToCancel = activeProcessing;
        }
        Cancel(processingToCancel);
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
        CancellationTokenSource? previousProcessing;
        var results = new VirtualCrossoverProcessedChannel?[snapshot.Channels.Count];
        var misses = new List<PendingChannel>();
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (snapshot.Revision != revision)
            {
                return null;
            }

            previousProcessing = activeProcessing;
            processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activeProcessing = processingCancellation;

            for (int index = 0; index < snapshot.Channels.Count; index++)
            {
                VirtualCrossoverChannelSnapshot channel = snapshot.Channels[index];
                var key = new CacheKey(channel.Source, channel.SampleRate, channel.Chain);
                if (cache.TryGetValue(key, out CacheEntry? entry))
                {
                    results[index] = new VirtualCrossoverProcessedChannel(
                        channel.Id,
                        entry.ImpulseResponse,
                        entry.PeakIndex);
                }
                else
                {
                    misses.Add(new PendingChannel(index, channel, key));
                }
            }
        }
        Cancel(previousProcessing);

        try
        {
            if (misses.Count > 0)
            {
                await Task.Run(() =>
                {
                    Parallel.For(
                        0,
                        misses.Count,
                        new ParallelOptions
                        {
                            CancellationToken = processingCancellation.Token
                        },
                        index =>
                        {
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
                                VirtualCrossoverAnalysis.FindPeakIndex(response));
                        });
                }, processingCancellation.Token);
            }

            lock (sync)
            {
                if (disposed || snapshot.Revision != revision ||
                    !ReferenceEquals(activeProcessing, processingCancellation))
                {
                    return null;
                }

                foreach (PendingChannel pending in misses)
                {
                    VirtualCrossoverProcessedChannel result = results[pending.ResultIndex]!;
                    cache[pending.Key] = new CacheEntry(
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
            lock (sync)
            {
                if (ReferenceEquals(activeProcessing, processingCancellation))
                {
                    activeProcessing = null;
                }
            }
            processingCancellation.Dispose();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? processingToCancel;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            processingToCancel = activeProcessing;
            activeProcessing = null;
            cache.Clear();
        }
        Cancel(processingToCancel);
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
        Complex[] ImpulseResponse,
        int PeakIndex);

    private sealed class CacheKey : IEquatable<CacheKey>
    {
        private readonly VirtualCrossoverSourceSnapshot source;
        private readonly int sampleRate;
        private readonly double gainDb;
        private readonly double delayMs;
        private readonly bool invertPolarity;
        private readonly CrossoverSpec? crossover;
        private readonly double peqPreampDb;
        private readonly PeqBand[] peqBands;

        public CacheKey(
            VirtualCrossoverSourceSnapshot source,
            int sampleRate,
            DspChannelChain chain)
        {
            this.source = source;
            this.sampleRate = sampleRate;
            gainDb = chain.GainDb;
            delayMs = chain.DelayMs;
            invertPolarity = chain.InvertPolarity;
            crossover = chain.Crossover;
            peqPreampDb = chain.Peq?.PreampDb ?? 0;
            peqBands = chain.Peq?.Bands.ToArray() ?? Array.Empty<PeqBand>();
        }

        public bool Equals(CacheKey? other) =>
            other != null &&
            ReferenceEquals(source, other.source) &&
            sampleRate == other.sampleRate &&
            gainDb == other.gainDb &&
            delayMs == other.delayMs &&
            invertPolarity == other.invertPolarity &&
            EqualityComparer<CrossoverSpec?>.Default.Equals(crossover, other.crossover) &&
            peqPreampDb == other.peqPreampDb &&
            peqBands.SequenceEqual(other.peqBands);

        public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(source);
            hash.Add(sampleRate);
            hash.Add(gainDb);
            hash.Add(delayMs);
            hash.Add(invertPolarity);
            hash.Add(crossover);
            hash.Add(peqPreampDb);
            foreach (PeqBand band in peqBands)
            {
                hash.Add(band);
            }
            return hash.ToHashCode();
        }
    }
}

/// <summary>
/// Write-once source owned by the processing layer. Construction copies the
/// panel's measurement array once, when the source is loaded, so background
/// work never observes later mutation of panel-owned data.
/// </summary>
internal sealed class VirtualCrossoverSourceSnapshot
{
    private readonly Complex[] impulseResponse;

    public VirtualCrossoverSourceSnapshot(Complex[] impulseResponse)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException("The impulse response is empty.", nameof(impulseResponse));
        }
        this.impulseResponse = impulseResponse.ToArray();
    }

    public Complex[] Apply(DspChannelChain chain, int sampleRate) =>
        VirtualCrossoverAnalysis.ApplyChain(impulseResponse, chain, sampleRate);
}

internal sealed class VirtualCrossoverChannelSnapshot
{
    public VirtualCrossoverChannelSnapshot(
        int id,
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
        Source = source;
        SampleRate = sampleRate;
        Chain = new DspChannelChain(
            chain.GainDb,
            chain.DelayMs,
            chain.InvertPolarity,
            chain.Crossover,
            chain.Peq == null
                ? null
                : new EqualizationCurve(chain.Peq.Bands, chain.Peq.PreampDb));
    }

    public int Id { get; }
    public VirtualCrossoverSourceSnapshot Source { get; }
    public int SampleRate { get; }
    public DspChannelChain Chain { get; }
}

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
    int PeakIndex);

internal sealed record VirtualCrossoverRenderResult(
    long Revision,
    IReadOnlyList<VirtualCrossoverProcessedChannel> Channels);
