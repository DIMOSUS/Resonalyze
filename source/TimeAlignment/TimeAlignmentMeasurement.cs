using System.Numerics;
using NAudio.Wave;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Coordinates ASIO loopback time-alignment measurement and builds microphone/reference impulse responses.
/// </summary>
public sealed class TimeAlignmentMeasurement : IDisposable
{
    private readonly object stateSync = new();
    private CancellationTokenSource? cancellationTokenSource;
    private Task<bool>? measurementTask;
    private volatile bool inProgress;
    private bool disposed;

    public event Action<bool>? Completed;
    internal event Action<InputLevelMeterSnapshot>? LevelsAvailable;

    public ExponentialSineSweep? Sweep { get; private set; }
    public Complex[]? MicrophoneImpulseResponse { get; private set; }
    public Complex[]? LoopbackImpulseResponse { get; private set; }
    public bool InProgress => inProgress;
    public int SampleRate { get; private set; }
    public int Octaves { get; private set; }
    public int Bits { get; private set; }
    public AudioBackend AudioBackend { get; private set; } = AudioBackend.Wave;
    public int OutputDeviceNumber { get; private set; } = -1;
    public int InputDeviceNumber { get; private set; } = -1;
    public PlaybackChannel PlaybackChannel { get; private set; }
    public string? AsioDriverName { get; private set; }
    public int MicrophoneInputChannelOffset { get; private set; }
    public int LoopbackInputChannelOffset { get; private set; }
    public int AsioOutputChannelOffset { get; private set; }
    public TimeAlignmentOptions Options { get; private set; } = new();
    public double PeakSample { get; private set; }
    public double DelayMilliseconds { get; private set; }
    public double FirstArrivalPeakSample { get; private set; }
    public double FirstArrivalDelayMilliseconds { get; private set; }
    public double StrongestPeakSample { get; private set; }
    public double StrongestDelayMilliseconds { get; private set; }
    public double[]? EnvelopeSamples { get; private set; }
    public int EnvelopePeakIndex { get; private set; }
    public double EnvelopePeak { get; private set; }
    public int StrongestEnvelopePeakIndex { get; private set; }
    public double StrongestEnvelopePeak { get; private set; }
    public double ConfidenceDecibels { get; private set; }
    public double MicrophonePeakDbFs { get; private set; }
    public double MicrophoneRmsDbFs { get; private set; }
    public bool MicrophoneClipped { get; private set; }
    public double LoopbackPeakDbFs { get; private set; }
    public double LoopbackRmsDbFs { get; private set; }
    public bool LoopbackClipped { get; private set; }
    public Exception? LastError { get; private set; }

    public void Init(
        int octaves,
        int sampleRate,
        int bits,
        double requestedDuration,
        AudioBackend audioBackend,
        int outputDeviceNumber,
        int inputDeviceNumber,
        PlaybackChannel playbackChannel,
        string? asioDriverName,
        int microphoneInputChannelOffset,
        int loopbackInputChannelOffset,
        int asioOutputChannelOffset,
        TimeAlignmentOptions options)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        if (InProgress)
        {
            throw new InvalidOperationException("Cannot reinitialize an active time-alignment measurement.");
        }
        if (audioBackend == AudioBackend.Asio &&
            string.IsNullOrWhiteSpace(asioDriverName))
        {
            throw new InvalidOperationException("Time Alignment requires an ASIO driver.");
        }
        if (microphoneInputChannelOffset == loopbackInputChannelOffset)
        {
            throw new InvalidOperationException("Microphone and loopback inputs must use different channels.");
        }

        SampleRate = sampleRate;
        Bits = bits;
        Octaves = octaves;
        AudioBackend = audioBackend;
        OutputDeviceNumber = outputDeviceNumber;
        InputDeviceNumber = inputDeviceNumber;
        PlaybackChannel = playbackChannel;
        AsioDriverName = asioDriverName;
        MicrophoneInputChannelOffset = microphoneInputChannelOffset;
        LoopbackInputChannelOffset = loopbackInputChannelOffset;
        AsioOutputChannelOffset = asioOutputChannelOffset;
        Options = options;
        MicrophoneImpulseResponse = null;
        LoopbackImpulseResponse = null;
        ResetMeasurementResult();
        LastError = null;

        Sweep?.Dispose();
        Sweep = new ExponentialSineSweep();
        Sweep.FillData(octaves, requestedDuration, bits, sampleRate);
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
            if (Sweep == null)
            {
                throw new InvalidOperationException("Time-alignment measurement is not initialized.");
            }

            cancellationTokenSource?.Dispose();
            cancellationTokenSource = new CancellationTokenSource();
            inProgress = true;
            MicrophoneImpulseResponse = null;
            LoopbackImpulseResponse = null;
            ResetMeasurementResult();
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

    private async Task<bool> RunCoreAsync(CancellationToken cancellationToken)
    {
        bool success = false;
        try
        {
            if (AudioBackend == AudioBackend.Asio)
            {
                await RunAsioAsync(Sweep!, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunWaveAsync(Sweep!, cancellationToken).ConfigureAwait(false);
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

    private async Task RunAsioAsync(
        ExponentialSineSweep sweep,
        CancellationToken cancellationToken)
    {
        int firstInputOffset = Math.Min(
            MicrophoneInputChannelOffset,
            LoopbackInputChannelOffset);
        int lastInputOffset = Math.Max(
            MicrophoneInputChannelOffset,
            LoopbackInputChannelOffset);
        int inputChannelCount = lastInputOffset - firstInputOffset + 1;

        using var session = new AsioFullDuplexSession(
            AsioDriverName ?? string.Empty,
            firstInputOffset,
            AsioOutputChannelOffset,
            inputChannelCount);
        session.LevelsAvailable += channels =>
            RaiseLevels(
                channels,
                MicrophoneInputChannelOffset - firstInputOffset,
                LoopbackInputChannelOffset - firstInputOffset);
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

        float[][] samples = session.GetSamplesSnapshot();
        int microphoneIndex = MicrophoneInputChannelOffset - firstInputOffset;
        int loopbackIndex = LoopbackInputChannelOffset - firstInputOffset;
        ProcessRecordedChannels(samples, microphoneIndex, loopbackIndex);
    }

    private async Task RunWaveAsync(
        ExponentialSineSweep sweep,
        CancellationToken cancellationToken)
    {
        using var recorder = new SoundRecorder();
        recorder.Init(
            SampleRate,
            Bits,
            2,
            InputDeviceNumber);
        recorder.LevelsAvailable += channels =>
            RaiseLevels(
                channels,
                MicrophoneInputChannelOffset,
                LoopbackInputChannelOffset);
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

        int requiredSamples = recordingStart + sweep.SweepSamples + SampleRate;
        await recorder.WaitForSamplesAsync(requiredSamples, cancellationToken).ConfigureAwait(false);
        await recorder.StopRecordingAsync().ConfigureAwait(false);

        ProcessRecordedChannels(
            recorder.GetSamplesSnapshot(),
            MicrophoneInputChannelOffset,
            LoopbackInputChannelOffset);
    }

    private void ProcessRecordedChannels(
        float[][] samples,
        int microphoneIndex,
        int loopbackIndex)
    {
        if ((uint)microphoneIndex >= (uint)samples.Length ||
            (uint)loopbackIndex >= (uint)samples.Length)
        {
            throw new InvalidOperationException("Recorded channel snapshot is incomplete.");
        }
        RecordedChannelValidator.EnsureDifferentSignals(
            samples,
            microphoneIndex,
            loopbackIndex,
            $"{AudioBackend} time alignment");

        int fftLength = DspMath.NextPowerOfTwo(
            checked(samples[microphoneIndex].Length * 2));
        double[]? filter = Options.UseBandpassWindow
            ? BandpassWindow.Create(
                fftLength,
                SampleRate,
                Options.BandpassCenterHz,
                Options.BandpassPassOctaves,
                Options.BandpassFadeOctaves)
            : null;

        double[] loopback = Array.ConvertAll(samples[loopbackIndex], x => (double)x);
        double[] microphone = Array.ConvertAll(samples[microphoneIndex], x => (double)x);
        CaptureSignalLevels(microphone, loopback);

        double[] relativeImpulseResponse = TransferFunction.ComputeRelativeIr(
            loopback,
            microphone,
            1e-12,
            filter);

        double[] envelope = SignalEnvelope.Envelope(relativeImpulseResponse);
        PeakSearchResult peakSearchResult = SignalEnvelope.FindPeak(
            envelope,
            SampleRate,
            CreatePeakSearchOptions(Options));
        int peakIndex = peakSearchResult.SelectedIndex;
        double peak = envelope[peakIndex];

        // The envelope peak locates each arrival robustly, but its sub-sample
        // position is only a broad-lobe parabola. Refine it with a GCC-PHAT
        // cross-correlation of the raw channels, confined to a short window around
        // the envelope peak: whitening collapses the delay to a sharp sinc, so the
        // refined lag tracks the true broadband arrival independent of the driver's
        // magnitude shape. A weak or edge-pinned peak falls back to the parabola.
        int refineRadius = ComputePhatSearchRadius(SampleRate);
        // Whiten once; both arrivals refine from the same correlation.
        PhaseTransformCorrelation phaseTransform =
            TransferFunction.ComputePhaseTransform(loopback, microphone, filter);
        double firstArrivalSample = RefineArrivalSample(
            phaseTransform, envelope, peakIndex, refineRadius);
        double strongestSample = RefineArrivalSample(
            phaseTransform, envelope, peakSearchResult.StrongestIndex, refineRadius);

        ConfidenceDecibels = SignalEnvelope.EstimatePeakConfidenceDecibels(
            envelope,
            peakIndex,
            peak);
        EnvelopeSamples = envelope;
        EnvelopePeakIndex = peakIndex;
        EnvelopePeak = peak;
        StrongestEnvelopePeakIndex = peakSearchResult.StrongestIndex;
        StrongestEnvelopePeak = peakSearchResult.StrongestPeak;
        FirstArrivalPeakSample =
            ToSignedDelaySamples(firstArrivalSample, envelope.Length);
        FirstArrivalDelayMilliseconds =
            FirstArrivalPeakSample * 1000.0 / SampleRate;
        StrongestPeakSample =
            ToSignedDelaySamples(strongestSample, envelope.Length);
        StrongestDelayMilliseconds = StrongestPeakSample * 1000.0 / SampleRate;
        PeakSample = FirstArrivalPeakSample;
        DelayMilliseconds = FirstArrivalDelayMilliseconds;
    }

    // The minimum normalized GCC-PHAT peak height for its refined lag to be
    // trusted over the envelope parabola. A clean sweep peaks well above this; a
    // value below it means the whitened correlation carries no clear delay.
    private const double PhatTrustCoefficient = 0.2;

    // A short refinement window (~0.1 ms) around the envelope peak: wide enough to
    // absorb the envelope's sub-sample bias, narrow enough not to slide onto a
    // neighbouring reflection or a band-limited side lobe.
    private static int ComputePhatSearchRadius(int sampleRate) =>
        Math.Clamp((int)Math.Round(sampleRate * 0.0001), 2, 8);

    // Refines one envelope arrival to sub-sample precision from the precomputed
    // GCC-PHAT correlation, falling back to the envelope parabola when the whitened
    // peak is weak or pinned to the search-window edge.
    private double RefineArrivalSample(
        PhaseTransformCorrelation phaseTransform,
        IReadOnlyList<double> envelope,
        int coarseIndex,
        int searchRadius)
    {
        PhaseTransformDelay phat = phaseTransform.RefineAround(coarseIndex, searchRadius);
        return phat.Refined && phat.PeakCorrelation >= PhatTrustCoefficient
            ? phat.LagSamples
            : coarseIndex + FindFractionalOffset(envelope, coarseIndex);
    }

    private static double FindFractionalOffset(
        IReadOnlyList<double> envelope,
        int peakIndex)
    {
        if (peakIndex <= 0 || peakIndex >= envelope.Count - 1)
        {
            return 0;
        }

        double y0 = envelope[peakIndex - 1];
        double y1 = envelope[peakIndex];
        double y2 = envelope[peakIndex + 1];
        return SignalEnvelope.FindFractionalPeakOffset(y0, y1, y2);
    }

    private static async Task PlayToEndAsync(
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
            await stopped.Task.ConfigureAwait(false);
        }
        finally
        {
            player.PlaybackStopped -= PlaybackStopped;
        }
    }

    private static double ToSignedDelaySamples(double wrappedPeakSample, int length) =>
        wrappedPeakSample <= length * 0.5
            ? wrappedPeakSample
            : wrappedPeakSample - length;

    private void CaptureSignalLevels(
        IReadOnlyList<double> microphone,
        IReadOnlyList<double> loopback)
    {
        (MicrophonePeakDbFs, MicrophoneRmsDbFs, MicrophoneClipped) =
            MeasureSignalLevel(microphone);
        (LoopbackPeakDbFs, LoopbackRmsDbFs, LoopbackClipped) =
            MeasureSignalLevel(loopback);
        LevelsAvailable?.Invoke(new InputLevelMeterSnapshot(
            new InputLevelMeterEntry(
                true,
                MicrophonePeakDbFs,
                MicrophoneRmsDbFs,
                MicrophoneClipped,
                false),
            new InputLevelMeterEntry(
                true,
                LoopbackPeakDbFs,
                LoopbackRmsDbFs,
                false,
                LoopbackClipped)));
    }

    private void RaiseLevels(
        AudioChannelLevel[] channels,
        int microphoneChannelIndex,
        int loopbackChannelIndex)
    {
        LevelsAvailable?.Invoke(new InputLevelMeterSnapshot(
            CreateEntry(channels, microphoneChannelIndex, fullScaleReference: false),
            CreateEntry(channels, loopbackChannelIndex, fullScaleReference: true)));
    }

    private static InputLevelMeterEntry CreateEntry(
        AudioChannelLevel[] channels,
        int channelIndex,
        bool fullScaleReference)
    {
        if ((uint)channelIndex >= (uint)channels.Length)
        {
            return InputLevelMeterEntry.Unavailable;
        }

        AudioChannelLevel level = channels[channelIndex];
        return new InputLevelMeterEntry(
            true,
            level.PeakDbFs,
            level.RmsDbFs,
            !fullScaleReference && level.FullScale,
            fullScaleReference && level.FullScale);
    }

    private static (double PeakDbFs, double RmsDbFs, bool Clipped) MeasureSignalLevel(
        IReadOnlyList<double> samples)
    {
        double peak = 0;
        double sumSquares = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            double magnitude = Math.Abs(samples[i]);
            peak = Math.Max(peak, magnitude);
            sumSquares += samples[i] * samples[i];
        }

        double rms = samples.Count > 0
            ? Math.Sqrt(sumSquares / samples.Count)
            : 0;
        return (
            DataHelper.AmplitudeToDecibels(peak),
            DataHelper.AmplitudeToDecibels(rms),
            peak >= 0.999);
    }

    private static PeakSearchOptions CreatePeakSearchOptions(TimeAlignmentOptions options) =>
        new()
        {
            Mode = PeakSearchMode.FirstArrival,
            FirstPeakThresholdBelowMaxDb = options.FirstPeakThresholdBelowMaxDb,
            FirstPeakMinimumSnrDb = options.FirstPeakMinimumSnrDb,
            SearchWindowMilliseconds = options.PeakSearchWindowMilliseconds
        };

    private void ResetMeasurementResult()
    {
        PeakSample = 0;
        DelayMilliseconds = 0;
        FirstArrivalPeakSample = 0;
        FirstArrivalDelayMilliseconds = 0;
        StrongestPeakSample = 0;
        StrongestDelayMilliseconds = 0;
        EnvelopeSamples = null;
        EnvelopePeakIndex = 0;
        EnvelopePeak = 0;
        StrongestEnvelopePeakIndex = 0;
        StrongestEnvelopePeak = 0;
        ConfidenceDecibels = 0;
        MicrophonePeakDbFs = 0;
        MicrophoneRmsDbFs = 0;
        MicrophoneClipped = false;
        LoopbackPeakDbFs = 0;
        LoopbackRmsDbFs = 0;
        LoopbackClipped = false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
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
        Sweep?.Dispose();
        GC.SuppressFinalize(this);
    }

}
