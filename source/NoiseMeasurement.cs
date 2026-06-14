using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using System.Numerics;

namespace Resonalyze
{
    public sealed class NoiseMeasurement : IDisposable
    {
        private readonly object stateSync = new();
        private readonly object dataSync = new();
        private SoundRecorder? soundRecorder;
        private CancellationTokenSource? cancellationTokenSource;
        private Task<bool>? measurementTask;
        private double[]? accumulatedData;
        private int sequencesCounter;
        private volatile bool inProgress;
        private bool disposed;

        public delegate void CompleteHandler(bool success);
        public event CompleteHandler? CompleteNotify;

        public NoiseSignal? noiseSignal;
        public bool InProgress => inProgress;
        public int SampleRate { get; private set; }
        public int Bits { get; private set; }
        public Chanels PlayCanels { get; private set; }
        public int SequenceLength { get; private set; }
        public Exception? LastError { get; private set; }

        public void Init(int sampleRate, int bits, double desireDuration, Chanels playCanels, int sequenceLength = 2048)
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
            PlayCanels = playCanels;
            SampleRate = sampleRate;
            Bits = bits;

            noiseSignal?.Dispose();
            noiseSignal = new NoiseSignal();
            noiseSignal.FillData(desireDuration, bits, sampleRate);

            if (soundRecorder != null)
            {
                soundRecorder.SequenceReadyNotify -= ProcessSequence;
                soundRecorder.Dispose();
            }
            soundRecorder = new SoundRecorder();
            soundRecorder.Init(sampleRate, bits, 1);
            soundRecorder.Sequence = sequenceLength;
            soundRecorder.SequenceReadyNotify += ProcessSequence;
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
                if (noiseSignal == null || soundRecorder == null)
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

        public double[]? GetAccDataSnapshot()
        {
            lock (dataSync)
            {
                return accumulatedData?.ToArray();
            }
        }

        private async Task<bool> RunCoreAsync(CancellationToken cancellationToken)
        {
            NoiseSignal signal = noiseSignal!;
            SoundRecorder recorder = soundRecorder!;
            RawSourceWaveStream stream = signal.rawSourceWaveStream[(int)PlayCanels];
            bool success = false;
            using var player = new WaveOutEvent();

            try
            {
                await recorder.StartRecordingAsync(cancellationToken).ConfigureAwait(false);
                player.Init(stream);

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    stream.Position = 0;
                    await PlayToEndAsync(player, cancellationToken).ConfigureAwait(false);
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
                player.Stop();
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
                    CompleteNotify?.Invoke(success);
                }
                catch
                {
                    // UI subscribers must not break measurement cleanup.
                }
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
                soundRecorder.SequenceReadyNotify -= ProcessSequence;
                soundRecorder.Dispose();
            }
            noiseSignal?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
