namespace Resonalyze;

/// <summary>Phase-aligned sweep geometry; whole-cycle endpoints widen the band outward. See docs/tech/sweep-measurement.md#sweep-generation.</summary>
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

    public double FrequencyRatio =>
        LowFrequencyHz > 0 ? HighFrequencyHz / LowFrequencyHz : 0.0;

    public double OctaveSpan =>
        FrequencyRatio > 0 ? Math.Log2(FrequencyRatio) : 0.0;

    public double ComputedDurationSeconds =>
        SampleRate > 0 ? SampleCount / (double)SampleRate : 0.0;

    /// <summary>Where the fade-in ends; above the requested edge when a short sweep pads the fade to its minimum.</summary>
    public double FullAmplitudeLowFrequencyHz => AmplitudeEdgeHz(FadeInSamples);

    public double FullAmplitudeHighFrequencyHz =>
        AmplitudeEdgeHz(SampleCount - FadeOutSamples);

    /// <summary>Whether the envelope is fully open across the requested band (short sweeps fail at both edges); panels warn from it.</summary>
    public bool Covers(double requestedLowHz, double requestedHighHz)
    {
        if (!IsValid || !(requestedHighHz > requestedLowHz) || requestedLowHz <= 0)
        {
            return false;
        }

        // One sample of slack absorbs fade-length rounding; a real shortfall is orders of magnitude larger.
        double perSample = Math.Exp(Math.Log(FrequencyRatio) / SampleCount);
        return FullAmplitudeLowFrequencyHz <= requestedLowHz * perSample &&
            FullAmplitudeHighFrequencyHz >= requestedHighHz / perSample;
    }

    private double AmplitudeEdgeHz(int sampleIndex)
    {
        if (!IsValid)
        {
            return 0.0;
        }
        double beta = Math.Log(FrequencyRatio);
        return LowFrequencyHz * Math.Exp(
            Math.Clamp(sampleIndex, 0, SampleCount) / (double)SampleCount * beta);
    }
}

/// <summary>Exponential sine sweep and its inverse filter (sample data only). See docs/tech/sweep-measurement.md#sweep-generation.</summary>
public sealed class ExponentialSineSweep : IDisposable
{
    private const double DesiredGuardOctaves = 0.5;

    // Keeps a hard start/end away when a short sweep cannot open a guard band.
    private const int MinFadeSamples = 128;

    /// <summary>Applied where requests resolve into sweeps, so a preview never promises a length the run shortens.</summary>
    public const double MaxDurationSeconds = 100.0;

    /// <summary>-6 dBFS headroom (matches generator/noise defaults; avoids inter-sample overs). See docs/tech/sweep-measurement.md#sweep-generation.</summary>
    public const double PlaybackAmplitude = 0.5;

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

    public double LowFrequencyHz => spec.LowFrequencyHz;

    public double HighFrequencyHz => spec.HighFrequencyHz;

    public double FrequencyRatio => spec.FrequencyRatio;

    public double OctaveSpan => spec.OctaveSpan;

    public ExpSweepSpec Spec => spec;

    /// <summary>Pure, deterministic request-to-geometry resolution for previews and restores; degenerate inputs return <c>default</c>.</summary>
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
        double highTargetCeiling = nyquist * (1.0 - 1e-4);
        double userHigh = Math.Min(highFrequencyHz, highTargetCeiling);
        if (userHigh <= lowFrequencyHz)
        {
            return default;
        }
        double userLow = lowFrequencyHz;

        int n = Math.Max(
            1,
            (int)Math.Round(sampleRate * Math.Min(requestedDuration, MaxDurationSeconds)));

        double guard = Math.Pow(2.0, DesiredGuardOctaves);
        double targetLow = userLow / guard;
        double targetHigh = Math.Min(userHigh * guard, highTargetCeiling);
        double betaTarget = Math.Log(targetHigh / targetLow);

        // Computed directly: p and q share ln(q/p), so nudging them independently makes the band run away.
        int p = Math.Max(1, (int)Math.Round(targetLow * n / (sampleRate * betaTarget)));
        int q = Math.Max(p + 1, (int)Math.Round(targetHigh * n / (sampleRate * betaTarget)));

        double HighEdgeHz(int endCycles) =>
            (double)sampleRate * endCycles * Math.Log((double)endCycles / p) / n;

        // Touch q only, preserving the bottom guard baked into targetLow.
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

        // Fades fill the guard bands (flat envelope over the request), else a minimum fade.
        int fadeIn = GuardFadeSamples(achievedLow, userLow, betaFinal, n);
        int fadeOut = GuardFadeSamples(userHigh, achievedHigh, betaFinal, n);
        fadeIn = Math.Max(fadeIn, MinFadeSamples);
        fadeOut = Math.Max(fadeOut, MinFadeSamples);
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

    /// <summary>Total duration pacing each ACHIEVED octave at <paramref name="perOctaveSeconds"/>; 0 for degenerate inputs.</summary>
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

    private static int GuardFadeSamples(double from, double to, double beta, int n)
    {
        if (!(to > from) || beta <= 0)
        {
            return 0;
        }
        return (int)Math.Round(n * Math.Log(to / from) / beta);
    }

    /// <summary>Resolves the geometry now; samples are synthesized lazily (restores need only the geometry).</summary>
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

    /// <summary>The reference's phase trajectory over scaled samples, as an independent clock plays it; generated analytically, not resampled.</summary>
    public void FillStretched(ExpSweepSpec reference, double timeScale, int bitsPerSample = 24)
    {
        ThrowIfDisposed();
        if (!reference.IsValid)
        {
            throw new ArgumentException("The reference sweep is not resolved.", nameof(reference));
        }
        if (!double.IsFinite(timeScale) || timeScale is <= 0.5 or >= 2.0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeScale));
        }
        if (bitsPerSample is not (16 or 24))
        {
            throw new NotSupportedException($"Unsupported sample size: {bitsPerSample} bits.");
        }

        int sampleCount = Math.Max(2, (int)Math.Round(reference.SampleCount * timeScale));
        double beta = Math.Log((double)reference.EndCycles / reference.StartCycles);
        double low = (double)reference.SampleRate * reference.StartCycles * beta / sampleCount;
        double high = (double)reference.SampleRate * reference.EndCycles * beta / sampleCount;
        int maxFade = Math.Max(0, (sampleCount - 1) / 2);
        spec = new ExpSweepSpec(
            low,
            high,
            sampleCount,
            reference.SampleRate,
            reference.StartCycles,
            reference.EndCycles,
            Math.Clamp((int)Math.Round(reference.FadeInSamples * timeScale), 0, maxFade),
            Math.Clamp((int)Math.Round(reference.FadeOutSamples * timeScale), 0, maxFade));
        BitsPerSample = bitsPerSample;
        RequestedDuration = spec.ComputedDurationSeconds;

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
            sweepData[i] = (float)(Math.Sin(phase) * envelope * PlaybackAmplitude);
        }

        // Envelope compensates less time per Hz at high frequencies. PlaybackAmplitude divided out twice (reversed-sweep headroom
        // and the attenuated excitation), keeping the IR at full-scale level; drop either and results step down 6 dB.
        double octaveSpan = beta / Math.Log(2.0);
        double inverseScale = beta /
            (1.0 - Math.Pow(2.0, -octaveSpan)) /
            (PlaybackAmplitude * PlaybackAmplitude);
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
