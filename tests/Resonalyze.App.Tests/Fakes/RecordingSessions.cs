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
