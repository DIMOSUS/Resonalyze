using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The EQ Wizard's headroom: the whole chain (a handed-off channel's own stages and the bank) against full
/// scale, the Virtual DSP read-out's rule. See docs/tech/eq-auto-tuner.md#headroom.</summary>
internal static class EqWizardHeadroom
{
    /// <summary>The chain the displayed bank runs in; null without a usable processor rate.</summary>
    public static DspChainResponseKey? Input(EqWizardSession session, EqualizationCurve eq) =>
        session.ProcessorSampleRateHz > 0
            ? new DspChainResponseKey(
                (session.Source?.PreviewChain ?? DspChannelChain.Identity) with { Peq = eq, DelayMs = 0 },
                session.ProcessorSampleRateHz)
            : null;

    /// <summary>Read in place, for the tuning sheet.</summary>
    public static double Of(DspChainResponseKey key) =>
        -DspChainPeak.InAudioBand(key.Chain, key.ProcessorSampleRate).GainDb;

    /// <summary>The headroom now, or null while a FIR chain waits for the reader's fill; the reader keeps only this chain.</summary>
    public static double? Read(ChainHeadroomReader reader, DspChainResponseKey key)
    {
        reader.Keep([key]);
        return -reader.Peak(key)?.PeakDb;
    }
}
