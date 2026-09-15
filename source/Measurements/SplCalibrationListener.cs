using System.Threading.Channels;
using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze;

public enum SplCalibrationFailure
{
    None,
    TooFewFrames,
    Clipped,
    OffFrequency,
    NoClearPeak,
    // Coupler not seated, or fitted/removed while listening.
    Unstable,
    // Dropped frames may be exactly the ones that would have failed the checks.
    CaptureOverrun
}

/// <summary>Raw facts of one listen; pass/fail policy is <see cref="SplCalibrationListener.Evaluate"/>.</summary>
/// <param name="LevelStabilityDb">Std. deviation of per-frame tone level, dB; NaN if too few frames.</param>
public readonly record struct SplCalibrationCaptureResult(
    SplToneReading Reading,
    double InputPeakDbFs,
    bool Clipped,
    double LevelStabilityDb,
    int FramesAnalyzed,
    bool Overran);

public readonly record struct SplCalibrationProgress(
    SplToneReading Reading,
    double InputPeakDbFs,
    bool Clipped,
    int FramesAnalyzed,
    double ElapsedSeconds);

/// <summary>Measures a calibrator tone on the mic alone via a silent streaming session; flat-top spectrum peak gives dBFS regardless of bin position.</summary>
public sealed class SplCalibrationListener
{
    // ~-0.009 dBFS.
    private const float ClipThreshold = 0.999f;

    private const int MinimumFrames = 4;

    // Priming frames are scanned for clipping but kept out of the level and stability estimate.
    private const int WarmupFrames = 2;

    // A seated calibrator holds hundredths of a dB; this rejects a drifting coupler.
    private const double MaximumLevelStabilityDb = 1.5;

    private readonly IAudioSessionFactory audioSessionFactory;

    public SplCalibrationListener(IAudioSessionFactory audioSessionFactory)
    {
        this.audioSessionFactory = audioSessionFactory ??
            throw new ArgumentNullException(nameof(audioSessionFactory));
    }

    /// <summary>Cancellation throws; reaching <paramref name="duration"/> completes normally with what was captured.</summary>
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

        // Single processing task: no locking; the capture thread only enqueues.
        double[]? accumulatedPower = null;
        int frameCount = 0;
        int rawFrameIndex = 0;
        double inputPeak = 0.0;
        bool clipped = false;
        var perFrameLevels = new List<double>();
        var frameBuffer = new float[frameLength];
        int fill = 0;
        long startTick = Environment.TickCount64;
        int droppedFrames = 0;
        // Backend packet loss before the bounded channel, which the drop counter cannot see.
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

    /// <summary>Checks ordered by which message helps the user most.</summary>
    public static SplCalibrationFailure Evaluate(SplCalibrationCaptureResult result)
    {
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
