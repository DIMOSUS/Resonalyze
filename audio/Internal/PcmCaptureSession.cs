namespace Resonalyze.Audio;

internal sealed class PcmCaptureSession : IAsyncDisposable, ISweepCaptureSession
{
    private readonly object sync = new();
    private readonly IAudioCaptureDevice device;
    private readonly IInterleavedSampleDecoder decoder;
    private readonly SampleWaiterRegistry sampleWaiters = new();
    private readonly int expectedSamples;
    private readonly PcmCapturePump capturePump;
    private readonly AudioLevelAccumulator levelAccumulator;
    private readonly Action? beforeSnapshotCopy;
    private CaptureAccumulator? accumulator;
    private float[][] decodeScratch = Array.Empty<float[]>();
    private double[] meterPeaks = Array.Empty<double>();
    private double[] meterSumSquares = Array.Empty<double>();
    private TaskCompletionSource<bool>? firstBufferReady;
    private TaskCompletionSource<Exception?>? deviceStopped;
    private long discontinuityCount;
    private long silentPacketCount;
    private long timestampErrorCount;
    // A waiter registered after the stop (the sweep waiter comes only after playback) faults at once. Cleared by StartAsync, not Reset.
    private Exception? terminalException;
    private bool paused;
    private int captureGeneration;
    private bool disposed;

    public PcmCaptureSession(
        IAudioCaptureDevice device,
        int sequence = 0,
        int expectedSamples = 0,
        IInterleavedSampleDecoder? decoder = null,
        Action? beforeSnapshotCopy = null)
    {
        this.device = device ?? throw new ArgumentNullException(nameof(device));
        this.decoder = decoder ?? InterleavedSampleDecoder.Create(device.CaptureFormat);
        if (this.decoder.ChannelCount != device.ChannelCount)
        {
            throw new ArgumentException("Decoder channel count must match the capture device.", nameof(decoder));
        }
        Sequence = sequence;
        this.expectedSamples = expectedSamples;
        this.beforeSnapshotCopy = beforeSnapshotCopy;
        accumulator = CreateAccumulator();
        levelAccumulator = new AudioLevelAccumulator(device.ChannelCount, device.CaptureFormat.SampleRate);
        capturePump = new PcmCapturePump(
            device.MaximumPacketBytes,
            ProcessCaptureBlock,
            HandleCaptureFailure);
        device.DataAvailable += HandleDataAvailable;
        device.Stopped += HandleStopped;
    }

    public event Action<float[]>? SequenceReady;
    public event Action<float[][]>? SequenceChannelsReady;
    public event Action? CaptureDiscontinuity;
    internal event Action<AudioChannelLevel[]>? LevelsAvailable;

    public int Sequence { get; set; }
    public int ReadSamples => accumulator?.ReadSamples ?? 0;
    public int AcceptedSamples => capturePump.AcceptedFrames;
    public long DiscontinuityCount => Interlocked.Read(ref discontinuityCount);
    public long SilentPacketCount => Interlocked.Read(ref silentPacketCount);
    public long TimestampErrorCount => Interlocked.Read(ref timestampErrorCount);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Reset();
        lock (sync)
        {
            terminalException = null;
        }
        firstBufferReady = SampleWaiterRegistry.NewSignal();
        deviceStopped = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
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

    public async Task WaitForStopAsync(CancellationToken cancellationToken)
    {
        Task<Exception?> task = deviceStopped?.Task ??
            throw new InvalidOperationException("Capture has not been started.");
        Exception? exception = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        throw exception ?? new InvalidOperationException("The capture device stopped unexpectedly.");
    }

    public Task WaitForSamplesAsync(int sampleCount, CancellationToken cancellationToken)
    {
        if (sampleCount <= 0)
        {
            return Task.CompletedTask;
        }
        lock (sync)
        {
            if ((accumulator?.ReadSamples ?? 0) >= sampleCount)
            {
                return Task.CompletedTask;
            }
            // A stopped device delivers no more samples and no stop event: fault instead of registering a waiter.
            if (terminalException != null)
            {
                return Task.FromException(terminalException);
            }
            return sampleWaiters.Add(sampleCount, cancellationToken);
        }
    }

    public float[][] CompleteCaptureSnapshot()
    {
        CaptureAccumulator? completed;
        Exception? captureFailure;
        lock (sync)
        {
            int completedGeneration = captureGeneration;
            int newGeneration = ++captureGeneration;
            completed = accumulator;
            accumulator = null;
            paused = true;
            sampleWaiters.CancelAll();
            captureFailure = capturePump.CompleteGeneration(
                completedGeneration,
                newGeneration);
        }

        if (captureFailure != null)
        {
            throw captureFailure;
        }
        beforeSnapshotCopy?.Invoke();
        return completed?.Snapshot() ?? Array.Empty<float[]>();
    }

    public void Reset()
    {
        CaptureAccumulator freshAccumulator = CreateAccumulator();
        lock (sync)
        {
            int newGeneration = ++captureGeneration;
            accumulator = freshAccumulator;
            sampleWaiters.CancelAll();
            paused = false;
            // Same lock as ProcessCaptureBlock's final check+append; an in-flight packet keeps its old generation.
            capturePump.Reset(newGeneration);
        }
    }

    /// <summary>Drops captured samples between averaged runs while meters stay live; WASAPI cannot be restarted. <see cref="Reset"/> resumes. See docs/tech/audio-layer.md#averaged-runs-keep-the-device-open.</summary>
    public void Pause()
    {
        lock (sync)
        {
            paused = true;
            sampleWaiters.CancelAll();
        }
    }

    private CaptureAccumulator CreateAccumulator() => new(
        device.ChannelCount,
        Sequence,
        Math.Max(expectedSamples, device.CaptureFormat.SampleRate));

    private void HandleDataAvailable(AudioCapturePacket packet)
    {
        try
        {
            capturePump.TryEnqueue(packet);
        }
        catch (Exception exception)
        {
            HandleCaptureFailure(Volatile.Read(ref captureGeneration), exception);
        }
    }

    internal void ProcessCaptureBlock(PcmCaptureBlock block)
    {
        int frameCount = block.BytesRecorded / device.CaptureFormat.BlockAlign;
        EnsureScratch(frameCount);
        int decodedFrames = decoder.Decode(block.Buffer.AsSpan(0, block.BytesRecorded), decodeScratch);
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

        List<float[][]>? readySequences = null;
        lock (sync)
        {
            if (block.Generation != captureGeneration)
            {
                return;
            }

            if (!paused && accumulator is { } activeAccumulator)
            {
                activeAccumulator.Append(decodeScratch, decodedFrames);
                readySequences = activeAccumulator.ExtractReadySequences();
                sampleWaiters.CompleteUpTo(activeAccumulator.ReadSamples);
            }
        }

        if (block.Discontinuity)
        {
            Interlocked.Increment(ref discontinuityCount);
            EventPublisher.Publish(CaptureDiscontinuity);
        }
        if (block.Silent)
        {
            Interlocked.Increment(ref silentPacketCount);
        }
        if (block.TimestampError)
        {
            Interlocked.Increment(ref timestampErrorCount);
        }
        firstBufferReady?.TrySetResult(true);
        AudioChannelLevel[]? levels = levelAccumulator.AddBlock(
            meterPeaks,
            meterSumSquares,
            decodedFrames);
        if (levels != null)
        {
            EventPublisher.Publish(LevelsAvailable, levels);
        }
        if (readySequences == null)
        {
            return;
        }
        foreach (float[][] sequence in readySequences)
        {
            EventPublisher.Publish(SequenceReady, sequence[0]);
            EventPublisher.Publish(SequenceChannelsReady, sequence);
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
        deviceStopped?.TrySetResult(args.Exception);
        firstBufferReady?.TrySetException(exception);
        lock (sync)
        {
            terminalException ??= exception;
            sampleWaiters.FaultAll(exception);
        }
    }

    private void HandleCaptureFailure(int generation, Exception exception)
    {
        lock (sync)
        {
            if (generation != captureGeneration)
            {
                return;
            }

            deviceStopped?.TrySetResult(exception);
            firstBufferReady?.TrySetException(exception);
            terminalException ??= exception;
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
        SequenceReady = null;
        SequenceChannelsReady = null;
        CaptureDiscontinuity = null;
        LevelsAvailable = null;
        capturePump.Dispose();
        lock (sync)
        {
            sampleWaiters.CancelAll();
        }
        await device.DisposeAsync().ConfigureAwait(false);
    }
}
