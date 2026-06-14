using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using Resonalyze.Dsp;
using System.Numerics;
using static System.Math;

namespace Resonalyze
{
    public sealed class ExpSweepMeasurement : IImpulseMeasurement, IDisposable
    {
        private readonly object stateSync = new();
        private SoundRecorder? soundRecorder;
        private CancellationTokenSource? cancellationTokenSource;
        private Task<bool>? measurementTask;
        private volatile bool inProgress;
        private bool disposed;

        public delegate void CompleteHandler(bool success);
        public event CompleteHandler? CompleteNotify;

        public ExponentialSineSweep? exponentialSineSweep;
        public Complex[]? ImpulseResponce { get; private set; }
        public bool InProgress => inProgress;
        public int SampleRate { get; private set; }
        public int Octaves { get; private set; }
        public int Bits { get; private set; }
        public Chanels PlayCanels { get; private set; }
        public int MaxMagnitudeInd { get; private set; }
        public Exception? LastError { get; private set; }
        public int RecordedSamples => soundRecorder?.ReadSamples ?? 0;

        public void Init(int octaves, int sampleRate, int bits, double desireDuration, Chanels playCanels)
        {
            ThrowIfDisposed();
            if (InProgress)
            {
                throw new InvalidOperationException("Cannot reinitialize an active measurement.");
            }

            PlayCanels = playCanels;
            SampleRate = sampleRate;
            Bits = bits;
            Octaves = octaves;
            ImpulseResponce = null;
            LastError = null;

            exponentialSineSweep?.Dispose();
            exponentialSineSweep = new ExponentialSineSweep();
            exponentialSineSweep.FillData(octaves, desireDuration, bits, sampleRate);

            soundRecorder?.Dispose();
            soundRecorder = new SoundRecorder();
            soundRecorder.Init(sampleRate, bits, 1);
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
                if (exponentialSineSweep == null || soundRecorder == null)
                {
                    throw new InvalidOperationException("Measurement is not initialized.");
                }

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = new CancellationTokenSource();
                inProgress = true;
                ImpulseResponce = null;
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

        public double HarmonicIROffset(double harmonic)
        {
            if (exponentialSineSweep == null)
            {
                return 0;
            }
            return exponentialSineSweep.SweepSamples * Log(harmonic) / Log(Pow(2, Octaves));
        }

        private async Task<bool> RunCoreAsync(CancellationToken cancellationToken)
        {
            ExponentialSineSweep sweep = exponentialSineSweep!;
            SoundRecorder recorder = soundRecorder!;
            int channel = (int)PlayCanels;
            bool success = false;
            using var player = new WaveOutEvent();

            try
            {
                await recorder.StartRecordingAsync(cancellationToken).ConfigureAwait(false);
                int recordingStart = recorder.ReadSamples;

                RawSourceWaveStream stream = sweep.rawSourceWaveStream[channel];
                stream.Position = 0;
                player.Init(stream);
                await PlayToEndAsync(player, cancellationToken).ConfigureAwait(false);
                stream.Position = 0;

                int requiredSamples = recordingStart + sweep.SweepSamples + SampleRate;
                await recorder.WaitForSamplesAsync(requiredSamples, cancellationToken).ConfigureAwait(false);
                await recorder.StopRecordingAsync().ConfigureAwait(false);

                ProcessImpulseResponse(recorder.GetSamplesSnapshot(), sweep);
                success = true;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                LastError = exception;
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
                    success = false;
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

            return success;
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

        private void ProcessImpulseResponse(float[][] sampleChannels, ExponentialSineSweep sweep)
        {
            float[] recorded = sampleChannels.Length > 0 ? sampleChannels[0] : Array.Empty<float>();
            if (recorded.Length == 0)
            {
                throw new InvalidOperationException("No audio samples were recorded.");
            }

            int fftLength = (int)Pow(2, Ceiling(Log2(Max(recorded.Length, sweep.InverseFiltere.Length))));
            Complex[] input = new Complex[fftLength];
            Complex[] inverseFilter = new Complex[fftLength];

            for (int i = 0; i < recorded.Length; i++)
            {
                input[i] = new Complex(recorded[i], 0);
            }
            for (int i = 0; i < sweep.InverseFiltere.Length; i++)
            {
                inverseFilter[i] = new Complex(sweep.InverseFiltere[i], 0);
            }

            Fourier.Forward(input, FourierOptions.Matlab);
            Fourier.Forward(inverseFilter, FourierOptions.Matlab);
            for (int i = 0; i < input.Length; i++)
            {
                input[i] *= inverseFilter[i];
            }
            Fourier.Inverse(input, FourierOptions.Matlab);

            double normalization = 2.0 / sweep.InverseFiltere.Length;
            double maxMagnitude = 0;
            int maxMagnitudeIndex = 0;
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = new Complex(input[i].Real * normalization, 0);
                if (input[i].Magnitude > maxMagnitude)
                {
                    maxMagnitude = input[i].Magnitude;
                    maxMagnitudeIndex = i;
                }
            }

            ImpulseResponce = input;
            MaxMagnitudeInd = maxMagnitudeIndex;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ExpSweepMeasurement));
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
            soundRecorder?.Dispose();
            exponentialSineSweep?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
