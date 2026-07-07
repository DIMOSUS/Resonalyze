using NAudio.Wave;

namespace Resonalyze;

internal sealed class AsioFullDuplexSession : IDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    private readonly object sync = new();
    private readonly List<SoundRecorderSampleWaiter> sampleWaiters = new();
    private readonly string driverName;
    private readonly int inputChannelOffset;
    private readonly int outputChannelOffset;
    private readonly int driverRecordChannelCount;
    private readonly AsioSampleConverter sampleConverter = new();
    private AsioOut? driver;
    private CaptureAccumulator? accumulator;
    private float[][] convertScratch = Array.Empty<float[]>();
    private int expectedTotalSamples;
    private TaskCompletionSource<bool>? firstBufferReady;
    private TaskCompletionSource<bool>? playbackStopped;
    private bool disposed;

    public event Action<float[]>? SequenceReady;
    public event Action<float[][]>? SequenceChannelsReady;
    public event Action<AudioChannelLevel[]>? LevelsAvailable;

    public AsioFullDuplexSession(
        string driverName,
        int inputChannelOffset,
        int outputChannelOffset,
        int inputChannelCount = 1)
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
        ChannelCount = inputChannelCount;
        driverRecordChannelCount = inputChannelOffset + inputChannelCount;
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

        firstBufferReady = NewSignal();
        playbackStopped = NewSignal();
        driver = new AsioOut(driverName)
        {
            AutoStop = autoStop,
            InputChannelOffset = 0,
            ChannelOffset = outputChannelOffset
        };
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
        driver.Play();

        using CancellationTokenRegistration registration =
            cancellationToken.Register(() => firstBufferReady.TrySetCanceled(cancellationToken));
        await firstBufferReady.Task.ConfigureAwait(false);
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

            var waiter = new SoundRecorderSampleWaiter(sampleCount, cancellationToken);
            sampleWaiters.Add(waiter);
            return waiter.Task;
        }
    }

    public async Task StopAsync()
    {
        AsioOut? activeDriver;
        Task stoppedTask;
        lock (sync)
        {
            activeDriver = driver;
            stoppedTask = playbackStopped?.Task ?? Task.CompletedTask;
        }

        if (activeDriver == null)
        {
            return;
        }

        try
        {
            activeDriver.Stop();
        }
        catch (InvalidOperationException)
        {
            playbackStopped?.TrySetResult(true);
        }

        try
        {
            await stoppedTask.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"The ASIO driver did not stop within {StopTimeout.TotalSeconds:0} seconds.",
                exception);
        }
        finally
        {
            DetachAndDisposeDriver(activeDriver);
        }
    }

    // Meaningful only without sequence extraction (the accumulator drops the
    // consumed prefix in sequence mode); no current caller mixes the two.
    public float[][] GetSamplesSnapshot()
    {
        lock (sync)
        {
            return accumulator?.Snapshot()
                ?? Array.Empty<float[]>();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopAndDisposeDriver();
        lock (sync)
        {
            foreach (SoundRecorderSampleWaiter waiter in sampleWaiters)
            {
                waiter.Cancel();
            }
            sampleWaiters.Clear();
        }

        GC.SuppressFinalize(this);
    }

    // Runs inside the ASIO buffer-switch callback: NAudio fills the playback
    // buffers only after this handler returns, so every microsecond spent here
    // delays the output. Conversion and level metering therefore run on reusable
    // scratch buffers outside the lock; only bounded array copies happen inside.
    private void ReceiveAudio(object? sender, AsioAudioAvailableEventArgs args)
    {
        if (args.InputBuffers.Length < driverRecordChannelCount)
        {
            firstBufferReady?.TrySetException(new InvalidOperationException(
                $"ASIO callback returned {args.InputBuffers.Length} input buffers, " +
                $"but {driverRecordChannelCount} were expected."));
            return;
        }

        int frames = args.SamplesPerBuffer;
        EnsureScratch(frames);
        var peaks = new double[ChannelCount];
        var sumSquares = new double[ChannelCount];
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            float[] scratch = convertScratch[channel];
            sampleConverter.Convert(
                args.InputBuffers[inputChannelOffset + channel],
                args.AsioSampleType,
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

        List<float[][]>? readySequences;
        lock (sync)
        {
            if (accumulator == null)
            {
                return;
            }

            accumulator.Append(convertScratch, frames);
            readySequences = accumulator.ExtractReadySequences();

            for (int i = sampleWaiters.Count - 1; i >= 0; i--)
            {
                if (accumulator.ReadSamples >= sampleWaiters[i].SampleCount)
                {
                    sampleWaiters[i].Complete();
                    sampleWaiters.RemoveAt(i);
                }
            }
        }

        firstBufferReady?.TrySetResult(true);
        LevelsAvailable?.Invoke(
            AudioLevelMetering.MeasureChannels(peaks, sumSquares, frames));
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
            return;
        }

        playbackStopped?.TrySetResult(true);
    }

    private void ResetBuffers()
    {
        lock (sync)
        {
            // Pre-allocating the expected recording length keeps List-style
            // growth re-allocations out of the ASIO callback; live (sequence)
            // mode stays bounded via the accumulator's tail trimming instead.
            accumulator = new CaptureAccumulator(
                ChannelCount,
                Sequence,
                Math.Max(expectedTotalSamples, 8192));

            foreach (SoundRecorderSampleWaiter waiter in sampleWaiters)
            {
                waiter.Cancel();
            }
            sampleWaiters.Clear();
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
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private sealed class SoundRecorderSampleWaiter
    {
        private readonly TaskCompletionSource<bool> completion = NewSignal();
        private readonly CancellationTokenRegistration registration;

        public SoundRecorderSampleWaiter(int sampleCount, CancellationToken cancellationToken)
        {
            SampleCount = sampleCount;
            registration = cancellationToken.Register(() =>
                completion.TrySetCanceled(cancellationToken));
        }

        public int SampleCount { get; }
        public Task Task => completion.Task;

        public void Complete()
        {
            registration.Dispose();
            completion.TrySetResult(true);
        }

        public void Cancel()
        {
            registration.Dispose();
            completion.TrySetCanceled();
        }
    }
}
