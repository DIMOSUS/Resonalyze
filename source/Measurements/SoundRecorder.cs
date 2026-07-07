using NAudio.Wave;

namespace Resonalyze
{
    /// <summary>
    /// Captures interleaved PCM input and exposes sample-count based asynchronous waits.
    /// </summary>
    public sealed class SoundRecorder : IDisposable
    {
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
        private readonly object sync = new();
        private readonly List<SampleWaiter> sampleWaiters = new();
        private WaveInEvent? waveSource;
        private CaptureAccumulator? accumulator;
        private float[][] decodeScratch = Array.Empty<float[]>();
        private int expectedTotalSamples;
        private TaskCompletionSource<bool>? firstBufferReady;
        private TaskCompletionSource<bool>? recordingStopped;
        private bool disposed;

        public event Action<float[]>? SequenceReady;
        public event Action<float[][]>? SequenceChannelsReady;
        internal event Action<AudioChannelLevel[]>? LevelsAvailable;

        public int Sequence { get; set; }
        public int ReadSamples => accumulator?.ReadSamples ?? 0;
        public int SampleRate { get; private set; }
        public int Bits { get; private set; }
        public int ChannelCount { get; private set; }
        public int InputDeviceNumber { get; private set; } = -1;

        public void Init(
            int sampleRate,
            int bits,
            int channelCount,
            int inputDeviceNumber = -1,
            int expectedSamples = 0)
        {
            ThrowIfDisposed();
            if (bits is not (16 or 24))
            {
                throw new NotSupportedException($"Unsupported sample size: {bits} bits.");
            }
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }
            if (channelCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channelCount));
            }

            StopAndDisposeSource();
            SampleRate = sampleRate;
            Bits = bits;
            ChannelCount = channelCount;
            InputDeviceNumber = inputDeviceNumber;
            expectedTotalSamples = expectedSamples;
            ResetBuffers();
        }

        public async Task StartRecordingAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (SampleRate == 0 || ChannelCount == 0)
            {
                throw new InvalidOperationException("Recorder is not initialized.");
            }

            StopAndDisposeSource();
            ResetBuffers();

            firstBufferReady = NewSignal();
            recordingStopped = NewSignal();
            waveSource = new WaveInEvent
            {
                DeviceNumber = InputDeviceNumber,
                WaveFormat = new WaveFormat(SampleRate, Bits, ChannelCount)
            };
            waveSource.DataAvailable += ReceiveData;
            waveSource.RecordingStopped += RecordingStopped;
            waveSource.StartRecording();

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

                var waiter = new SampleWaiter(sampleCount, cancellationToken);
                sampleWaiters.Add(waiter);
                return waiter.Task;
            }
        }

        public async Task StopRecordingAsync()
        {
            WaveInEvent? source;
            Task stoppedTask;
            lock (sync)
            {
                source = waveSource;
                stoppedTask = recordingStopped?.Task ?? Task.CompletedTask;
            }

            if (source == null)
            {
                return;
            }

            try
            {
                source.StopRecording();
            }
            catch (InvalidOperationException)
            {
                recordingStopped?.TrySetResult(true);
            }

            try
            {
                await stoppedTask.WaitAsync(StopTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException(
                    $"The audio input did not stop within {StopTimeout.TotalSeconds:0} seconds.",
                    exception);
            }
            finally
            {
                DetachAndDisposeSource(source);
            }
        }

        // Meaningful only without sequence extraction (the accumulator drops the
        // consumed prefix in sequence mode); no current caller mixes the two.
        public float[][] GetSamplesSnapshot()
        {
            lock (sync)
            {
                return accumulator?.Snapshot() ?? Array.Empty<float[]>();
            }
        }

        private void ReceiveData(object? sender, WaveInEventArgs args)
        {
            int bytesPerSample = Bits / 8;
            int bytesPerFrame = bytesPerSample * ChannelCount;
            int frameCount = args.BytesRecorded / bytesPerFrame;

            // Decode and meter on reusable scratch outside the lock; only bounded
            // array copies happen inside it (mirrors AsioFullDuplexSession).
            EnsureScratch(frameCount);
            var peaks = new double[ChannelCount];
            var sumSquares = new double[ChannelCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                int frameOffset = frame * bytesPerFrame;
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    int sampleOffset = frameOffset + channel * bytesPerSample;
                    float sample = ReadPcmSample(args.Buffer, sampleOffset, bytesPerSample);
                    decodeScratch[channel][frame] = sample;
                    double magnitude = Math.Abs(sample);
                    peaks[channel] = Math.Max(peaks[channel], magnitude);
                    sumSquares[channel] += sample * sample;
                }
            }

            List<float[][]>? readySequences;
            lock (sync)
            {
                if (accumulator == null)
                {
                    return;
                }

                accumulator.Append(decodeScratch, frameCount);
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
                AudioLevelMetering.MeasureChannels(peaks, sumSquares, frameCount));
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
            if (decodeScratch.Length == ChannelCount &&
                decodeScratch[0].Length >= frames)
            {
                return;
            }

            decodeScratch = new float[ChannelCount][];
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                decodeScratch[channel] = new float[frames];
            }
        }

        private static float ReadPcmSample(byte[] buffer, int offset, int bytesPerSample)
        {
            int value = bytesPerSample == 2
                ? (short)(buffer[offset] | buffer[offset + 1] << 8)
                : buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;

            if (bytesPerSample == 3 && (value & 0x800000) != 0)
            {
                value |= unchecked((int)0xff000000);
            }

            int maxValue = (1 << (bytesPerSample * 8 - 1)) - 1;
            return Math.Clamp(value / (float)maxValue, -1.0f, 1.0f);
        }

        private void RecordingStopped(object? sender, StoppedEventArgs args)
        {
            if (args.Exception != null)
            {
                firstBufferReady?.TrySetException(args.Exception);
                recordingStopped?.TrySetException(args.Exception);
            }
            else
            {
                firstBufferReady?.TrySetException(
                    new InvalidOperationException("Recording stopped before the first audio buffer arrived."));
                recordingStopped?.TrySetResult(true);
            }
        }

        private void ResetBuffers()
        {
            lock (sync)
            {
                // At least one second pre-allocated; a known recording length
                // (e.g. the sweep) avoids growth re-allocations mid-capture.
                accumulator = new CaptureAccumulator(
                    ChannelCount,
                    Sequence,
                    Math.Max(expectedTotalSamples, SampleRate));

                foreach (SampleWaiter waiter in sampleWaiters)
                {
                    waiter.Cancel();
                }
                sampleWaiters.Clear();
            }
        }

        private void StopAndDisposeSource()
        {
            WaveInEvent? source;
            lock (sync)
            {
                source = waveSource;
            }
            if (source == null)
            {
                return;
            }

            DetachAndDisposeSource(source);
        }

        private void DetachAndDisposeSource(WaveInEvent source)
        {
            lock (sync)
            {
                if (ReferenceEquals(waveSource, source))
                {
                    waveSource = null;
                }
            }

            source.DataAvailable -= ReceiveData;
            source.RecordingStopped -= RecordingStopped;
            source.Dispose();
        }

        private static TaskCompletionSource<bool> NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SoundRecorder));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopAndDisposeSource();
            lock (sync)
            {
                foreach (SampleWaiter waiter in sampleWaiters)
                {
                    waiter.Cancel();
                }
                sampleWaiters.Clear();
            }
            GC.SuppressFinalize(this);
        }

        private sealed class SampleWaiter
        {
            private readonly TaskCompletionSource<bool> completion = NewSignal();
            private readonly CancellationTokenRegistration registration;

            public SampleWaiter(int sampleCount, CancellationToken cancellationToken)
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
}
