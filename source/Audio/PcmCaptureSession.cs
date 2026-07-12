namespace Resonalyze;

public sealed class PcmCaptureSession : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly IAudioCaptureDevice device;
    private readonly IInterleavedSampleDecoder decoder;
    private readonly SampleWaiterRegistry sampleWaiters = new();
    private readonly int expectedSamples;
    private CaptureAccumulator accumulator;
    private float[][] decodeScratch = Array.Empty<float[]>();
    private double[] meterPeaks = Array.Empty<double>();
    private double[] meterSumSquares = Array.Empty<double>();
    private TaskCompletionSource<bool>? firstBufferReady;
    private bool disposed;

    public PcmCaptureSession(IAudioCaptureDevice device, int sequence = 0, int expectedSamples = 0)
    {
        this.device = device ?? throw new ArgumentNullException(nameof(device));
        decoder = InterleavedSampleDecoder.Create(device.CaptureFormat);
        Sequence = sequence;
        this.expectedSamples = expectedSamples;
        accumulator = CreateAccumulator();
        device.DataAvailable += HandleDataAvailable;
        device.Stopped += HandleStopped;
    }

    public event Action<float[]>? SequenceReady;
    public event Action<float[][]>? SequenceChannelsReady;
    internal event Action<AudioChannelLevel[]>? LevelsAvailable;

    public int Sequence { get; set; }
    public int ReadSamples => accumulator.ReadSamples;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Reset();
        firstBufferReady = SampleWaiterRegistry.NewSignal();
        try
        {
            await device.StartAsync(cancellationToken).ConfigureAwait(false);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => firstBufferReady.TrySetCanceled(cancellationToken));
            await firstBufferReady.Task.ConfigureAwait(false);
        }
        catch
        {
            await device.StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task StopAsync() => device.StopAsync();

    public Task WaitForSamplesAsync(int sampleCount, CancellationToken cancellationToken)
    {
        if (sampleCount <= 0)
        {
            return Task.CompletedTask;
        }
        lock (sync)
        {
            return accumulator.ReadSamples >= sampleCount
                ? Task.CompletedTask
                : sampleWaiters.Add(sampleCount, cancellationToken);
        }
    }

    public float[][] GetSamplesSnapshot()
    {
        lock (sync)
        {
            return accumulator.Snapshot();
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            accumulator = CreateAccumulator();
            sampleWaiters.CancelAll();
        }
    }

    private CaptureAccumulator CreateAccumulator() => new(
        device.ChannelCount,
        Sequence,
        Math.Max(expectedSamples, device.CaptureFormat.SampleRate));

    private void HandleDataAvailable(object? sender, AudioCaptureDataEventArgs args)
    {
        int frameCount = args.BytesRecorded / args.Format.BlockAlign;
        EnsureScratch(frameCount);
        int decodedFrames = decoder.Decode(args.Buffer.Span[..args.BytesRecorded], decodeScratch);
        Array.Clear(meterPeaks);
        Array.Clear(meterSumSquares);
        for (int channel = 0; channel < device.ChannelCount; channel++)
        {
            for (int frame = 0; frame < decodedFrames; frame++)
            {
                float sample = decodeScratch[channel][frame];
                meterPeaks[channel] = Math.Max(meterPeaks[channel], Math.Abs(sample));
                meterSumSquares[channel] += sample * sample;
            }
        }

        List<float[][]>? readySequences;
        lock (sync)
        {
            accumulator.Append(decodeScratch, decodedFrames);
            readySequences = accumulator.ExtractReadySequences();
            sampleWaiters.CompleteUpTo(accumulator.ReadSamples);
        }

        firstBufferReady?.TrySetResult(true);
        LevelsAvailable?.Invoke(
            AudioLevelMetering.MeasureChannels(meterPeaks, meterSumSquares, decodedFrames));
        if (readySequences == null)
        {
            return;
        }
        foreach (float[][] sequence in readySequences)
        {
            SequenceReady?.Invoke(sequence[0]);
            SequenceChannelsReady?.Invoke(sequence);
        }
    }

    private void EnsureScratch(int frames)
    {
        if (decodeScratch.Length == device.ChannelCount &&
            decodeScratch.Length > 0 && decodeScratch[0].Length >= frames)
        {
            return;
        }
        decodeScratch = Enumerable.Range(0, device.ChannelCount)
            .Select(_ => new float[frames])
            .ToArray();
        meterPeaks = new double[device.ChannelCount];
        meterSumSquares = new double[device.ChannelCount];
    }

    private void HandleStopped(object? sender, AudioDeviceStoppedEventArgs args)
    {
        Exception exception = args.Exception ?? new InvalidOperationException(
            "Recording stopped before the requested samples arrived.");
        firstBufferReady?.TrySetException(exception);
        lock (sync)
        {
            sampleWaiters.FaultAll(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        device.DataAvailable -= HandleDataAvailable;
        device.Stopped -= HandleStopped;
        lock (sync)
        {
            sampleWaiters.CancelAll();
        }
        await device.DisposeAsync().ConfigureAwait(false);
    }
}
