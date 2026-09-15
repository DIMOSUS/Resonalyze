using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
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
                FillPinkPeriodic(random, periodLength);
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

    // One FFT-block period with exact 1/sqrt(f) magnitude and random phase, tiled: converges without spectral variance.
    private void FillPinkPeriodic(Random random, int periodLength)
    {
        int n = Math.Max(2, periodLength);
        var spectrum = new Complex[n];
        int half = n / 2;
        for (int k = 1; k <= half; k++)
        {
            double magnitude = 1.0 / Math.Sqrt(k);
            if (k == n - k)
            {
                spectrum[k] = new Complex(random.NextDouble() < 0.5 ? -magnitude : magnitude, 0);
                continue;
            }

            double phase = random.NextDouble() * 2.0 * Math.PI;
            Complex value = Complex.FromPolarCoordinates(magnitude, phase);
            spectrum[k] = value;
            spectrum[n - k] = Complex.Conjugate(value);
        }

        Fourier.Inverse(spectrum, FourierOptions.Default);

        var period = new double[n];
        double peak = 0;
        for (int i = 0; i < n; i++)
        {
            period[i] = spectrum[i].Real;
            peak = Math.Max(peak, Math.Abs(period[i]));
        }

        double scale = peak > 0 ? 0.5 / peak : 1.0;
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            FloatData[sampleIndex] = (float)(period[sampleIndex % n] * scale);
        }
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
