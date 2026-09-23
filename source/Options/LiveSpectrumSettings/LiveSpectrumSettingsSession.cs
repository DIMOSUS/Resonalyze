using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>The Live Spectrum settings panel's state: each field as its control shows it, the user's own picks that
/// survive what MMM and periodic pink force, and what the analyzer offers now. An arrow key in a closed list moves a field
/// without a rule or the user's pick. See docs/tech/live-spectrum.md#settings-panel-code-map.</summary>
internal sealed class LiveSpectrumSettingsSession
{
    private NoiseColor userSignal = NoiseColor.PinkPeriodic;
    private WindowType userWindow = WindowType.Hann;
    private int userOverlap = 50;
    private bool userInputMagnitude;
    private bool userTilt;
    private bool userSpl;
    private AveragingSpeed userAveraging = AveragingSpeed.Medium;
    private int userSmoothing = 6;

    public LiveAnalysisMode Mode { get; private set; } = LiveAnalysisMode.TransferFunction;

    /// <summary>The excitations the mode offers: Silent only without a reference, periodic pink only in MMM.</summary>
    public IReadOnlyList<NoiseColor> Signals { get; private set; } = [];

    public NoiseColor Signal { get; set; } = NoiseColor.PinkPeriodic;

    public int SampleRateHz { get; private set; }

    public int SequenceLength { get; set; } = LiveSpectrumSettingsChoices.SequenceLengths[0];

    public WindowType Window { get; set; } = WindowType.Hann;

    /// <summary>False while periodic pink forces a rectangular window.</summary>
    public bool WindowEditable { get; private set; } = true;

    public int OverlapPercent { get; set; } = LiveSpectrumSettingsChoices.OverlapPercents[0];

    /// <summary>False while periodic pink forces the overlap off.</summary>
    public bool OverlapEditable { get; private set; } = true;

    public int SmoothingInverseOctaves { get; set; }

    public AveragingSpeed Averaging { get; set; } = AveragingSpeed.Medium;

    public int CoherenceLimitPercent { get; set; } = LiveSpectrumSettingsChoices.CoherenceLimits[0];

    public bool MainCurve { get; set; }

    public bool InputMagnitude { get; set; }

    public bool PeakHold { get; set; }

    public bool Coherence { get; set; }

    public bool Tilt { get; set; }

    /// <summary>The slope compensation needs a known excitation: RTA with a real noise. Evaluated when the signal is
    /// committed or the mode changes.</summary>
    public bool TiltApplicable { get; private set; }

    public bool Spl { get; set; }

    public bool SplAvailable { get; private set; }

    /// <summary>dB SPL without a calibration while a live curve exists that view-only would hide.</summary>
    public bool SplViewOnlyConflict { get; private set; }

    /// <summary>Without a loopback the analyzer falls back to RTA whatever Transfer says.</summary>
    public bool HasTransferReference { get; private set; } = true;

    /// <summary>What the plot is corrected through, as the shell names it; a read-out, not a choice.</summary>
    public string Calibration { get; private set; } = string.Empty;

    public bool IsMmm => Mode.IsSpatialAverageCapture();

    /// <summary>MMM shares the reference-free path.</summary>
    public bool IsReferenceFree => Mode.IsReferenceFree();

    /// <summary>Reference-free with its recipe free: the only mode the dB SPL scale and the slope compensation are
    /// the user's.</summary>
    public bool IsRta => IsReferenceFree && !IsMmm;

    /// <summary>MMM pins the excitation, the averaging and the smoothing to the one recipe a spatial average is valid
    /// under.</summary>
    public bool RecipeEditable => !IsMmm;

    /// <summary>The limit dims the transfer function, which a reference-free mode does not draw.</summary>
    public bool CoherenceLimitEditable => !IsReferenceFree;

    public bool InputMagnitudeInteractive => !IsReferenceFree;

    public bool SplInteractive => IsRta;

    public bool TiltInteractive => !IsMmm && TiltApplicable;

    public void Load(
        LiveSpectrumOptions options,
        bool isSplAvailable,
        bool hasLiveCurve,
        bool hasTransferReference,
        int sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(options);

        userSignal = options.NoiseColor;
        SampleRateHz = sampleRateHz;
        SequenceLength = LiveSpectrumSettingsChoices.Floor(
            LiveSpectrumSettingsChoices.SequenceLengths, options.SequenceLength);
        userOverlap = options.OverlapPercent;
        OverlapPercent = LiveSpectrumSettingsChoices.Floor(
            LiveSpectrumSettingsChoices.OverlapPercents, options.OverlapPercent);
        userSmoothing = SmoothingPresetOptions.Normalize(options.SmoothingInverseOctaves);
        SmoothingInverseOctaves = userSmoothing;
        userWindow = options.WindowType;
        Window = LiveSpectrumSettingsChoices.Offered(LiveSpectrumSettingsChoices.Windows, options.WindowType);
        userAveraging = options.AveragingSpeed;
        Averaging = LiveSpectrumSettingsChoices.Offered(LiveSpectrumSettingsChoices.Averagings, options.AveragingSpeed);
        CoherenceLimitPercent = LiveSpectrumSettingsChoices.Floor(
            LiveSpectrumSettingsChoices.CoherenceLimits, options.CoherenceThresholdPercent);
        MainCurve = options.ShowMainCurve;
        InputMagnitude = options.ShowInputMagnitude;
        userInputMagnitude = options.ShowInputMagnitude;
        PeakHold = options.PeakHold;
        Coherence = options.ShowCoherence;
        Tilt = options.CompensateNoiseTilt;
        userTilt = options.CompensateNoiseTilt;
        SetAvailability(isSplAvailable, hasLiveCurve, hasTransferReference);
        Spl = options.MagnitudeScale == MagnitudeScale.SoundPressureLevel;
        userSpl = Spl;
        Mode = options.AnalysisMode;
        ApplyMode();
        Calibration = string.Empty;
    }

    public void SetAvailability(bool isSplAvailable, bool hasLiveCurve, bool hasTransferReference)
    {
        SplAvailable = isSplAvailable;
        SplViewOnlyConflict = !isSplAvailable && hasLiveCurve;
        HasTransferReference = hasTransferReference;
    }

    public void SetCalibration(string? text) => Calibration = text ?? string.Empty;

    /// <summary>A mode picked or forced; the rules run only when it changes.</summary>
    public void SelectMode(LiveAnalysisMode mode)
    {
        if (mode == Mode)
        {
            return;
        }

        Mode = mode;
        ApplyMode();
    }

    /// <summary>Also forgets the user's dB SPL pick, which the next apply would otherwise write back.</summary>
    public void ForceSplOff()
    {
        userSpl = false;
        Spl = false;
    }

    // A field's setter is a move; a commit makes the shown value the user's pick and runs the rules it drives.
    public void CommitSignal()
    {
        userSignal = Signal;
        ApplyPeriodicPink();
        TiltApplicable = IsRta && Signal != NoiseColor.Silent;
    }

    public void CommitWindow() => userWindow = Window;

    public void CommitOverlap() => userOverlap = OverlapPercent;

    public void CommitSmoothing() => userSmoothing = SmoothingInverseOctaves;

    public void CommitAveraging() => userAveraging = Averaging;

    /// <summary>A click the box took; a muted box ignores it and keeps the user's pick.</summary>
    public void ClickInputMagnitude()
    {
        if (InputMagnitudeInteractive)
        {
            userInputMagnitude = InputMagnitude;
        }
    }

    public void ClickTilt()
    {
        if (TiltInteractive)
        {
            userTilt = Tilt;
        }
    }

    public void ClickSpl()
    {
        if (SplInteractive)
        {
            userSpl = Spl;
        }
    }

    /// <summary>What the analyzer takes: the user's own picks where a mode or periodic pink forces the field.</summary>
    public void WriteTo(LiveSpectrumOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AnalysisMode = IsMmm
            ? LiveAnalysisMode.Mmm
            : IsRta
                ? LiveAnalysisMode.Rta
                : LiveAnalysisMode.TransferFunction;
        // MMM offers periodic pink only; the user's real choice is kept.
        options.NoiseColor = IsMmm ? userSignal : Signals.Count > 0 ? Signal : NoiseColor.PinkPeriodic;
        options.SequenceLength = SequenceLength;
        options.OverlapPercent = userOverlap;
        options.SmoothingInverseOctaves = userSmoothing;
        options.WindowType = userWindow;
        options.AveragingSpeed = userAveraging;
        options.ShowMainCurve = MainCurve;
        options.ShowInputMagnitude = userInputMagnitude;
        options.PeakHold = PeakHold;
        options.ShowCoherence = Coherence;
        options.CoherenceThresholdPercent = CoherenceLimitPercent;
        options.CompensateNoiseTilt = userTilt;
        options.MagnitudeScale = userSpl ? MagnitudeScale.SoundPressureLevel : MagnitudeScale.Relative;
    }

    // RTA and MMM show no transfer curves and force the RTA on; MMM pins its recipe and offers periodic pink only.
    private void ApplyMode()
    {
        Signals = LiveSpectrumSettingsChoices.Signals(IsReferenceFree, IsMmm);
        // Only Silent can be missing (leaving RTA): fall back like the controller's normalization.
        Signal = Signals.Contains(userSignal) ? userSignal : NoiseColor.PinkPeriodic;
        ApplyPeriodicPink();
        Spl = IsMmm || userSpl;
        Tilt = IsMmm || userTilt;
        Averaging = IsMmm
            ? AveragingSpeed.Infinite
            : LiveSpectrumSettingsChoices.Offered(LiveSpectrumSettingsChoices.Averagings, userAveraging);
        SmoothingInverseOctaves = IsMmm ? 0 : userSmoothing;
        TiltApplicable = IsRta && Signal != NoiseColor.Silent;
        InputMagnitude = IsReferenceFree || userInputMagnitude;
    }

    // Periodic pink is leakage-free with a rectangular window and gains nothing from overlap.
    private void ApplyPeriodicPink()
    {
        bool periodicPink = Signals.Count > 0 && Signal == NoiseColor.PinkPeriodic;
        WindowEditable = !periodicPink;
        OverlapEditable = !periodicPink;
        Window = periodicPink
            ? WindowType.Rectangular
            : LiveSpectrumSettingsChoices.Offered(LiveSpectrumSettingsChoices.Windows, userWindow);
        OverlapPercent = LiveSpectrumSettingsChoices.Floor(
            LiveSpectrumSettingsChoices.OverlapPercents, periodicPink ? 0 : userOverlap);
    }
}
