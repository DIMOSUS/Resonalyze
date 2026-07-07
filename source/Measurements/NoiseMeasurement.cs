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
        private readonly object levelSync = new();
        private SoundRecorder? soundRecorder;
        private SoundRecorder? loopbackRecorder;
        private LoopbackSequencePairer? loopbackPairer;
        private AudioChannelLevel[] latestMicrophoneLevels = Array.Empty<AudioChannelLevel>();
        private AudioChannelLevel[] latestLoopbackLevels = Array.Empty<AudioChannelLevel>();
        private CancellationTokenSource? cancellationTokenSource;
        private Task<bool>? measurementTask;
        private ChannelWriter<float[][]>? sequenceWriter;
        private Complex[]? accumulatedCrossSpectrum;
        private double[]? accumulatedReferencePowerSpectrum;
        private double[]? accumulatedTargetPowerSpectrum;
        private double transferAlpha = 1.0;
        private bool infiniteAveraging;
        private int averagedFrameCount;
        private int sequencesCounter;
        private long lastDropTickMs;
        private int droppedFrameTotal;
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
        // When set (and different from the microphone device), the Wave loopback is captured
        // from this separate input device. Best-effort aligned (see LoopbackSequencePairer).
        public int? WaveLoopbackDeviceNumber { get; private set; }
        public int SequenceLength { get; private set; }
        public LiveSpectrumOptions LiveSpectrumOptions { get; private set; } = new();
        public Exception? LastError { get; private set; }
        public bool HasConfiguredLoopback =>
            AudioBackend == AudioBackend.Wave
                ? WaveLoopbackInputChannelOffset.HasValue &&
                    (UsesSeparateWaveLoopbackDevice ||
                        WaveLoopbackInputChannelOffset.Value != WaveInputChannelOffset)
                : AsioLoopbackInputChannelOffset.HasValue &&
                    AsioLoopbackInputChannelOffset.Value != AsioInputChannelOffset;

        // Loopback configured on a different Wave device than the microphone.
        private bool UsesSeparateWaveLoopbackDevice =>
            AudioBackend == AudioBackend.Wave &&
            WaveLoopbackInputChannelOffset.HasValue &&
            WaveLoopbackDeviceNumber.HasValue &&
            WaveLoopbackDeviceNumber.Value != InputDeviceNumber;

        // True only when a separate loopback device must actually be captured and paired.
        private bool PairsSeparateLoopbackDevice => UsesSeparateWaveLoopbackDevice;

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
            LiveSpectrumOptions? liveSpectrumOptions = null,
            int? waveLoopbackDeviceNumber = null)
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
            WaveLoopbackDeviceNumber = waveLoopbackDeviceNumber;
            LiveSpectrumOptions = liveSpectrumOptions ?? new LiveSpectrumOptions();

            signal?.Dispose();
            signal = new NoiseSignal();
            signal.FillData(
                requestedDuration,
                bits,
                sampleRate,
                LiveSpectrumOptions.NoiseColor,
                SequenceLength);

            DisposeRecorders();
            lock (levelSync)
            {
                latestMicrophoneLevels = Array.Empty<AudioChannelLevel>();
                latestLoopbackLevels = Array.Empty<AudioChannelLevel>();
            }

            bool separateLoopbackDevice = PairsSeparateLoopbackDevice;
            soundRecorder = new SoundRecorder();
            int recorderChannelCount = separateLoopbackDevice
                ? WaveInputChannelOffset + 1
                : GetRequiredWaveInputChannelCount();
            soundRecorder.Init(
                sampleRate,
                bits,
                recorderChannelCount,
                inputDeviceNumber);
            soundRecorder.Sequence = sequenceLength;

            if (separateLoopbackDevice)
            {
                loopbackRecorder = new SoundRecorder();
                loopbackRecorder.Init(
                    sampleRate,
                    bits,
                    WaveLoopbackInputChannelOffset!.Value + 1,
                    WaveLoopbackDeviceNumber!.Value);
                loopbackRecorder.Sequence = sequenceLength;
                // Microphone block is index 0 of its recorder's channels (single channel
                // captured), loopback block likewise; the pairer combines them into [mic, loop].
                loopbackPairer = new LoopbackSequencePairer(
                    WaveInputChannelOffset,
                    WaveLoopbackInputChannelOffset!.Value,
                    ProcessSequenceChannels);
                soundRecorder.SequenceChannelsReady += loopbackPairer.PushMicrophone;
                loopbackRecorder.SequenceChannelsReady += loopbackPairer.PushLoopback;
                soundRecorder.LevelsAvailable += HandleMicrophoneOnlyLevels;
                loopbackRecorder.LevelsAvailable += HandleLoopbackOnlyLevels;
            }
            else
            {
                soundRecorder.SequenceChannelsReady += ProcessSequenceChannels;
                soundRecorder.LevelsAvailable += HandleWaveLevelsAvailable;
            }
        }

        private void DisposeRecorders()
        {
            if (soundRecorder != null)
            {
                soundRecorder.SequenceChannelsReady -= ProcessSequenceChannels;
                soundRecorder.LevelsAvailable -= HandleWaveLevelsAvailable;
                soundRecorder.LevelsAvailable -= HandleMicrophoneOnlyLevels;
                if (loopbackPairer != null)
                {
                    soundRecorder.SequenceChannelsReady -= loopbackPairer.PushMicrophone;
                }
                soundRecorder.Dispose();
                soundRecorder = null;
            }
            if (loopbackRecorder != null)
            {
                loopbackRecorder.LevelsAvailable -= HandleLoopbackOnlyLevels;
                if (loopbackPairer != null)
                {
                    loopbackRecorder.SequenceChannelsReady -= loopbackPairer.PushLoopback;
                }
                loopbackRecorder.Dispose();
                loopbackRecorder = null;
            }
            loopbackPairer = null;
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
                    accumulatedCrossSpectrum = null;
                    accumulatedReferencePowerSpectrum = null;
                    accumulatedTargetPowerSpectrum = null;
                    sequencesCounter = 0;
                    averagedFrameCount = 0;
                }
                Interlocked.Exchange(ref lastDropTickMs, 0);
                Interlocked.Exchange(ref droppedFrameTotal, 0);
                // Drop any blocks left queued by a previous run so the first transfer frame
                // cannot pair a fresh block with a stale one.
                loopbackPairer?.Reset();

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

        /// <summary>
        /// Produces a consistent display snapshot. The accumulators are cloned
        /// under the data lock and the heavier H1/coherence/magnitude math runs
        /// outside the lock, so a slow or busy UI consumer cannot stall the
        /// background accumulation that drives the actual measurement.
        /// </summary>
        public LiveSpectrumSnapshot? GetAccumulatedSpectrumSnapshot()
        {
            Complex[] crossSpectrum;
            double[] referencePowerSpectrum;
            double[] targetPowerSpectrum;
            lock (dataSync)
            {
                if (accumulatedCrossSpectrum == null ||
                    accumulatedReferencePowerSpectrum == null ||
                    accumulatedTargetPowerSpectrum == null)
                {
                    return null;
                }

                crossSpectrum = (Complex[])accumulatedCrossSpectrum.Clone();
                referencePowerSpectrum = (double[])accumulatedReferencePowerSpectrum.Clone();
                targetPowerSpectrum = (double[])accumulatedTargetPowerSpectrum.Clone();
            }

            double[] magnitude = SpectrumAnalysis.ComputeH1MagnitudeSpectrum(
                crossSpectrum,
                referencePowerSpectrum);
            double[] coherence = SpectrumAnalysis.ComputeCoherence(
                crossSpectrum,
                referencePowerSpectrum,
                targetPowerSpectrum);
            return new LiveSpectrumSnapshot(magnitude, coherence);
        }

        private static double AlphaFromTimeConstant(double frameInterval, double timeConstant)
        {
            if (frameInterval <= 0.0 || timeConstant <= 0.0)
            {
                return 1.0;
            }

            return Math.Clamp(1.0 - Math.Exp(-frameInterval / timeConstant), 1e-6, 1.0);
        }

        // Exponential smoothing time constant for the transfer function, in seconds.
        private static double GetAveragingTimeConstant(AveragingSpeed speed) => speed switch
        {
            AveragingSpeed.Fast => 0.3,
            AveragingSpeed.Slow => 3.0,
            AveragingSpeed.Infinite => 0.0,
            _ => 1.0
        };

        /// <summary>
        /// Clears the running average and peak state without stopping capture,
        /// so the analyzer starts integrating from scratch. Safe to call from
        /// the UI thread while a measurement is active.
        /// </summary>
        public void ResetAccumulation()
        {
            lock (dataSync)
            {
                accumulatedCrossSpectrum = null;
                accumulatedReferencePowerSpectrum = null;
                accumulatedTargetPowerSpectrum = null;
                sequencesCounter = 0;
                averagedFrameCount = 0;
            }
        }

        public void RefreshLiveAveraging()
        {
            lock (dataSync)
            {
                UpdateAveragingParameters();
            }
        }

        /// <summary>
        /// True when capture blocks were dropped within the given window because
        /// the processing pipeline could not keep up (a CPU-overload signal).
        /// </summary>
        public bool HasRecentDrops(int windowMilliseconds = 1000)
        {
            long lastDrop = Interlocked.Read(ref lastDropTickMs);
            return inProgress &&
                lastDrop != 0 &&
                Environment.TickCount64 - lastDrop < windowMilliseconds;
        }

        private void OnSequenceDropped(float[][] dropped)
        {
            Interlocked.Increment(ref droppedFrameTotal);
            Interlocked.Exchange(ref lastDropTickMs, Environment.TickCount64);
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
                },
                OnSequenceDropped);
            Volatile.Write(ref sequenceWriter, sequenceChannel.Writer);
            int hopSize = ComputeHopSize();
            lock (dataSync)
            {
                UpdateAveragingParameters();
            }
            var reframer = new OverlapReframer(SequenceLength, hopSize);
            Task processingTask = ProcessSequencesAsync(
                sequenceChannel.Reader,
                reframer,
                cancellationToken);

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
                // A live capture only ends when the user stops it; cancellation
                // is the normal completion, not a failure.
                success = true;
            }
            catch (Exception exception)
            {
                LastError = exception;
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
                    SoundRecorder? loopback = loopbackRecorder;
                    if (loopback != null)
                    {
                        await loopback.StopRecordingAsync().ConfigureAwait(false);
                    }
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
            SoundRecorder? loopback = loopbackRecorder;
            if (loopback != null)
            {
                await loopback.StartRecordingAsync(cancellationToken).ConfigureAwait(false);
            }

            // A looping provider keeps the excitation seamless: restarting playback
            // per pass (the old play-to-end loop) left an audible gap at every loop
            // boundary and a hole in the captured noise.
            stream.Position = 0;
            player.Init(new LoopingWaveProvider(stream));
            await PlayUntilCancelledAsync(player, cancellationToken).ConfigureAwait(false);
        }

        private static async Task PlayUntilCancelledAsync(
            WaveOutEvent player,
            CancellationToken cancellationToken)
        {
            var stopped = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

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
                // The looping provider never runs dry; only cancellation or a
                // device error ends playback.
                await stopped.Task.ConfigureAwait(false);
            }
            finally
            {
                player.PlaybackStopped -= PlaybackStopped;
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

        // Separate loopback device: microphone and loopback levels come from two recorders.
        // Keep the latest of each and raise a combined snapshot so both meters update live.
        private void HandleMicrophoneOnlyLevels(AudioChannelLevel[] channels)
        {
            lock (levelSync)
            {
                latestMicrophoneLevels = channels;
            }
            RaiseCombinedDualDeviceLevels();
        }

        private void HandleLoopbackOnlyLevels(AudioChannelLevel[] channels)
        {
            lock (levelSync)
            {
                latestLoopbackLevels = channels;
            }
            RaiseCombinedDualDeviceLevels();
        }

        private void RaiseCombinedDualDeviceLevels()
        {
            AudioChannelLevel[] microphoneChannels;
            AudioChannelLevel[] loopbackChannels;
            lock (levelSync)
            {
                microphoneChannels = latestMicrophoneLevels;
                loopbackChannels = latestLoopbackLevels;
            }

            InputLevelMeterEntry microphone = CreateLevelEntry(
                microphoneChannels,
                WaveInputChannelOffset,
                loopbackReference: false);
            InputLevelMeterEntry loopback = WaveLoopbackInputChannelOffset is int loopbackChannel
                ? CreateLevelEntry(loopbackChannels, loopbackChannel, loopbackReference: true)
                : InputLevelMeterEntry.Unavailable;
            LevelsAvailable?.Invoke(new InputLevelMeterSnapshot(microphone, loopback));
        }

        private static InputLevelMeterEntry CreateLevelEntry(
            AudioChannelLevel[] channels,
            int index,
            bool loopbackReference) =>
            (uint)index < (uint)channels.Length
                ? new InputLevelMeterEntry(
                    true,
                    channels[index].PeakDbFs,
                    channels[index].RmsDbFs,
                    !loopbackReference && channels[index].FullScale,
                    loopbackReference && channels[index].FullScale)
                : InputLevelMeterEntry.Unavailable;

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
            OverlapReframer reframer,
            CancellationToken cancellationToken)
        {
            await foreach (float[][] sequence in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (float[][] frame in reframer.Push(sequence))
                {
                    AccumulateTransferSequence(frame);
                }
            }
        }

        private int ComputeHopSize()
        {
            int overlapPercent = Math.Clamp(EffectiveOverlapPercent, 0, 75);
            if (overlapPercent <= 0)
            {
                return SequenceLength;
            }

            int hop = SequenceLength * (100 - overlapPercent) / 100;
            return Math.Clamp(hop, 1, SequenceLength);
        }

        // Periodic pink noise is a deterministic single-period signal analyzed with a
        // rectangular window, so overlapping frames are highly correlated and add no
        // effective averaging. Disable overlap for it to avoid wasting CPU.
        private int EffectiveOverlapPercent =>
            LiveSpectrumOptions.NoiseColor == NoiseColor.PinkPeriodic
                ? 0
                : LiveSpectrumOptions.OverlapPercent;

        private void UpdateAveragingParameters()
        {
            int hopSize = ComputeHopSize();
            double frameInterval = SampleRate > 0 ? (double)hopSize / SampleRate : 0.0;
            double transferSeconds =
                GetAveragingTimeConstant(LiveSpectrumOptions.AveragingSpeed);
            infiniteAveraging = LiveSpectrumOptions.AveragingSpeed == AveragingSpeed.Infinite;
            transferAlpha = AlphaFromTimeConstant(frameInterval, transferSeconds);
        }

        private void AccumulateTransferSequence(float[][] sequence)
        {
            TransferSpectrumFrame frame = ComputeTransferSpectrumFrame(sequence);

            lock (dataSync)
            {
                if (accumulatedCrossSpectrum == null ||
                    accumulatedReferencePowerSpectrum == null ||
                    accumulatedTargetPowerSpectrum == null)
                {
                    if (sequencesCounter <= 2)
                    {
                        // Discard the first frames so the device can settle.
                        sequencesCounter++;
                        return;
                    }

                    if (infiniteAveraging)
                    {
                        // Honest cumulative mean: seed with the first real frame.
                        accumulatedCrossSpectrum = frame.CrossSpectrum.ToArray();
                        accumulatedReferencePowerSpectrum =
                            frame.ReferencePowerSpectrum.ToArray();
                        accumulatedTargetPowerSpectrum =
                            frame.TargetPowerSpectrum.ToArray();
                        averagedFrameCount = 1;
                        sequencesCounter++;
                        return;
                    }

                    accumulatedCrossSpectrum = new Complex[frame.CrossSpectrum.Length];
                    accumulatedReferencePowerSpectrum =
                        new double[frame.ReferencePowerSpectrum.Length];
                    accumulatedTargetPowerSpectrum =
                        new double[frame.TargetPowerSpectrum.Length];
                    averagedFrameCount = 1;
                }

                double alpha = infiniteAveraging
                    ? 1.0 / (averagedFrameCount + 1)
                    : transferAlpha;
                for (int i = 0; i < accumulatedCrossSpectrum.Length; i++)
                {
                    accumulatedCrossSpectrum[i] =
                        (1 - alpha) * accumulatedCrossSpectrum[i] +
                        alpha * frame.CrossSpectrum[i];
                    accumulatedReferencePowerSpectrum[i] =
                        (1 - alpha) * accumulatedReferencePowerSpectrum[i] +
                        alpha * frame.ReferencePowerSpectrum[i];
                    accumulatedTargetPowerSpectrum[i] =
                        (1 - alpha) * accumulatedTargetPowerSpectrum[i] +
                        alpha * frame.TargetPowerSpectrum[i];
                }
                averagedFrameCount++;
                sequencesCounter++;
            }
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
                sequence[microphoneIndex],
                EffectiveWindowType);
        }

        // Periodic pink noise is exactly one FFT-length period, so every analysis block
        // holds a whole period: a rectangular (no) window then gives a leakage-free
        // spectrum with perfect bin resolution. Force it regardless of the stored window.
        private WindowType EffectiveWindowType =>
            LiveSpectrumOptions.NoiseColor == NoiseColor.PinkPeriodic
                ? WindowType.Rectangular
                : LiveSpectrumOptions.WindowType;

        private int GetRequiredWaveInputChannelCount() =>
            CaptureChannelLayout.RequiredWaveInputChannelCount(
                WaveInputChannelOffset,
                WaveLoopbackInputChannelOffset);

        private int GetAsioCaptureFirstInputOffset() =>
            CaptureChannelLayout.AsioFirstInputOffset(
                AsioInputChannelOffset,
                AsioLoopbackInputChannelOffset);

        private int GetAsioCaptureInputChannelCount() =>
            CaptureChannelLayout.AsioInputChannelCount(
                AsioInputChannelOffset,
                AsioLoopbackInputChannelOffset);

        private int GetMicrophoneSequenceIndex() =>
            PairsSeparateLoopbackDevice
                ? 0
                : AudioBackend == AudioBackend.Wave
                    ? WaveInputChannelOffset
                    : AsioInputChannelOffset - GetAsioCaptureFirstInputOffset();

        private int? GetLoopbackSequenceIndex() =>
            PairsSeparateLoopbackDevice
                ? 1
                : AudioBackend == AudioBackend.Wave
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
            DisposeRecorders();
            signal?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
