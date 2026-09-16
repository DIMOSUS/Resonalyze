using System.Collections.Concurrent;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>Deterministic broadband noise for live measurements (sample data only).</summary>
public sealed class NoiseSignal : IDisposable
{
    private bool disposed;

    public float[] FloatData { get; private set; } = Array.Empty<float>();
    public int SampleRate { get; private set; }
    public int Samples { get; private set; }
    public int BitsPerSample { get; private set; }
    public double RequestedDuration { get; private set; }

    public void FillData(
        double requestedDuration,
        int bitsPerSample = 24,
        int sampleRate = 44_100,
        NoiseColor noiseColor = NoiseColor.PinkPeriodic,
        int periodLength = 2048)
    {
        ThrowIfDisposed();
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

        BitsPerSample = bitsPerSample;
        SampleRate = sampleRate;
        RequestedDuration = requestedDuration;
        Samples = checked((int)(sampleRate * requestedDuration));
        if (noiseColor == NoiseColor.PinkPeriodic)
        {
            // Looped by playback: a non-whole number of periods jumps phase at the seam and breaks the rectangular window.
            int period = Math.Max(2, periodLength);
            Samples = period * Math.Max(
                1,
                (int)Math.Round(sampleRate * requestedDuration / (double)period));
        }

        FloatData = new float[Samples];

        var random = new Random(42);
        switch (noiseColor)
        {
            case NoiseColor.Silent:
                break;
            case NoiseColor.PinkPeriodic:
                FillPinkPeriodic(periodLength);
                break;
            case NoiseColor.Pink:
                FillPink(random);
                break;
            case NoiseColor.Brown:
                FillBrown(random);
                break;
            default:
                FillWhite(random);
                break;
        }
    }

    private void FillWhite(Random random)
    {
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            FloatData[sampleIndex] = (float)(random.NextDouble() - 0.5);
        }
    }

    // Kellett bank from Dsp.KellettPinkFilter, shared with tilt compensation, which must model this exact filter.
    private void FillPink(Random random)
    {
        IReadOnlyList<(double A, double G)> poles = KellettPinkFilter.Poles;
        var states = new double[poles.Count];
        double delayed = 0;
        double peak = 0;
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            double white = random.NextDouble() * 2.0 - 1.0;
            double pink = KellettPinkFilter.DirectGain * white + delayed;
            for (int pole = 0; pole < states.Length; pole++)
            {
                states[pole] = poles[pole].A * states[pole] + poles[pole].G * white;
                pink += states[pole];
            }
            delayed = KellettPinkFilter.DelayedGain * white;

            FloatData[sampleIndex] = (float)pink;
            double magnitude = Math.Abs(pink);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        if (peak <= 0)
        {
            return;
        }

        float scale = (float)(0.5 / peak);
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            FloatData[sampleIndex] *= scale;
        }
    }

    /// <summary>Periodic pink covers this band only: below it the energy is 29% of the total at 65536/48 kHz, drives excursion and
    /// reaches no display point (the grid starts at 20 Hz). See docs/tech/live-spectrum.md#periodic-pink-excitation.</summary>
    public const double PeriodicPinkLowHz = 10.0;

    /// <summary>Just above 20 kHz·√2, the widest per-bin read (1/1-octave smoothing at the top grid point).</summary>
    public const double PeriodicPinkHighHz = 28_300.0;

    // Keyed by period and rate: the phase search is ~250 ms at 65536 samples, and a run restarts on every option change.
    private static readonly ConcurrentDictionary<(int Length, int SampleRate), double[]> PinkPeriods = new();

    // One FFT-block period with exact 1/sqrt(f) magnitude in the band, tiled: converges without spectral variance.
    private void FillPinkPeriodic(int periodLength)
    {
        int n = Math.Max(2, periodLength);
        double[] period = PinkPeriods.GetOrAdd((n, SampleRate), key => SynthesizePinkPeriod(key.Length, key.SampleRate));
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            FloatData[sampleIndex] = (float)period[sampleIndex % n];
        }
    }

    /// <summary>−12 dBFS, 6 dB under the other colours: at the mic the cabin restores a noise-like crest, and at 0.5 the peaks
    /// matched a sweep's. See docs/tech/live-spectrum.md#periodic-pink-excitation.</summary>
    public const double PeriodicPinkPeak = 0.25;

    internal static double[] SynthesizePinkPeriod(int length, int sampleRate)
    {
        var magnitudes = new double[(length / 2) + 1];
        double highHz = Math.Min(PeriodicPinkHighHz, sampleRate / 2.0);
        for (int k = 1; k < magnitudes.Length; k++)
        {
            double frequency = k * (double)sampleRate / length;
            magnitudes[k] = frequency >= PeriodicPinkLowHz && frequency <= highHz ? 1.0 / Math.Sqrt(k) : 0.0;
        }

        double[] period = PeriodicNoiseSynthesis.Synthesize(magnitudes, length);
        double peak = period.Max(Math.Abs);
        double scale = peak > 0 ? PeriodicPinkPeak / peak : 1.0;
        for (int i = 0; i < period.Length; i++)
        {
            period[i] *= scale;
        }

        return period;
    }

    // Leak derived from a fixed corner so the spectrum does not change with sample rate (0.99 put it at 76 Hz @48k, 305 Hz @192k).
    // Shared with the tilt compensation's leaky-integrator model.
    internal const double BrownCornerHz = 76.0;

    private void FillBrown(Random random)
    {
        double leak = Math.Clamp(
            1.0 - 2.0 * Math.PI * BrownCornerHz / Math.Max(1, SampleRate),
            0.0,
            0.99999);
        double value = 0;
        double sum = 0;
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            double white = random.NextDouble() * 2.0 - 1.0;
            value = leak * value + white * (1.0 - leak);
            FloatData[sampleIndex] = (float)value;
            sum += value;
        }

        float mean = (float)(sum / Math.Max(1, Samples));
        double peak = 0;
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            float centered = FloatData[sampleIndex] - mean;
            FloatData[sampleIndex] = centered;
            peak = Math.Max(peak, Math.Abs(centered));
        }

        if (peak <= 0)
        {
            return;
        }

        float scale = (float)(0.5 / peak);
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            FloatData[sampleIndex] *= scale;
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
