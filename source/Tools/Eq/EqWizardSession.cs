using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What Return sends back to Virtual DSP: the bank, and the target LEVEL it was fitted against.</summary>
internal sealed record EqWizardReturn(
    VirtualDspEqReturnToken Token,
    EqualizationCurve Bank,
    double TargetLevelDb);

/// <summary>
/// The EQ Wizard's state, UI-free: the curve being equalized and how it is read, the target and its level, the processor
/// the bank is realised on, the Auto Tune settings, the bank with its undo history, the phase gate and the Virtual DSP
/// handoff. Every value is held as its field shows it (<see cref="EqWizardLimits"/>). The panel writes it from its
/// controls and presents what the readers return. An imported curve is a SNAPSHOT: no link back to its slot, history
/// entry or file. See docs/tech/eq-auto-tuner.md#code-map.
/// </summary>
internal sealed class EqWizardSession
{
    public const int DefaultSampleRateHz = 48_000;

    private static readonly decimal DefaultGainMinDb = -15m;
    private static readonly decimal DefaultGainMaxDb = 6m;
    private static readonly decimal DefaultAutoTuneMaxQ = 6.0m;

    private Func<string?, CalibrationFile?>? calibrationResolver;
    private EqWizardCurve? sourceCurve;
    private bool sourceCurveStale = true;
    private EqWizardPhaseContext? phaseContext;
    private int quiet;

    public EqWizardSession()
    {
        Bank = new EqWizardBank(
            EqWizardLimits.BandGain(DefaultGainMinDb, DefaultGainMaxDb),
            Announce);
        Previews = new EqWizardPreviews(this);
    }

    /// <summary>Raised after a change the settings file keeps; not while persisted settings are being applied.</summary>
    public event Action? SettingsChanged;

    // ---- source and calibration ----

    public EqWizardCurveSource? Source { get; private set; }

    public IReadOnlyList<MicrophoneCalibrationEntry> CalibrationEntries { get; private set; } = [];

    /// <summary>Effective for the loaded source; loading a curve forces Own/Off without touching the IR preference.</summary>
    public EqWizardCalibrationChoice CalibrationChoice { get; private set; } = EqWizardCalibrationChoice.Off;

    /// <summary>The persisted choice for impulse responses; see <see cref="EqWizardCalibration.UpdatedIrPreference"/>.</summary>
    public string? PreferredIrCalibrationId { get; private set; }

    public IReadOnlyList<EqWizardCalibrationOption> CalibrationOptions =>
        EqWizardCalibration.Options(Source, CalibrationEntries, CalibrationChoice);

    /// <summary>A text import's calibration is baked in and cannot be undone, so its selector is locked.</summary>
    public bool CalibrationSelectable =>
        CalibrationOptions.Count > 1 && (Source?.SupportsCalibration ?? true);

    /// <summary>The selected width, also where the selector does not apply: reading it as Off would persist Off over the user's preference.</summary>
    public int SourceSmoothingInverseOctaves { get; private set; } =
        OverlaySmoothing.SupportedInverseOctaves[0];

    /// <summary>A curve smoothed when it was captured, or that never said, would compound a second smoothing.</summary>
    public bool SmoothingSelectable => Source?.SupportsSmoothing ?? true;

    /// <summary>The bare source curve. Cached: it changes only with source, smoothing, calibration or processor rate, never with the bank.</summary>
    public EqWizardCurve? SourceCurve
    {
        get
        {
            if (sourceCurveStale)
            {
                sourceCurve = EqWizardSourceCurve.Compute(this);
                sourceCurveStale = false;
            }

            return sourceCurve;
        }
    }

    public EqWizardPreviews Previews { get; }

    // ---- target ----

    /// <summary>The target as one value; shared with Virtual DSP, which edits it back.</summary>
    public EqTargetCurve Target { get; private set; } = new(
        TargetPreset.Flat,
        TargetCurveSpec.FromPreset(TargetPreset.Flat),
        3,
        TargetDeviationMode.Deviation,
        UiPalette.CurveTargetDefault,
        2,
        OverlayLineStyle.Dash,
        0);

    /// <summary>The target's level (dB): the user's knob, which a load leaves alone and only a handoff sets.</summary>
    public decimal TargetOffsetDb { get; private set; }

    /// <summary>Narrowed to the Virtual DSP panel's range during a handoff: the level travels back and would be clamped there.</summary>
    public NumericFieldRange TargetOffsetRange { get; private set; } = EqWizardLimits.TargetOffset;

    // ---- processor ----

    /// <summary>Used when the source states no processor; a source's processor overrides without changing it.</summary>
    public int ManualSampleRateHz { get; private set; } = DefaultSampleRateHz;

    /// <summary>
    /// Rate the biquads are REALISED at: the processor's, independent of the measurement's.
    /// See docs/tech/eq-auto-tuner.md#processor-rate-and-q-convention.
    /// </summary>
    public int ProcessorSampleRateHz => Source?.ProcessorProfile?.SampleRateHz ?? ManualSampleRateHz;

    public bool SampleRateLocked => Source?.ProcessorProfile != null;

    /// <summary>A non-standard processor rate joins the list: the tune must be realised at exactly that rate.</summary>
    public IReadOnlyList<int> SampleRateChoices =>
        DspProcessorCatalog.SelectableSampleRatesHz.Contains(ProcessorSampleRateHz)
            ? DspProcessorCatalog.SelectableSampleRatesHz
            : [.. DspProcessorCatalog.SelectableSampleRatesHz, ProcessorSampleRateHz];

    /// <summary>The user's own convention (persisted); a handoff's processor convention must never be saved over it.</summary>
    public PeqQConvention ManualQConvention { get; private set; } = PeqQConvention.Rbj;

    /// <summary>
    /// The DSP's peaking-band Q convention, locked to a handoff's processor like the rate. Moves the tuning-sheet numbers
    /// ONLY; fit, plot and profile exports stay RBJ.
    /// </summary>
    public PeqQConvention QConvention => Source?.ProcessorProfile?.QConvention ?? ManualQConvention;

    public bool QConventionLocked => Source?.ProcessorProfile?.QConvention != null;

    // ---- Auto Tune ----

    // The two windows a handoff knows: its passband, and that widened down the skirts. Kept so the crossover checkbox
    // may move the window while it still stands where it was put, and never over an edge the user typed.
    private (decimal From, decimal To)? passbandWindow;
    private (decimal From, decimal To)? slopeWindow;

    /// <summary>Lower edge of the Auto Tune window; also bounds the error metrics.</summary>
    public decimal WindowFromHz { get; private set; } = EqWizardLimits.WindowFrequency.Minimum;

    public decimal WindowToHz { get; private set; } = EqWizardLimits.WindowFrequency.Maximum;

    /// <summary>Every band's lowest gain (the maximum cut); also bounds the fit.</summary>
    public decimal GainMinDb { get; private set; } = DefaultGainMinDb;

    public decimal GainMaxDb { get; private set; } = DefaultGainMaxDb;

    public decimal AutoTuneMaxQ { get; private set; } = DefaultAutoTuneMaxQ;

    public int BandLimit { get; private set; } = EqWizardLimits.MaxBands;

    public EqAutoTuneBoosts Boosts { get; private set; } = EqAutoTuneBoosts.RefillOwnCuts;

    public bool AllowShelves { get; private set; }

    /// <summary>
    /// Whether the handed-over channel's crossover shapes the target, so the fit follows the filter's slope instead of
    /// stopping at the passband. Only a source read through a chain with a crossover has one
    /// (<see cref="TargetCrossover"/>).
    /// </summary>
    public bool CrossoverInTarget { get; private set; } = true;

    /// <summary>The slope that shapes the target, or null: no chain behind the source, or no crossover in it.</summary>
    public EqTargetSlope? TargetCrossover => EqTargetCrossover.Of(Source);

    // ---- bank and view ----

    public EqWizardBank Bank { get; }

    /// <summary>Shows the curves without the bank; the bank itself is untouched.</summary>
    public bool Bypass { get; private set; }

    public bool ShowEqCurve { get; private set; } = true;

    /// <summary>Phase is a mode, not an extra curve: magnitudes leave the plot, their statistics stay current.</summary>
    public bool PhaseMode { get; private set; }

    // ---- phase gate ----

    /// <summary>Neighbours, window and τ for the phase view; null for a magnitude-only source.</summary>
    public EqWizardPhaseContext? PhaseContext => Source is { Measurement: not null } ? phaseContext : null;

    /// <summary>Pinned: one absolute window for every curve. Unpinned: each on its own driver's arrival.</summary>
    public bool PhaseGatePinned { get; private set; }

    // ---- handoff ----

    /// <summary>The Virtual DSP channel side the bank belongs to; any other load ends it, so it never names a curve no longer shown.</summary>
    public VirtualDspEqReturnToken? HandoffToken { get; private set; }

    // ---- writes ----

    public void ConfigureCalibration(
        Func<string?, CalibrationFile?> resolver,
        IReadOnlyList<MicrophoneCalibrationEntry> entries)
    {
        calibrationResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        CalibrationEntries = entries ?? throw new ArgumentNullException(nameof(entries));
        SettleCalibrationChoice();
        InvalidateSourceCurve();
    }

    /// <summary>Installs a source; any handoff ends first, so Return never sends a bank tuned against another curve.</summary>
    public void Load(EqWizardCurveSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EndHandoff();
        Source = source;
        // Before anything draws: a window left from the previous source would open on an arrival this one lacks.
        (phaseContext, PhaseGatePinned) = EqWizardPhase.Seed(source);
        Previews.InvalidatePhase();
        CalibrationChoice = EqWizardCalibration.Choose(source, PreferredIrCalibrationId);
        SettleCalibrationChoice();
        InvalidateSourceCurve();
    }

    public void SelectCalibration(EqWizardCalibrationChoice choice)
    {
        CalibrationChoice = choice;
        PreferredIrCalibrationId = EqWizardCalibration.UpdatedIrPreference(
            PreferredIrCalibrationId, Source?.Kind, choice);
        InvalidateSourceCurve();
        Announce();
    }

    /// <summary>A pinned source reads the correction it arrived with; any other choice asks the configured calibrations.</summary>
    public CalibrationFile? ResolveCalibration() =>
        CalibrationChoice.Pinned
            ? Source?.PinnedCalibration
            : calibrationResolver?.Invoke(CalibrationChoice.MicrophoneCalibrationId);

    /// <summary>How a stored spatial average is read; a capture's "Own" is its own correction, not the IR's beside it.</summary>
    public SpatialAverageCalibration SpatialAverageCalibrationFor(EqWizardCurveSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CalibrationChoice.Own ? SpatialAverageCalibration.Own
            : CalibrationChoice.Pinned ? source.SpatialAverageCalibration
            : CalibrationChoice.IsOff ? SpatialAverageCalibration.Off
            : SpatialAverageCalibration.Specific(ResolveCalibration());
    }

    /// <returns>False for a width the selector does not offer, which is ignored like the selector ignored it.</returns>
    public bool SetSourceSmoothing(int inverseOctaves)
    {
        if (!OverlaySmoothing.SupportedInverseOctaves.Contains(inverseOctaves) ||
            inverseOctaves == SourceSmoothingInverseOctaves)
        {
            return false;
        }

        SourceSmoothingInverseOctaves = inverseOctaves;
        InvalidateSourceCurve();
        Announce();
        return true;
    }

    public void SetTarget(EqTargetCurve target)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Announce();
    }

    /// <summary>The target dialog's live preview: drawn, not kept (Cancel restores the target it opened on).</summary>
    public void PreviewTarget(OverlayTargetPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        Target = Target with
        {
            Spec = preview.Spec,
            ToleranceDb = preview.ToleranceDb,
            DeviationMode = preview.DeviationMode,
            Color = preview.Color,
            StrokeThickness = preview.StrokeThickness,
            LineStyle = preview.LineStyle,
            SmoothingInverseOctaves = preview.SmoothingInverseOctaves
        };
    }

    /// <inheritdoc cref="PreviewTarget"/>
    public void RestoreTarget(EqTargetCurve target) =>
        Target = target ?? throw new ArgumentNullException(nameof(target));

    public void SetTargetOffset(decimal offsetDb)
    {
        TargetOffsetDb = TargetOffsetRange.Contain(offsetDb);
        Announce();
    }

    public void SetManualSampleRate(int sampleRateHz)
    {
        if (sampleRateHz == ManualSampleRateHz)
        {
            return;
        }

        ManualSampleRateHz = sampleRateHz;
        // Every curve realised the chain at the old rate; the rendered ones are keyed by the bank alone.
        InvalidateSourceCurve();
        Announce();
    }

    public void SetManualQConvention(PeqQConvention convention)
    {
        ManualQConvention = convention;
        Announce();
    }

    /// <summary>The convention the host keeps beside these settings, restored like <see cref="ApplySettings"/>: nothing to save.</summary>
    public void RestoreManualQConvention(PeqQConvention convention) => ManualQConvention = convention;

    /// <summary>Pushes the upper edge when the window would close; at its limit the edited edge gives way instead.</summary>
    public void SetWindowFrom(decimal fromHz)
    {
        NumericFieldRange range = EqWizardLimits.WindowFrequency;
        WindowFromHz = range.Assign(fromHz);
        if (WindowFromHz <= WindowToHz - EqWizardLimits.MinFrequencyGapHz)
        {
            return;
        }

        decimal desiredTo = WindowFromHz + EqWizardLimits.MinFrequencyGapHz;
        if (desiredTo <= range.Maximum)
        {
            WindowToHz = range.Assign(desiredTo);
        }
        else
        {
            WindowToHz = range.Maximum;
            WindowFromHz = range.Assign(range.Maximum - EqWizardLimits.MinFrequencyGapHz);
        }
    }

    /// <inheritdoc cref="SetWindowFrom"/>
    public void SetWindowTo(decimal toHz)
    {
        NumericFieldRange range = EqWizardLimits.WindowFrequency;
        WindowToHz = range.Assign(toHz);
        if (WindowFromHz <= WindowToHz - EqWizardLimits.MinFrequencyGapHz)
        {
            return;
        }

        decimal desiredFrom = WindowToHz - EqWizardLimits.MinFrequencyGapHz;
        if (desiredFrom >= range.Minimum)
        {
            WindowFromHz = range.Assign(desiredFrom);
        }
        else
        {
            WindowFromHz = range.Minimum;
            WindowToHz = range.Assign(range.Minimum + EqWizardLimits.MinFrequencyGapHz);
        }
    }

    /// <summary>The window a handoff sets from its crossovers: both edges at once, kept at least the minimum gap apart.</summary>
    public void SetAutoTuneWindow(double minHz, double maxHz)
    {
        NumericFieldRange range = EqWizardLimits.WindowFrequency;
        decimal from = Math.Clamp(
            (decimal)Math.Min(minHz, maxHz),
            range.Minimum,
            range.Maximum - EqWizardLimits.MinFrequencyGapHz);
        decimal to = Math.Clamp(
            (decimal)Math.Max(minHz, maxHz),
            from + EqWizardLimits.MinFrequencyGapHz,
            range.Maximum);
        WindowFromHz = range.Assign(from);
        WindowToHz = range.Assign(to);
    }

    /// <summary>The Auto Tune window, ordered and at least 1 Hz wide.</summary>
    public (double MinHz, double MaxHz) FrequencyWindow
    {
        get
        {
            double fromHz = (double)WindowFromHz;
            double toHz = (double)WindowToHz;
            double minHz = Math.Min(fromHz, toHz);
            double maxHz = Math.Max(fromHz, toHz);
            if (maxHz - minHz < 1)
            {
                maxHz = minHz + 1;
            }

            return (minHz, maxHz);
        }
    }

    /// <summary>Pushes Max Boost when the range would close; at its limit Max Cut gives way. Every band's gain follows.</summary>
    /// <returns>Whether a band's gain was clamped (a pending bank edit).</returns>
    public bool SetGainMin(decimal minimumDb)
    {
        GainMinDb = EqWizardLimits.GainMinimum.Assign(minimumDb);
        if (GainMinDb > GainMaxDb - EqWizardLimits.MinGainGapDb)
        {
            decimal desiredMax = GainMinDb + EqWizardLimits.MinGainGapDb;
            if (desiredMax <= EqWizardLimits.GainMaximum.Maximum)
            {
                GainMaxDb = EqWizardLimits.GainMaximum.Assign(desiredMax);
            }
            else
            {
                GainMaxDb = EqWizardLimits.GainMaximum.Maximum;
                GainMinDb = EqWizardLimits.GainMinimum.Assign(
                    EqWizardLimits.GainMaximum.Maximum - EqWizardLimits.MinGainGapDb);
            }
        }

        return ApplyGainRange();
    }

    /// <inheritdoc cref="SetGainMin"/>
    public bool SetGainMax(decimal maximumDb)
    {
        GainMaxDb = EqWizardLimits.GainMaximum.Assign(maximumDb);
        if (GainMinDb > GainMaxDb - EqWizardLimits.MinGainGapDb)
        {
            decimal desiredMin = GainMaxDb - EqWizardLimits.MinGainGapDb;
            if (desiredMin >= EqWizardLimits.GainMinimum.Minimum)
            {
                GainMinDb = EqWizardLimits.GainMinimum.Assign(desiredMin);
            }
            else
            {
                GainMinDb = EqWizardLimits.GainMinimum.Minimum;
                GainMaxDb = EqWizardLimits.GainMaximum.Assign(
                    EqWizardLimits.GainMinimum.Minimum + EqWizardLimits.MinGainGapDb);
            }
        }

        return ApplyGainRange();
    }

    public void SetAutoTuneMaxQ(decimal maxQ)
    {
        AutoTuneMaxQ = EqWizardLimits.AutoTuneMaxQ.Assign(maxQ);
        Announce();
    }

    public void SetBandLimit(int limit) =>
        BandLimit = Math.Clamp(limit, EqWizardLimits.MinAutoTuneBandLimit, EqWizardLimits.MaxBands);

    public void SetBoosts(EqAutoTuneBoosts boosts)
    {
        Boosts = boosts;
        Announce();
    }

    public void SetAllowShelves(bool allowShelves)
    {
        AllowShelves = allowShelves;
        Announce();
    }

    /// <summary>
    /// Takes the channel's crossover into the target (or out of it). The window follows — widened down the skirts, or
    /// back to the passband — but only while it still stands where the handoff or this switch last put it.
    /// </summary>
    public void SetCrossoverInTarget(bool include)
    {
        CrossoverInTarget = include;
        if (include)
        {
            MoveWindow(from: passbandWindow, to: slopeWindow);
        }
        else
        {
            MoveWindow(from: slopeWindow, to: passbandWindow);
        }

        InvalidateSourceCurve();
        Announce();
    }

    public void SetShowEqCurve(bool show)
    {
        ShowEqCurve = show;
        Announce();
    }

    public void SetBypass(bool bypass) => Bypass = bypass;

    public void SetPhaseMode(bool phaseMode) => PhaseMode = phaseMode;

    /// <summary>A gate edited from the context the dialog opened on; see <see cref="EqWizardPhase.ApplyGate"/>.</summary>
    public void ApplyPhaseGate(
        EqWizardPhaseContext opened,
        double offsetMs,
        bool autoOffset,
        double leftMs,
        double plateauMs,
        double rightMs,
        PhaseWindowMode windowMode,
        int fdwCycles,
        PhaseDetrendMode detrendMode,
        double detrendMs)
    {
        PhaseGatePinned = !autoOffset;
        phaseContext = EqWizardPhase.ApplyGate(
            opened, offsetMs, autoOffset, leftMs, plateauMs, rightMs,
            windowMode, fdwCycles, detrendMode, detrendMs);
        Previews.InvalidatePhase();
    }

    /// <summary>Cancel returns to the stored gate, which is what makes the dialog's live preview safe.</summary>
    public void RestorePhaseGate(EqWizardPhaseContext context, bool pinned)
    {
        phaseContext = context ?? throw new ArgumentNullException(nameof(context));
        PhaseGatePinned = pinned;
        Previews.InvalidatePhase();
    }

    /// <summary>
    /// Installs a Virtual DSP channel side: its curve as the source (read with the panel's smoothing), its PEQ as the
    /// bank (one undo step), its crossovers as the Auto Tune window, and the panel's target level within the panel's range.
    /// </summary>
    public void BeginHandoff(VirtualDspEqHandoffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SetSourceSmoothing(request.SmoothingInverseOctaves);
        Load(request.Source);
        Bank.Replace(request.BankSeed);
        Bypass = false;
        passbandWindow = null;
        slopeWindow = null;
        if (request is { AutoTuneMinHz: { } minHz, AutoTuneMaxHz: { } maxHz })
        {
            SetAutoTuneWindow(minHz, maxHz);
            passbandWindow = (WindowFromHz, WindowToHz);
            if (EqTargetCrossover.Of(request.Source) is { } slope)
            {
                (double slopeMinHz, double slopeMaxHz) = EqTargetCrossover.SlopeWindow(
                    slope,
                    minHz,
                    maxHz,
                    ProcessorSampleRateHz,
                    request.Source.Measurement?.LowestMeasuredFrequencyHz,
                    request.Source.Measurement?.HighestMeasuredFrequencyHz);
                SetAutoTuneWindow(slopeMinHz, slopeMaxHz);
                slopeWindow = (WindowFromHz, WindowToHz);
                if (!CrossoverInTarget)
                {
                    SetAutoTuneWindow(minHz, maxHz);
                }
            }
        }

        // Same dB frame as the Virtual DSP plot, so its target level applies verbatim (the one case a load moves it).
        TargetOffsetRange = EqWizardLimits.TargetOffset
            .WithMinimum((decimal)request.TargetLevelMinDb)
            .WithMaximum((decimal)request.TargetLevelMaxDb);
        TargetOffsetDb = TargetOffsetRange.Clamp(request.TargetLevelDb);
        HandoffToken = request.Token;
        Announce();
    }

    /// <summary>Ends the handoff with what Return sends; null without one. The wizard keeps the bank for export either way.</summary>
    public EqWizardReturn? CompleteHandoff()
    {
        if (HandoffToken is not { } token)
        {
            return null;
        }

        EqualizationCurve bank = Bank.Curve;
        EndHandoff();
        return new EqWizardReturn(token, bank, (double)TargetOffsetDb);
    }

    /// <summary>Leaves the handoff without applying: the channel keeps its PEQ and the wizard keeps its edits.</summary>
    public bool LeaveHandoff()
    {
        if (HandoffToken == null)
        {
            return false;
        }

        EndHandoff();
        return true;
    }

    // ---- settings ----

    public MeasurementSettingsFile.EqWizardSettings CaptureSettings() => new()
    {
        Preset = Target.Preset,
        TiltDbPerOctave = Target.Spec.TiltDbPerOctave,
        BassShelfGainDb = Target.Spec.BassShelfGainDb,
        BassShelfFrequencyHz = Target.Spec.BassShelfFrequencyHz,
        BassShelfWidthOctaves = Target.Spec.BassShelfWidthOctaves,
        TrebleShelfGainDb = Target.Spec.TrebleShelfGainDb,
        TrebleShelfFrequencyHz = Target.Spec.TrebleShelfFrequencyHz,
        TrebleShelfWidthOctaves = Target.Spec.TrebleShelfWidthOctaves,
        PresenceGainDb = Target.Spec.PresenceGainDb,
        PresenceFrequencyHz = Target.Spec.PresenceFrequencyHz,
        PresenceWidthOctaves = Target.Spec.PresenceWidthOctaves,
        TargetImportedName = Target.Spec.Imported?.Name,
        TargetImportedCurve = Target.Spec.Imported?.ToStorage(),
        ToleranceDb = Target.ToleranceDb,
        DeviationMode = Target.DeviationMode,
        TargetColorArgb = Target.Color.ToArgb(),
        TargetStrokeThickness = Target.StrokeThickness,
        TargetLineStyle = Target.LineStyle,
        TargetSmoothingInverseOctaves = Target.SmoothingInverseOctaves,
        TargetOffsetDb = (double)TargetOffsetDb,
        GainMinDb = (double)GainMinDb,
        GainMaxDb = (double)GainMaxDb,
        Bands = Bank.Bands
            .Select(band => new MeasurementSettingsFile.PeqBandSettings
            {
                FrequencyHz = band.FrequencyHz,
                Q = band.Q,
                GainDb = band.GainDb,
                Type = band.Type
            })
            .ToList(),
        PreampDb = Bank.PreampDb,
        BandCount = Bank.Bands.Count,
        SourceSmoothingInverseOctaves = SourceSmoothingInverseOctaves,
        CalibrationId = PreferredIrCalibrationId,
        ManualSampleRateHz = ManualSampleRateHz,
        AutoTuneBoosts = Boosts,
        CutsOnly = Boosts != EqAutoTuneBoosts.Allowed,
        AllowShelves = AllowShelves,
        CrossoverInTarget = CrossoverInTarget,
        AutoTuneMaxQ = (double)AutoTuneMaxQ,
        ShowEqCurve = ShowEqCurve
    };

    /// <summary>
    /// Restores persisted settings (no source: only the IR calibration preference persists). Every value is normalised as
    /// its field would take it, because the file may hold non-finite numbers or undefined enums; the restored bank becomes
    /// the history's baseline.
    /// </summary>
    public void ApplySettings(MeasurementSettingsFile.EqWizardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        quiet++;
        try
        {
            Target = new EqTargetCurve(
                settings.Preset,
                new TargetCurveSpec(
                    settings.TiltDbPerOctave,
                    settings.BassShelfGainDb,
                    settings.BassShelfFrequencyHz,
                    settings.BassShelfWidthOctaves,
                    settings.TrebleShelfGainDb,
                    settings.TrebleShelfFrequencyHz,
                    settings.TrebleShelfWidthOctaves,
                    settings.PresenceGainDb,
                    settings.PresenceFrequencyHz,
                    settings.PresenceWidthOctaves)
                {
                    // The importer returns no shape for anything unreadable.
                    Imported = ImportedTargetCurve.FromStorage(
                        settings.TargetImportedName,
                        settings.TargetImportedCurve)
                },
                settings.ToleranceDb,
                settings.DeviationMode,
                Color.FromArgb(settings.TargetColorArgb),
                settings.TargetStrokeThickness,
                settings.TargetLineStyle,
                settings.TargetSmoothingInverseOctaves).Normalized();
            PreferredIrCalibrationId = settings.ResolveCalibrationId();
            CalibrationChoice = EqWizardCalibrationChoice.Microphone(PreferredIrCalibrationId);
            ManualSampleRateHz = settings.ManualSampleRateHz > 0
                ? settings.ManualSampleRateHz
                : DefaultSampleRateHz;
            SetTargetOffset(TargetOffsetRange.Clamp(settings.TargetOffsetDb));
            // One edge after the other, as the fields take them, so the push between them lands the same way.
            SetGainMin(EqWizardLimits.GainMinimum.Clamp(settings.GainMinDb));
            SetGainMax(EqWizardLimits.GainMaximum.Clamp(settings.GainMaxDb));
            SetAutoTuneMaxQ(EqWizardLimits.AutoTuneMaxQ.Clamp(settings.AutoTuneMaxQ));
            Boosts = settings.ResolveAutoTuneBoosts();
            AllowShelves = settings.AllowShelves;
            CrossoverInTarget = settings.CrossoverInTarget;
            ShowEqCurve = settings.ShowEqCurve;
            SetSourceSmoothing(settings.SourceSmoothingInverseOctaves);
            Bank.Load(
                settings.Bands != null
                    ? settings.Bands
                        .Take(EqWizardLimits.MaxBands)
                        .Select(band => new PeqBand(
                            band.FrequencyHz,
                            band.Q,
                            band.GainDb,
                            // An undefined enum number becomes a bell HERE, where it enters the app.
                            Enum.IsDefined(band.Type) ? band.Type : PeqBandType.Peaking))
                    : EqWizardBank.DefaultBands(settings.BandCount),
                settings.PreampDb);
            SettleCalibrationChoice();
            InvalidateSourceCurve();
        }
        finally
        {
            quiet--;
        }
    }

    private bool ApplyGainRange()
    {
        bool clamped = Bank.SetGainRange(EqWizardLimits.BandGain(GainMinDb, GainMaxDb));
        Announce();
        return clamped;
    }

    // The selector cannot show a choice its options lost; it falls back to the first, Off.
    private void SettleCalibrationChoice()
    {
        IReadOnlyList<EqWizardCalibrationOption> options = CalibrationOptions;
        if (!options.Any(option => option.Choice == CalibrationChoice))
        {
            CalibrationChoice = options[0].Choice;
        }
    }

    // Only a window still standing exactly where it was put follows the switch; a typed edge is the user's.
    private void MoveWindow((decimal From, decimal To)? from, (decimal From, decimal To)? to)
    {
        if (from is not { } standing ||
            to is not { } wanted ||
            WindowFromHz != standing.From ||
            WindowToHz != standing.To)
        {
            return;
        }

        WindowFromHz = wanted.From;
        WindowToHz = wanted.To;
    }

    private void InvalidateSourceCurve()
    {
        sourceCurveStale = true;
        Previews.InvalidateSourceCurves();
    }

    private void EndHandoff()
    {
        TargetOffsetRange = EqWizardLimits.TargetOffset;
        HandoffToken = null;
    }

    private void Announce()
    {
        if (quiet == 0)
        {
            SettingsChanged?.Invoke();
        }
    }
}
