using NAudio.Wave;

namespace Resonalyze;

public enum PlaybackChannel : byte
{
    Mono = 0,
    Left = 1,
    Right = 2,
    Stereo = 3
}

/// <summary>
/// Generates an exponential sine sweep and its amplitude-compensated inverse filter.
/// </summary>
public sealed class ExponentialSineSweep : IDisposable
{
    private PcmStreamSet? streamSet;
    private bool disposed;

    public float[] SweepData { get; private set; } = Array.Empty<float>();
    public float[] InverseFilter { get; private set; } = Array.Empty<float>();
    public float[] Frequencies { get; private set; } = Array.Empty<float>();
    public int SampleRate { get; private set; }
    public int SweepSamples { get; private set; }
    public int BitsPerSample { get; private set; }
    public int Octaves { get; private set; }
    public double RequestedDuration { get; private set; }
    public double ComputedDuration =>
        SampleRate > 0 ? SweepSamples / (double)SampleRate : 0.0;

    public double CalculateDuration(double requestedDuration)
    {
        ValidateGenerationParameters(Octaves, requestedDuration, BitsPerSample, SampleRate);

        double frequencyRatio = Math.Pow(2.0, Octaves);
        double logarithmicRatio = Math.Log(frequencyRatio);
        double targetLength = SampleRate * requestedDuration;
        double phaseFactor = (Math.PI / frequencyRatio) / logarithmicRatio;

        // Quantizing the phase count makes the sweep end at a full cycle. This reduces
        // the discontinuity at the boundary without changing the requested duration materially.
        double cycleCount = Math.Max(
            1,
            Math.Round(phaseFactor * targetLength / (2.0 * Math.PI)));
        double exactLength = cycleCount * 2.0 * Math.PI / phaseFactor;

        return Math.Max(1, (int)Math.Round(exactLength)) / (double)SampleRate;
    }

    public void FillData(
        int octaves,
        double requestedDuration,
        int bitsPerSample = 24,
        int sampleRate = 44_100)
    {
        ThrowIfDisposed();
        ValidateGenerationParameters(octaves, requestedDuration, bitsPerSample, sampleRate);

        Octaves = octaves;
        BitsPerSample = bitsPerSample;
        SampleRate = sampleRate;
        RequestedDuration = requestedDuration;

        double frequencyRatio = Math.Pow(2.0, octaves);
        double logarithmicRatio = Math.Log(frequencyRatio);
        double phaseFactor = (Math.PI / frequencyRatio) / logarithmicRatio;
        double targetLength = sampleRate * requestedDuration;
        double cycleCount = Math.Max(
            1,
            Math.Round(phaseFactor * targetLength / (2.0 * Math.PI)));
        double exactLength = cycleCount * 2.0 * Math.PI / phaseFactor;
        int sampleCount = Math.Max(1, (int)Math.Round(exactLength));

        SweepSamples = sampleCount;
        SweepData = new float[sampleCount];
        InverseFilter = new float[sampleCount];
        Frequencies = new float[sampleCount];

        double octaveLength = sampleCount / (double)octaves;
        for (int i = 0; i < sampleCount; i++)
        {
            double exponentialPosition = Math.Exp(i / (double)sampleCount * logarithmicRatio);
            Frequencies[i] =
                (float)(sampleRate / 2.0 / frequencyRatio * (exactLength / sampleCount) * exponentialPosition);
            SweepData[i] =
                (float)Math.Sin(phaseFactor * exactLength * exponentialPosition) *
                (float)Math.Min(i / octaveLength, 1.0);
        }

        // Time reversal performs the deconvolution. The exponential envelope compensates
        // for the sweep spending progressively less time per hertz at high frequencies.
        double inverseScale = octaves * Math.Log(2.0) / (1.0 - Math.Pow(2.0, -octaves));
        double perSampleDecay = Math.Pow(2.0, octaves / (double)sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            InverseFilter[i] =
                (float)(SweepData[sampleCount - i - 1] * Math.Pow(perSampleDecay, -i) * inverseScale);
        }

        streamSet?.Dispose();
        streamSet = new PcmStreamSet(SweepData, sampleRate, bitsPerSample);
    }

    public RawSourceWaveStream GetStream(PlaybackChannel channel)
    {
        ThrowIfDisposed();
        if (streamSet == null)
        {
            throw new InvalidOperationException("The sweep is not generated.");
        }

        return streamSet.GetStream(channel);
    }

    private static void ValidateGenerationParameters(
        int octaves,
        double requestedDuration,
        int bitsPerSample,
        int sampleRate)
    {
        if (octaves <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(octaves));
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
        streamSet?.Dispose();
        streamSet = null;
        GC.SuppressFinalize(this);
    }
}
