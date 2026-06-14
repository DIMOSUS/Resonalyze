using NAudio.Wave;

namespace Resonalyze
{
    public sealed class SoundRecorder : IDisposable
    {
        private readonly object sync = new();
        private readonly List<SampleWaiter> sampleWaiters = new();
        private WaveInEvent? waveSource;
        private List<float>[] samples = Array.Empty<List<float>>();
        private TaskCompletionSource<bool>? firstBufferReady;
        private TaskCompletionSource<bool>? recordingStopped;
        private int sequenceStart;
        private bool disposed;

        public delegate void SequenceReadyHandler(float[] sequence);
        public event SequenceReadyHandler? SequenceReadyNotify;

        public int Sequence { get; set; }
        public int ReadSamples { get; private set; }
        public int SampleRate { get; private set; }
        public int Bits { get; private set; }
        public int Chanels { get; private set; }

        public void Init(int sampleRate, int bits, int chanels)
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
            if (chanels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chanels));
            }

            StopAndDisposeSource();
            SampleRate = sampleRate;
            Bits = bits;
            Chanels = chanels;
            ResetBuffers();
        }

        public async Task StartRecordingAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (SampleRate == 0 || Chanels == 0)
            {
                throw new InvalidOperationException("Recorder is not initialized.");
            }

            StopAndDisposeSource();
            ResetBuffers();

            firstBufferReady = NewSignal();
            recordingStopped = NewSignal();
            waveSource = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, Bits, Chanels)
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
            await stoppedTask.ConfigureAwait(false);
        }

        public float[][] GetSamplesSnapshot()
        {
            lock (sync)
            {
                var snapshot = new float[samples.Length][];
                for (int channel = 0; channel < samples.Length; channel++)
                {
                    snapshot[channel] = samples[channel].ToArray();
                }
                return snapshot;
            }
        }

        private void ReceiveData(object? sender, WaveInEventArgs args)
        {
            int bytesPerSample = Bits / 8;
            int bytesPerFrame = bytesPerSample * Chanels;
            int frameCount = args.BytesRecorded / bytesPerFrame;
            List<float[]> readySequences = new();

            lock (sync)
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    int frameOffset = frame * bytesPerFrame;
                    for (int channel = 0; channel < Chanels; channel++)
                    {
                        int sampleOffset = frameOffset + channel * bytesPerSample;
                        samples[channel].Add(ReadPcmSample(args.Buffer, sampleOffset, bytesPerSample));
                    }
                    ReadSamples++;
                }

                if (Sequence > 0)
                {
                    while (ReadSamples - sequenceStart >= Sequence)
                    {
                        float[] sequence = new float[Sequence];
                        samples[0].CopyTo(sequenceStart, sequence, 0, Sequence);
                        sequenceStart += Sequence;
                        readySequences.Add(sequence);
                    }
                }

                for (int i = sampleWaiters.Count - 1; i >= 0; i--)
                {
                    if (ReadSamples >= sampleWaiters[i].SampleCount)
                    {
                        sampleWaiters[i].Complete();
                        sampleWaiters.RemoveAt(i);
                    }
                }
            }

            firstBufferReady?.TrySetResult(true);
            foreach (float[] sequence in readySequences)
            {
                SequenceReadyNotify?.Invoke(sequence);
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
                ReadSamples = 0;
                sequenceStart = 0;
                samples = new List<float>[Chanels];
                for (int channel = 0; channel < Chanels; channel++)
                {
                    samples[channel] = new List<float>(SampleRate);
                }

                foreach (SampleWaiter waiter in sampleWaiters)
                {
                    waiter.Cancel();
                }
                sampleWaiters.Clear();
            }
        }

        private void StopAndDisposeSource()
        {
            WaveInEvent? source = waveSource;
            waveSource = null;
            if (source == null)
            {
                return;
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
