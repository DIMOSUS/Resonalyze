using Resonalyze.Audio;
using Resonalyze.Dsp;

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

    // The physical arrangement behind protective-HPF compensation: loopback is
    // captured directly from the sound-card output, while only the microphone
    // path passes through the external DSP's high-pass.
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

    // The field failure this whole diagnosis exists for: the loopback wire is
    // connected and carries the sweep, but the input stage it feeds is being
    // driven past its limit, so what comes back is an asymmetrically saturated
    // copy. Every per-run check passes — it is neither silent nor clipped, and
    // it peaks well below full scale, exactly as the real one did at
    // -14.6 dBFS — while the transfer function divides a clean microphone by a
    // nonlinear reference.
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
            // Asymmetric saturation: the positive half compresses into a knee
            // and the negative one passes, which is what a single-ended input
            // driven past its limit does — and why the field record carried
            // even harmonics (H2 and H4) with almost no odd ones. The knee
            // depth was measured, not guessed: it puts this capture at ~-8 dB
            // of harmonic content and ~10 dB of transfer compactness, the same
            // class as the field one (-12 dB and 15.4 dB) with margin under the
            // gate's 22 dB.
            double sample = signal.MonoSamples[i] * 0.25;
            loop[i] = (float)(sample > 0 ? 0.01 * Math.Tanh(sample / 0.01) : sample);

            // The microphone is a real arrival: delayed, with a short decay and
            // a noise floor, so the refusal is not an artifact of feeding the
            // estimator a mathematically exact copy of the excitation.
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

    // NoiseMicrophone with a LOUD clean loopback. Pairs with a distorting run
    // whose loopback is quiet: the aggregate loopback peak (a maximum over
    // runs) then comes from THIS run, while the distortion came from the
    // other — the refusal must quote the levels of the run that carried the
    // fault, not the fleet-wide maximum.
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

    // Both inputs driven past their limit: the loopback exactly as in
    // DistortingLoopback, and the microphone a delayed copy saturated the same
    // asymmetric way. The refusal leads with the reference (everything is
    // divided by it) but must say the microphone crossed the threshold too.
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
                // The OPPOSITE half from the loopback's knee: two matching
                // nonlinearities partially cancel in the mic/loop ratio and
                // the shape gate can pass the average; opposing halves cannot.
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

    // The loopback twin of NaNMicrophone. The run is accepted (NaN compares
    // false against every level threshold) and the averaged transfer fails
    // closed — and this run's loopback distortion reading is a MISSING
    // measurement (the NaN smears over the whole deconvolution), not a clean
    // one, which is what the judged-run count in the refusal must reflect.
    public static AudioCaptureResult NaNLoopback(AudioPlaybackSignal signal, int tailSamples)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        loop[100] = float.NaN;
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

    /// <summary>
    /// A measurement pair plus ONE array microphone carrying noise instead of the
    /// sweep: an unused preamp hissing, the wrong socket, a failed capsule.
    /// </summary>
    /// <remarks>
    /// The point is that it passes every level check there is — it is neither silent
    /// nor clipped nor short, and it sits at an entirely ordinary level. What it does
    /// not do is divide into a response.
    /// </remarks>
    public static AudioCaptureResult WithNoisyArrayMicrophone(
        AudioPlaybackSignal signal,
        int tailSamples,
        double peak = 0.05)
    {
        (float[] mic, float[] loop) = BuildChannels(signal, tailSamples, 0.5f, 0.25f);
        var noise = new float[mic.Length];
        // Deterministic, so the verdict is the same on every run and on every machine.
        uint state = 0x9E3779B9;
        for (int i = 0; i < noise.Length; i++)
        {
            state = state * 1664525u + 1013904223u;
            noise[i] = (float)(((state >> 8) / (double)0x00FFFFFF - 0.5) * 2.0 * peak);
        }

        return new AudioCaptureResult(
            [mic, loop, noise],
            MicrophoneChannel: 0,
            LoopbackChannel: 1,
            StereoSeparationExpected: false,
            AudioCaptureAnomalies.None,
            Diagnostics: null)
        {
            ArrayChannels = [2]
        };
    }

    /// <summary>
    /// A measurement pair plus array microphones on channels 2, 3, ... Each
    /// array scale is that microphone's level relative to the played sweep, so a
    /// test can state what its transfer level must come out as.
    /// </summary>
    public static AudioCaptureResult WithArray(
        AudioPlaybackSignal signal,
        int tailSamples,
        params float[] arrayScales) =>
        WithArray(signal, tailSamples, edge: null, sampleRateHz: 0, arrayScales);

    /// <summary>
    /// The same, with the protective high-pass in the acoustic path: every
    /// microphone passes through it and the loopback — taken from the card output,
    /// ahead of the external DSP — does not.
    /// </summary>
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
