using System.Threading.Channels;
using System.Numerics;
using NAudio.Wave;
using Resonalyze.Dsp;
using Resonalyze.Options;

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
        private ChannelWriter<float[][]>? sequenceWriter;
        private double[]? accumulatedData;
        private Complex[]? accumulatedCrossSpectrum;
        private double[]? accumulatedReferencePowerSpectrum;
        private int sequencesCounter;
        private volatile bool inProgress;
        private bool disposed;

        public event Action<bool>? Completed;
        internal event Action<InputLevelMeterSnapshot>? LevelsAvailable;

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
        public int? AsioLoopbackInputChannelOffset { get; private set; }
        public int AsioOutputChannelOffset { get; private set; }
        public int WaveInputChannelOffset { get; private set; }
        public int? WaveLoopbackInputChannelOffset { get; private set; }
        public int SequenceLength { get; private set; }
        public LiveSpectrumOptions LiveSpectrumOptions { get; private set; } = new();
        public Exception? LastError { get; private set; }
        public bool HasConfiguredLoopback =>
            AudioBackend == AudioBackend.Wave
                ? WaveLoopbackInputChannelOffset.HasValue &&
                    WaveLoopbackInputChannelOffset.Value != WaveInputChannelOffset
                : AsioLoopbackInputChannelOffset.HasValue &&
                    AsioLoopbackInputChannelOffset.Value != AsioInputChannelOffset;

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
            int asioOutputChannelOffset = 0,
            int waveInputChannelOffset = 0,
            int? waveLoopbackInputChannelOffset = null,
            int? asioLoopbackInputChannelOffset = null,
            LiveSpectrumOptions? liveSpectrumOptions = null)
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
            AsioLoopbackInputChannelOffset = asioLoopbackInputChannelOffset;
            AsioOutputChannelOffset = asioOutputChannelOffset;
            WaveInputChannelOffset = Math.Clamp(waveInputChannelOffset, 0, 1);
            WaveLoopbackInputChannelOffset = NormalizeOptionalWaveChannel(
                waveLoopbackInputChannelOffset);
            LiveSpectrumOptions = liveSpectrumOptions ?? new LiveSpectrumOptions();

            signal?.Dispose();
            signal = new NoiseSignal();
            signal.FillData(requestedDuration, bits, sampleRate);

            if (soundRecorder != null)
            {
                soundRecorder.SequenceChannelsReady -= ProcessSequenceChannels;
                soundRecorder.LevelsAvailable -= HandleWaveLevelsAvailable;
                soundRecorder.Dispose();
            }
            soundRecorder = new SoundRecorder();
            soundRecorder.Init(
                sampleRate,
                bits,
                GetRequiredWaveInputChannelCount(),
                inputDeviceNumber);
            soundRecorder.Sequence = sequenceLength;
            soundRecorder.SequenceChannelsReady += ProcessSequenceChannels;
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
                if (signal == null || soundRecorder == null)
                {
                    throw new InvalidOperationException("Measurement is not initialized.");
                }

                lock (dataSync)
                {
                    accumulatedData = null;
                    accumulatedCrossSpectrum = null;
                    accumulatedReferencePowerSpectrum = null;
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
            var sequenceChannel = Channel.CreateBounded<float[][]>(
                new BoundedChannelOptions(4)
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
                GetAsioCaptureFirstInputOffset(),
                AsioOutputChannelOffset,
                GetAsioCaptureInputChannelCount())
            {
                Sequence = SequenceLength
            };
            session.SequenceChannelsReady += ProcessSequenceChannels;
            session.LevelsAvailable += HandleAsioLevelsAvailable;
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
                session.SequenceChannelsReady -= ProcessSequenceChannels;
                session.LevelsAvailable -= HandleAsioLevelsAvailable;
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

        private void ProcessSequenceChannels(float[][] sequence)
        {
            Volatile.Read(ref sequenceWriter)?.TryWrite(sequence);
        }

        private void HandleWaveLevelsAvailable(AudioChannelLevel[] channels)
        {
            RaiseLevels(channels);
        }

        private void HandleAsioLevelsAvailable(AudioChannelLevel[] channels)
        {
            RaiseLevels(channels);
        }

        private void RaiseLevels(AudioChannelLevel[] channels)
        {
            int microphoneIndex = GetMicrophoneSequenceIndex();
            int? loopbackIndex = GetLoopbackSequenceIndex();

            InputLevelMeterEntry microphone =
                (uint)microphoneIndex < (uint)channels.Length
                ? new InputLevelMeterEntry(
                    true,
                    channels[microphoneIndex].PeakDbFs,
                    channels[microphoneIndex].RmsDbFs,
                    channels[microphoneIndex].FullScale,
                    false)
                : InputLevelMeterEntry.Unavailable;
            InputLevelMeterEntry loopback =
                loopbackIndex.HasValue && (uint)loopbackIndex.Value < (uint)channels.Length
                    ? new InputLevelMeterEntry(
                        true,
                        channels[loopbackIndex.Value].PeakDbFs,
                        channels[loopbackIndex.Value].RmsDbFs,
                        channels[loopbackIndex.Value].FullScale,
                        true)
                    : InputLevelMeterEntry.Unavailable;
            LevelsAvailable?.Invoke(new InputLevelMeterSnapshot(
                microphone,
                loopback));
        }

        private async Task ProcessSequencesAsync(
            ChannelReader<float[][]> reader,
            CancellationToken cancellationToken)
        {
            await foreach (float[][] sequence in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                AccumulateSequence(sequence);
            }
        }

        private void AccumulateSequence(float[][] sequence)
        {
            if (LiveSpectrumOptions.Mode == LiveSpectrumMode.TransferFunction)
            {
                AccumulateTransferSequence(sequence);
                return;
            }

            double[] magnitudes =
                SpectrumAnalysis.ComputeMagnitudeSpectrum(sequence[GetMicrophoneSequenceIndex()]);

            const double lerpCoefficient = 0.01;
            const double outOfRangeLerpCoefficient = 0.2;
            const int boxFilterRadius = 16;
            double[] minimums = BuildBoxFilteredExtrema(
                magnitudes,
                boxFilterRadius,
                findMinimum: true);
            double[] maximums = BuildBoxFilteredExtrema(
                magnitudes,
                boxFilterRadius,
                findMinimum: false);
            lock (dataSync)
            {
                if (accumulatedData == null)
                {
                    if (sequencesCounter > 2)
                    {
                        accumulatedData = magnitudes;
                    }
                }
                else
                {
                    for (int i = 0; i < accumulatedData.Length; i++)
                    {
                        double history = accumulatedData[i];
                        double currentLerpCoefficient =
                            history >= minimums[i] && history <= maximums[i]
                                ? lerpCoefficient
                                : outOfRangeLerpCoefficient;
                        accumulatedData[i] =
                            (1 - currentLerpCoefficient) * history +
                            currentLerpCoefficient * magnitudes[i];
                    }
                }
                sequencesCounter++;
            }
        }

        private void AccumulateTransferSequence(float[][] sequence)
        {
            TransferSpectrumFrame frame = ComputeTransferSpectrumFrame(sequence);

            const double lerpCoefficient = 0.01;
            lock (dataSync)
            {
                if (accumulatedCrossSpectrum == null ||
                    accumulatedReferencePowerSpectrum == null)
                {
                    if (sequencesCounter > 2)
                    {
                        accumulatedCrossSpectrum = frame.CrossSpectrum.ToArray();
                        accumulatedReferencePowerSpectrum =
                            frame.ReferencePowerSpectrum.ToArray();
                        accumulatedData = SpectrumAnalysis.ComputeH1MagnitudeSpectrum(
                            accumulatedCrossSpectrum,
                            accumulatedReferencePowerSpectrum);
                    }
                }
                else
                {
                    for (int i = 0; i < accumulatedCrossSpectrum.Length; i++)
                    {
                        accumulatedCrossSpectrum[i] =
                            (1 - lerpCoefficient) * accumulatedCrossSpectrum[i] +
                            lerpCoefficient * frame.CrossSpectrum[i];
                        accumulatedReferencePowerSpectrum[i] =
                            (1 - lerpCoefficient) * accumulatedReferencePowerSpectrum[i] +
                            lerpCoefficient * frame.ReferencePowerSpectrum[i];
                    }

                    accumulatedData = SpectrumAnalysis.ComputeH1MagnitudeSpectrum(
                        accumulatedCrossSpectrum,
                        accumulatedReferencePowerSpectrum);
                }

                sequencesCounter++;
            }
        }

        private static double[] BuildBoxFilteredExtrema(
            IReadOnlyList<double> source,
            int radius,
            bool findMinimum)
        {
            if (radius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            var filtered = new double[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                int start = Math.Max(0, i - radius);
                int end = Math.Min(source.Count - 1, i + radius);
                double extreme = source[i];
                for (int j = start; j <= end; j++)
                {
                    extreme = findMinimum
                        ? Math.Min(extreme, source[j])
                        : Math.Max(extreme, source[j]);
                }

                filtered[i] = extreme;
            }

            return filtered;
        }

        private TransferSpectrumFrame ComputeTransferSpectrumFrame(float[][] sequence)
        {
            int microphoneIndex = GetMicrophoneSequenceIndex();
            int? loopbackIndex = GetLoopbackSequenceIndex();
            if (!loopbackIndex.HasValue ||
                (uint)microphoneIndex >= (uint)sequence.Length ||
                (uint)loopbackIndex.Value >= (uint)sequence.Length)
            {
                throw new InvalidOperationException(
                    "Live transfer function requires a configured loopback input.");
            }

            return SpectrumAnalysis.ComputeTransferSpectrumFrame(
                sequence[loopbackIndex.Value],
                sequence[microphoneIndex]);
        }

        private int GetRequiredWaveInputChannelCount()
        {
            int lastChannel = WaveInputChannelOffset;
            if (LiveSpectrumOptions.Mode == LiveSpectrumMode.TransferFunction &&
                WaveLoopbackInputChannelOffset.HasValue)
            {
                lastChannel = Math.Max(lastChannel, WaveLoopbackInputChannelOffset.Value);
            }

            return lastChannel + 1;
        }

        private int GetAsioCaptureFirstInputOffset()
        {
            if (LiveSpectrumOptions.Mode == LiveSpectrumMode.TransferFunction &&
                AsioLoopbackInputChannelOffset.HasValue)
            {
                return Math.Min(AsioInputChannelOffset, AsioLoopbackInputChannelOffset.Value);
            }

            return AsioInputChannelOffset;
        }

        private int GetAsioCaptureInputChannelCount()
        {
            int firstInputOffset = GetAsioCaptureFirstInputOffset();
            int lastInputOffset = AsioInputChannelOffset;
            if (LiveSpectrumOptions.Mode == LiveSpectrumMode.TransferFunction &&
                AsioLoopbackInputChannelOffset.HasValue)
            {
                lastInputOffset = Math.Max(lastInputOffset, AsioLoopbackInputChannelOffset.Value);
            }

            return lastInputOffset - firstInputOffset + 1;
        }

        private int GetMicrophoneSequenceIndex() =>
            AudioBackend == AudioBackend.Wave
                ? WaveInputChannelOffset
                : AsioInputChannelOffset - GetAsioCaptureFirstInputOffset();

        private int? GetLoopbackSequenceIndex() =>
            AudioBackend == AudioBackend.Wave
                ? WaveLoopbackInputChannelOffset
                : AsioLoopbackInputChannelOffset.HasValue
                    ? AsioLoopbackInputChannelOffset.Value - GetAsioCaptureFirstInputOffset()
                    : null;

        private static int? NormalizeOptionalWaveChannel(int? channel) =>
            channel.HasValue
                ? Math.Clamp(channel.Value, 0, 1)
                : null;

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
                soundRecorder.SequenceChannelsReady -= ProcessSequenceChannels;
                soundRecorder.LevelsAvailable -= HandleWaveLevelsAvailable;
                soundRecorder.Dispose();
            }
            signal?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
