using NAudio.Wave;

namespace Resonalyze
{
    /// <summary>
    /// Captures interleaved PCM input and exposes sample-count based asynchronous waits.
    /// </summary>
    public sealed class SoundRecorder : IDisposable
    {
        private readonly object sync = new();
        private readonly SampleWaiterRegistry sampleWaiters = new();
        private WaveInEvent? waveSource;
        private CaptureAccumulator? accumulator;
        private float[][] decodeScratch = Array.Empty<float[]>();
        private double[] meterPeaks = Array.Empty<double>();
        private double[] meterSumSquares = Array.Empty<double>();
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
            try
            {
                waveSource.StartRecording();

                using CancellationTokenRegistration registration =
                    cancellationToken.Register(() => firstBufferReady.TrySetCanceled(cancellationToken));
                await firstBufferReady.Task.ConfigureAwait(false);
            }
            catch
            {
                // If the device fails to start, stops before the first buffer,
                // or the caller cancels while waiting for that first buffer, do
                // not leave an active WaveInEvent (and its callbacks) behind.
                StopAndDisposeSource();
                throw;
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

                return sampleWaiters.Add(sampleCount, cancellationToken);
            }
        }

        public async Task StopRecordingAsync()
        {
            WaveInEvent? source;
            TaskCompletionSource<bool>? stoppedSignal;
            lock (sync)
            {
                source = waveSource;
                stoppedSignal = recordingStopped;
            }

            if (source == null)
            {
                return;
            }

            try
            {
                await AudioCaptureStop.StopAndWaitAsync(
                    source.StopRecording,
                    stoppedSignal,
                    stoppedSignal?.Task ?? Task.CompletedTask,
                    "The audio input").ConfigureAwait(false);
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
            double[] peaks = meterPeaks;
            double[] sumSquares = meterSumSquares;
            Array.Clear(peaks);
            Array.Clear(sumSquares);
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
                sampleWaiters.CompleteUpTo(accumulator.ReadSamples);
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
            if (meterPeaks.Length != ChannelCount)
            {
                meterPeaks = new double[ChannelCount];
                meterSumSquares = new double[ChannelCount];
            }

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

            // A waiter blocked on a sample count that will never arrive (the
            // device unplugged mid-capture) used to hang until a manual Abort:
            // no more samples are coming, so fault every pending wait.
            lock (sync)
            {
                sampleWaiters.FaultAll(
                    args.Exception ??
                    new InvalidOperationException(
                        "Recording stopped before the requested samples arrived."));
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

                sampleWaiters.CancelAll();
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
            SampleWaiterRegistry.NewSignal();

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
                sampleWaiters.CancelAll();
            }
            GC.SuppressFinalize(this);
        }
    }
}
