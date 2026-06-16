using System.Numerics;
using System.Threading.Channels;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;

namespace Resonalyze
{
    /// <summary>
    /// Streams captured noise blocks to a background FFT accumulator.
    /// </summary>
    public sealed class NoiseMeasurement : IDisposable
    {
        private readonly object stateSync = new();
        private readonly object dataSync = new();
        private SoundRecorder? soundRecorder;
        private CancellationTokenSource? cancellationTokenSource;
        private Task<bool>? measurementTask;
        private ChannelWriter<float[]>? sequenceWriter;
        private double[]? accumulatedData;
        private int sequencesCounter;
        private volatile bool inProgress;
        private bool disposed;

        public event Action<bool>? Completed;

        private NoiseSignal? signal;
        public bool InProgress => inProgress;
        public int SampleRate { get; private set; }
        public int Bits { get; private set; }
        public PlaybackChannel PlaybackChannel { get; private set; }
        public AudioBackend AudioBackend { get; private set; } = AudioBackend.Wave;
        public int OutputDeviceNumber { get; private set; } = -1;
        public int InputDeviceNumber { get; private set; } = -1;
        public string? AsioDriverName { get; private set; }
        public int AsioInputChannelOffset { get; private set; }
        public int AsioOutputChannelOffset { get; private set; }
        public int SequenceLength { get; private set; }
        public Exception? LastError { get; private set; }

        public void Init(
            int sampleRate,
            int bits,
            double requestedDuration,
            PlaybackChannel playbackChannel,
            int sequenceLength = 2048,
            int outputDeviceNumber = -1,
            int inputDeviceNumber = -1,
            AudioBackend audioBackend = AudioBackend.Wave,
            string? asioDriverName = null,
            int asioInputChannelOffset = 0,
            int asioOutputChannelOffset = 0)
        {
            ThrowIfDisposed();
            if (InProgress)
            {
                throw new InvalidOperationException("Cannot reinitialize an active measurement.");
            }
            if (sequenceLength < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(sequenceLength));
            }

            SequenceLength = sequenceLength;
            PlaybackChannel = playbackChannel;
            SampleRate = sampleRate;
            Bits = bits;
            OutputDeviceNumber = outputDeviceNumber;
            InputDeviceNumber = inputDeviceNumber;
            AudioBackend = audioBackend;
            AsioDriverName = asioDriverName;
            AsioInputChannelOffset = asioInputChannelOffset;
            AsioOutputChannelOffset = asioOutputChannelOffset;

            signal?.Dispose();
            signal = new NoiseSignal();
            signal.FillData(requestedDuration, bits, sampleRate);

            if (soundRecorder != null)
            {
                soundRecorder.SequenceReady -= ProcessSequence;
                soundRecorder.Dispose();
            }
            soundRecorder = new SoundRecorder();
            soundRecorder.Init(sampleRate, bits, 1, inputDeviceNumber);
            soundRecorder.Sequence = sequenceLength;
            soundRecorder.SequenceReady += ProcessSequence;
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
                if (signal == null || soundRecorder == null)
                {
                    throw new InvalidOperationException("Measurement is not initialized.");
                }

                lock (dataSync)
                {
                    accumulatedData = null;
                    sequencesCounter = 0;
                }

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = new CancellationTokenSource();
                inProgress = true;
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

        public double[]? GetAccumulatedSpectrumSnapshot()
        {
            lock (dataSync)
            {
                return accumulatedData?.ToArray();
            }
        }

        private async Task<bool> RunCoreAsync(CancellationToken cancellationToken)
        {
            NoiseSignal noiseSignal = signal!;
            SoundRecorder recorder = soundRecorder!;
            RawSourceWaveStream stream = noiseSignal.GetStream(PlaybackChannel);
            bool success = false;
            var sequenceChannel = Channel.CreateBounded<float[]>(
                new BoundedChannelOptions(8)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.DropOldest
                });
            Volatile.Write(ref sequenceWriter, sequenceChannel.Writer);
            Task processingTask = ProcessSequencesAsync(sequenceChannel.Reader, cancellationToken);

            try
            {
                if (AudioBackend == AudioBackend.Asio)
                {
                    await RunAsioAsync(noiseSignal, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await RunWaveAsync(stream, recorder, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                LastError = exception;
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref sequenceWriter, null)?.TryComplete();
                try
                {
                    await processingTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    LastError ??= exception;
                }

                try
                {
                    await recorder.StopRecordingAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    LastError ??= exception;
                }

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

        private async Task RunWaveAsync(
            RawSourceWaveStream stream,
            SoundRecorder recorder,
            CancellationToken cancellationToken)
        {
            using var player = new WaveOutEvent
            {
                DeviceNumber = OutputDeviceNumber
            };
            await recorder.StartRecordingAsync(cancellationToken).ConfigureAwait(false);
            player.Init(stream);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Position = 0;
                await PlayToEndAsync(player, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task RunAsioAsync(
            NoiseSignal noiseSignal,
            CancellationToken cancellationToken)
        {
            using var session = new AsioFullDuplexSession(
                AsioDriverName ?? string.Empty,
                AsioInputChannelOffset,
                AsioOutputChannelOffset)
            {
                Sequence = SequenceLength
            };
            session.SequenceReady += ProcessSequence;
            try
            {
                using FloatArrayWaveStream stream = FloatArrayWaveStream.FromMonoSamples(
                    noiseSignal.FloatData,
                    SampleRate,
                    PlaybackChannel);
                var loopingProvider = new LoopingWaveProvider(stream);
                await session.StartAsync(
                    loopingProvider,
                    SampleRate,
                    autoStop: false,
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                session.SequenceReady -= ProcessSequence;
                await session.StopAsync().ConfigureAwait(false);
            }
        }

        private static async Task PlayToEndAsync(WaveOutEvent player, CancellationToken cancellationToken)
        {
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void PlaybackStopped(object? sender, StoppedEventArgs args)
            {
                if (args.Exception != null)
                {
                    stopped.TrySetException(args.Exception);
                }
                else
                {
                    stopped.TrySetResult(true);
                }
            }

            player.PlaybackStopped += PlaybackStopped;
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() =>
                {
                    player.Stop();
                    stopped.TrySetCanceled(cancellationToken);
                });

            try
            {
                player.Play();
                await stopped.Task.ConfigureAwait(false);
            }
            finally
            {
                player.PlaybackStopped -= PlaybackStopped;
            }
        }

        private void ProcessSequence(float[] sequence)
        {
            Volatile.Read(ref sequenceWriter)?.TryWrite(sequence);
        }

        private async Task ProcessSequencesAsync(
            ChannelReader<float[]> reader,
            CancellationToken cancellationToken)
        {
            await foreach (float[] sequence in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                AccumulateSequence(sequence);
            }
        }

        private void AccumulateSequence(float[] sequence)
        {
            Complex[] spectrum = new Complex[SequenceLength];
            for (int i = 0; i < SequenceLength; i++)
            {
                spectrum[i] = new Complex(sequence[i], 0);
            }
            Fourier.Forward(spectrum, FourierOptions.Matlab);

            const double lerpCoefficient = 0.01;
            lock (dataSync)
            {
                if (accumulatedData == null)
                {
                    if (sequencesCounter > 2)
                    {
                        accumulatedData = new double[SequenceLength / 2];
                        for (int i = 0; i < accumulatedData.Length; i++)
                        {
                            accumulatedData[i] = spectrum[i].Magnitude;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < accumulatedData.Length; i++)
                    {
                        accumulatedData[i] =
                            (1 - lerpCoefficient) * accumulatedData[i] +
                            lerpCoefficient * spectrum[i].Magnitude;
                    }
                }
                sequencesCounter++;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NoiseMeasurement));
            }
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
            if (soundRecorder != null)
            {
                soundRecorder.SequenceReady -= ProcessSequence;
                soundRecorder.Dispose();
            }
            signal?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
