using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Peak gain of DSP chains over the audio band, remembered by response. A chain without a FIR is read at once;
/// a FIR chain takes tens of milliseconds, so it is read off the UI thread and asked for again once it lands. Used on
/// the UI thread only. See docs/tech/virtual-dsp-analysis.md#headroom.</summary>
internal sealed class ChainHeadroomReader
{
    private readonly Dictionary<DspChainResponseKey, (double PeakHz, double PeakDb)> known = [];
    private readonly HashSet<DspChainResponseKey> wanted = [];
    private bool reading;

    /// <summary>The chain's peak, or null while a FIR chain waits for <see cref="FillAsync"/>.</summary>
    public (double PeakHz, double PeakDb)? Peak(DspChainResponseKey key)
    {
        if (known.TryGetValue(key, out (double PeakHz, double PeakDb) peak))
        {
            return peak;
        }

        if (key.Chain.Fir != null)
        {
            wanted.Add(key);
            return null;
        }

        peak = DspChainPeak.InAudioBand(key.Chain, key.ProcessorSampleRate);
        known[key] = peak;
        return peak;
    }

    /// <summary>A peak already read; never reads.</summary>
    public (double PeakHz, double PeakDb)? KnownPeak(DspChainResponseKey key) =>
        known.TryGetValue(key, out (double PeakHz, double PeakDb) peak) ? peak : null;

    /// <summary>Forgets every chain but <paramref name="live"/>: an older chain can hold a replaced FIR kernel and its
    /// caches, and a waiting one no longer on screen is not worth reading.</summary>
    public void Keep(IReadOnlyCollection<DspChainResponseKey> live)
    {
        var keep = live.ToHashSet();
        foreach (DspChainResponseKey stale in known.Keys.Where(key => !keep.Contains(key)).ToList())
        {
            known.Remove(stale);
        }

        wanted.IntersectWith(keep);
    }

    public bool Waiting => wanted.Count > 0;

    /// <summary>Reads the waiting chains one at a time on a worker, calling <paramref name="landed"/> on the caller's
    /// context after each; a call while a read runs only lets that run pick up what is waiting by then.</summary>
    public async Task FillAsync(Action landed)
    {
        if (reading)
        {
            return;
        }

        reading = true;
        try
        {
            while (wanted.FirstOrDefault() is { } key)
            {
                (double PeakHz, double PeakDb) peak =
                    await Task.Run(() => DspChainPeak.InAudioBand(key.Chain, key.ProcessorSampleRate));
                // Kept only if still wanted: Keep may have dropped the chain while it was being read.
                if (wanted.Remove(key))
                {
                    known[key] = peak;
                    landed();
                }
            }
        }
        finally
        {
            reading = false;
        }
    }
}
