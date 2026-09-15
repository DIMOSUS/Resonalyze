using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>Chain response built once at the PROCESSOR's rate (bilinear warping differs per rate); the record rate is passed separately.
/// See docs/tech/dsp-chain-response.md.</summary>
public sealed class PreparedDspResponse
{
    private const int PhaseRefreshInterval = 4096;

    private readonly double linearGain;
    private readonly double delayMs;
    private readonly double delayProcessorSamples;
    private readonly int processorRate;
    private readonly BiquadCoefficients[] sections;
    private readonly FirFilter? fir;

    private PreparedDspResponse(
        double linearGain,
        double delayMs,
        double delayProcessorSamples,
        int processorRate,
        BiquadCoefficients[] sections,
        FirFilter? fir)
    {
        this.linearGain = linearGain;
        this.delayMs = delayMs;
        this.delayProcessorSamples = delayProcessorSamples;
        this.processorRate = processorRate;
        this.sections = sections;
        this.fir = fir;
    }

    /// <summary><paramref name="sampleRate"/> is the processor's rate, not the measurement's.</summary>
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

        if (PhaseRotationControl.Realize(chain.PhaseRotation, sampleRate) is { } rotation)
        {
            sections.AddRange(AllPassFilter.BuildSections(rotation, sampleRate));
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

                sections.Add(PeqBiquad.Compute(band, sampleRate));
            }
        }

        return new PreparedDspResponse(
            linearGain,
            chain.DelayMs,
            chain.DelayMs * sampleRate / 1_000.0,
            sampleRate,
            sections.ToArray(),
            chain.Fir);
    }

    public bool IsTimeDomainScaleOnly =>
        delayMs == 0 && sections.Length == 0 && fir == null;

    /// <summary>A record above the processor rate always needs the spectrum path to cut the band past the processor's Nyquist.</summary>
    public bool CanScaleInTimeDomain(int signalSampleRate) =>
        IsTimeDomainScaleOnly && signalSampleRate <= processorRate;

    /// <summary>Zero-padding for the slowest biquad pole to decay by <paramref name="targetDecayDb"/>, in record samples, clamped;
    /// plus N − 1 FIR samples outside the clamp. See docs/tech/dsp-chain-response.md#tail-padding.</summary>
    public int RequiredTailSamples(
        double targetDecayDb,
        int minSamples,
        int maxSamples,
        int signalSampleRate)
    {
        if (signalSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(signalSampleRate));
        }

        double maxRadius = 0.0;
        foreach (BiquadCoefficients section in sections)
        {
            // Additive feedback convention: poles are roots of z² − A1·z − A2 (not the textbook sign).
            double discriminant = section.A1 * section.A1 + 4.0 * section.A2;
            double radius;
            if (discriminant < 0.0)
            {
                radius = Math.Sqrt(Math.Max(0.0, -section.A2));
            }
            else
            {
                double root = Math.Sqrt(discriminant);
                radius = Math.Max(
                    Math.Abs((section.A1 + root) * 0.5),
                    Math.Abs((section.A1 - root) * 0.5));
            }

            maxRadius = Math.Max(maxRadius, radius);
        }

        int firTail = FirTailSamples(signalSampleRate);
        if (maxRadius >= 1.0)
        {
            return maxSamples + firTail;
        }
        if (maxRadius <= 0.0)
        {
            return minSamples + firTail;
        }

        double required = Math.Log(
            Math.Pow(10.0, -Math.Abs(targetDecayDb) / 20.0)) / Math.Log(maxRadius);
        required *= (double)signalSampleRate / processorRate;
        return (int)Math.Clamp(Math.Ceiling(required), minSamples, maxSamples) + firTail;
    }

    // N − 1 record samples; no safety sample (the caller rounds to a power of two).
    private int FirTailSamples(int signalSampleRate) =>
        fir == null
            ? 0
            : (int)Math.Ceiling((double)(fir.Length - 1) * signalSampleRate / processorRate);

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
        double radians = -Math.Tau * frequencyHz / processorRate;
        Complex z1 = UnitPhasor(radians);
        Complex delay = delayMs == 0
            ? Complex.One
            : UnitPhasor(radians * delayProcessorSamples);
        Complex response = Response(z1, delay);
        return fir == null ? response : response * fir.Response(z1);
    }

    /// <summary>Closed-form group delay in ms (biquads + FIR + bulk delay); FIR returns NaN at a kernel null.</summary>
    public double GroupDelayMs(double frequencyHz)
    {
        double samples = 0;
        foreach (BiquadCoefficients section in sections)
        {
            samples += BiquadResponse.GroupDelaySamples(
                section, frequencyHz, processorRate);
        }

        if (fir != null)
        {
            samples += fir.GroupDelaySamples(
                UnitPhasor(-Math.Tau * frequencyHz / processorRate));
        }

        return (samples / processorRate * 1_000.0) + delayMs;
    }

    /// <summary>Multiplies the record's spectrum by the chain's response on the processor's unit circle; bins above the processor Nyquist are zeroed.
    /// See docs/tech/dsp-chain-response.md#processor-rate-vs-record-rate.</summary>
    public void ApplyToSpectrum(Complex[] spectrum, int signalSampleRate)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        if (signalSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(signalSampleRate));
        }

        int length = spectrum.Length;
        int half = length / 2;
        double rateRatio = (double)signalSampleRate / processorRate;
        double delaySamples = delayMs * signalSampleRate / 1_000.0;

        if (sections.Length == 0 && fir == null)
        {
            ApplyGainAndDelayToSpectrum(spectrum, delaySamples);
        }
        else
        {
            Complex[]? firBins = fir == null ? null : FirSpectrumBins(fir, length, rateRatio);
            Complex zStep = Complex.Exp(new Complex(0, -Math.Tau * rateRatio / length));
            Complex delayStep = GetDelayStep(length, delaySamples);
            Complex z1 = Complex.One;
            Complex delay = Complex.One;

            spectrum[0] *= Response(z1, delay) * (firBins?[0] ?? Complex.One);
            for (int i = 1; i < half; i++)
            {
                if (i % PhaseRefreshInterval == 0)
                {
                    z1 = UnitPhasor(-Math.Tau * i * rateRatio / length);
                    delay = DelayPhasor(i, length, delaySamples);
                }
                else
                {
                    z1 *= zStep;
                    delay *= delayStep;
                }

                Complex response = Response(z1, delay);
                if (firBins != null)
                {
                    response *= firBins[i];
                }

                spectrum[i] *= response;
                spectrum[length - i] *= Complex.Conjugate(response);
            }

            z1 = UnitPhasor(-Math.PI * rateRatio);
            delay = DelayPhasor(half, length, delaySamples);
            // Nyquist bin has no conjugate partner: keep it real.
            spectrum[half] *= (Response(z1, delay) * (firBins?[half] ?? Complex.One)).Real;
        }

        SilenceAboveProcessorNyquist(spectrum, rateRatio);
    }

    /// <summary>FIR response at every record bin: exact DFT when length/rateRatio is whole, else chirp-z; cached per kernel.
    /// See docs/tech/dsp-chain-response.md#fir-bins.</summary>
    private static Complex[] FirSpectrumBins(FirFilter fir, int length, double rateRatio)
    {
        FirBinsCache cache = FirBinsCaches.GetOrCreateValue(fir);
        lock (cache)
        {
            if (cache.Find(length, rateRatio) is { } cached)
            {
                return cached;
            }

            Complex[] bins = ComputeFirSpectrumBins(fir, length, rateRatio);
            cache.Add(length, rateRatio, bins);
            return bins;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FirFilter, FirBinsCache>
        FirBinsCaches = new();

    // A few entries: one kernel can sit on channels with different record lengths.
    private sealed class FirBinsCache
    {
        private const int Capacity = 4;
        private readonly List<(int Length, double RateRatio, Complex[] Bins)> entries = [];

        public Complex[]? Find(int length, double rateRatio)
        {
            foreach ((int cachedLength, double cachedRatio, Complex[] bins) in entries)
            {
                if (cachedLength == length && cachedRatio == rateRatio)
                {
                    return bins;
                }
            }

            return null;
        }

        public void Add(int length, double rateRatio, Complex[] bins)
        {
            if (entries.Count == Capacity)
            {
                entries.RemoveAt(0);
            }

            entries.Add((length, rateRatio, bins));
        }
    }

    private static Complex[] ComputeFirSpectrumBins(FirFilter fir, int length, double rateRatio)
    {
        int half = length / 2;
        var bins = new Complex[half + 1];
        int lastBin = Math.Min(half, (int)Math.Floor(half / rateRatio));

        double grid = length / rateRatio;
        long gridLength = (long)Math.Round(grid);
        if (Math.Abs(grid - gridLength) < 1e-6 && gridLength >= fir.Length &&
            gridLength <= int.MaxValue)
        {
            Complex[] spectrum = fir.Spectrum((int)gridLength);
            for (int i = 0; i <= lastBin; i++)
            {
                bins[i] = spectrum[i];
            }

            return bins;
        }

        Complex[] chirp = fir.ChirpSpectrum(lastBin + 1, Math.Tau * rateRatio / length);
        Array.Copy(chirp, bins, lastBin + 1);
        return bins;
    }

    private static void SilenceAboveProcessorNyquist(
        Complex[] spectrum,
        double rateRatio)
    {
        int half = spectrum.Length / 2;
        int lastBin = (int)Math.Floor(half / rateRatio);
        for (int i = lastBin + 1; i <= half; i++)
        {
            spectrum[i] = Complex.Zero;
            spectrum[spectrum.Length - i] = Complex.Zero;
        }
    }

    private Complex GetDelayStep(int length, double delaySamples) =>
        delayMs == 0
            ? Complex.One
            : Complex.Exp(new Complex(0, -Math.Tau * delaySamples / length));

    private Complex DelayPhasor(int bin, int length, double delaySamples) =>
        delayMs == 0
            ? Complex.One
            : UnitPhasor(-Math.Tau * delaySamples * bin / length);

    private static Complex UnitPhasor(double radians) =>
        Complex.Exp(new Complex(0, radians));

    private void ApplyGainAndDelayToSpectrum(Complex[] spectrum, double delaySamples)
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
        Complex delayStep = GetDelayStep(length, delaySamples);
        Complex delay = Complex.One;

        spectrum[0] *= linearGain;
        for (int i = 1; i < half; i++)
        {
            delay = i % PhaseRefreshInterval == 0
                ? DelayPhasor(i, length, delaySamples)
                : delay * delayStep;
            Complex response = linearGain * delay;
            spectrum[i] *= response;
            spectrum[length - i] *= Complex.Conjugate(response);
        }

        delay = DelayPhasor(half, length, delaySamples);
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
