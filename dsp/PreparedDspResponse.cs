using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// Prepared frequency response of a <see cref="DspChannelChain"/>.
/// Filter coefficients and scalar gain are built once, then reused for plot
/// drawing and FFT-bin processing.
/// <para>
/// The rate passed here is the PROCESSOR's — the rate the hardware being
/// simulated runs its biquads at — and it is the only rate the coefficients
/// know. It is deliberately NOT the measurement's: the bilinear transform
/// warps every corner by the rate it was designed at, so a chain built at the
/// measurement rate is a different filter from the one the device realizes
/// (an LR4 low-pass at 8 kHz designed at 48 kHz sits 1.5 dB below the 96 kHz
/// one at 10 kHz and 4.1 dB at 12 kHz). <see cref="ApplyToSpectrum"/> takes
/// the record's own rate separately.
/// </para>
/// </summary>
public sealed class PreparedDspResponse
{
    private const int PhaseRefreshInterval = 4096;

    private readonly double linearGain;
    private readonly double delayMs;
    private readonly double delayProcessorSamples;
    private readonly int processorRate;
    private readonly BiquadCoefficients[] sections;
    // The chain's FIR stage, at the processor's rate like the sections; null when
    // the chain has none.
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

    /// <summary>
    /// Builds the cascade at <paramref name="sampleRate"/> — the PROCESSOR's
    /// processing rate, not the measurement's (see the type remarks).
    /// </summary>
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

    /// <summary>
    /// True when this chain is a scalar — no filters, no delay — so a caller can
    /// multiply the record and skip the FFT entirely.
    /// </summary>
    public bool IsTimeDomainScaleOnly =>
        delayMs == 0 && sections.Length == 0 && fir == null;

    /// <summary>
    /// <see cref="IsTimeDomainScaleOnly"/>, and the record holds nothing the processor
    /// would have to cut. A record sampled ABOVE the processing rate always needs the
    /// spectrum path even for a scalar chain: the band past the processor's Nyquist
    /// has to go (see <see cref="ApplyToSpectrum"/>), or a bypassed channel would keep
    /// ultrasonics that every filtered channel beside it loses — and the two would then
    /// sum, and be timed, against different bandwidths.
    /// </summary>
    public bool CanScaleInTimeDomain(int signalSampleRate) =>
        IsTimeDomainScaleOnly && signalSampleRate <= processorRate;

    /// <summary>
    /// Zero-padding (samples) needed for this chain's ringing to decay by
    /// <paramref name="targetDecayDb"/> before a circular FFT would wrap the
    /// tail into the early response. Follows the slowest pole of the biquad
    /// cascade: a 20 Hz / Q 10 peaking filter rings for ~13.8·Q/(π·f) × ln10/…
    /// hundreds of milliseconds — far past any fixed pad sized for crossovers.
    /// Clamped to [<paramref name="minSamples"/>, <paramref name="maxSamples"/>];
    /// a numerically unstable section (pole radius ≥ 1) gets the maximum.
    /// <para>
    /// The pole radius is a per-sample decay at the PROCESSOR's rate, so the
    /// count it yields is converted to <paramref name="signalSampleRate"/>
    /// before it is clamped: the ringing lasts a fixed number of milliseconds,
    /// and it is the record's own samples that have to hold it.
    /// </para>
    /// <para>
    /// A FIR stage adds its whole kernel length on top, OUTSIDE the clamp: the
    /// convolution of a record with an N-tap kernel is exactly N − 1 samples longer,
    /// there is no decay to wait for, and a cap here would wrap the kernel's tail
    /// into the record's head. The kernel is bounded at load instead (see
    /// <see cref="FirFilter.MaximumTaps"/>).
    /// </para>
    /// </summary>
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
            // BiquadCoefficients uses the ADDITIVE feedback convention
            // (y[n] = … + A1·y[n−1] + A2·y[n−2], denominator
            // 1 − A1·z⁻¹ − A2·z⁻²), so the poles are the roots of
            // z² − A1·z − A2 = 0 — NOT the textbook 1 + a1·z⁻¹ + a2·z⁻² form,
            // whose formulas mis-read every ordinary stable section here as
            // unstable and pinned the padding at the maximum.
            double discriminant = section.A1 * section.A1 + 4.0 * section.A2;
            double radius;
            if (discriminant < 0.0)
            {
                // Complex conjugate poles: |p|² = the roots' product = −A2.
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

    // The kernel's length in the RECORD's samples — the room a linear convolution
    // needs past the input's end. Zero without a FIR stage.
    private int FirTailSamples(int signalSampleRate) =>
        fir == null
            ? 0
            : (int)Math.Ceiling((double)fir.Length * signalSampleRate / processorRate);

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

    /// <summary>
    /// Group delay τ_g = -dφ/dω of the whole chain at <paramref name="frequencyHz"/>, in
    /// milliseconds, summed in closed form from the biquad cascade (see
    /// <see cref="BiquadResponse.GroupDelaySamples"/>). The bulk delay adds itself; the
    /// scalar gain — including the constant π a polarity flip contributes — has no
    /// frequency dependence and so adds nothing.
    /// <para>
    /// Closed form rather than a secant of the complex response reading -Im(H'/H):
    /// that never wraps, but it approximates H rather than φ and so flattens exactly
    /// the sharp peaks worth seeing — a Q-20 all-pass near Nyquist reads 1.4 ms
    /// against a true 127 ms. It would also be free to disagree with the readouts
    /// that share the helper.
    /// </para>
    /// <para>
    /// The FIR stage adds its own closed form (<see cref="FirFilter.GroupDelaySamples"/>),
    /// which is NaN at a kernel's true null — the honest answer there, and one the
    /// plot draws as a gap rather than a spike.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Multiplies <paramref name="spectrum"/> — the FFT of a record sampled at
    /// <paramref name="signalSampleRate"/> — by this chain's response, bin by bin
    /// (conjugate-mirrored, so a real input stays real).
    /// <para>
    /// The two rates are independent. Bin <c>i</c> sits at
    /// <c>i·signalSampleRate/N</c> Hz, which is
    /// <c>ω = 2π·i·signalSampleRate/(N·processorRate)</c> on the PROCESSOR's unit
    /// circle — so a 48 kHz measurement reads a 96 kHz chain across the lower half
    /// of that circle, which is exactly the band a 48 kHz record can carry. This is
    /// not an approximation: a chain is LTI and invents no frequency its input
    /// lacks, so what it does to a band-limited record is fully described by H over
    /// that band. The user's measuring rate and the device's processing rate are
    /// therefore free to differ, and the simulated filters stay the ones the device
    /// realizes.
    /// </para>
    /// <para>
    /// Bins past the processor's own Nyquist — a record sampled ABOVE the
    /// processing rate — are zeroed rather than left to the periodic continuation
    /// of H, which would filter them with a mirrored response no device produces.
    /// The processor reconstructs nothing up there, and zeroing is also what makes
    /// the same setup measured at 96 and at 192 kHz simulate alike.
    /// </para>
    /// <para>
    /// A FIR stage is read the same way, as a response on the processor's circle
    /// (see <see cref="FirSpectrumBins"/>): the kernel's DFT on the grid that puts
    /// its bins exactly under the record's, or the kernel evaluated bin by bin where
    /// no such grid exists.
    /// </para>
    /// </summary>
    public void ApplyToSpectrum(Complex[] spectrum, int signalSampleRate)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        if (signalSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(signalSampleRate));
        }

        int length = spectrum.Length;
        int half = length / 2;
        // ω per bin on the processor's circle, in units of the record's own bin
        // spacing: 1 when the rates agree, ½ for a 48 kHz record through a 96 kHz
        // processor, 2 the other way round.
        double rateRatio = (double)signalSampleRate / processorRate;
        // The delay is a time, not a sample count, so it is expressed in the
        // RECORD's samples — that is the grid the phase ramp runs on.
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
            // The record's Nyquist bin has no conjugate partner; a real scale keeps
            // a real impulse real (the discarded imaginary part is a half-sample
            // artifact). Below the processor's Nyquist the chain's response there is
            // genuinely complex, so this drops a fraction of one bin — the record's
            // top edge, 24 kHz for a 48 kHz measurement.
            spectrum[half] *= (Response(z1, delay) * (firBins?[half] ?? Complex.One)).Real;
        }

        SilenceAboveProcessorNyquist(spectrum, rateRatio);
    }

    /// <summary>
    /// The FIR stage's response at every record bin 0…length/2, on the processor's
    /// unit circle: bin <c>i</c> sits at ω = 2π·i·rateRatio/length.
    /// <para>
    /// FAST PATH: when <c>length / rateRatio</c> is a whole number M — the rates agree
    /// (M = length), a 48 kHz record through a 96 kHz processor (M = 2·length), or
    /// the reverse (M = length/2) — the kernel's M-point DFT has its bin <c>i</c> at
    /// exactly that ω, so one FFT of the zero-padded kernel answers every bin. Exact,
    /// not interpolated: a DFT bin IS the kernel's response at its own frequency.
    /// </para>
    /// <para>
    /// Otherwise (44.1 kHz against 48 kHz, say) no DFT grid lands on the record's
    /// bins, and the kernel is evaluated at each of them by Horner's rule — one
    /// complex multiply per tap per bin, which is slow for a long kernel over a long
    /// record but still the same exact number. Bins past the processor's Nyquist are
    /// left zero on both paths; the caller silences them regardless.
    /// </para>
    /// <para>
    /// CACHED on the kernel: the bins depend on the kernel, the record length and the
    /// rate pair alone — not on the gain, delay or biquads beside it — and every knob
    /// turn on the channel re-renders it through the same bins. So the slow path's
    /// seconds (a 16k-tap kernel over a 128k-point render is a billion multiplies)
    /// are paid once per kernel and rate pair, not once per edit. A kernel that is
    /// unloaded takes its bins with it; the table is weakly keyed.
    /// </para>
    /// </summary>
    private static Complex[] FirSpectrumBins(FirFilter fir, int length, double rateRatio)
    {
        FirBinsCache cache = FirBinsCaches.GetOrCreateValue(fir);
        // One lock per kernel: two channels rendering the same kernel in parallel
        // wait for one computation rather than each running their own.
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

    // A kernel's last few bin sets, one per (record length, rate pair) it was
    // rendered at. A few rather than one because the same kernel can sit on two
    // channels whose records differ in length; the cap keeps a kernel from hoarding
    // every length it was ever tried at.
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
        // Bins the processor reconstructs at all: at or below its own Nyquist.
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

        Complex zStep = Complex.Exp(new Complex(0, -Math.Tau * rateRatio / length));
        Complex z1 = Complex.One;
        for (int i = 0; i <= lastBin; i++)
        {
            if (i % PhaseRefreshInterval == 0)
            {
                z1 = UnitPhasor(-Math.Tau * i * rateRatio / length);
            }

            bins[i] = fir.Response(z1);
            z1 *= zStep;
        }

        return bins;
    }

    // Everything the processor cannot reconstruct. Only a record sampled above the
    // processing rate has such bins (rateRatio > 1); at or below it the loop does
    // not run.
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
