using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>
/// Generates deterministic broadband noise for repeatable live-spectrum measurements.
/// </summary>
public sealed class NoiseSignal : IDisposable
{
    private PcmStreamSet? streamSet;
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
            // The buffer is looped by playback; a length that is not a whole
            // number of periods puts a phase jump at every loop seam, and the
            // analyzer's rectangular window assumes exact periodicity.
            int period = Math.Max(2, periodLength);
            Samples = period * Math.Max(
                1,
                (int)Math.Round(sampleRate * requestedDuration / (double)period));
        }

        FloatData = new float[Samples];

        // A fixed seed keeps measurements reproducible and makes regressions diagnosable.
        var random = new Random(42);
        switch (noiseColor)
        {
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

        streamSet?.Dispose();
        streamSet = new PcmStreamSet(FloatData, sampleRate, bitsPerSample);
    }

    // Uniform white noise in [-0.5, 0.5): equal energy per hertz.
    private void FillWhite(Random random)
    {
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            FloatData[sampleIndex] = (float)(random.NextDouble() - 0.5);
        }
    }

    // Pink noise (-3 dB/octave) via Paul Kellett's economical filter bank, then
    // normalized to the same 0.5 peak as the white path so playback level matches.
    private void FillPink(Random random)
    {
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        double peak = 0;
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            double white = random.NextDouble() * 2.0 - 1.0;
            b0 = 0.99886 * b0 + white * 0.0555179;
            b1 = 0.99332 * b1 + white * 0.0750759;
            b2 = 0.96900 * b2 + white * 0.1538520;
            b3 = 0.86650 * b3 + white * 0.3104856;
            b4 = 0.55000 * b4 + white * 0.5329522;
            b5 = -0.7616 * b5 - white * 0.0168980;
            double pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362;
            b6 = white * 0.115926;

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

    // Periodic pink noise: synthesise one period of length = the analyzer FFT block
    // with an exactly pink magnitude spectrum (amplitude ∝ 1/sqrt(f)) and random
    // phase, then tile it across the buffer. Being deterministic and period-synchronous
    // with the FFT, it converges far faster than random noise with no spectral variance.
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
                // Nyquist bin has no conjugate partner and must stay real.
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

    // Brown/red noise (-6 dB/octave) via a leaky integrator of white noise. The
    // leak keeps the random walk from drifting off; the mean is removed and the
    // result normalized to the same 0.5 peak as the other colours.
    private void FillBrown(Random random)
    {
        const double leak = 0.99;
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

    public RawSourceWaveStream GetStream(PlaybackChannel channel)
    {
        ThrowIfDisposed();
        if (streamSet == null)
        {
            throw new InvalidOperationException("The noise signal is not generated.");
        }

        return streamSet.GetStream(channel);
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
        streamSet?.Dispose();
        streamSet = null;
        GC.SuppressFinalize(this);
    }
}
