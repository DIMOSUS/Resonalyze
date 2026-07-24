namespace Resonalyze;

/// <summary>
/// The fully resolved parameters of an exponential sine sweep after phase
/// alignment. A sweep is requested by a low/high frequency band and a
/// duration; the generator snaps the endpoints to a whole number of cycles
/// (<see cref="StartCycles"/>, <see cref="EndCycles"/>) so both ends land on a
/// zero crossing, which nudges the achieved band outward from the request.
/// The achieved band therefore always ENCLOSES the requested one, with the
/// fade-in/out regions living in the guard bands outside it.
/// </summary>
public readonly record struct ExpSweepSpec(
    double LowFrequencyHz,
    double HighFrequencyHz,
    int SampleCount,
    int SampleRate,
    int StartCycles,
    int EndCycles,
    int FadeInSamples,
    int FadeOutSamples)
{
    public bool IsValid =>
        SampleCount > 0 &&
        SampleRate > 0 &&
        LowFrequencyHz > 0 &&
        HighFrequencyHz > LowFrequencyHz &&
        StartCycles >= 1 &&
        EndCycles > StartCycles;

    /// <summary>Achieved high/low ratio (equals <c>EndCycles / StartCycles</c>).</summary>
    public double FrequencyRatio =>
        LowFrequencyHz > 0 ? HighFrequencyHz / LowFrequencyHz : 0.0;

    /// <summary>Achieved span in octaves — the value shown next to the inputs.</summary>
    public double OctaveSpan =>
        FrequencyRatio > 0 ? Math.Log2(FrequencyRatio) : 0.0;

    public double ComputedDurationSeconds =>
        SampleRate > 0 ? SampleCount / (double)SampleRate : 0.0;
}

/// <summary>
/// Generates an exponential sine sweep and its amplitude-compensated inverse
/// filter. Pure signal generation: it exposes float sample data only, and the
/// audio layer builds any playback stream from it.
/// </summary>
/// <remarks>
/// The sweep is defined by its low and high frequency (not an octave count),
/// so it can end below Nyquist. Phase alignment is preserved: the absolute
/// Farina phase is <c>phi(i) = 2*pi*p * exp(i/N * ln(q/p))</c>, so
/// <c>phi(0) = 2*pi*p</c> and <c>phi(N) = 2*pi*q</c> with integer p, q — both
/// ends sit on a whole cycle. The requested band is rounded outward to those
/// whole-cycle endpoints and given guard bands for the fades, so the achieved
/// band always encloses the request.
/// </remarks>
public sealed class ExponentialSineSweep : IDisposable
{
    // Guard band (each side) the endpoint rounding aims for, so the fade
    // regions have room to sit OUTSIDE the requested band. The bottom guard
    // is often larger (few whole cycles are available at low frequencies) and
    // the top guard is capped just under Nyquist; both are best-effort.
    private const double DesiredGuardOctaves = 0.5;

    // A floor on the fade length so the excitation never starts or ends on a
    // hard edge when the requested duration is too short to open a guard band
    // (the achieved band then cannot fully enclose the request — the honest,
    // wider real range is reported through the spec).
    private const int MinFadeSamples = 128;

    private bool disposed;
    private bool generated;
    private float[] sweepData = Array.Empty<float>();
    private float[] inverseFilter = Array.Empty<float>();
    private ExpSweepSpec spec;

    public float[] SweepData
    {
        get
        {
            EnsureGenerated();
            return sweepData;
        }
    }

    public float[] InverseFilter
    {
        get
        {
            EnsureGenerated();
            return inverseFilter;
        }
    }

    public int SampleRate => spec.SampleRate;
    public int SweepSamples => spec.SampleCount;
    public int BitsPerSample { get; private set; }
    public double RequestedDuration { get; private set; }
    public double ComputedDuration => spec.ComputedDurationSeconds;

    /// <summary>Achieved (phase-aligned) low edge of the sweep, in hertz.</summary>
    public double LowFrequencyHz => spec.LowFrequencyHz;

    /// <summary>Achieved (phase-aligned) high edge of the sweep, in hertz.</summary>
    public double HighFrequencyHz => spec.HighFrequencyHz;

    /// <summary>Achieved high/low frequency ratio.</summary>
    public double FrequencyRatio => spec.FrequencyRatio;

    /// <summary>Achieved span in octaves (log2 of the ratio).</summary>
    public double OctaveSpan => spec.OctaveSpan;

    /// <summary>The resolved parameters, for callers that need the full geometry.</summary>
    public ExpSweepSpec Spec => spec;

    /// <summary>
    /// Resolves a requested band and duration to the phase-aligned sweep that
    /// encloses it. Pure and deterministic, so the option panels can preview a
    /// configuration (real range, octave count, computed duration) without
    /// generating any samples, and a restore reproduces the same geometry.
    /// Degenerate inputs return <c>default</c> (<see cref="ExpSweepSpec.IsValid"/>
    /// is false) instead of throwing — this feeds display fields on
    /// settings-load paths.
    /// </summary>
    public static ExpSweepSpec ComputeSpec(
        double lowFrequencyHz,
        double highFrequencyHz,
        double requestedDuration,
        int sampleRate)
    {
        if (sampleRate <= 0 ||
            !double.IsFinite(requestedDuration) ||
            requestedDuration <= 0 ||
            !double.IsFinite(lowFrequencyHz) ||
            !double.IsFinite(highFrequencyHz) ||
            lowFrequencyHz <= 0 ||
            highFrequencyHz <= lowFrequencyHz)
        {
            return default;
        }

        double nyquist = sampleRate / 2.0;
        // Keep the sweep strictly below Nyquist; the outward rounding of the
        // high edge must never alias.
        double highTargetCeiling = nyquist * (1.0 - 1e-4);
        double userHigh = Math.Min(highFrequencyHz, highTargetCeiling);
        if (userHigh <= lowFrequencyHz)
        {
            return default;
        }
        double userLow = lowFrequencyHz;

        int n = Math.Max(1, (int)Math.Round(sampleRate * requestedDuration));

        // Target band: the requested band widened by a guard on each side (so
        // the fades sit outside it), with the top capped just under Nyquist.
        double guard = Math.Pow(2.0, DesiredGuardOctaves);
        double targetLow = userLow / guard;
        double targetHigh = Math.Min(userHigh * guard, highTargetCeiling);
        double betaTarget = Math.Log(targetHigh / targetLow);

        // The whole-cycle endpoints closest to the target band, computed
        // DIRECTLY (p from the low edge, q from the high edge) rather than by
        // iterative widening: p and q share the ln(q/p) factor, so nudging them
        // independently makes the band run away. Everything is double, so
        // sampleRate*q cannot overflow Int32 on long or high-rate sweeps.
        int p = Math.Max(1, (int)Math.Round(targetLow * n / (sampleRate * betaTarget)));
        int q = Math.Max(p + 1, (int)Math.Round(targetHigh * n / (sampleRate * betaTarget)));

        double HighEdgeHz(int endCycles) =>
            (double)sampleRate * endCycles * Math.Log((double)endCycles / p) / n;

        // Guarantee the requested top is covered, then keep the sweep below
        // Nyquist without dropping back under the request. Both touch q only (a
        // few steps), so the bottom guard baked into targetLow is preserved.
        while (HighEdgeHz(q) < userHigh)
        {
            q++;
        }
        while (q > p + 1 &&
            HighEdgeHz(q) > highTargetCeiling &&
            HighEdgeHz(q - 1) >= userHigh)
        {
            q--;
        }

        double betaFinal = Math.Log((double)q / p);
        double achievedLow = (double)sampleRate * p * betaFinal / n;
        double achievedHigh = (double)sampleRate * q * betaFinal / n;

        // Fades fill the guard bands so the envelope is flat across the
        // requested [userLow, userHigh]. When the band could not open a guard
        // (achieved edge inside the request), fall back to a minimum fade so
        // the excitation still starts/ends smoothly.
        int fadeIn = GuardFadeSamples(achievedLow, userLow, betaFinal, n);
        int fadeOut = GuardFadeSamples(userHigh, achievedHigh, betaFinal, n);
        fadeIn = Math.Max(fadeIn, MinFadeSamples);
        fadeOut = Math.Max(fadeOut, MinFadeSamples);
        // Keep a non-empty flat middle even for very short sweeps.
        int maxFade = Math.Max(0, (n - 1) / 2);
        fadeIn = Math.Clamp(fadeIn, 0, maxFade);
        fadeOut = Math.Clamp(fadeOut, 0, maxFade);

        return new ExpSweepSpec(
            achievedLow,
            achievedHigh,
            n,
            sampleRate,
            p,
            q,
            fadeIn,
            fadeOut);
    }

    /// <summary>
    /// The total sweep duration that paces each ACHIEVED octave at roughly
    /// <paramref name="perOctaveSeconds"/>. The achieved octave span depends on the
    /// band and sample rate and only marginally on the duration, so a couple of
    /// evaluations converge. Returns 0 for degenerate inputs.
    /// </summary>
    public static double TotalDurationForOctavePace(
        double lowFrequencyHz,
        double highFrequencyHz,
        double perOctaveSeconds,
        int sampleRate)
    {
        if (!(perOctaveSeconds > 0) ||
            !(highFrequencyHz > lowFrequencyHz) ||
            lowFrequencyHz <= 0)
        {
            return 0.0;
        }

        // Seed with the requested span plus a rough guard-band allowance, then
        // refine against the actual achieved span.
        double span = Math.Log2(highFrequencyHz / lowFrequencyHz) + 1.0;
        for (int iteration = 0; iteration < 2; iteration++)
        {
            ExpSweepSpec probe = ComputeSpec(
                lowFrequencyHz, highFrequencyHz, perOctaveSeconds * span, sampleRate);
            if (!probe.IsValid)
            {
                break;
            }
            span = probe.OctaveSpan;
        }
        return perOctaveSeconds * span;
    }

    /// <summary>
    /// The per-octave pace of a sweep that runs for <paramref name="totalSeconds"/>
    /// over the band — the inverse of <see cref="TotalDurationForOctavePace"/>, for
    /// showing a stored total duration as a per-octave value. Returns 0 for
    /// degenerate inputs.
    /// </summary>
    public static double OctavePaceForTotalDuration(
        double lowFrequencyHz,
        double highFrequencyHz,
        double totalSeconds,
        int sampleRate)
    {
        ExpSweepSpec spec = ComputeSpec(
            lowFrequencyHz, highFrequencyHz, totalSeconds, sampleRate);
        return spec.IsValid && spec.OctaveSpan > 0
            ? totalSeconds / spec.OctaveSpan
            : 0.0;
    }

    // Samples between the achieved edge (frequency `from`) and the requested
    // edge (frequency `to`), i.e. the guard-band width in samples. Zero when
    // the achieved edge does not extend past the request.
    private static int GuardFadeSamples(double from, double to, double beta, int n)
    {
        if (!(to > from) || beta <= 0)
        {
            return 0;
        }
        return (int)Math.Round(n * Math.Log(to / from) / beta);
    }

    /// <summary>
    /// Sets the sweep parameters (a requested low/high band and duration) and
    /// resolves them to the achieved, phase-aligned geometry. The signal itself
    /// is synthesized lazily on first use: restoring a measurement from a file
    /// or History only needs the parameters (for the harmonic geometry and the
    /// panels), not megabytes of sweep and inverse-filter data.
    /// </summary>
    public void FillData(
        double lowFrequencyHz,
        double highFrequencyHz,
        double requestedDuration,
        int bitsPerSample = 24,
        int sampleRate = 44_100)
    {
        ThrowIfDisposed();
        ValidateGenerationParameters(
            lowFrequencyHz,
            highFrequencyHz,
            requestedDuration,
            bitsPerSample,
            sampleRate);

        ExpSweepSpec resolved = ComputeSpec(
            lowFrequencyHz,
            highFrequencyHz,
            requestedDuration,
            sampleRate);
        if (!resolved.IsValid)
        {
            throw new ArgumentException(
                "The sweep band cannot be resolved for the given duration and sample rate.");
        }

        BitsPerSample = bitsPerSample;
        RequestedDuration = requestedDuration;
        spec = resolved;

        generated = false;
        sweepData = Array.Empty<float>();
        inverseFilter = Array.Empty<float>();
    }

    private void EnsureGenerated()
    {
        ThrowIfDisposed();
        if (generated)
        {
            return;
        }
        if (!spec.IsValid)
        {
            throw new InvalidOperationException("The sweep is not configured.");
        }

        int sampleCount = spec.SampleCount;
        double beta = Math.Log((double)spec.EndCycles / spec.StartCycles);
        double startPhase = 2.0 * Math.PI * spec.StartCycles;

        sweepData = new float[sampleCount];
        inverseFilter = new float[sampleCount];

        int fadeIn = spec.FadeInSamples;
        int fadeOut = spec.FadeOutSamples;
        int fadeOutStart = sampleCount - fadeOut;
        for (int i = 0; i < sampleCount; i++)
        {
            double phase = startPhase * Math.Exp(i / (double)sampleCount * beta);
            double envelope = 1.0;
            if (fadeIn > 0 && i < fadeIn)
            {
                envelope = i / (double)fadeIn;
            }
            else if (fadeOut > 0 && i >= fadeOutStart)
            {
                envelope = (sampleCount - 1 - i) / (double)fadeOut;
            }
            sweepData[i] = (float)Math.Sin(phase) * (float)envelope;
        }

        // Time reversal performs the deconvolution. The exponential envelope
        // compensates for the sweep spending progressively less time per hertz
        // at high frequencies. Expressed through the octave span so it matches
        // the (possibly fractional) achieved band.
        double octaveSpan = beta / Math.Log(2.0);
        double inverseScale = beta / (1.0 - Math.Pow(2.0, -octaveSpan));
        double perSampleDecay = Math.Pow(2.0, octaveSpan / sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            inverseFilter[i] =
                (float)(sweepData[sampleCount - i - 1] * Math.Pow(perSampleDecay, -i) * inverseScale);
        }

        generated = true;
    }

    private static void ValidateGenerationParameters(
        double lowFrequencyHz,
        double highFrequencyHz,
        double requestedDuration,
        int bitsPerSample,
        int sampleRate)
    {
        if (!double.IsFinite(lowFrequencyHz) || lowFrequencyHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lowFrequencyHz));
        }
        if (!double.IsFinite(highFrequencyHz) || highFrequencyHz <= lowFrequencyHz)
        {
            throw new ArgumentOutOfRangeException(nameof(highFrequencyHz));
        }
        if (!double.IsFinite(requestedDuration) || requestedDuration <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedDuration));
        }
        if (bitsPerSample is not (16 or 24))
        {
            throw new NotSupportedException($"Unsupported sample size: {bitsPerSample} bits.");
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        GC.SuppressFinalize(this);
    }
}
