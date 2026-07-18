using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One channel/side's immutable inputs to an Auto delay run: its identity
/// (reference equality keys the engine's override maps), the measured IR to
/// search over, its sample rate and its base DSP chain (gain, crossover, PEQ —
/// the delay and polarity are supplied per step as overrides). Captured before
/// the search so the reprocessor reads no live model state while it runs.
/// </summary>
internal sealed record AlignmentReprocessInput(
    IAlignmentChannel Channel,
    Complex[] MeasuredImpulseResponse,
    int SampleRate,
    DspChannelChain BaseChain);

/// <summary>
/// The shared Auto delay reprocessor for the single-side and stereo runs alike.
/// It crops every channel's measured IR to one shared direct-sound window (a
/// common offset keeps the inter-channel timing intact), then reprocesses the
/// cropped IRs through the current delay/polarity overrides on demand — the
/// delegate the <see cref="AutoAlignmentEngine"/> drives. A per-channel cache
/// reuses a channel's processed IR when its chain is unchanged between junction
/// steps (only one or two channels move per step), so the FFTs shrink to the
/// crop and unchanged channels are never re-FFT'd. Cache misses run in parallel;
/// the cache is written back on the calling (engine) thread only, so the whole
/// object is used from one thread at a time.
/// </summary>
internal sealed class AlignmentReprocessor
{
    private readonly IReadOnlyList<IAlignmentChannel> channels;
    private readonly Complex[][] croppedImpulseResponses;
    private readonly int[] sampleRates;
    private readonly DspChannelChain[] baseChains;
    private readonly Dictionary<IAlignmentChannel, CacheEntry> cache = new();

    public AlignmentReprocessor(
        IReadOnlyList<AlignmentReprocessInput> inputs,
        int cropLength,
        int cropPrePeakSamples)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        channels = inputs.Select(input => input.Channel).ToList();
        sampleRates = inputs.Select(input => input.SampleRate).ToArray();
        baseChains = inputs.Select(input => input.BaseChain).ToArray();
        // One shared crop offset for every channel keeps the inter-channel
        // timing intact; the search only reads the gated direct sound, so the
        // final delays match a full-length run at a fraction of the FFT cost.
        croppedImpulseResponses = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            inputs.Select(input => input.MeasuredImpulseResponse).ToList(),
            cropLength,
            cropPrePeakSamples);
    }

    /// <summary>The channels in input (and result) order.</summary>
    public IReadOnlyList<IAlignmentChannel> Channels => channels;

    /// <summary>
    /// Reprocesses every channel through its current override (delay + polarity
    /// on top of its base chain) and returns the snapshots in channel order.
    /// Cache misses (all channels on the first call, usually one or two per
    /// cascade step afterwards) run in parallel; the cache is written back on
    /// this (caller's) thread only.
    /// </summary>
    public IReadOnlyList<AlignmentSnapshot> Reprocess(
        IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var results = new CacheEntry[channels.Count];
        var keys = new CacheKey[channels.Count];
        var chains = new DspChannelChain[channels.Count];
        var missing = new List<int>();
        for (int i = 0; i < channels.Count; i++)
        {
            IAlignmentChannel channel = channels[i];
            AlignmentOverride over = overrides.GetValueOrDefault(channel);
            chains[i] = baseChains[i] with
            {
                DelayMs = over.DelayMs,
                InvertPolarity = over.InvertPolarity
            };
            keys[i] = new CacheKey(croppedImpulseResponses[i], sampleRates[i], chains[i]);
            CacheEntry? cached = cache.GetValueOrDefault(channel);
            if (cached?.Key.Equals(keys[i]) == true)
            {
                results[i] = cached;
            }
            else
            {
                missing.Add(i);
            }
        }

        Parallel.ForEach(missing, i =>
        {
            Complex[] result = VirtualCrossoverAnalysis.ApplyChain(
                croppedImpulseResponses[i], chains[i], sampleRates[i],
                out ValidSampleRange validRange);
            results[i] = new CacheEntry(
                keys[i], result, VirtualCrossoverAnalysis.FindPeakIndex(result),
                validRange);
        });
        foreach (int i in missing)
        {
            cache[channels[i]] = results[i];
        }

        return channels
            .Select((channel, i) => new AlignmentSnapshot(
                channel,
                results[i].ImpulseResponse,
                results[i].PeakIndex,
                results[i].ValidRange))
            .ToList();
    }

    private sealed record CacheEntry(
        CacheKey Key,
        Complex[] ImpulseResponse,
        int PeakIndex,
        ValidSampleRange ValidRange);

    // Identity of a processed result: the cropped source (reference), the sample
    // rate and the chain by value (independent equal-valued PEQ chains match, so
    // an unchanged chain hits the cache). Reuses the shared DspChannelChainCacheKey.
    private sealed class CacheKey : IEquatable<CacheKey>
    {
        private readonly Complex[] source;
        private readonly int sampleRate;
        private readonly DspChannelChainCacheKey chain;

        public CacheKey(Complex[] source, int sampleRate, DspChannelChain chain)
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

        public override int GetHashCode() => HashCode.Combine(source, sampleRate, chain);
    }
}
