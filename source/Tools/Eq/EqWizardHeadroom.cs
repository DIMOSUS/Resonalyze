using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The EQ Wizard's headroom: the whole chain (a handed-off channel's own stages and the bank) against full
/// scale, the Virtual DSP read-out's rule. A chain with a FIR takes tens of milliseconds, so it is read off the UI
/// thread with the latest request superseding the rest. See docs/tech/eq-auto-tuner.md#headroom.</summary>
internal sealed class EqWizardHeadroom
{
    private (DspChainResponseKey Key, double HeadroomDb)? known;
    private DspChainResponseKey? wanted;
    private bool reading;

    /// <summary>The chain the displayed bank runs in; null without a usable processor rate.</summary>
    public static DspChainResponseKey? Input(EqWizardSession session, EqualizationCurve eq) =>
        session.ProcessorSampleRateHz > 0
            ? new DspChainResponseKey(
                (session.Source?.PreviewChain ?? DspChannelChain.Identity) with { Peq = eq, DelayMs = 0 },
                session.ProcessorSampleRateHz)
            : null;

    public static double Of(DspChainResponseKey key) =>
        -DspChainPeak.InAudioBand(key.Chain, key.ProcessorSampleRate).GainDb;

    /// <summary>The headroom now, or null while a FIR chain waits for <see cref="FillAsync"/>.</summary>
    public double? Read(DspChainResponseKey key)
    {
        if (known is { } hit && hit.Key.Equals(key))
        {
            wanted = null;
            return hit.HeadroomDb;
        }

        if (key.Chain.Fir == null)
        {
            wanted = null;
            known = (key, Of(key));
            return known.Value.HeadroomDb;
        }

        wanted = key;
        return null;
    }

    /// <summary>Reads the chain <see cref="Read"/> left waiting and hands it to <paramref name="landed"/> on the caller's
    /// context while it is still the one wanted; a call while a read runs only lets that read pick up the latest.</summary>
    public async Task FillAsync(Action<double> landed)
    {
        if (reading)
        {
            return;
        }

        reading = true;
        try
        {
            while (wanted is { } key && !(known is { } hit && hit.Key.Equals(key)))
            {
                double value = await Task.Run(() => Of(key));
                known = (key, value);
                if (key.Equals(wanted))
                {
                    wanted = null;
                    landed(value);
                }
            }
        }
        finally
        {
            reading = false;
        }
    }
}
