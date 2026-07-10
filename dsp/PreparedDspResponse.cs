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

    /// <summary>
    /// Zero-padding (samples) needed for this chain's ringing to decay by
    /// <paramref name="targetDecayDb"/> before a circular FFT would wrap the
    /// tail into the early response. Follows the slowest pole of the biquad
    /// cascade: a 20 Hz / Q 10 peaking filter rings for ~13.8·Q/(π·f) × ln10/…
    /// hundreds of milliseconds — far past any fixed pad sized for crossovers.
    /// Clamped to [<paramref name="minSamples"/>, <paramref name="maxSamples"/>];
    /// a numerically unstable section (pole radius ≥ 1) gets the maximum.
    /// </summary>
    public int RequiredTailSamples(double targetDecayDb, int minSamples, int maxSamples)
    {
        double maxRadius = 0.0;
        foreach (BiquadCoefficients section in sections)
        {
            double discriminant = section.A1 * section.A1 - 4.0 * section.A2;
            double radius;
            if (discriminant < 0.0)
            {
                // Complex conjugate poles: |p|² = A2.
                radius = Math.Sqrt(Math.Max(0.0, section.A2));
            }
            else
            {
                double root = Math.Sqrt(discriminant);
                radius = Math.Max(
                    Math.Abs((-section.A1 + root) * 0.5),
                    Math.Abs((-section.A1 - root) * 0.5));
            }

            maxRadius = Math.Max(maxRadius, radius);
        }

        if (maxRadius >= 1.0)
        {
            return maxSamples;
        }
        if (maxRadius <= 0.0)
        {
            return minSamples;
        }

        double required = Math.Log(
            Math.Pow(10.0, -Math.Abs(targetDecayDb) / 20.0)) / Math.Log(maxRadius);
        return (int)Math.Clamp(Math.Ceiling(required), minSamples, maxSamples);
    }

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

    /// <summary>
    /// Filter group delay τ_g = -dφ/dω at <paramref name="frequencyHz"/>, in
    /// milliseconds, read from the complex response by a central difference
    /// (-Im(H'/H) / 2π). Working from the complex response avoids phase
    /// unwrapping, which a coarse log grid could alias at a steep crossover.
    /// </summary>
    public double GroupDelayMs(double frequencyHz)
    {
        double delta = Math.Max(frequencyHz * 1e-3, 1e-6);
        double lowFrequency = Math.Max(frequencyHz - delta, 1e-3);
        double highFrequency = frequencyHz + delta;
        Complex low = Response(lowFrequency);
        Complex high = Response(highFrequency);
        Complex center = Response(frequencyHz);
        if (center.Magnitude < 1e-20)
        {
            return 0;
        }

        Complex derivative = (high - low) / (highFrequency - lowFrequency);
        double phaseSlope = (derivative / center).Imaginary; // dφ/df
        return -phaseSlope / (2.0 * Math.PI) * 1000.0;
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
