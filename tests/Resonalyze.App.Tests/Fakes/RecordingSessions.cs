using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

/// <summary>Builds synthetic <see cref="AudioCaptureResult"/> values from a played signal.</summary>
internal static class SyntheticCapture
{
    // A microphone channel scaled off the sweep (peak ~0.5, so it is neither
    // silent nor clipped) and a loopback that differs from it, so the run
    // passes both the quality check and the stereo-separation validator.
    public static AudioCaptureResult Good(
        AudioPlaybackSignal signal,
        int tailSamples,
        AudioCaptureAnomalies anomalies = AudioCaptureAnomalies.None)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true, anomalies, Diagnostics: null);
    }

    // A silent microphone channel: the quality check rejects the run.
    public static AudioCaptureResult SilentMicrophone(AudioPlaybackSignal signal, int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.0f, 0.25f);
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    // A CLEANLY attenuated loopback wire at ~-41 dBFS (the readme's
    // "playback level well down" workflow taken far): still an exact copy of
    // the sweep, so the scale-invariant transfer estimate is perfectly
    // usable and the measurement must succeed.
    public static AudioCaptureResult QuietCleanLoopback(AudioPlaybackSignal signal, int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.0089f);
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    // The field failure: the "loopback" input picked up bleed, not the wire
    // — uncorrelated content at ~-41 dBFS (deterministic LCG noise). Every
    // per-run check passes, but the transfer function divides the microphone
    // by garbage and the shape gate must refuse it, naming the level.
    public static AudioCaptureResult BleedLoopback(AudioPlaybackSignal signal, int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.0f);
        uint state = 555_555_555u;
        for (int i = 0; i < loop.Length; i++)
        {
            state = state * 1_664_525u + 1_013_904_223u;
            loop[i] = (float)((state / 4_294_967_296.0 - 0.5) * 2 * 0.0089);
        }
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    // A capture poisoned by one non-finite sample: NaN slips every level
    // comparison, the transfer IR comes out NaN, and only the fail-closed
    // shape gate stands between it and a published measurement.
    public static AudioCaptureResult NaNMicrophone(AudioPlaybackSignal signal, int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        mic[100] = float.NaN;
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    // A level-plausible capture whose microphone recorded only noise
    // uncorrelated with the sweep (deterministic LCG): every per-run level
    // check passes, but the transfer function divides into stationary noise
    // — the shape gate's case.
    public static AudioCaptureResult NoiseMicrophone(AudioPlaybackSignal signal, int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.0f, 0.25f);
        uint state = 987_654_321u;
        for (int i = 0; i < mic.Length; i++)
        {
            state = state * 1_664_525u + 1_013_904_223u;
            mic[i] = (float)(state / 4_294_967_296.0 - 0.5) * 0.5f;
        }
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    private static (float[] Microphone, float[] Loopback) BuildChannels(
        AudioPlaybackSignal signal, int tailSamples, float micScale, float loopScale)
    {
        int length = signal.SampleCount + Math.Max(0, tailSamples);
        var mic = new float[length];
        var loop = new float[length];
        for (int i = 0; i < signal.SampleCount; i++)
        {
            mic[i] = signal.MonoSamples[i] * micScale;
            loop[i] = signal.MonoSamples[i] * loopScale;
        }
        return (mic, loop);
    }
}

/// <summary>
/// A duplex session bound to one signal (like the real sessions) whose per-run
/// behaviour is supplied by a callback.
/// </summary>
internal sealed class RecordingDuplexSession : IAudioDuplexSession
{
    private readonly AudioPlaybackSignal signal;
    private readonly Func<int, AudioPlaybackSignal, int, CancellationToken, Task<AudioCaptureResult>> onCapture;

    public RecordingDuplexSession(
        AudioPlaybackSignal signal,
        Func<int, AudioPlaybackSignal, int, CancellationToken, Task<AudioCaptureResult>> onCapture)
    {
        this.signal = signal;
        this.onCapture = onCapture;
    }

    public event Action<AudioInputLevels>? InputLevelsAvailable;

    public int CaptureCount { get; private set; }
    public bool Disposed { get; private set; }

    public async Task<AudioCaptureResult> PlayAndCaptureAsync(
        int captureTailSamples, CancellationToken cancellationToken)
    {
        _ = InputLevelsAvailable;
        int run = ++CaptureCount;
        return await onCapture(run, signal, captureTailSamples, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A streaming session that raises a fixed number of frames then behaves as configured.</summary>
internal sealed class RecordingStreamingSession : IAudioStreamingSession
{
    private readonly int framesToRaise;
    private readonly bool failAfterFrames;

    public RecordingStreamingSession(int framesToRaise, bool failAfterFrames)
    {
        this.framesToRaise = framesToRaise;
        this.failAfterFrames = failAfterFrames;
    }

    public event Action<AudioCaptureFrame>? FrameAvailable;
    public event Action<AudioInputLevels>? InputLevelsAvailable;
    public event Action? CaptureDiscontinuity;

    public bool Disposed { get; private set; }
    public AudioPlaybackSignal? LastPlaybackSignal { get; private set; }

    public async Task RunAsync(
        AudioPlaybackSignal loopingSignal, int sequenceLength, CancellationToken cancellationToken)
    {
        LastPlaybackSignal = loopingSignal;
        _ = CaptureDiscontinuity;
        for (int f = 0; f < framesToRaise; f++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mic = new float[sequenceLength];
            var loop = new float[sequenceLength];
            for (int i = 0; i < sequenceLength; i++)
            {
                double phase = 2.0 * Math.PI * 8.0 * i / sequenceLength;
                mic[i] = (float)Math.Sin(phase);
                loop[i] = (float)(0.5 * Math.Sin(phase));
            }
            FrameAvailable?.Invoke(new AudioCaptureFrame([mic, loop], 0, 1));
            InputLevelsAvailable?.Invoke(new AudioInputLevels(
                new AudioChannelLevel(-6, -9, false),
                new AudioChannelLevel(-12, -15, false)));
            await Task.Delay(5, cancellationToken);
        }

        if (failAfterFrames)
        {
            throw new InvalidOperationException("The fake capture device stopped unexpectedly.");
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
