using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>One channel's LTI DSP chain (gain, delay, polarity, crossover, phase control, PEQ incl. all-pass bands, FIR).
/// Multiplying a measured transfer response by <see cref="Response"/> predicts the capture; stage order does not matter.</summary>
public sealed record DspChannelChain(
    double GainDb = 0,
    double DelayMs = 0,
    bool InvertPolarity = false,
    CrossoverSpec? Crossover = null,
    EqualizationCurve? Peq = null,
    PhaseRotationSpec PhaseRotation = default,
    FirFilter? Fir = null)
{
    /// <summary>The one |GainDb| limit shared by validator, gain balance and UI.</summary>
    public const double MaximumGainDb = 60;

    public static DspChannelChain Identity { get; } = new();

    /// <summary>The low-pass edge the crossover realizes (a low- or band-pass), else null.</summary>
    public CrossoverEdge? LowPassEdge =>
        Crossover is { Kind: CrossoverKind.LowPass or CrossoverKind.BandPass } spec ? spec.LowPassEdge : null;

    /// <summary>The high-pass edge the crossover realizes (a high- or band-pass), else null.</summary>
    public CrossoverEdge? HighPassEdge =>
        Crossover is { Kind: CrossoverKind.HighPass or CrossoverKind.BandPass } spec ? spec.HighPassEdge : null;

    public Complex Response(double frequencyHz, double sampleRateHz)
    {
        double linearGain = Math.Pow(10.0, GainDb / 20.0) * (InvertPolarity ? -1.0 : 1.0);
        Complex response = linearGain * Complex.Exp(
            new Complex(0, -Math.Tau * frequencyHz * DelayMs / 1_000.0));

        if (Crossover is { Kind: not CrossoverKind.Off } crossover)
        {
            response *= CrossoverFilter.Response(crossover, frequencyHz, sampleRateHz);
        }

        if (PhaseRotationControl.Realize(PhaseRotation, sampleRateHz) is { } rotation)
        {
            response *= AllPassFilter.Response(rotation, frequencyHz, sampleRateHz);
        }

        if (Fir is { } fir)
        {
            response *= fir.Response(frequencyHz, sampleRateHz);
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
                    PeqBiquad.Compute(band, sampleRateHz),
                    frequencyHz,
                    sampleRateHz);
            }
        }

        return response;
    }
}
