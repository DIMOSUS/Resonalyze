using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

internal static class SyntheticCapture
{
    // Mic peak ~0.5 and a loopback that differs from it: passes quality and stereo-separation checks.
    public static AudioCaptureResult Good(
        AudioPlaybackSignal signal,
        int tailSamples,
        AudioCaptureAnomalies anomalies = AudioCaptureAnomalies.None)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true, anomalies, Diagnostics: null);
    }

    public static AudioCaptureResult SilentMicrophone(AudioPlaybackSignal signal, int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.0f, 0.25f);
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    // A cleanly attenuated loopback (~-41 dBFS) is still an exact copy: the measurement must succeed.
    public static AudioCaptureResult QuietCleanLoopback(AudioPlaybackSignal signal, int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.0089f);
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    // Loopback taken from the card output; only the microphone path passes the external DSP's high-pass.
    public static AudioCaptureResult ProtectedLoudspeaker(
        AudioPlaybackSignal signal,
        int tailSamples,
        CrossoverEdge edge,
        double sampleRateHz)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        IReadOnlyList<BiquadCoefficients> sections =
            CrossoverFilter.BuildSections(edge, highPass: true, sampleRateHz);
        foreach (BiquadCoefficients section in sections)
        {
            ApplySection(mic, section);
        }

        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    // Loopback bleed, not the wire: every per-run check passes, the shape gate must refuse it.
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

    // Overdriven loopback input: asymmetrically saturated copy peaking well below full scale (field: -14.6 dBFS).
    public static AudioCaptureResult DistortingLoopback(
        AudioPlaybackSignal signal,
        int tailSamples)
    {
        int length = signal.SampleCount + Math.Max(0, tailSamples);
        var mic = new float[length];
        var loop = new float[length];
        uint state = 24_680u;
        double NextNoise()
        {
            state = state * 1_664_525u + 1_013_904_223u;
            return state / 4_294_967_296.0 - 0.5;
        }

        for (int i = 0; i < signal.SampleCount; i++)
        {
            // Knee depth measured: ~-8 dB harmonics, ~10 dB compactness (field -12 dB / 15.4 dB, gate 22 dB).
            double sample = signal.MonoSamples[i] * 0.25;
            loop[i] = (float)(sample > 0 ? 0.01 * Math.Tanh(sample / 0.01) : sample);

            for (int echo = 0; echo <= 3; echo++)
            {
                int at = i + 40 + echo * 137;
                if (at < length)
                {
                    mic[at] += signal.MonoSamples[i] * 0.5f * (float)Math.Pow(0.45, echo);
                }
            }
        }
        for (int i = 0; i < length; i++)
        {
            mic[i] += (float)(NextNoise() * 0.002);
            loop[i] += (float)(NextNoise() * 0.0002);
        }

        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    // NaN slips every level comparison; only the fail-closed shape gate stops it.
    public static AudioCaptureResult NaNMicrophone(AudioPlaybackSignal signal, int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        mic[100] = float.NaN;
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    // Aggregate loopback peak comes from this run while the distortion is the other's: the refusal must quote the faulty run.
    public static AudioCaptureResult NoiseMicrophoneLoudLoopback(
        AudioPlaybackSignal signal,
        int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.0f, 0.9f);
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

    public static AudioCaptureResult DistortingBothInputs(
        AudioPlaybackSignal signal,
        int tailSamples)
    {
        int length = signal.SampleCount + Math.Max(0, tailSamples);
        var mic = new float[length];
        var loop = new float[length];
        uint state = 24_680u;
        double NextNoise()
        {
            state = state * 1_664_525u + 1_013_904_223u;
            return state / 4_294_967_296.0 - 0.5;
        }

        for (int i = 0; i < signal.SampleCount; i++)
        {
            double sample = signal.MonoSamples[i] * 0.25;
            loop[i] = (float)(sample > 0 ? 0.01 * Math.Tanh(sample / 0.01) : sample);
            double micSample = signal.MonoSamples[i] * 0.5;
            if (i + 40 < length)
            {
                // Opposite half from the loopback's knee: matching nonlinearities would cancel in the mic/loop ratio.
                mic[i + 40] = (float)(micSample < 0
                    ? -0.02 * Math.Tanh(-micSample / 0.02)
                    : micSample);
            }
        }
        for (int i = 0; i < length; i++)
        {
            mic[i] += (float)(NextNoise() * 0.002);
            loop[i] += (float)(NextNoise() * 0.0002);
        }

        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

    // NaN smears the whole deconvolution: the distortion reading is missing, not clean.
    public static AudioCaptureResult NaNLoopback(AudioPlaybackSignal signal, int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        loop[100] = float.NaN;
        return new AudioCaptureResult(
            [mic, loop], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null);
    }

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

    /// <summary>One array microphone carries noise at an ordinary level: passes every level check.</summary>
    public static AudioCaptureResult WithNoisyArrayMicrophone(
        AudioPlaybackSignal signal,
        int tailSamples,
        double peak = 0.05)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        return new AudioCaptureResult(
            [mic, loop, Noise(mic.Length, peak)],
            MicrophoneChannel: 0,
            LoopbackChannel: 1,
            StereoSeparationExpected: false,
            AudioCaptureAnomalies.None,
            Diagnostics: null)
        {
            ArrayChannels = [2]
        };
    }

    /// <summary>Intermittent noise: averaged, three good runs keep the shape compact, so it must be caught per run.</summary>
    public static AudioCaptureResult WithArrayMicrophoneNoisyOnThisCapture(
        AudioPlaybackSignal signal,
        int tailSamples,
        bool noisy,
        double peak = 0.05)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        (float[] array, _) = BuildChannels(signal, tailSamples, 0.4f, 0.25f);
        return new AudioCaptureResult(
            [mic, loop, noisy ? Noise(mic.Length, peak) : array],
            MicrophoneChannel: 0,
            LoopbackChannel: 1,
            StereoSeparationExpected: false,
            AudioCaptureAnomalies.None,
            Diagnostics: null)
        {
            ArrayChannels = [2]
        };
    }

    public static AudioCaptureResult WithMeasurementMicrophoneNoisyOnThisCapture(
        AudioPlaybackSignal signal,
        int tailSamples,
        bool noisy,
        double peak = 0.005)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        return new AudioCaptureResult(
            [noisy ? Noise(mic.Length, peak) : mic, loop],
            MicrophoneChannel: 0,
            LoopbackChannel: 1,
            StereoSeparationExpected: false,
            AudioCaptureAnomalies.None,
            Diagnostics: null);
    }

    /// <summary>The measurement microphone carries noise: the array's cruder verdict must not blame a working input first.</summary>
    public static AudioCaptureResult WithNoisyMeasurementMicrophone(
        AudioPlaybackSignal signal,
        int tailSamples,
        double peak = 0.05)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        return new AudioCaptureResult(
            [Noise(mic.Length, peak), loop, mic],
            MicrophoneChannel: 0,
            LoopbackChannel: 1,
            StereoSeparationExpected: false,
            AudioCaptureAnomalies.None,
            Diagnostics: null)
        {
            ArrayChannels = [2]
        };
    }

    private static float[] Noise(int length, double peak)
    {
        var noise = new float[length];
        uint state = 0x9E3779B9;
        for (int i = 0; i < length; i++)
        {
            state = state * 1664525u + 1013904223u;
            noise[i] = (float)(((state >> 8) / (double)0x00FFFFFF - 0.5) * 2.0 * peak);
        }

        return noise;
    }

    /// <summary>Array scales are relative to the played sweep, so tests can state the expected transfer level.</summary>
    public static AudioCaptureResult WithArray(
        AudioPlaybackSignal signal,
        int tailSamples,
        params float[] arrayScales) =>
        WithArray(signal, tailSamples, edge: null, sampleRateHz: 0, arrayScales);

    public static AudioCaptureResult WithArray(
        AudioPlaybackSignal signal,
        int tailSamples,
        CrossoverEdge? edge,
        double sampleRateHz,
        params float[] arrayScales)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        var channels = new List<float[]> { mic, loop };
        foreach (float scale in arrayScales)
        {
            (float[] arrayMic, _) = BuildChannels(signal, tailSamples, scale, 0f);
            channels.Add(arrayMic);
        }

        if (edge is { } highPass)
        {
            IReadOnlyList<BiquadCoefficients> sections =
                CrossoverFilter.BuildSections(highPass, highPass: true, sampleRateHz);
            foreach (BiquadCoefficients section in sections)
            {
                ApplySection(mic, section);
                for (int i = 2; i < channels.Count; i++)
                {
                    ApplySection(channels[i], section);
                }
            }
        }

        return new AudioCaptureResult(
            [.. channels], 0, 1, StereoSeparationExpected: true,
            AudioCaptureAnomalies.None, Diagnostics: null)
        {
            ArrayChannels = [.. Enumerable.Range(2, arrayScales.Length)]
        };
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

    private static void ApplySection(float[] samples, BiquadCoefficients section)
    {
        double x1 = 0;
        double x2 = 0;
        double y1 = 0;
        double y2 = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double x = samples[i];
            double y = section.B0 * x + section.B1 * x1 + section.B2 * x2 +
                section.A1 * y1 + section.A2 * y2;
            samples[i] = (float)y;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
        }
    }
}

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

internal sealed class RecordingStreamingSession : IAudioStreamingSession
{
    private readonly int framesToRaise;
    private readonly bool failAfterFrames;
    private readonly float microphonePeak;
    private readonly IReadOnlyList<float>? microphonePeaks;
    private readonly IReadOnlyList<float>? loopbackPeaks;

    /// <param name="microphonePeak">The microphone tone's peak; the default 1.0 reaches full scale in every frame.</param>
    /// <param name="microphonePeaks">Per-frame peaks, cycled, in place of <paramref name="microphonePeak"/>; <paramref name="loopbackPeaks"/> likewise for the loopback's 0.5.</param>
    public RecordingStreamingSession(
        int framesToRaise,
        bool failAfterFrames,
        float microphonePeak = 1.0f,
        IReadOnlyList<float>? microphonePeaks = null,
        IReadOnlyList<float>? loopbackPeaks = null)
    {
        this.framesToRaise = framesToRaise;
        this.failAfterFrames = failAfterFrames;
        this.microphonePeak = microphonePeak;
        this.microphonePeaks = microphonePeaks;
        this.loopbackPeaks = loopbackPeaks;
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
            float peak = microphonePeaks is { Count: > 0 } peaks ? peaks[f % peaks.Count] : microphonePeak;
            float loopPeak = loopbackPeaks is { Count: > 0 } loops ? loops[f % loops.Count] : 0.5f;
            for (int i = 0; i < sequenceLength; i++)
            {
                double phase = 2.0 * Math.PI * 8.0 * i / sequenceLength;
                mic[i] = peak * (float)Math.Sin(phase);
                loop[i] = loopPeak * (float)Math.Sin(phase);
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
