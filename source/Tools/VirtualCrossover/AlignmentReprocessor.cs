using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One channel/side's inputs to an Auto delay run, captured before the search so no live model state (processing rate included) is read mid-run.
/// Channel identity is by reference: it keys the engine's override maps.</summary>
internal sealed record AlignmentReprocessInput(
    IAlignmentChannel Channel,
    Complex[] MeasuredImpulseResponse,
    int SampleRate,
    int ProcessorSampleRate,
    DspChannelChain BaseChain);

/// <summary>Crops every IR to one shared direct-sound window and reprocesses through the engine's overrides on demand, caching unchanged chains.
/// Misses run in parallel; the cache is written on the calling thread only.</summary>
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

    /// <summary>Base search crop; kept exactly at 48/96 kHz so archived results do not move, doubled at higher rates until
    /// 8/7 · (<see cref="VirtualCrossoverAnalysis.MaximumAlignmentGateMs"/> + 175 ms spread reserve) = 600 ms fits after the 1/8 pre-peak reserve.</summary>
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

    internal static int SearchCropPrePeakSamples(int sampleRate) =>
        SearchCropLength(sampleRate) / 8;

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
        // One shared crop offset keeps inter-channel timing; the search only reads gated direct sound.
        croppedImpulseResponses = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            inputs.Select(input => input.MeasuredImpulseResponse).ToList(),
            cropLength,
            cropPrePeakSamples);
        // Chain-free response for the engine's predicted-arrival probe; independent of overrides, so outside the cache.
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

    public IReadOnlyList<IAlignmentChannel> Channels => channels;

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
                // Chain CAPTURED at construction: the probe reads it on a background thread while the panel may be edited.
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

    // Chain compared by value, so an unchanged chain hits the cache.
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
