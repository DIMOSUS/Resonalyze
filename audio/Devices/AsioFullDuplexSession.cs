using System.Diagnostics;
using NAudio.Wave;

namespace Resonalyze.Audio;

internal sealed class AsioFullDuplexSession : IDisposable
{
    private readonly object sync = new();
    private readonly SampleWaiterRegistry sampleWaiters = new();
    private readonly string driverName;
    private readonly int inputChannelOffset;
    private readonly int outputChannelOffset;
    private readonly int driverRecordChannelCount;
    private readonly AsioSampleConverter sampleConverter = new();
    private readonly AsioCapturePump capturePump;
    private readonly Action? beforeCaptureCommit;
    private readonly Action? beforeSnapshotCopy;
    private AsioOut? driver;
    private Timer? watchdogTimer;
    private AsioCallbackWatchdog? watchdog;
    private long watchdogStart;
    private long callbackCount;
    private int watchdogGeneration;
    private CaptureAccumulator? accumulator;
    private float[][] convertScratch = Array.Empty<float[]>();
    private double[] meterPeaks = Array.Empty<double>();
    private double[] meterSumSquares = Array.Empty<double>();
    private AudioLevelAccumulator? levelAccumulator;
    private int expectedTotalSamples;
    private int captureGeneration;
    private TaskCompletionSource<bool>? firstBufferReady;
    private TaskCompletionSource<bool>? playbackStopped;
    // A waiter registered after the driver stopped faults at once. Cleared by StartAsync, never by ResetCapture.
    private Exception? terminalException;
    private bool disposed;

    public event Action<float[]>? SequenceReady;
    public event Action<float[][]>? SequenceChannelsReady;
    public event Action<AudioChannelLevel[]>? LevelsAvailable;

    public AsioFullDuplexSession(
        string driverName,
        int inputChannelOffset,
        int outputChannelOffset,
        int inputChannelCount = 1,
        Action? beforeCaptureCommit = null,
        Action? beforeSnapshotCopy = null)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            throw new InvalidOperationException("ASIO driver is not selected.");
        }
        if (inputChannelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputChannelCount));
        }
        if (inputChannelOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputChannelOffset));
        }

        this.driverName = driverName;
        this.inputChannelOffset = inputChannelOffset;
        this.outputChannelOffset = outputChannelOffset;
        this.beforeCaptureCommit = beforeCaptureCommit;
        this.beforeSnapshotCopy = beforeSnapshotCopy;
        ChannelCount = inputChannelCount;
        driverRecordChannelCount = inputChannelOffset + inputChannelCount;
        capturePump = new AsioCapturePump(ChannelCount, ProcessCaptureBlock, HandleCaptureFailure);
    }

    public int Sequence { get; set; }
    public int ReadSamples => accumulator?.ReadSamples ?? 0;
    // A sweep position: its run starts with a Reset, and Live never reads it.
    public int AcceptedSamples => checked((int)capturePump.AcceptedFrames);
    public int ChannelCount { get; }

    public async Task StartAsync(
        IWaveProvider playbackProvider,
        int sampleRate,
        bool autoStop,
        CancellationToken cancellationToken,
        int expectedTotalSamples = 0)
    {
        ThrowIfDisposed();
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        this.expectedTotalSamples = expectedTotalSamples;
        StopAndDisposeDriver();
        ResetBuffers();
        levelAccumulator = new AudioLevelAccumulator(ChannelCount, sampleRate);
        lock (sync)
        {
            terminalException = null;
        }

        firstBufferReady = NewSignal();
        playbackStopped = NewSignal();
        driver = new AsioOut(driverName)
        {
            AutoStop = autoStop,
            InputChannelOffset = 0,
            ChannelOffset = outputChannelOffset
        };
        try
        {
            if (!driver.IsSampleRateSupported(sampleRate))
            {
                throw new InvalidOperationException(
                    $"ASIO driver '{driverName}' does not support {sampleRate} Hz.");
            }
            if (inputChannelOffset < 0 ||
                driverRecordChannelCount > driver.DriverInputChannelCount)
            {
                throw new InvalidOperationException(
                    $"ASIO input channel {inputChannelOffset + 1} is not available for driver '{driverName}'.");
            }
            if (outputChannelOffset < 0 ||
                outputChannelOffset + playbackProvider.WaveFormat.Channels > driver.DriverOutputChannelCount)
            {
                throw new InvalidOperationException(
                    $"ASIO output channel pair starting at {outputChannelOffset + 1} is not available for driver '{driverName}'.");
            }

            driver.AudioAvailable += ReceiveAudio;
            driver.PlaybackStopped += PlaybackStopped;
            driver.InitRecordAndPlayback(
                playbackProvider,
                driverRecordChannelCount,
                sampleRate);
            // After the rate is set: many drivers answer the host's own rate change with a reset request.
            driver.DriverResetRequest += DriverResetRequested;
            capturePump.Prepare(checked(driver.FramesPerBuffer * sizeof(float)));
            driver.Play();
            StartWatchdog();

            using CancellationTokenRegistration registration =
                cancellationToken.Register(() => firstBufferReady.TrySetCanceled(cancellationToken));
            await firstBufferReady.Task.ConfigureAwait(false);
        }
        catch
        {
            // A driver failing startup must be detached here, or its callbacks run until owner teardown.
            StopAndDisposeDriver();
            throw;
        }
    }

    /// <summary>Restarts only the accumulator on the running driver: averaged runs skip driver re-init (seconds on slow drivers).</summary>
    public void ResetCapture(int expectedTotalSamples)
    {
        ThrowIfDisposed();
        this.expectedTotalSamples = expectedTotalSamples;
        ResetBuffers();
    }

    public Task WaitForSamplesAsync(int sampleCount, CancellationToken cancellationToken)
    {
        if (sampleCount <= 0)
        {
            return Task.CompletedTask;
        }

        lock (sync)
        {
            if (ReadSamples >= sampleCount)
            {
                return Task.CompletedTask;
            }
            // A stopped driver delivers no more samples and no stop event: fault instead of registering a waiter.
            if (terminalException != null)
            {
                return Task.FromException(terminalException);
            }

            return sampleWaiters.Add(sampleCount, cancellationToken);
        }
    }

    public async Task StopAsync()
    {
        AsioOut? activeDriver;
        TaskCompletionSource<bool>? stoppedSignal;
        lock (sync)
        {
            activeDriver = driver;
            stoppedSignal = playbackStopped;
        }

        if (activeDriver == null)
        {
            return;
        }

        StopWatchdog();
        try
        {
            await AudioCaptureStop.StopAndWaitAsync(
                activeDriver.Stop,
                stoppedSignal,
                stoppedSignal?.Task ?? Task.CompletedTask,
                "The ASIO driver").ConfigureAwait(false);
        }
        finally
        {
            DetachAndDisposeDriver(activeDriver);
        }
    }

    /// <summary>Finishes blocks accepted before the callback stopped; normal teardown drops them on purpose.</summary>
    internal void DrainCapture() => capturePump.Drain();

    /// <summary>Atomically ends the accumulation epoch; the large copy runs outside the lock so the worker keeps metering.</summary>
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

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SequenceReady = null;
        SequenceChannelsReady = null;
        LevelsAvailable = null;
        StopAndDisposeDriver();
        capturePump.Dispose();
        lock (sync)
        {
            sampleWaiters.CancelAll();
        }

        GC.SuppressFinalize(this);
    }

    // NAudio fills playback only after this returns: bounded copies only, the worker does the rest.
    private void ReceiveAudio(object? sender, AsioAudioAvailableEventArgs args)
    {
        Interlocked.Increment(ref callbackCount);
        if (args.InputBuffers.Length < driverRecordChannelCount)
        {
            firstBufferReady?.TrySetException(new InvalidOperationException(
                $"ASIO callback returned {args.InputBuffers.Length} input buffers, " +
                $"but {driverRecordChannelCount} were expected."));
            return;
        }

        try
        {
            capturePump.TryEnqueue(
                args.InputBuffers,
                inputChannelOffset,
                args.AsioSampleType,
                args.SamplesPerBuffer);
        }
        catch (Exception exception)
        {
            HandleCaptureFailure(Volatile.Read(ref captureGeneration), exception);
        }
    }

    internal void ProcessCaptureBlock(AsioCaptureBlock block)
    {
        int frames = block.FrameCount;
        EnsureScratch(frames);
        double[] peaks = meterPeaks;
        double[] sumSquares = meterSumSquares;
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            float[] scratch = convertScratch[channel];
            sampleConverter.Convert(
                block.Channels[channel],
                block.SampleType,
                scratch,
                frames);
            double peak = 0;
            double sum = 0;
            for (int i = 0; i < frames; i++)
            {
                double sample = scratch[i];
                double magnitude = Math.Abs(sample);
                peak = Math.Max(peak, magnitude);
                sum += sample * sample;
            }

            peaks[channel] = peak;
            sumSquares[channel] = sum;
        }

        // A paused capture (null accumulator) keeps metering between averaging runs.
        List<float[][]>? readySequences = null;
        beforeCaptureCommit?.Invoke();
        lock (sync)
        {
            if (block.Generation != captureGeneration)
            {
                return;
            }

            if (accumulator != null)
            {
                accumulator.Append(convertScratch, frames);
                readySequences = accumulator.ExtractReadySequences();
                sampleWaiters.CompleteUpTo(accumulator.ReadSamples);
            }
        }

        firstBufferReady?.TrySetResult(true);
        AudioChannelLevel[]? levels = levelAccumulator?.AddBlock(peaks, sumSquares, frames);
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

    private void HandleCaptureFailure(int generation, Exception exception)
    {
        lock (sync)
        {
            // Paused between averaged runs (no accumulator): the samples are dropped anyway, and the next run's reset starts
            // the pump afresh, so a backlog while the last run's analysis holds the cores is not the device failing. Any
            // other failure is.
            if (generation != captureGeneration || (accumulator == null && capturePump.IsOverflow(exception)))
            {
                return;
            }

            FailLocked(exception);
        }
    }

    /// <summary>The driver asked the host to reset it (settings changed, device removed): it delivers no more audio.</summary>
    internal void ReportDriverReset() =>
        Fail(new InvalidOperationException(
            $"The ASIO driver '{driverName}' asked to be reset (its settings changed or the device was removed). " +
            "Check the device, then measure again."));

    // A request before the first buffer is the driver acknowledging the rate this start set, not a lost device; a driver
    // that really died before then is caught by the watchdog.
    private void DriverResetRequested(object? sender, EventArgs args)
    {
        if (firstBufferReady is { Task.IsCompletedSuccessfully: true })
        {
            ReportDriverReset();
        }
    }

    private void Fail(Exception exception)
    {
        lock (sync)
        {
            FailLocked(exception);
        }
    }

    private void FailLocked(Exception exception)
    {
        firstBufferReady?.TrySetException(exception);
        playbackStopped?.TrySetException(exception);
        terminalException ??= exception;
        sampleWaiters.FaultAll(exception);
    }

    // The generation keeps a tick of an old run's timer, already past its check, from stopping a newer run's watchdog.
    private void CheckCallbacks(int generation)
    {
        Timer? stalledTimer;
        lock (sync)
        {
            if (generation != watchdogGeneration ||
                watchdog is not { } current ||
                !current.IsStalled(Interlocked.Read(ref callbackCount), Stopwatch.GetElapsedTime(watchdogStart)))
            {
                return;
            }

            FailLocked(new InvalidOperationException(
                $"The ASIO driver '{driverName}' delivered no audio for " +
                $"{AsioCallbackWatchdog.StallTimeout.TotalSeconds:0} seconds (device removed or driver error). " +
                "Check the device, then measure again."));
            stalledTimer = DetachWatchdogLocked();
        }

        stalledTimer?.Dispose();
    }

    private void StartWatchdog()
    {
        Timer? previous;
        lock (sync)
        {
            previous = DetachWatchdogLocked();
            int generation = watchdogGeneration;
            watchdogStart = Stopwatch.GetTimestamp();
            watchdog = new AsioCallbackWatchdog(Interlocked.Read(ref callbackCount), TimeSpan.Zero);
            watchdogTimer = new Timer(
                _ => CheckCallbacks(generation),
                null,
                AsioCallbackWatchdog.CheckInterval,
                AsioCallbackWatchdog.CheckInterval);
        }

        previous?.Dispose();
    }

    private void StopWatchdog()
    {
        Timer? timer;
        lock (sync)
        {
            timer = DetachWatchdogLocked();
        }

        timer?.Dispose();
    }

    private Timer? DetachWatchdogLocked()
    {
        watchdogGeneration++;
        watchdog = null;
        Timer? timer = watchdogTimer;
        watchdogTimer = null;
        return timer;
    }

    private void EnsureScratch(int frames)
    {
        if (meterPeaks.Length != ChannelCount)
        {
            meterPeaks = new double[ChannelCount];
            meterSumSquares = new double[ChannelCount];
        }

        if (convertScratch.Length == ChannelCount &&
            convertScratch[0].Length >= frames)
        {
            return;
        }

        convertScratch = new float[ChannelCount][];
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            convertScratch[channel] = new float[frames];
        }
    }

    private void PlaybackStopped(object? sender, StoppedEventArgs args)
    {
        // A driver that stops on its own (AutoStop at the end of the signal) calls back no more, which is not a stall.
        StopWatchdog();
        if (args.Exception != null)
        {
            firstBufferReady?.TrySetException(args.Exception);
            playbackStopped?.TrySetException(args.Exception);
        }
        else
        {
            playbackStopped?.TrySetResult(true);
        }

        // Fault waiters, or one blocked on samples that never come hangs until Abort.
        Exception failure = args.Exception ??
            new InvalidOperationException(
                "ASIO playback stopped before the requested samples arrived.");
        lock (sync)
        {
            terminalException ??= failure;
            sampleWaiters.FaultAll(failure);
        }
    }

    /// <summary>Completes when the driver stops (faulted on failure). Live consumers must await it, or an unplugged device freezes them.</summary>
    public Task StoppedAsync() =>
        playbackStopped?.Task ?? Task.CompletedTask;

    private void ResetBuffers()
    {
        // Allocate (possibly LOH) before the lock; the epoch is paused meanwhile so the worker keeps running.
        var freshAccumulator = new CaptureAccumulator(
            ChannelCount,
            Sequence,
            Math.Max(expectedTotalSamples, 8192));
        lock (sync)
        {
            int newGeneration = ++captureGeneration;
            accumulator = freshAccumulator;

            sampleWaiters.CancelAll();
            capturePump.Reset(newGeneration);
        }
    }

    private void StopAndDisposeDriver()
    {
        AsioOut? activeDriver;
        lock (sync)
        {
            activeDriver = driver;
        }

        if (activeDriver == null)
        {
            return;
        }

        DetachAndDisposeDriver(activeDriver);
    }

    private void DetachAndDisposeDriver(AsioOut activeDriver)
    {
        lock (sync)
        {
            if (ReferenceEquals(driver, activeDriver))
            {
                driver = null;
            }
        }

        StopWatchdog();
        activeDriver.AudioAvailable -= ReceiveAudio;
        activeDriver.PlaybackStopped -= PlaybackStopped;
        activeDriver.DriverResetRequest -= DriverResetRequested;
        activeDriver.Dispose();
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        SampleWaiterRegistry.NewSignal();

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
