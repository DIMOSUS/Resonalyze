namespace Resonalyze.Audio;

internal sealed class PcmCaptureSession : IAsyncDisposable, ISweepCaptureSession
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
    private TaskCompletionSource<Exception?>? deviceStopped;
    // A terminal device failure is remembered so a sample waiter registered
    // AFTER the stop (e.g. the sweep waiter, created only once playback ends)
    // faults immediately instead of hanging forever. Cleared only by a real
    // StartAsync, never by Reset between averaged runs.
    private Exception? terminalException;
    private bool paused;
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
    public event Action? CaptureDiscontinuity;
    internal event Action<AudioChannelLevel[]>? LevelsAvailable;

    public int Sequence { get; set; }
    public int ReadSamples => accumulator.ReadSamples;
    public long DiscontinuityCount { get; private set; }
    public long SilentPacketCount { get; private set; }
    public long TimestampErrorCount { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Reset();
        lock (sync)
        {
            // A real (re)start clears any remembered terminal failure.
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
            if (accumulator.ReadSamples >= sampleCount)
            {
                return Task.CompletedTask;
            }
            // The samples are not all here and a stopped device will deliver
            // neither more of them nor a fresh stop event: fault immediately
            // rather than register a waiter that only an Abort could complete.
            if (terminalException != null)
            {
                return Task.FromException(terminalException);
            }
            return sampleWaiters.Add(sampleCount, cancellationToken);
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
            // Resetting for the next run resumes accumulation after a pause.
            paused = false;
        }
    }

    /// <summary>
    /// Stops appending captured samples while the device keeps running (and keeps
    /// raising level meters) — used between averaged sweep runs, where a long
    /// confirmation pause would otherwise grow the capture buffer without bound.
    /// The old Wave path stopped the device between runs; WASAPI cannot be
    /// restarted, so it drops packets instead. <see cref="Reset"/> resumes.
    /// </summary>
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

    private void HandleDataAvailable(object? sender, AudioCaptureDataEventArgs args)
    {
        if (args.Discontinuity)
        {
            DiscontinuityCount++;
            CaptureDiscontinuity?.Invoke();
        }
        if (args.Silent)
        {
            SilentPacketCount++;
        }
        if (args.TimestampError)
        {
            TimestampErrorCount++;
        }
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

        List<float[][]>? readySequences = null;
        lock (sync)
        {
            // While paused (between averaged runs) the device keeps running so the
            // level meter stays live, but samples are dropped instead of appended —
            // otherwise a long confirmation pause grows the buffer without bound.
            if (!paused)
            {
                accumulator.Append(decodeScratch, decodedFrames);
                readySequences = accumulator.ExtractReadySequences();
                sampleWaiters.CompleteUpTo(accumulator.ReadSamples);
            }
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
        deviceStopped?.TrySetResult(args.Exception);
        firstBufferReady?.TrySetException(exception);
        lock (sync)
        {
            // Remember the failure so a waiter registered after this point (the
            // sweep waiter is created only once playback ends) faults at once.
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
        lock (sync)
        {
            sampleWaiters.CancelAll();
        }
        await device.DisposeAsync().ConfigureAwait(false);
    }
}
