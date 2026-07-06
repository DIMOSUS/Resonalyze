using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// Prepared frequency response of a <see cref="DspChannelChain"/>.
/// Filter coefficients and scalar gain are built once, then reused for plot
/// drawing and FFT-bin processing.
/// </summary>
public sealed class PreparedDspResponse
{
    private const int PhaseRefreshInterval = 4096;

    private readonly double linearGain;
    private readonly double delayMs;
    private readonly double delaySamples;
    private readonly int sampleRate;
    private readonly BiquadCoefficients[] sections;

    private PreparedDspResponse(
        double linearGain,
        double delayMs,
        double delaySamples,
        int sampleRate,
        BiquadCoefficients[] sections)
    {
        this.linearGain = linearGain;
        this.delayMs = delayMs;
        this.delaySamples = delaySamples;
        this.sampleRate = sampleRate;
        this.sections = sections;
    }

    public static PreparedDspResponse Create(DspChannelChain chain, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        double linearGain = Math.Pow(10.0, chain.GainDb / 20.0) *
            (chain.InvertPolarity ? -1.0 : 1.0);
        var sections = new List<BiquadCoefficients>();

        if (chain.Crossover is { Kind: not CrossoverKind.Off } crossover)
        {
            AddCrossoverSections(sections, crossover, sampleRate);
        }

        if (chain.Peq is { } peq)
        {
            linearGain *= Math.Pow(10.0, peq.PreampDb / 20.0);
            foreach (PeqBand band in peq.Bands)
            {
                if (band.IsTransparent)
                {
                    continue;
                }

                sections.Add(PeakingBiquad.Compute(band, sampleRate));
            }
        }

        return new PreparedDspResponse(
            linearGain,
            chain.DelayMs,
            chain.DelayMs * sampleRate / 1_000.0,
            sampleRate,
            sections.ToArray());
    }

    public bool IsTimeDomainScaleOnly =>
        delayMs == 0 && sections.Length == 0;

    public Complex[] ApplyTimeDomainScale(Complex[] impulseResponse, int length)
    {
        var result = new Complex[length];
        if (linearGain == 1)
        {
            Array.Copy(impulseResponse, result, impulseResponse.Length);
            return result;
        }

        for (int i = 0; i < impulseResponse.Length; i++)
        {
            result[i] = impulseResponse[i] * linearGain;
        }

        return result;
    }

    public Complex Response(double frequencyHz)
    {
        double radians = -Math.Tau * frequencyHz / sampleRate;
        Complex z1 = UnitPhasor(radians);
        Complex delay = delayMs == 0
            ? Complex.One
            : UnitPhasor(radians * delaySamples);
        return Response(z1, delay);
    }

    public void ApplyToSpectrum(Complex[] spectrum)
    {
        if (sections.Length == 0)
        {
            ApplyGainAndDelayToSpectrum(spectrum);
            return;
        }

        int length = spectrum.Length;
        int half = length / 2;
        Complex zStep = Complex.Exp(new Complex(0, -Math.Tau / length));
        Complex delayStep = GetDelayStep(length);
        Complex z1 = Complex.One;
        Complex delay = Complex.One;

        spectrum[0] *= Response(z1, delay);
        for (int i = 1; i < half; i++)
        {
            if (i % PhaseRefreshInterval == 0)
            {
                z1 = UnitPhasor(-Math.Tau * i / length);
                delay = DelayPhasor(i, length);
            }
            else
            {
                z1 *= zStep;
                delay *= delayStep;
            }

            Complex response = Response(z1, delay);
            spectrum[i] *= response;
            spectrum[length - i] *= Complex.Conjugate(response);
        }

        z1 = UnitPhasor(-Math.PI);
        delay = DelayPhasor(half, length);
        // The Nyquist bin has no conjugate partner; a real scale keeps a real
        // impulse real (the discarded imaginary part is a half-sample artifact).
        spectrum[half] *= Response(z1, delay).Real;
    }

    private Complex GetDelayStep(int length) =>
        delayMs == 0
            ? Complex.One
            : Complex.Exp(new Complex(0, -Math.Tau * delaySamples / length));

    private Complex DelayPhasor(int bin, int length) =>
        delayMs == 0
            ? Complex.One
            : UnitPhasor(-Math.Tau * delaySamples * bin / length);

    private static Complex UnitPhasor(double radians) =>
        Complex.Exp(new Complex(0, radians));

    private void ApplyGainAndDelayToSpectrum(Complex[] spectrum)
    {
        if (delayMs == 0)
        {
            for (int i = 0; i < spectrum.Length; i++)
            {
                spectrum[i] *= linearGain;
            }

            return;
        }

        int length = spectrum.Length;
        int half = length / 2;
        Complex delayStep = GetDelayStep(length);
        Complex delay = Complex.One;

        spectrum[0] *= linearGain;
        for (int i = 1; i < half; i++)
        {
            delay = i % PhaseRefreshInterval == 0
                ? DelayPhasor(i, length)
                : delay * delayStep;
            Complex response = linearGain * delay;
            spectrum[i] *= response;
            spectrum[length - i] *= Complex.Conjugate(response);
        }

        delay = DelayPhasor(half, length);
        spectrum[half] *= (linearGain * delay).Real;
    }

    private Complex Response(Complex z1, Complex delay)
    {
        Complex response = linearGain * delay;
        Complex z2 = z1 * z1;
        foreach (BiquadCoefficients section in sections)
        {
            response *= BiquadResponse.Evaluate(section, z1, z2);
        }

        return response;
    }

    private static void AddCrossoverSections(
        List<BiquadCoefficients> sections,
        CrossoverSpec spec,
        double sampleRate)
    {
        if (spec.Kind is CrossoverKind.LowPass or CrossoverKind.BandPass)
        {
            CrossoverEdge edge = spec.LowPassEdge
                ?? throw new InvalidOperationException(
                    "The crossover kind requires a low-pass edge.");
            sections.AddRange(CrossoverFilter.BuildSections(
                edge, highPass: false, sampleRate));
        }
        if (spec.Kind is CrossoverKind.HighPass or CrossoverKind.BandPass)
        {
            CrossoverEdge edge = spec.HighPassEdge
                ?? throw new InvalidOperationException(
                    "The crossover kind requires a high-pass edge.");
            sections.AddRange(CrossoverFilter.BuildSections(
                edge, highPass: true, sampleRate));
        }
    }
}
