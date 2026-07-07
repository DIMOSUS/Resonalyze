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
    private bool generated;
    private float[] sweepData = Array.Empty<float>();
    private float[] inverseFilter = Array.Empty<float>();

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

    public int SampleRate { get; private set; }
    public int SweepSamples { get; private set; }
    public int BitsPerSample { get; private set; }
    public int Octaves { get; private set; }
    public double RequestedDuration { get; private set; }
    public double ComputedDuration =>
        SampleRate > 0 ? SweepSamples / (double)SampleRate : 0.0;

    /// <summary>
    /// The duration the sweep will actually have for the given parameters,
    /// after cycle quantization. Static so the option panels can preview a
    /// configuration without depending on the last generated sweep's state.
    /// Degenerate inputs return 0 instead of throwing — this feeds display
    /// fields on settings-load paths.
    /// </summary>
    public static double CalculateDuration(
        int octaves,
        double requestedDuration,
        int sampleRate)
    {
        if (octaves <= 0 ||
            sampleRate <= 0 ||
            !double.IsFinite(requestedDuration) ||
            requestedDuration <= 0)
        {
            return 0.0;
        }

        double exactLength = ComputeQuantizedSweepLength(
            octaves,
            requestedDuration,
            sampleRate);
        return Math.Max(1, (int)Math.Round(exactLength)) / (double)sampleRate;
    }

    // Quantizing the phase count makes the sweep end at a full cycle. This reduces
    // the discontinuity at the boundary without changing the requested duration materially.
    private static double ComputeQuantizedSweepLength(
        int octaves,
        double requestedDuration,
        int sampleRate)
    {
        double frequencyRatio = Math.Pow(2.0, octaves);
        double logarithmicRatio = Math.Log(frequencyRatio);
        double phaseFactor = (Math.PI / frequencyRatio) / logarithmicRatio;
        double targetLength = sampleRate * requestedDuration;
        double cycleCount = Math.Max(
            1,
            Math.Round(phaseFactor * targetLength / (2.0 * Math.PI)));
        return cycleCount * 2.0 * Math.PI / phaseFactor;
    }

    /// <summary>
    /// Sets the sweep parameters and the resulting sample count. The signal
    /// itself is synthesized lazily on first use: restoring a measurement from
    /// a file or History only needs the parameters (for HarmonicIROffset and
    /// the panels), not megabytes of sweep and inverse-filter data.
    /// </summary>
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

        double exactLength = ComputeQuantizedSweepLength(
            octaves,
            requestedDuration,
            sampleRate);
        SweepSamples = Math.Max(1, (int)Math.Round(exactLength));

        generated = false;
        sweepData = Array.Empty<float>();
        inverseFilter = Array.Empty<float>();
        streamSet?.Dispose();
        streamSet = null;
    }

    private void EnsureGenerated()
    {
        ThrowIfDisposed();
        if (generated)
        {
            return;
        }
        if (SweepSamples <= 0)
        {
            throw new InvalidOperationException("The sweep is not configured.");
        }

        double frequencyRatio = Math.Pow(2.0, Octaves);
        double logarithmicRatio = Math.Log(frequencyRatio);
        double phaseFactor = (Math.PI / frequencyRatio) / logarithmicRatio;
        double exactLength = ComputeQuantizedSweepLength(
            Octaves,
            RequestedDuration,
            SampleRate);
        int sampleCount = SweepSamples;

        sweepData = new float[sampleCount];
        inverseFilter = new float[sampleCount];

        double octaveLength = sampleCount / (double)Octaves;
        for (int i = 0; i < sampleCount; i++)
        {
            double exponentialPosition = Math.Exp(i / (double)sampleCount * logarithmicRatio);
            sweepData[i] =
                (float)Math.Sin(phaseFactor * exactLength * exponentialPosition) *
                (float)Math.Min(i / octaveLength, 1.0);
        }

        // Time reversal performs the deconvolution. The exponential envelope compensates
        // for the sweep spending progressively less time per hertz at high frequencies.
        double inverseScale = Octaves * Math.Log(2.0) / (1.0 - Math.Pow(2.0, -Octaves));
        double perSampleDecay = Math.Pow(2.0, Octaves / (double)sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            inverseFilter[i] =
                (float)(sweepData[sampleCount - i - 1] * Math.Pow(perSampleDecay, -i) * inverseScale);
        }

        streamSet = new PcmStreamSet(sweepData, SampleRate, BitsPerSample);
        generated = true;
    }

    public RawSourceWaveStream GetStream(PlaybackChannel channel)
    {
        EnsureGenerated();
        return streamSet!.GetStream(channel);
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
