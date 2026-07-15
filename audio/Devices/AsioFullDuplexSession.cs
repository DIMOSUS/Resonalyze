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
    private CaptureAccumulator? accumulator;
    private float[][] convertScratch = Array.Empty<float[]>();
    private double[] meterPeaks = Array.Empty<double>();
    private double[] meterSumSquares = Array.Empty<double>();
    private AudioLevelAccumulator? levelAccumulator;
    private int expectedTotalSamples;
    private int captureGeneration;
    private TaskCompletionSource<bool>? firstBufferReady;
    private TaskCompletionSource<bool>? playbackStopped;
    // Remembered so a sample waiter registered after the driver stopped (e.g. a
    // later averaged run) faults immediately instead of hanging. Cleared only by
    // a real StartAsync, never by ResetCapture/PauseCapture between runs.
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
            // A real (re)start clears any remembered terminal failure; ResetCapture
            // between averaged runs deliberately does not.
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
            capturePump.Prepare(checked(driver.FramesPerBuffer * sizeof(float)));
            driver.Play();

            using CancellationTokenRegistration registration =
                cancellationToken.Register(() => firstBufferReady.TrySetCanceled(cancellationToken));
            await firstBufferReady.Task.ConfigureAwait(false);
        }
        catch
        {
            // A driver that fails validation or playback startup (or never
            // produces the first callback before cancellation) must be detached
            // here; otherwise it keeps running with live callbacks until the
            // owner's teardown gets around to disposing the session.
            StopAndDisposeDriver();
            throw;
        }
    }

    /// <summary>
    /// Starts a fresh capture on the running driver. An averaged sweep reuses
    /// the open ASIO session across runs — the driver keeps playing whatever
    /// the provider produces (silence after the sweep ends) and only the
    /// accumulator restarts, instead of paying a full driver re-initialization
    /// (seconds on slow drivers) for every run.
    /// </summary>
    public void ResetCapture(int expectedTotalSamples)
    {
        ThrowIfDisposed();
        this.expectedTotalSamples = expectedTotalSamples;
        ResetBuffers();
    }

    /// <summary>
    /// Stops accumulating samples while the driver keeps running (and keeps
    /// raising level meters) — used between averaging runs, where minutes of a
    /// confirmation pause would otherwise grow the capture buffer with
    /// silence. <see cref="ResetCapture"/> starts the next run's capture.
    /// </summary>
    public void PauseCapture()
    {
        ThrowIfDisposed();
        lock (sync)
        {
            int newGeneration = ++captureGeneration;
            accumulator = null;
            sampleWaiters.CancelAll();
            capturePump.Reset(newGeneration);
        }
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
            // The samples are not all here and a stopped driver will deliver
            // neither more of them nor a fresh stop event: fault immediately
            // rather than register a waiter that only an Abort could complete.
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

    /// <summary>
    /// Atomically ends the current accumulation epoch and returns its samples.
    /// The old accumulator is detached under the session lock; the potentially
    /// large allocation/copy then runs without blocking the capture worker from
    /// returning queue slots or continuing level metering.
    /// </summary>
    public float[][] CompleteCaptureSnapshot()
    {
        CaptureAccumulator? completed;
        lock (sync)
        {
            int newGeneration = ++captureGeneration;
            completed = accumulator;
            accumulator = null;
            sampleWaiters.CancelAll();
            capturePump.Reset(newGeneration);
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
        StopAndDisposeDriver();
        capturePump.Dispose();
        lock (sync)
        {
            sampleWaiters.CancelAll();
        }

        GC.SuppressFinalize(this);
    }

    // NAudio fills playback only after this callback returns. Keep it to bounded
    // copies into the pump's reusable slots; all conversion and publication is
    // performed by the dedicated capture worker.
    private void ReceiveAudio(object? sender, AsioAudioAvailableEventArgs args)
    {
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

        // A paused capture (accumulator == null) skips accumulation but keeps
        // metering, so the input meter stays live between averaging runs.
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
            if (generation != captureGeneration)
            {
                return;
            }

            firstBufferReady?.TrySetException(exception);
            playbackStopped?.TrySetException(exception);
            terminalException ??= exception;
            sampleWaiters.FaultAll(exception);
        }
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
        if (args.Exception != null)
        {
            firstBufferReady?.TrySetException(args.Exception);
            playbackStopped?.TrySetException(args.Exception);
        }
        else
        {
            playbackStopped?.TrySetResult(true);
        }

        // No more samples are coming: a waiter blocked on a sample count the
        // stopped driver will never deliver used to hang until a manual Abort.
        Exception failure = args.Exception ??
            new InvalidOperationException(
                "ASIO playback stopped before the requested samples arrived.");
        lock (sync)
        {
            // Remember the failure so a waiter registered by a later run faults
            // at once instead of hanging.
            terminalException ??= failure;
            sampleWaiters.FaultAll(failure);
        }
    }

    /// <summary>
    /// Completes when the driver stops — successfully on a normal stop, with
    /// the driver's exception on a failure. A live consumer that otherwise
    /// waits only on its own cancellation must also await this, or an
    /// unplugged device leaves it frozen with no error and no completion.
    /// </summary>
    public Task StoppedAsync() =>
        playbackStopped?.Task ?? Task.CompletedTask;

    private void ResetBuffers()
    {
        // Allocate before taking the session lock: an averaged run may reserve
        // LOH-sized channel buffers while the driver is still delivering packets.
        // During this preparation the completed epoch is paused/null, so the
        // worker can continue metering and returning queue slots.
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

        activeDriver.AudioAvailable -= ReceiveAudio;
        activeDriver.PlaybackStopped -= PlaybackStopped;
        activeDriver.Dispose();
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        SampleWaiterRegistry.NewSignal();

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
