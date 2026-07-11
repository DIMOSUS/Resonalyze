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
        private Complex[]? accumulatedCrossSpectrum;
        private double[]? accumulatedReferencePowerSpectrum;
        private double[]? accumulatedTargetPowerSpectrum;
        private double transferAlpha = 1.0;
        private bool infiniteAveraging;
        private int averagedFrameCount;
        private int sequencesCounter;
        private long lastDropTickMs;
        private int droppedFrameTotal;
        // Bumped on every dropped capture block; the processing loop compares it
        // against the generation it has consumed and resets the reframer, so no
        // FFT frame is ever built across the discontinuity a drop leaves behind.
        private long dropGeneration;
        // Frames the coherence estimate must accumulate before it is shown at
        // all: below this the EMA has too few effective degrees of freedom and
        // the estimate reads near 1 regardless of the real channel relation.
        private const int MinCoherenceFrames = 4;
        private AveragingSpeed appliedAveragingSpeed;
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
            // Periodic pink repeats one FFT-length period exactly, and both
            // playback paths loop the stream seamlessly — so ONE period is the
            // whole signal. Materializing the full requested duration used to
            // allocate ~35 MB of LOH arrays (60 s mono floats + the stereo
            // playback copy) at the start of a live session, for a visible
            // memory spike and cold-start page-commit work right next to the
            // first audio callbacks. Non-periodic colours still need the full
            // length (a short loop of aperiodic noise would seam audibly).
            double signalDuration =
                LiveSpectrumOptions.NoiseColor == NoiseColor.PinkPeriodic
                    ? SequenceLength / (double)sampleRate
                    : requestedDuration;
            signal.FillData(
                signalDuration,
                bits,
                sampleRate,
                LiveSpectrumOptions.NoiseColor,
                SequenceLength);

            DisposeRecorders();

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

        private void DisposeRecorders()
        {
            if (soundRecorder != null)
            {
                soundRecorder.SequenceChannelsReady -= ProcessSequenceChannels;
                soundRecorder.LevelsAvailable -= HandleWaveLevelsAvailable;
                soundRecorder.Dispose();
                soundRecorder = null;
            }
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
            int frameCount;
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
                frameCount = averagedFrameCount;
            }

            double[] magnitude = SpectrumAnalysis.ComputeH1MagnitudeSpectrum(
                crossSpectrum,
                referencePowerSpectrum);
            // γ² of a single frame is identically 1 in every energized bin
            // whatever the real relation between the channels, and the first
            // few EMA frames are barely better — the display would mark the
            // whole curve "trusted" exactly when no statistics exist yet. Until
            // a few frames have accumulated the coherence is UNKNOWN (null),
            // not perfect.
            double[]? coherence = frameCount >= MinCoherenceFrames
                ? SpectrumAnalysis.ComputeCoherence(
                    crossSpectrum,
                    referencePowerSpectrum,
                    targetPowerSpectrum)
                : null;
            // The microphone auto-power is already accumulated for coherence, so the
            // reference-free RTA magnitude comes for free: normalize it by the same
            // window's coherent gain the frame was measured with so its level is
            // window-independent, matching ComputePowerSpectrum's convention.
            double[] inputMagnitude = SpectrumAnalysis.ComputeInputMagnitudeSpectrum(
                targetPowerSpectrum,
                EffectiveWindowType,
                SequenceLength);
            return new LiveSpectrumSnapshot(magnitude, coherence, inputMagnitude);
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
                // A Slow accumulation is not a valid Fast average (and vice
                // versa): grafting the new alpha onto the old mode's memory
                // mixes two different statistics — the curve keeps the long
                // memory of the previous mode for its whole decay. Switching
                // the averaging speed starts the new mode's statistics from
                // scratch, like the explicit Reset does.
                if (LiveSpectrumOptions.AveragingSpeed != appliedAveragingSpeed)
                {
                    accumulatedCrossSpectrum = null;
                    accumulatedReferencePowerSpectrum = null;
                    accumulatedTargetPowerSpectrum = null;
                    sequencesCounter = 0;
                    averagedFrameCount = 0;
                }

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
            // Consumed by the processing loop: a drop means the next dequeued
            // block is NOT contiguous with the reframer's buffered tail.
            Interlocked.Increment(ref dropGeneration);
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
            // Warm the whole analysis path BEFORE the driver starts: the first
            // real frame otherwise pays MathNet's FFT initialization, the JIT
            // of every spectrum method and the first large-array commits while
            // the audio callbacks are already running on a millisecond budget —
            // audible as cold-start dropouts in the first seconds.
            WarmUpAnalysisPath();

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
                // Wait on the user's stop AND on the driver's own stop: an
                // unplugged ASIO device stops delivering callbacks, and a
                // cancellation-only wait left the measurement frozen — plot
                // stuck, InProgress true, no Completed, no error.
                Task stopped = session.StoppedAsync();
                Task cancelled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                Task finished = await Task.WhenAny(stopped, cancelled).ConfigureAwait(false);
                await finished.ConfigureAwait(false);
                if (finished == stopped)
                {
                    throw new InvalidOperationException(
                        "The ASIO driver stopped unexpectedly (device removed or driver error).");
                }
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

        private void RaiseLevels(AudioChannelLevel[] channels)
        {
            // The shared mapping flags a full-scale loopback as the reference
            // (not clipped); this path used to set both flags at once.
            LevelsAvailable?.Invoke(InputLevelMapping.Map(
                channels,
                GetMicrophoneSequenceIndex(),
                GetLoopbackSequenceIndex()));
        }

        private async Task ProcessSequencesAsync(
            ChannelReader<float[][]> reader,
            OverlapReframer reframer,
            CancellationToken cancellationToken)
        {
            long consumedDropGeneration = Interlocked.Read(ref dropGeneration);
            await foreach (float[][] sequence in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // A dropped block means this sequence is not contiguous with
                // the reframer's buffered tail: an FFT frame built across the
                // seam reads the artificial step as a broadband burst and
                // poisons H1, coherence and the EMA for seconds after the
                // overload indicator clears. Start from a clean buffer instead
                // of splicing over the hole.
                long generation = Interlocked.Read(ref dropGeneration);
                if (generation != consumedDropGeneration)
                {
                    consumedDropGeneration = generation;
                    reframer.Reset();
                }

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
            appliedAveragingSpeed = LiveSpectrumOptions.AveragingSpeed;
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

                    // Seed the accumulators with the first real frame for every
                    // averaging mode. Cross- and reference-power carry the same
                    // running scale as target-power, so H1 magnitude and coherence
                    // divide it out regardless of the seed; but the RTA overlay
                    // reads sqrt(target-power) directly, so a zero seed would make
                    // it start sqrt(alpha) low and ramp up over the averaging time
                    // constant even for a steady input. Seeding gives an unbiased
                    // running estimate from the first frame instead.
                    accumulatedCrossSpectrum = frame.CrossSpectrum.ToArray();
                    accumulatedReferencePowerSpectrum =
                        frame.ReferencePowerSpectrum.ToArray();
                    accumulatedTargetPowerSpectrum =
                        frame.TargetPowerSpectrum.ToArray();
                    averagedFrameCount = 1;
                    sequencesCounter++;
                    return;
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

        // One synthetic frame through the exact code path the live analysis
        // runs (window creation, both forward FFTs, H1, coherence, the RTA
        // magnitude): everything is JIT-compiled and MathNet's internals are
        // initialized before the first real capture block, off the audio
        // thread's budget. A few milliseconds once per start.
        private void WarmUpAnalysisPath()
        {
            var reference = new float[SequenceLength];
            var target = new float[SequenceLength];
            reference[0] = 1.0f;
            target[0] = 1.0f;
            TransferSpectrumFrame frame = SpectrumAnalysis.ComputeTransferSpectrumFrame(
                reference,
                target,
                EffectiveWindowType);
            _ = SpectrumAnalysis.ComputeH1MagnitudeSpectrum(
                frame.CrossSpectrum,
                frame.ReferencePowerSpectrum);
            _ = SpectrumAnalysis.ComputeCoherence(
                frame.CrossSpectrum,
                frame.ReferencePowerSpectrum,
                frame.TargetPowerSpectrum);
            _ = SpectrumAnalysis.ComputeInputMagnitudeSpectrum(
                frame.TargetPowerSpectrum,
                EffectiveWindowType,
                SequenceLength);
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
            DisposeRecorders();
            signal?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
