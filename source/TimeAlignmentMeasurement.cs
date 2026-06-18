using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Coordinates ASIO loopback time-alignment measurement and builds microphone/reference impulse responses.
/// </summary>
public sealed class TimeAlignmentMeasurement : IDisposable
{
    private readonly object stateSync = new();
    private CancellationTokenSource? cancellationTokenSource;
    private Task<bool>? measurementTask;
    private volatile bool inProgress;
    private bool disposed;

    public event Action<bool>? Completed;

    public ExponentialSineSweep? Sweep { get; private set; }
    public Complex[]? MicrophoneImpulseResponse { get; private set; }
    public Complex[]? LoopbackImpulseResponse { get; private set; }
    public bool InProgress => inProgress;
    public int SampleRate { get; private set; }
    public int Octaves { get; private set; }
    public int Bits { get; private set; }
    public string? AsioDriverName { get; private set; }
    public int MicrophoneInputChannelOffset { get; private set; }
    public int LoopbackInputChannelOffset { get; private set; }
    public int AsioOutputChannelOffset { get; private set; }
    public TimeAlignmentOptions Options { get; private set; } = new();
    public double PeakSample { get; private set; }
    public double DelayMilliseconds { get; private set; }
    public double[]? EnvelopeSamples { get; private set; }
    public int EnvelopePeakIndex { get; private set; }
    public double EnvelopePeak { get; private set; }
    public double ConfidenceDecibels { get; private set; }
    public double MicrophonePeakDbFs { get; private set; }
    public double MicrophoneRmsDbFs { get; private set; }
    public bool MicrophoneClipped { get; private set; }
    public double LoopbackPeakDbFs { get; private set; }
    public double LoopbackRmsDbFs { get; private set; }
    public bool LoopbackClipped { get; private set; }
    public Exception? LastError { get; private set; }

    public void Init(
        int octaves,
        int sampleRate,
        int bits,
        double requestedDuration,
        string? asioDriverName,
        int microphoneInputChannelOffset,
        int loopbackInputChannelOffset,
        int asioOutputChannelOffset,
        TimeAlignmentOptions options)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        if (InProgress)
        {
            throw new InvalidOperationException("Cannot reinitialize an active time-alignment measurement.");
        }
        if (string.IsNullOrWhiteSpace(asioDriverName))
        {
            throw new InvalidOperationException("Time Alignment requires an ASIO driver.");
        }
        if (microphoneInputChannelOffset == loopbackInputChannelOffset)
        {
            throw new InvalidOperationException("Microphone and loopback inputs must use different ASIO channels.");
        }

        SampleRate = sampleRate;
        Bits = bits;
        Octaves = octaves;
        AsioDriverName = asioDriverName;
        MicrophoneInputChannelOffset = microphoneInputChannelOffset;
        LoopbackInputChannelOffset = loopbackInputChannelOffset;
        AsioOutputChannelOffset = asioOutputChannelOffset;
        Options = options;
        MicrophoneImpulseResponse = null;
        LoopbackImpulseResponse = null;
        ResetMeasurementResult();
        LastError = null;

        Sweep?.Dispose();
        Sweep = new ExponentialSineSweep();
        Sweep.FillData(octaves, requestedDuration, bits, sampleRate);
    }

    public Task<bool> RunAsync()
    {
        ThrowIfDisposed();
        lock (stateSync)
        {
            if (measurementTask is { IsCompleted: false })
            {
                return measurementTask;
            }
            if (Sweep == null)
            {
                throw new InvalidOperationException("Time-alignment measurement is not initialized.");
            }

            cancellationTokenSource?.Dispose();
            cancellationTokenSource = new CancellationTokenSource();
            inProgress = true;
            MicrophoneImpulseResponse = null;
            LoopbackImpulseResponse = null;
            ResetMeasurementResult();
            LastError = null;
            measurementTask = RunCoreAsync(cancellationTokenSource.Token);
            return measurementTask;
        }
    }

    public async Task AbortAsync()
    {
        Task<bool>? runningTask;
        lock (stateSync)
        {
            cancellationTokenSource?.Cancel();
            runningTask = measurementTask;
        }

        if (runningTask != null)
        {
            try
            {
                await runningTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task<bool> RunCoreAsync(CancellationToken cancellationToken)
    {
        bool success = false;
        try
        {
            await RunAsioAsync(Sweep!, cancellationToken).ConfigureAwait(false);
            success = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            LastError = exception;
        }
        finally
        {
            lock (stateSync)
            {
                inProgress = false;
            }
            try
            {
                Completed?.Invoke(success);
            }
            catch
            {
                // UI subscribers must not break measurement cleanup.
            }
        }

        return success;
    }

    private async Task RunAsioAsync(
        ExponentialSineSweep sweep,
        CancellationToken cancellationToken)
    {
        int firstInputOffset = Math.Min(
            MicrophoneInputChannelOffset,
            LoopbackInputChannelOffset);
        int lastInputOffset = Math.Max(
            MicrophoneInputChannelOffset,
            LoopbackInputChannelOffset);
        int inputChannelCount = lastInputOffset - firstInputOffset + 1;

        using var session = new AsioFullDuplexSession(
            AsioDriverName ?? string.Empty,
            firstInputOffset,
            AsioOutputChannelOffset,
            inputChannelCount);
        using FloatArrayWaveStream stream = FloatArrayWaveStream.FromMonoSamples(
            sweep.SweepData,
            SampleRate,
            PlaybackChannel.Mono);

        await session.StartAsync(
            stream,
            SampleRate,
            autoStop: false,
            cancellationToken).ConfigureAwait(false);
        int requiredSamples = session.ReadSamples + sweep.SweepSamples + SampleRate;
        await session.WaitForSamplesAsync(requiredSamples, cancellationToken).ConfigureAwait(false);
        await session.StopAsync().ConfigureAwait(false);

        float[][] samples = session.GetSamplesSnapshot();
        int microphoneIndex = MicrophoneInputChannelOffset - firstInputOffset;
        int loopbackIndex = LoopbackInputChannelOffset - firstInputOffset;
        if ((uint)microphoneIndex >= (uint)samples.Length ||
            (uint)loopbackIndex >= (uint)samples.Length)
        {
            throw new InvalidOperationException("Recorded ASIO channel snapshot is incomplete.");
        }

        int fftLength = DspMath.NextPowerOfTwo(
            checked(samples[microphoneIndex].Length * 2));
        double[]? filter = Options.UseBandpassWindow
            ? BandpassWindow.Create(
                fftLength,
                SampleRate,
                Options.BandpassCenterHz,
                Options.BandpassPassOctaves,
                Options.BandpassFadeOctaves)
            : null;

        double[] loopback = Array.ConvertAll(samples[loopbackIndex], x => (double)x);
        double[] microphone = Array.ConvertAll(samples[microphoneIndex], x => (double)x);
        CaptureSignalLevels(microphone, loopback);

        double[] relativeImpulseResponse = TransferFunction.ComputeRelativeIr(
            loopback,
            microphone,
            1e-12,
            filter);

        var envelope = SignalEnvelope.Envelope(relativeImpulseResponse);
        double peak = 0;
        int peakIndex = 0;
        for (int i = 0; i < envelope.Length; i++)
        {
            if (envelope[i] > peak)
            {
                peak = envelope[i];
                peakIndex = i;
            }
        }

        double fractionalOffset = 0;
        if (peakIndex > 0 && peakIndex < (envelope.Length - 1))
        {
            double y0 = envelope[peakIndex - 1];
            double y1 = envelope[peakIndex];
            double y2 = envelope[peakIndex + 1];
            fractionalOffset = FindFractionalPeakOffset(y0, y1, y2);
        }

        ConfidenceDecibels = EstimatePeakConfidenceDecibels(
            envelope,
            peakIndex,
            peak);
        EnvelopeSamples = envelope;
        EnvelopePeakIndex = peakIndex;
        EnvelopePeak = peak;
        double wrappedPeakSample = peakIndex + fractionalOffset;
        PeakSample = ToSignedDelaySamples(wrappedPeakSample, envelope.Length);
        DelayMilliseconds = PeakSample * 1000.0 / SampleRate;
    }

    private static double FindFractionalPeakOffset(double previous, double center, double next)
    {
        double denominator = previous - 2.0 * center + next;
        if (Math.Abs(denominator) < 1e-12)
        {
            return 0.0;
        }

        double offset = 0.5 * (previous - next) / denominator;
        return Math.Clamp(offset, -0.5, 0.5);
    }

    private static double ToSignedDelaySamples(double wrappedPeakSample, int length) =>
        wrappedPeakSample <= length * 0.5
            ? wrappedPeakSample
            : wrappedPeakSample - length;

    private void CaptureSignalLevels(
        IReadOnlyList<double> microphone,
        IReadOnlyList<double> loopback)
    {
        (MicrophonePeakDbFs, MicrophoneRmsDbFs, MicrophoneClipped) =
            MeasureSignalLevel(microphone);
        (LoopbackPeakDbFs, LoopbackRmsDbFs, LoopbackClipped) =
            MeasureSignalLevel(loopback);
    }

    private static (double PeakDbFs, double RmsDbFs, bool Clipped) MeasureSignalLevel(
        IReadOnlyList<double> samples)
    {
        double peak = 0;
        double sumSquares = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            double magnitude = Math.Abs(samples[i]);
            peak = Math.Max(peak, magnitude);
            sumSquares += samples[i] * samples[i];
        }

        double rms = samples.Count > 0
            ? Math.Sqrt(sumSquares / samples.Count)
            : 0;
        return (
            DataHelper.AmplitudeToDecibels(peak),
            DataHelper.AmplitudeToDecibels(rms),
            peak >= 0.999);
    }

    private static double EstimatePeakConfidenceDecibels(
        IReadOnlyList<double> envelope,
        int peakIndex,
        double peak)
    {
        int exclusionRadius = Math.Max(8, envelope.Count / 200);
        double sumSquares = 0;
        int count = 0;
        for (int i = 0; i < envelope.Count; i++)
        {
            if (Math.Abs(i - peakIndex) <= exclusionRadius)
            {
                continue;
            }

            sumSquares += envelope[i] * envelope[i];
            count++;
        }

        double noiseRms = count > 0
            ? Math.Sqrt(sumSquares / count)
            : 0;
        return DataHelper.AmplitudeToDecibels(peak / Math.Max(noiseRms, 1e-12));
    }

    private void ResetMeasurementResult()
    {
        PeakSample = 0;
        DelayMilliseconds = 0;
        EnvelopeSamples = null;
        EnvelopePeakIndex = 0;
        EnvelopePeak = 0;
        ConfidenceDecibels = 0;
        MicrophonePeakDbFs = 0;
        MicrophoneRmsDbFs = 0;
        MicrophoneClipped = false;
        LoopbackPeakDbFs = 0;
        LoopbackRmsDbFs = 0;
        LoopbackClipped = false;
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
        cancellationTokenSource?.Cancel();
        try
        {
            measurementTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        cancellationTokenSource?.Dispose();
        Sweep?.Dispose();
        GC.SuppressFinalize(this);
    }
}
