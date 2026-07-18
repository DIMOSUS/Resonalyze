using System.Threading.Channels;
using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>How a calibration capture failed, or <see cref="None"/> on success.</summary>
public enum SplCalibrationFailure
{
    None,
    // The capture barely ran — a device or driver problem, not a tone problem.
    TooFewFrames,
    // The input railed at full scale: the level is meaningless. Lower the gain.
    Clipped,
    // The loudest tone was not near the calibrator frequency.
    OffFrequency,
    // Nothing stood clearly above the noise: no calibrator tone, or it is buried.
    NoClearPeak,
    // A tone was found but its level drifted during the capture (coupler not
    // seated, or being fitted/removed while listening).
    Unstable,
    // The processing pipeline could not keep up and capture frames were dropped, so
    // the peak / clipping / stability checks may have missed the very frames that
    // would have failed the calibration. The result cannot be trusted.
    CaptureOverrun
}

/// <summary>
/// The outcome of one calibration listen. Carries the raw facts; the pass/fail
/// policy is <see cref="SplCalibrationListener.Evaluate"/>.
/// </summary>
/// <param name="Reading">The tone found in the averaged spectrum.</param>
/// <param name="InputPeakDbFs">The loudest microphone sample of the whole capture, in dBFS.</param>
/// <param name="Clipped">Whether any sample reached full scale.</param>
/// <param name="LevelStabilityDb">
/// The standard deviation of the per-frame tone level, in dB; a steady calibrator
/// reads a small fraction of a dB. <see cref="double.NaN"/> if too few frames.
/// </param>
/// <param name="FramesAnalyzed">How many analysis frames were averaged.</param>
/// <param name="Overran">
/// Whether any capture frame was dropped before analysis. When true the peak,
/// clipping and stability figures are computed over an incomplete capture and
/// must not be trusted.
/// </param>
public readonly record struct SplCalibrationCaptureResult(
    SplToneReading Reading,
    double InputPeakDbFs,
    bool Clipped,
    double LevelStabilityDb,
    int FramesAnalyzed,
    bool Overran);

/// <summary>A live progress update raised while a calibration listen runs.</summary>
public readonly record struct SplCalibrationProgress(
    SplToneReading Reading,
    double InputPeakDbFs,
    bool Clipped,
    int FramesAnalyzed,
    double ElapsedSeconds);

/// <summary>
/// Listens to the microphone alone — no reference, no meaningful playback — to
/// measure the level of an acoustic calibrator's tone. It opens a streaming
/// session (the only capturing session the audio layer offers) with a silent
/// looping signal and reads just the microphone channel, accumulating a
/// flat-top power spectrum whose peak bin gives the tone's level in dBFS to a
/// fraction of a dB regardless of where the tone falls between bins.
/// </summary>
public sealed class SplCalibrationListener
{
    // A sample this close to full scale is treated as clipped: the true level is
    // then unknowable and the calibration must be rejected. ~-0.009 dBFS.
    private const float ClipThreshold = 0.999f;

    // Below this the capture did not really run (device/driver failure); there is
    // nothing to judge.
    private const int MinimumFrames = 4;

    // The first frames can catch the device still priming (a silent or partial
    // block); they are scanned for clipping but kept out of the averaged spectrum
    // and the stability estimate so a cold start does not skew the level.
    private const int WarmupFrames = 2;

    // A seated calibrator holds its level to hundredths of a dB; this tolerates
    // normal capture jitter while rejecting a coupler that is drifting or being
    // fitted mid-listen.
    private const double MaximumLevelStabilityDb = 1.5;

    private readonly IAudioSessionFactory audioSessionFactory;

    public SplCalibrationListener(IAudioSessionFactory audioSessionFactory)
    {
        this.audioSessionFactory = audioSessionFactory ??
            throw new ArgumentNullException(nameof(audioSessionFactory));
    }

    /// <summary>
    /// Captures for up to <paramref name="duration"/> and returns the averaged
    /// tone reading. Cancelling <paramref name="cancellationToken"/> aborts (throws
    /// <see cref="OperationCanceledException"/>); reaching the duration completes
    /// normally with whatever was captured.
    /// </summary>
    public async Task<SplCalibrationCaptureResult> CaptureAsync(
        AudioSessionRequest request,
        int frameLength,
        SplToneCriteria criteria,
        TimeSpan duration,
        IProgress<SplCalibrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (frameLength < 2 || (frameLength & (frameLength - 1)) != 0)
        {
            throw new ArgumentException("Frame length must be a power of two.", nameof(frameLength));
        }

        double binWidthHz = (double)request.SampleRate / frameLength;
        int targetBin = (int)Math.Round(criteria.TargetFrequencyHz / binWidthHz);
        int toleranceBins = Math.Max(1, (int)Math.Ceiling(criteria.FrequencyToleranceHz / binWidthHz));

        // Accumulated on the single processing task, so no locking is needed; the
        // capture thread only enqueues raw frames.
        double[]? accumulatedPower = null;
        int frameCount = 0;
        int rawFrameIndex = 0;
        double inputPeak = 0.0;
        bool clipped = false;
        var perFrameLevels = new List<double>();
        var frameBuffer = new float[frameLength];
        int fill = 0;
        long startTick = Environment.TickCount64;
        // Incremented on the capture thread when a raw block is dropped because the
        // processing task fell behind; any drop invalidates the whole capture.
        int droppedFrames = 0;
        // Set when the backend reports a capture packet discontinuity — loss BEFORE
        // the bounded channel, which the drop counter above cannot see.
        int captureDiscontinuities = 0;

        var frames = Channel.CreateBounded<float[]>(
            new BoundedChannelOptions(8)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest
            },
            _ => Interlocked.Increment(ref droppedFrames));

        void ProcessFrame()
        {
            for (int i = 0; i < frameBuffer.Length; i++)
            {
                float magnitude = Math.Abs(frameBuffer[i]);
                if (magnitude > inputPeak)
                {
                    inputPeak = magnitude;
                }
                if (magnitude >= ClipThreshold)
                {
                    clipped = true;
                }
            }

            // Warm-up frames still count for clipping (the gain is wrong from the
            // first sample) but not for the level, which the device may not have
            // settled to yet.
            if (rawFrameIndex++ < WarmupFrames)
            {
                return;
            }

            double[] power = SpectrumAnalysis.ComputePowerSpectrum(frameBuffer, WindowType.FlatTop);
            accumulatedPower ??= new double[power.Length];
            for (int i = 0; i < accumulatedPower.Length; i++)
            {
                accumulatedPower[i] += power[i];
            }

            frameCount++;
            perFrameLevels.Add(PeakLevelNearTarget(power, targetBin, toleranceBins));

            if (progress != null)
            {
                var averaged = new double[accumulatedPower.Length];
                for (int i = 0; i < averaged.Length; i++)
                {
                    averaged[i] = accumulatedPower[i] / frameCount;
                }

                progress.Report(new SplCalibrationProgress(
                    SplToneAnalysis.Analyze(averaged, binWidthHz, criteria),
                    InputLevelToDbFs(inputPeak),
                    clipped,
                    frameCount,
                    (Environment.TickCount64 - startTick) / 1000.0));
            }
        }

        Task processing = Task.Run(async () =>
        {
            await foreach (float[] block in frames.Reader.ReadAllAsync())
            {
                int position = 0;
                while (position < block.Length)
                {
                    int take = Math.Min(frameLength - fill, block.Length - position);
                    Array.Copy(block, position, frameBuffer, fill, take);
                    fill += take;
                    position += take;
                    if (fill == frameLength)
                    {
                        ProcessFrame();
                        fill = 0;
                    }
                }
            }
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(duration);

        var silence = new AudioPlaybackSignal(
            new float[request.SampleRate],
            request.SampleRate,
            request.BitsPerSample,
            request.PlaybackChannel,
            Loop: true);

        IAudioStreamingSession? session = null;
        void HandleFrame(AudioCaptureFrame frame)
        {
            int micIndex = frame.MicrophoneChannel;
            if ((uint)micIndex < (uint)frame.Channels.Length)
            {
                frames.Writer.TryWrite(frame.Channels[micIndex]);
            }
        }

        void HandleDiscontinuity() => Interlocked.Increment(ref captureDiscontinuities);

        try
        {
            session = await audioSessionFactory
                .OpenStreamingAsync(request, timeoutCts.Token).ConfigureAwait(false);
            session.FrameAvailable += HandleFrame;
            session.CaptureDiscontinuity += HandleDiscontinuity;
            await session.RunAsync(silence, frameLength, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Reaching the requested duration cancels the timeout token — the
            // normal way this capture ends. A caller cancellation is re-thrown below.
        }
        finally
        {
            if (session != null)
            {
                session.FrameAvailable -= HandleFrame;
                session.CaptureDiscontinuity -= HandleDiscontinuity;
                await session.DisposeAsync().ConfigureAwait(false);
            }

            frames.Writer.TryComplete();
            await processing.ConfigureAwait(false);
        }

        // A caller cancellation (not the duration timeout) is an abort, not a result.
        cancellationToken.ThrowIfCancellationRequested();

        double[] finalSpectrum = accumulatedPower ?? new double[frameLength / 2];
        if (frameCount > 0)
        {
            for (int i = 0; i < finalSpectrum.Length; i++)
            {
                finalSpectrum[i] /= frameCount;
            }
        }

        return new SplCalibrationCaptureResult(
            SplToneAnalysis.Analyze(finalSpectrum, binWidthHz, criteria),
            InputLevelToDbFs(inputPeak),
            clipped,
            StandardDeviationDb(perFrameLevels),
            frameCount,
            Volatile.Read(ref droppedFrames) > 0 ||
                Volatile.Read(ref captureDiscontinuities) > 0);
    }

    /// <summary>
    /// The pass/fail policy over a capture result. Checks are ordered by which
    /// message is most useful to the user first.
    /// </summary>
    public static SplCalibrationFailure Evaluate(SplCalibrationCaptureResult result)
    {
        // A dropped frame means the clip / peak / stability checks ran over an
        // incomplete capture — the missing frames could be the failing ones — so
        // nothing else about the result can be trusted.
        if (result.Overran)
        {
            return SplCalibrationFailure.CaptureOverrun;
        }
        if (result.FramesAnalyzed < MinimumFrames)
        {
            return SplCalibrationFailure.TooFewFrames;
        }
        if (result.Clipped)
        {
            return SplCalibrationFailure.Clipped;
        }
        if (!result.Reading.WithinFrequencyTolerance)
        {
            return SplCalibrationFailure.OffFrequency;
        }
        if (!result.Reading.HasClearPeak)
        {
            return SplCalibrationFailure.NoClearPeak;
        }
        if (!double.IsFinite(result.LevelStabilityDb) ||
            result.LevelStabilityDb > MaximumLevelStabilityDb)
        {
            return SplCalibrationFailure.Unstable;
        }

        return SplCalibrationFailure.None;
    }

    private static double PeakLevelNearTarget(double[] power, int targetBin, int toleranceBins)
    {
        int low = Math.Max(1, targetBin - toleranceBins);
        int high = Math.Min(power.Length - 1, targetBin + toleranceBins);
        double peak = 0.0;
        for (int bin = low; bin <= high; bin++)
        {
            if (power[bin] > peak)
            {
                peak = power[bin];
            }
        }

        return 10.0 * Math.Log10(Math.Max(peak, 1e-40));
    }

    private static double StandardDeviationDb(IReadOnlyList<double> levels)
    {
        if (levels.Count < 2)
        {
            return double.NaN;
        }

        double mean = 0.0;
        foreach (double level in levels)
        {
            mean += level;
        }
        mean /= levels.Count;

        double variance = 0.0;
        foreach (double level in levels)
        {
            double delta = level - mean;
            variance += delta * delta;
        }

        return Math.Sqrt(variance / levels.Count);
    }

    private static double InputLevelToDbFs(double amplitude) =>
        20.0 * Math.Log10(Math.Max(amplitude, 1e-10));
}
