using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// The linear DSP chain of one virtual-crossover channel, mirroring what a DSP
/// applies before the driver: gain, delay, a polarity switch, the crossover
/// filters, an all-pass stage and a PEQ stage. Because every stage is LTI,
/// multiplying a measured transfer response by <see cref="Response"/> predicts
/// exactly what the microphone would capture after dialing these settings into the
/// hardware. Being LTI, the stages also commute: the order they are applied in here
/// does not change the response.
/// </summary>
public sealed record DspChannelChain(
    double GainDb = 0,
    double DelayMs = 0,
    bool InvertPolarity = false,
    CrossoverSpec? Crossover = null,
    EqualizationCurve? Peq = null,
    AllPassSpec? AllPass = null)
{
    public static DspChannelChain Identity { get; } = new();

    /// <summary>
    /// Complex response of the whole chain at the given frequency:
    /// gain · (±1) · e^{-jw·tau} · H_crossover · H_peq. The delay term realizes an
    /// exact fractional-sample delay; the filters are evaluated as the digital
    /// biquads a DSP would run at this sample rate.
    /// </summary>
    public Complex Response(double frequencyHz, double sampleRateHz)
    {
        double linearGain = Math.Pow(10.0, GainDb / 20.0) * (InvertPolarity ? -1.0 : 1.0);
        Complex response = linearGain * Complex.Exp(
            new Complex(0, -Math.Tau * frequencyHz * DelayMs / 1_000.0));

        if (Crossover is { Kind: not CrossoverKind.Off } crossover)
        {
            response *= CrossoverFilter.Response(crossover, frequencyHz, sampleRateHz);
        }

        // Independent of the crossover: real processors run the all-pass as its own
        // stage, so it applies even with the crossover switched off.
        if (AllPass is { Type: not AllPassType.Off } allPass)
        {
            response *= AllPassFilter.Response(allPass, frequencyHz, sampleRateHz);
        }

        if (Peq is { } peq)
        {
            response *= Math.Pow(10.0, peq.PreampDb / 20.0);
            foreach (PeqBand band in peq.Bands)
            {
                if (band.IsTransparent)
                {
                    continue;
                }

                response *= BiquadResponse.Evaluate(
                    PeakingBiquad.Compute(band, sampleRateHz),
                    frequencyHz,
                    sampleRateHz);
            }
        }

        return response;
    }
}
