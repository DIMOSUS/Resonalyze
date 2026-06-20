using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using Resonalyze.Dsp;
using static System.Math;

namespace Resonalyze
{
    /// <summary>
    /// Coordinates sweep playback, recording, and FFT-based deconvolution.
    /// </summary>
    public sealed class ExpSweepMeasurement : IImpulseMeasurement, IDisposable
    {
        private readonly object stateSync = new();
        private SoundRecorder? soundRecorder;
        private CancellationTokenSource? cancellationTokenSource;
        private Task<bool>? measurementTask;
        private volatile bool inProgress;
        private bool disposed;

        public event Action<bool>? Completed;
        internal event Action<InputLevelMeterSnapshot>? LevelsAvailable;

        public ExponentialSineSweep? Sweep { get; private set; }
        public Complex[]? ImpulseResponse { get; private set; }
        public bool InProgress => inProgress;
        public int SampleRate { get; private set; }
        public int Octaves { get; private set; }
        public int Bits { get; private set; }
        public PlaybackChannel PlaybackChannel { get; private set; }
        public AudioBackend AudioBackend { get; private set; } = AudioBackend.Wave;
        public int OutputDeviceNumber { get; private set; } = -1;
        public int InputDeviceNumber { get; private set; } = -1;
        public string? AsioDriverName { get; private set; }
        public int WaveInputChannelOffset { get; private set; }
        public int? WaveLoopbackInputChannelOffset { get; private set; }
        public int AsioInputChannelOffset { get; private set; }
        public int? AsioLoopbackInputChannelOffset { get; private set; }
        public int AsioOutputChannelOffset { get; private set; }
        public int PeakIndex { get; private set; }
        public Exception? LastError { get; private set; }
        public int RecordedSamples => soundRecorder?.ReadSamples ?? 0;

        public void Init(
            int octaves,
            int sampleRate,
            int bits,
            double requestedDuration,
            PlaybackChannel playbackChannel,
            int outputDeviceNumber = -1,
            int inputDeviceNumber = -1,
            AudioBackend audioBackend = AudioBackend.Wave,
            string? asioDriverName = null,
            int asioInputChannelOffset = 0,
            int asioOutputChannelOffset = 0,
            int waveInputChannelOffset = 0,
            int? waveLoopbackInputChannelOffset = null,
            int? asioLoopbackInputChannelOffset = null)
        {
            ThrowIfDisposed();
            if (InProgress)
            {
                throw new InvalidOperationException("Cannot reinitialize an active measurement.");
            }

            PlaybackChannel = Enum.IsDefined(playbackChannel)
                ? playbackChannel
                : PlaybackChannel.Mono;
            SampleRate = sampleRate;
            Bits = bits;
            Octaves = octaves;
            OutputDeviceNumber = outputDeviceNumber;
            InputDeviceNumber = inputDeviceNumber;
            AudioBackend = audioBackend;
            AsioDriverName = asioDriverName;
            WaveInputChannelOffset = Math.Clamp(waveInputChannelOffset, 0, 1);
            WaveLoopbackInputChannelOffset = NormalizeOptionalWaveChannel(
                waveLoopbackInputChannelOffset);
            AsioInputChannelOffset = asioInputChannelOffset;
            AsioLoopbackInputChannelOffset = asioLoopbackInputChannelOffset;
            AsioOutputChannelOffset = asioOutputChannelOffset;
            ImpulseResponse = null;
            LastError = null;

            Sweep?.Dispose();
            Sweep = new ExponentialSineSweep();
            Sweep.FillData(octaves, requestedDuration, bits, sampleRate);

            if (soundRecorder != null)
            {
                soundRecorder.LevelsAvailable -= HandleWaveLevelsAvailable;
                soundRecorder.Dispose();
            }
            soundRecorder = new SoundRecorder();
            int recorderChannelCount = audioBackend == AudioBackend.Wave
                ? GetRequiredWaveInputChannelCount()
                : 1;
            soundRecorder.Init(
                sampleRate,
                bits,
                recorderChannelCount,
                inputDeviceNumber);
            soundRecorder.LevelsAvailable += HandleWaveLevelsAvailable;
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
                if (Sweep == null || soundRecorder == null)
                {
                    throw new InvalidOperationException("Measurement is not initialized.");
                }

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = new CancellationTokenSource();
                inProgress = true;
                ImpulseResponse = null;
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
            if (Sweep == null)
            {
                return 0;
            }
            return Sweep.SweepSamples * Log(harmonic) / Log(Pow(2, Octaves));
        }

        public void RestoreImpulseResponse(
            int octaves,
            int sampleRate,
            int bits,
            double sweepDurationSeconds,
            PlaybackChannel playChannel,
            Complex[] impulseResponse,
            int maxMagnitudeIndex)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(impulseResponse);
            if (InProgress)
            {
                throw new InvalidOperationException(
                    "Cannot load an impulse response while a measurement is running.");
            }
            if (impulseResponse.Length == 0)
            {
                throw new ArgumentException(
                    "Impulse response cannot be empty.",
                    nameof(impulseResponse));
            }
            if ((uint)maxMagnitudeIndex >= (uint)impulseResponse.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(maxMagnitudeIndex));
            }

            Init(
                octaves,
                sampleRate,
                bits,
                sweepDurationSeconds,
                playChannel,
                OutputDeviceNumber,
                InputDeviceNumber,
                AudioBackend,
                AsioDriverName,
                AsioInputChannelOffset,
                AsioOutputChannelOffset,
                WaveInputChannelOffset,
                WaveLoopbackInputChannelOffset,
                AsioLoopbackInputChannelOffset);
            ImpulseResponse = impulseResponse.ToArray();
            PeakIndex = maxMagnitudeIndex;
            LastError = null;
        }

        private void HandleWaveLevelsAvailable(AudioChannelLevel[] channels)
        {
            RaiseLevels(MapWaveLevels(channels));
        }

        private async Task<bool> RunCoreAsync(CancellationToken cancellationToken)
        {
            ExponentialSineSweep sweep = Sweep!;
            SoundRecorder recorder = soundRecorder!;
            bool success = false;

            try
            {
                if (AudioBackend == AudioBackend.Asio)
                {
                    await RunAsioAsync(sweep, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await RunWaveAsync(sweep, recorder, cancellationToken).ConfigureAwait(false);
                }
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
            ExponentialSineSweep sweep,
            SoundRecorder recorder,
            CancellationToken cancellationToken)
        {
            using var player = new WaveOutEvent
            {
                DeviceNumber = OutputDeviceNumber
            };
            await recorder.StartRecordingAsync(cancellationToken).ConfigureAwait(false);
            int recordingStart = recorder.ReadSamples;

            RawSourceWaveStream stream = sweep.GetStream(PlaybackChannel);
            stream.Position = 0;
            player.Init(stream);
            await PlayToEndAsync(player, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;

            int requiredSamples = recordingStart + sweep.SweepSamples + SampleRate;
            await recorder.WaitForSamplesAsync(requiredSamples, cancellationToken).ConfigureAwait(false);
            await recorder.StopRecordingAsync().ConfigureAwait(false);

            ProcessImpulseResponse(recorder.GetSamplesSnapshot(), sweep);
        }

        private async Task RunAsioAsync(
            ExponentialSineSweep sweep,
            CancellationToken cancellationToken)
        {
            using var session = new AsioFullDuplexSession(
                AsioDriverName ?? string.Empty,
                AsioInputChannelOffset,
                AsioOutputChannelOffset);
            session.LevelsAvailable += HandleAsioLevelsAvailable;
            using FloatArrayWaveStream stream = FloatArrayWaveStream.FromMonoSamples(
                sweep.SweepData,
                SampleRate,
                PlaybackChannel);

            await session.StartAsync(
                stream,
                SampleRate,
                autoStop: false,
                cancellationToken).ConfigureAwait(false);
            int requiredSamples = session.ReadSamples + sweep.SweepSamples + SampleRate;
            await session.WaitForSamplesAsync(requiredSamples, cancellationToken).ConfigureAwait(false);
            await session.StopAsync().ConfigureAwait(false);
            session.LevelsAvailable -= HandleAsioLevelsAvailable;

            ProcessImpulseResponse(session.GetSamplesSnapshot(), sweep);
        }

        private void HandleAsioLevelsAvailable(AudioChannelLevel[] channels)
        {
            RaiseLevels(MapAsioLevels(channels));
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
            int channelIndex = AudioBackend == AudioBackend.Wave
                ? WaveInputChannelOffset
                : 0;
            if (AudioBackend == AudioBackend.Wave &&
                sampleChannels.Length > 1 &&
                (WaveInputChannelOffset > 0 ||
                    WaveLoopbackInputChannelOffset.HasValue))
            {
                RecordedChannelValidator.EnsureDifferentSignals(
                    sampleChannels,
                    0,
                    1,
                    "Wave measurement");
            }

            float[] recorded = (uint)channelIndex < (uint)sampleChannels.Length
                ? sampleChannels[channelIndex]
                : Array.Empty<float>();
            if (recorded.Length == 0)
            {
                throw new InvalidOperationException("No audio samples were recorded.");
            }

            // Zero-padding to the full linear-convolution length prevents circular wraparound
            // from moving late response energy to the beginning of the impulse response.
            int convolutionLength = checked(recorded.Length + sweep.InverseFilter.Length - 1);
            int fftLength = DspMath.NextPowerOfTwo(convolutionLength);
            Complex[] input = new Complex[fftLength];
            Complex[] inverseFilter = new Complex[fftLength];

            for (int i = 0; i < recorded.Length; i++)
            {
                input[i] = new Complex(recorded[i], 0);
            }
            for (int i = 0; i < sweep.InverseFilter.Length; i++)
            {
                inverseFilter[i] = new Complex(sweep.InverseFilter[i], 0);
            }

            Fourier.Forward(input, FourierOptions.Matlab);
            Fourier.Forward(inverseFilter, FourierOptions.Matlab);
            for (int i = 0; i < input.Length; i++)
            {
                input[i] *= inverseFilter[i];
            }
            Fourier.Inverse(input, FourierOptions.Matlab);

            var impulseResponse = new Complex[convolutionLength];
            double normalization = 2.0 / sweep.InverseFilter.Length;
            double maxMagnitude = 0;
            int maxMagnitudeIndex = 0;
            for (int i = 0; i < impulseResponse.Length; i++)
            {
                impulseResponse[i] = new Complex(input[i].Real * normalization, 0);
                if (impulseResponse[i].Magnitude > maxMagnitude)
                {
                    maxMagnitude = impulseResponse[i].Magnitude;
                    maxMagnitudeIndex = i;
                }
            }

            ImpulseResponse = impulseResponse;
            PeakIndex = maxMagnitudeIndex;
            RaiseLevels(CreateFinalLevelSnapshot(sampleChannels));
        }

        private InputLevelMeterSnapshot CreateFinalLevelSnapshot(float[][] sampleChannels)
        {
            AudioChannelLevel[] measuredLevels = MeasureRecordedChannels(sampleChannels);
            return AudioBackend == AudioBackend.Wave
                ? MapWaveLevels(measuredLevels)
                : MapAsioLevels(measuredLevels);
        }

        private InputLevelMeterSnapshot MapWaveLevels(AudioChannelLevel[] channels)
        {
            InputLevelMeterEntry microphone = CreateEntry(
                TryGetLevel(channels, WaveInputChannelOffset),
                fullScaleReference: false);
            InputLevelMeterEntry loopback = CreateEntry(
                WaveLoopbackInputChannelOffset is int loopbackChannel
                    ? TryGetLevel(channels, loopbackChannel)
                    : null,
                fullScaleReference: true);
            return new InputLevelMeterSnapshot(microphone, loopback);
        }

        private static InputLevelMeterSnapshot MapAsioLevels(AudioChannelLevel[] channels)
        {
            InputLevelMeterEntry microphone = CreateEntry(
                TryGetLevel(channels, 0),
                fullScaleReference: false);
            return new InputLevelMeterSnapshot(
                microphone,
                InputLevelMeterEntry.Unavailable);
        }

        private void RaiseLevels(InputLevelMeterSnapshot snapshot)
        {
            LevelsAvailable?.Invoke(snapshot);
        }

        private static AudioChannelLevel? TryGetLevel(
            AudioChannelLevel[] channels,
            int channelIndex)
        {
            return (uint)channelIndex < (uint)channels.Length
                ? channels[channelIndex]
                : null;
        }

        private static InputLevelMeterEntry CreateEntry(
            AudioChannelLevel? level,
            bool fullScaleReference)
        {
            if (level == null)
            {
                return InputLevelMeterEntry.Unavailable;
            }

            AudioChannelLevel value = level.Value;
            return new InputLevelMeterEntry(
                true,
                value.PeakDbFs,
                value.RmsDbFs,
                !fullScaleReference && value.FullScale,
                fullScaleReference && value.FullScale);
        }

        private static AudioChannelLevel[] MeasureRecordedChannels(float[][] sampleChannels)
        {
            var levels = new AudioChannelLevel[sampleChannels.Length];
            for (int channel = 0; channel < sampleChannels.Length; channel++)
            {
                levels[channel] = MeasureRecordedChannel(sampleChannels[channel]);
            }

            return levels;
        }

        private static AudioChannelLevel MeasureRecordedChannel(float[] samples)
        {
            double peak = 0;
            double sumSquares = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double magnitude = Math.Abs(samples[i]);
                peak = Math.Max(peak, magnitude);
                sumSquares += samples[i] * samples[i];
            }

            double rms = samples.Length > 0
                ? Math.Sqrt(sumSquares / samples.Length)
                : 0;
            return new AudioChannelLevel(
                DataHelper.AmplitudeToDecibels(peak),
                DataHelper.AmplitudeToDecibels(rms),
                peak >= 0.999);
        }

        private int GetRequiredWaveInputChannelCount()
        {
            int maxChannelOffset = WaveInputChannelOffset;
            if (WaveLoopbackInputChannelOffset.HasValue)
            {
                maxChannelOffset = Math.Max(
                    maxChannelOffset,
                    WaveLoopbackInputChannelOffset.Value);
            }

            return maxChannelOffset + 1;
        }

        private static int? NormalizeOptionalWaveChannel(int? offset)
        {
            return offset.HasValue
                ? Math.Clamp(offset.Value, 0, 1)
                : null;
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
            Sweep?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
