namespace Resonalyze;

/// <summary>
/// Live Spectrum's state, without UI: the analyzer and its next run's inputs, what the plot shows (the run's
/// accumulation, held after a stop, or a loaded capture in its place) and the peak-hold envelope over it.
/// </summary>
/// <remarks>
/// <see cref="Changed"/> announces what the form's live surfaces read: running, what is shown and through which
/// calibration, what Save would write, and whether a reference is routed. Frames are not announced; the plot reads
/// them on its own clock. See docs/tech/live-spectrum.md#code-map.
/// </remarks>
internal sealed class LiveSpectrumSession : IDisposable
{
    private readonly NoiseMeasurement analyzer;
    private readonly Func<SplCalibration?> splAnchor;
    private readonly Func<string?, CapturedMicrophoneCalibration> resolveCalibration;
    // The next run's filter, frozen onto the accumulation when it starts.
    private ProtectiveHighPassConfiguration configuredProtectiveHighPass =
        ProtectiveHighPassConfiguration.Off;
    // The last accumulation read; kept after a stop, so a rebuild, an overlay capture or Save still has it.
    private LiveSpectrumSnapshot? heldSnapshot;
    // State, not a one-off draw: every rebuild shows it until a run or a discard replaces it.
    private LiveCaptureDocument? loadedCapture;

    /// <param name="splAnchor">The configured SPL calibration; it anchors the live input only when taken on it.</param>
    /// <param name="resolveCalibration">Turns a calibration id into the curve a run freezes.</param>
    public LiveSpectrumSession(
        NoiseMeasurement analyzer,
        LiveSpectrumOptions options,
        Func<SplCalibration?> splAnchor,
        Func<string?, CapturedMicrophoneCalibration> resolveCalibration)
    {
        this.analyzer = analyzer;
        Options = options;
        this.splAnchor = splAnchor;
        this.resolveCalibration = resolveCalibration;
    }

    /// <summary>Raised on the UI thread when a surface reading this session may show something else.</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread after <see cref="Changed"/> when a run ended in a device failure.</summary>
    public event Action<Exception>? Failed;

    /// <summary>The analyzer's run ended, raised on the analyzer's thread (true for a stop, false for a failure);
    /// <see cref="RunEnded"/> is its half on the UI thread.</summary>
    public event Action<bool>? Completed
    {
        add => analyzer.Completed += value;
        remove => analyzer.Completed -= value;
    }

    public LiveSpectrumOptions Options { get; }

    public LiveSpectrumCurves Curves { get; } = new();

    public LivePeakHold PeakHold { get; } = new();

    public bool InProgress => analyzer.InProgress;

    public bool HasConfiguredLoopback => analyzer.HasConfiguredLoopback;

    public int SampleRate => analyzer.SampleRate;

    /// <summary>The analyzer's read of the accumulation so far; only a redraw's skip test needs it.</summary>
    public int AveragedFrameCount => analyzer.AveragedFrameCount;

    public bool HasRecentDrops => analyzer.HasRecentDrops();

    public LiveSpectrumDisplay Display => LiveSpectrumDisplay.Of(Options, analyzer.Setup, splAnchor());

    public LiveSpectrumSnapshot? HeldSnapshot => heldSnapshot;

    public LiveCaptureDocument? LoadedCapture => loadedCapture;

    /// <summary>Whether a view-only SPL scale would hide a curve (drives the amber SPL warning).</summary>
    public bool HasDisplayableCurve => analyzer.InProgress || heldSnapshot != null;

    /// <summary>Calibration of the curve on the plot: a loaded capture's own, else the id frozen on the accumulation; null for an empty plot.</summary>
    public string? DisplayedCalibrationName =>
        loadedCapture is { } document
            ? document.Calibration?.Name ?? string.Empty
            : HasDisplayableCurve
                ? analyzer.CaptureMicrophoneCalibrationName
                : null;

    /// <summary>Whether a held accumulation can be saved; a loaded capture is excluded (re-saving would restamp its recipe).</summary>
    public bool HasCaptureToSave =>
        loadedCapture == null && heldSnapshot?.InputMagnitude is { Length: > 1 };

    /// <summary>The held snapshot as a capture document, or null. Stop and hold first, or the newest frames are missing.</summary>
    public LiveCaptureDocument? BuildCaptureDocument() =>
        heldSnapshot is { } snapshot
            ? Curves.CaptureDocument(
                Display,
                snapshot.InputMagnitude,
                snapshot.FrameCount,
                title: string.Empty,
                snapshot.ClippedFrameCount)
            : null;

    /// <summary>The held RTA as an overlay stores it.</summary>
    public RawCurveCapture? BuildRawRtaCapture() =>
        Curves.RawRta(Display, heldSnapshot?.InputMagnitude);

    /// <summary>MMM integration progress: the curve settles visually long before the average does. Null outside MMM.</summary>
    public LiveCaptureProgress? Progress(LiveSpectrumDisplay display)
    {
        if (!display.Mode.IsSpatialAverageCapture())
        {
            return null;
        }

        int frames;
        int clipped;
        double seconds;
        string state;
        if (loadedCapture is { } document)
        {
            frames = document.Recipe.AveragedFrameCount;
            clipped = document.Recipe.ClippedFrameCount ?? 0;
            seconds = document.Recipe.IntegratedSeconds;
            state = "Loaded";
        }
        else
        {
            bool running = analyzer.InProgress;
            frames = running ? analyzer.AveragedFrameCount : heldSnapshot?.FrameCount ?? 0;
            clipped = running ? analyzer.ClippedFrameCount : heldSnapshot?.ClippedFrameCount ?? 0;
            int sampleRate = display.Setup.SampleRate;
            if (sampleRate < 1)
            {
                return null;
            }

            seconds = (double)frames * display.Setup.HopSize / sampleRate;
            state = running ? "Integrating" : "Capture held";
        }

        return frames > 0 ? new LiveCaptureProgress(state, seconds, frames, clipped) : null;
    }

    /// <summary>A new analysis frame's accumulation, now the held one; null before the first frame.</summary>
    public LiveSpectrumSnapshot? ReadFrame(LiveSpectrumDisplay display)
    {
        LiveSpectrumSnapshot? snapshot =
            analyzer.GetAccumulatedSpectrumSnapshot(display.NeedsInputMagnitude);
        if (snapshot != null)
        {
            heldSnapshot = snapshot;
        }

        return snapshot;
    }

    /// <summary>For a rebuild: a fresh read (accumulators survive a stop, so a scale switch gets curves the held
    /// snapshot lacks), falling back to the held one.</summary>
    public LiveSpectrumSnapshot? Reread(LiveSpectrumDisplay display)
    {
        LiveSpectrumSnapshot? snapshot =
            analyzer.GetAccumulatedSpectrumSnapshot(display.NeedsInputMagnitude) ?? heldSnapshot;
        if (snapshot != null)
        {
            heldSnapshot = snapshot;
        }

        return snapshot;
    }

    /// <summary>Configures the analyzer, and the next run's high-pass, from the measurement settings.</summary>
    /// <remarks>A stopped accumulation belongs to the capture session it was read in: under another rate or frame length
    /// the plot and Save would place its bins on the wrong frequencies, and under another input or route Save would file
    /// it under that session, with that input's SPL anchor. A new capture session discards it.</remarks>
    public void Configure(MeasurementSettingsFile.SweepMeasurementSettings measurementSettings)
    {
        SetProtectiveHighPass(measurementSettings);
        Guid captureSessionBefore = analyzer.CaptureSessionId;
        analyzer.Init(
            measurementSettings.SampleRate,
            measurementSettings.Bits,
            60,
            measurementSettings.PlaybackChannel,
            Options.SequenceLength,
            measurementSettings.OutputDeviceNumber,
            measurementSettings.InputDeviceNumber,
            measurementSettings.AudioBackend,
            measurementSettings.AsioDriverName,
            measurementSettings.AsioInputChannelOffset,
            measurementSettings.AsioOutputChannelOffset,
            measurementSettings.WaveInputChannelOffset,
            measurementSettings.WaveLoopbackInputChannelOffset,
            measurementSettings.AsioLoopbackInputChannelOffset,
            Options,
            measurementSettings.WasapiCaptureEndpointId,
            measurementSettings.WasapiRenderEndpointId,
            measurementSettings.WasapiBufferMilliseconds);
        if (analyzer.CaptureSessionId != captureSessionBefore)
        {
            // A loaded capture carries its own geometry and stays.
            analyzer.ResetAccumulation();
            heldSnapshot = null;
            PeakHold.Clear();
        }

        NormalizeSilentSignal();
        // Routing may have gained or lost the loopback live Transfer needs.
        Changed?.Invoke();
    }

    /// <summary>Updates the next run's protective high-pass without reconfiguring (and so restarting) the analyzer.</summary>
    public void SetProtectiveHighPass(MeasurementSettingsFile.SweepMeasurementSettings measurementSettings)
    {
        ArgumentNullException.ThrowIfNull(measurementSettings);
        configuredProtectiveHighPass = measurementSettings.ToProtectiveHighPass();
    }

    /// <summary>Starts a fresh accumulation in place of whatever was shown.</summary>
    public void Start()
    {
        NormalizeSilentSignal();
        PeakHold.Suspend();
        heldSnapshot = null;
        loadedCapture = null;
        // Frozen for this accumulation so the divided-out filter and the saved recipe agree.
        analyzer.SetCaptureProtectiveHighPass(configuredProtectiveHighPass);
        // Calibration curve frozen too: bins are re-rendered on every redraw and on Save.
        analyzer.SetCaptureMicrophoneCalibration(resolveCalibration(Options.CalibrationId));
        _ = analyzer.RunAsync();
        Changed?.Invoke();
    }

    /// <summary>Stops and harvests the final accumulation into the held one; <see cref="AbortAsync"/> would drop the newest frames.</summary>
    /// <returns>The final accumulation, or null when the run read nothing (the held one is then kept).</returns>
    public async Task<LiveSpectrumSnapshot?> StopAsync()
    {
        LiveSpectrumSnapshot? finalSnapshot = analyzer.GetAccumulatedSpectrumSnapshot(
            Display.NeedsInputMagnitude);
        await analyzer.AbortAsync();
        heldSnapshot = finalSnapshot ?? heldSnapshot;
        Changed?.Invoke();
        return finalSnapshot;
    }

    /// <summary>Stops without the last reading, for a run that is being thrown away.</summary>
    public async Task AbortAsync()
    {
        if (analyzer.InProgress)
        {
            await analyzer.AbortAsync();
        }

        Changed?.Invoke();
    }

    /// <summary>Called on the UI thread once the analyzer reports its run ended, on its own or by a stop.</summary>
    /// <param name="success">False for a device failure; a stop reports success.</param>
    public void RunEnded(bool success)
    {
        Changed?.Invoke();
        if (!success && analyzer.LastError is { } error)
        {
            Failed?.Invoke(error);
        }
    }


    /// <summary>Restarts the average without stopping the capture.</summary>
    public void ResetAverage()
    {
        analyzer.ResetAccumulation();
        PeakHold.Suspend();
    }

    /// <summary>Takes a display option: an Infinite average restarts, and an envelope under another transform is dropped.</summary>
    /// <returns>Whether the accumulation restarted.</returns>
    public bool ApplyDisplayOptions()
    {
        analyzer.RefreshLiveAveraging();
        bool restarted = false;
        // Infinite restarts on option changes, except spatial-average captures (the accumulation is the measurement). Keyed on mode, not stored speed.
        if (!Options.AnalysisMode.IsSpatialAverageCapture() &&
            Options.EffectiveAveragingSpeed == AveragingSpeed.Infinite)
        {
            analyzer.ResetAccumulation();
            restarted = true;
        }

        if (!Options.PeakHold)
        {
            PeakHold.Clear();
        }

        PeakHold.Follow(Display.PeakHoldKey);
        return restarted;
    }

    /// <summary>Replaces the held curve with a stored capture.</summary>
    public void ShowLoaded(LiveCaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        heldSnapshot = null;
        PeakHold.Suspend();
        loadedCapture = document;
        Changed?.Invoke();
    }

    /// <summary>Discards the accumulation, the held curve, its envelope and a loaded capture: nothing of it may be
    /// redrawn under new parameters or in a new session.</summary>
    public void Discard()
    {
        analyzer.ResetAccumulation();
        loadedCapture = null;
        heldSnapshot = null;
        PeakHold.Clear();
        Changed?.Invoke();
    }

    /// <summary>The SPL anchor moved: drops the envelope drawn under the old one. The capture itself keeps running.</summary>
    public void InvalidateCalibration()
    {
        PeakHold.Suspend();
        // Whether dB SPL would hide a curve depends on the anchor.
        Changed?.Invoke();
    }

    public void Dispose() => analyzer.Dispose();

    private void NormalizeSilentSignal()
    {
        if (Options.NormalizeSignalType())
        {
            analyzer.RefreshPlaybackSignal();
        }
    }
}

/// <summary>How far an MMM walk has integrated, as the plot reads it out.</summary>
internal sealed record LiveCaptureProgress(string State, double Seconds, int Frames, int ClippedFrames)
{
    public string Text => ClippedFrames > 0
        ? $"{State} — {Seconds:0} s, {Frames} frames, {ClippedFrames} clipped"
        : $"{State} — {Seconds:0} s, {Frames} frames";
}
