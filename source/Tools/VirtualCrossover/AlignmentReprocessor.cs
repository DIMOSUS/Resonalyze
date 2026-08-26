using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One channel/side's immutable inputs to an Auto delay run: its identity
/// (reference equality keys the engine's override maps), the measured IR to
/// search over, its sample rate, the rate the project's processor realizes filters
/// at, and its base DSP chain (gain, crossover, PEQ — the delay and polarity are
/// supplied per step as overrides). Captured before the search so the reprocessor
/// reads no live model state while it runs — the processing rate included, which the
/// user can change from the panel while a run is in flight.
/// </summary>
internal sealed record AlignmentReprocessInput(
    IAlignmentChannel Channel,
    Complex[] MeasuredImpulseResponse,
    int SampleRate,
    int ProcessorSampleRate,
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
    private readonly int[] processorSampleRates;
    private readonly DspChannelChain[] baseChains;
    private readonly Dictionary<IAlignmentChannel, CacheEntry> cache = new();
    private readonly Complex[]?[] bypassedImpulseResponses;
    private readonly ValidSampleRange[] bypassedValidRanges;

    /// <summary>
    /// The shared search crop, sized by TIME. The base length was tuned when
    /// every field rate was 48 or 96 kHz (1.4 / 0.7 s of decay); the
    /// band-sized alignment windows made the requirement explicit — after
    /// the pre-peak reserve (1/8 of the crop) the crop must still hold the
    /// longest window the band sizing can ask for
    /// (<see cref="VirtualCrossoverAnalysis.MaximumAlignmentGateMs"/>) plus
    /// the channels' arrival/delay spread (the fleet's worst measured is
    /// ~46 ms; 175 ms of reserve): 8/7 · 525 = 600 ms. At 48/96 kHz the base
    /// length already exceeds that and is kept EXACTLY, so archived results
    /// do not move; higher rates double it until the budget fits (192 kHz →
    /// 131_072, 384 kHz → 262_144 — where the fixed length left 149 ms after
    /// the reserve, less than a single sub-band window).
    /// </summary>
    internal const int BaseSearchCropLength = 65_536;
    private const double SearchCropSpreadReserveMs = 175.0;

    internal static int SearchCropLength(int sampleRate)
    {
        double requiredSamples = sampleRate / 1_000.0 *
            (VirtualCrossoverAnalysis.MaximumAlignmentGateMs +
                SearchCropSpreadReserveMs) * 8.0 / 7.0;
        int length = BaseSearchCropLength;
        while (length < requiredSamples)
        {
            length *= 2;
        }
        return length;
    }

    /// <summary>The pre-peak reserve: the crop's own 1/8, at every rate.</summary>
    internal static int SearchCropPrePeakSamples(int sampleRate) =>
        SearchCropLength(sampleRate) / 8;

    /// <summary>
    /// The production constructor: the crop sizes itself from the inputs'
    /// sample rate (see <see cref="SearchCropLength"/>), so a high-rate
    /// session cannot silently truncate the low junctions' windows.
    /// </summary>
    public AlignmentReprocessor(IReadOnlyList<AlignmentReprocessInput> inputs)
        : this(
            inputs,
            SearchCropLength(MaxSampleRate(inputs)),
            SearchCropPrePeakSamples(MaxSampleRate(inputs)))
    {
    }

    private static int MaxSampleRate(IReadOnlyList<AlignmentReprocessInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return inputs.Select(input => input.SampleRate)
            .DefaultIfEmpty(48_000)
            .Max();
    }

    public AlignmentReprocessor(
        IReadOnlyList<AlignmentReprocessInput> inputs,
        int cropLength,
        int cropPrePeakSamples)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        channels = inputs.Select(input => input.Channel).ToList();
        sampleRates = inputs.Select(input => input.SampleRate).ToArray();
        processorSampleRates = inputs
            .Select(input => input.ProcessorSampleRate)
            .ToArray();
        baseChains = inputs.Select(input => input.BaseChain).ToArray();
        // One shared crop offset for every channel keeps the inter-channel
        // timing intact; the search only reads the gated direct sound, so the
        // final delays match a full-length run at a fraction of the FFT cost.
        croppedImpulseResponses = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            inputs.Select(input => input.MeasuredImpulseResponse).ToList(),
            cropLength,
            cropPrePeakSamples);
        // The chain-free response of each channel, computed once: the
        // engine's predicted-arrival honesty probe reads it to tell a
        // crossover's own smear from a room mode the crossover steered the
        // band into (see AlignmentSnapshot.BypassedImpulseResponse). It never
        // changes with the overrides, so it stays out of the per-round cache.
        bypassedImpulseResponses = new Complex[croppedImpulseResponses.Length][];
        bypassedValidRanges = new ValidSampleRange[croppedImpulseResponses.Length];
        Parallel.For(0, croppedImpulseResponses.Length, i =>
        {
            bypassedImpulseResponses[i] = VirtualCrossoverAnalysis.ApplyChain(
                croppedImpulseResponses[i],
                DspChannelChain.Identity,
                sampleRates[i],
                processorSampleRates[i],
                out ValidSampleRange bypassedRange);
            bypassedValidRanges[i] = bypassedRange;
        });
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
                processorSampleRates[i], out ValidSampleRange validRange);
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
                results[i].ValidRange,
                // The chain CAPTURED at construction, never the live model:
                // the engine's predicted-arrival probe reads it on a
                // background thread while the user may be editing the panel.
                baseChains[i],
                bypassedImpulseResponses[i],
                bypassedValidRanges[i]))
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
