using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Everything one Auto Tune needs, assembled the way the EQ Wizard assembles
/// it for a Virtual DSP handoff, but with no wizard on screen: the curve the
/// fit corrects, the target on the same frequencies, the tuner's options, the
/// coherence that gates its boosts, and the all-pass bands that are kept out
/// of the fit and put back afterwards.
/// </summary>
internal sealed record EqHeadlessTuneInputs(
    IReadOnlyList<SignalPoint> Source,
    IReadOnlyList<SignalPoint> Target,
    EqAutoTuner.Options Options,
    IReadOnlyList<SignalPoint>? Coherence,
    IReadOnlyList<PeqBand> KeptAllPass,
    double MinHz,
    double MaxHz,
    bool CutsOnly);

/// <summary>
/// The Auto Tune settings the EQ Wizard holds at the moment — Max Filters, the
/// band gain range, Max Q, Cuts only, Shelves — as CreateAutoTuneOptions reads
/// them off its controls. An import's fit takes the wizard's CURRENT policy, so
/// the button and the import produce the same bank for the same project;
/// <see cref="Default"/> is what the wizard opens with, for a host that has no
/// wizard to ask.
/// </summary>
internal sealed record EqAutoTunePolicy(
    int MaxBands,
    double BandGainMinDb,
    double BandGainMaxDb,
    double MaxQ,
    bool CutsOnly,
    bool AllowShelves)
{
    public static EqAutoTunePolicy Default { get; } = new(
        EqualizationCurve.MaxBandCount,
        EqAutoTuneHeadless.BandGainMinDb,
        EqAutoTuneHeadless.BandGainMaxDb,
        EqAutoTuneHeadless.MaxQ,
        CutsOnly: true,
        AllowShelves: false);
}

/// <summary>
/// Auto Tune without the wizard, for an AI import that asked for it. The
/// curves and the options are the wizard's own constructions — the same gated
/// preview or spatial-average curve <c>ComputeSourceCurve</c> draws for a
/// handoff, the same target on the source's frequencies, the same option
/// mapping <c>CreateAutoTuneOptions</c> makes — held here where a test can pin
/// the two against each other, because "almost the same curve" is the failure
/// that only shows on a live session. The wizard's current Auto Tune settings
/// stand in for the fields a request leaves out; all-pass bands are always
/// kept, since a headless run cannot ask.
/// </summary>
internal static class EqAutoTuneHeadless
{
    /// <summary>The wizard's Gain min/max for a fitted band, as it opens.</summary>
    public const double BandGainMinDb = -15;
    public const double BandGainMaxDb = 6;

    /// <summary>The wizard's Max Q as it opens: bells no narrower than this.</summary>
    public const double MaxQ = 6.0;

    /// <summary>The wizard's preamp field runs ±80 dB; Cuts only lets the auto preamp use it.</summary>
    public const double PreampRangeDb = 80;

    /// <summary>The wizard's From/To fields: 20 Hz to 20 kHz, at least 1 Hz apart.</summary>
    public const double WindowMinHz = 20;
    public const double WindowMaxHz = 20_000;
    public const double MinWindowGapHz = 1;

    /// <summary>
    /// The curve the wizard would draw as Source for a Virtual DSP handoff: the
    /// spatial average through the preview chain when the handoff carries one,
    /// otherwise the impulse response through the chain and the Virtual DSP
    /// gate. <paramref name="appliedBank"/> is the bank the preview chain runs
    /// with (null = none), and the calibration is the one the panel pinned.
    /// </summary>
    public static IReadOnlyList<SignalPoint> SourceCurve(
        EqWizardCurveSource source,
        int smoothingInverseOctaves,
        EqualizationCurve? appliedBank)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != EqWizardSourceKind.VirtualDspChannel)
        {
            throw new ArgumentException(
                "Only a Virtual DSP handoff can be tuned without the wizard.", nameof(source));
        }

        int processorRate = ProcessorRate(source);
        // The wizard pins the panel's correction whenever the panel pinned any,
        // and its Off is Off (ChooseCalibration).
        bool pinned = source.PinsCorrection;

        if (source.SpatialAverage is { } document)
        {
            List<double> grid = document.ToCurvePoints().Select(point => point.X).ToList();
            List<SignalPoint>? curve = SpatialAverageHybrid.BuildChannelCurve(
                document,
                (source.PreviewChain ?? DspChannelChain.Identity) with { Peq = appliedBank },
                processorRate,
                pinned ? source.SpatialAverageCalibration : SpatialAverageCalibration.Off,
                grid,
                smoothingInverseOctaves);
            if (curve == null)
            {
                return Array.Empty<SignalPoint>();
            }

            double offset = source.SpatialAverageOffsetDb;
            // An average keeps its gaps: a NaN is a frequency the capture did not
            // cover, and the fit reads it as such.
            return curve
                .Where(point => double.IsFinite(point.X) && point.X > 0)
                .Select(point => new SignalPoint(point.X, point.Y + offset))
                .ToList();
        }

        if (!source.IsGated)
        {
            throw new ArgumentException(
                "A Virtual DSP handoff is gated or carries a spatial average.", nameof(source));
        }

        IReadOnlyList<SignalPoint> gated = EqWizardGatedPreview.Render(
            new EqWizardGatedPreviewRequest(
                source.PreviewImpulseResponse!,
                source.PreviewChain!,
                appliedBank,
                source.Measurement!.PeakIndex,
                source.Measurement.SampleRate,
                processorRate,
                source.GateSettings!,
                pinned ? source.PinnedCalibration : null,
                smoothingInverseOctaves,
                new MeasuredBand(
                    source.Measurement.LowestMeasuredFrequencyHz,
                    source.Measurement.HighestMeasuredFrequencyHz)));
        // A windowed response has no gaps to keep: a hole is a point left out.
        return gated
            .Where(point => double.IsFinite(point.X) && point.X > 0 && double.IsFinite(point.Y))
            .ToList();
    }

    /// <summary>
    /// The fit's inputs for a handoff: the source with any all-pass bands of the
    /// current bank already applied (the fit tunes around what they do, as the
    /// wizard's "keep them" does), the target on its frequencies, and the
    /// options — the request's window unless the reply narrows it, the reply's
    /// shelves and cuts-only choices where it states them, the wizard's current
    /// policy for the rest. The window is taken as stated: the review has already
    /// held it to the wizard's fields, and a headless run must not reinterpret
    /// what the user confirmed.
    /// </summary>
    public static EqHeadlessTuneInputs Prepare(
        VirtualDspEqHandoffRequest request,
        TargetCurveSpec targetSpec,
        EqAutoTunePolicy policy,
        double? minHz,
        double? maxHz,
        bool? allowShelves,
        bool? cutsOnly)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(targetSpec);
        ArgumentNullException.ThrowIfNull(policy);
        bool boostsAllowed = !(cutsOnly ?? policy.CutsOnly);
        bool shelves = allowShelves ?? policy.AllowShelves;

        EqWizardCurveSource source = request.Source;
        List<PeqBand> allPass = request.BankSeed.Bands
            .Where(band => band.Type.IsAllPass())
            .ToList();
        // Through a window an all-pass is not flat (filtering and windowing do not
        // commute), so a gated source is rendered WITH them; an average is not
        // windowed and an all-pass leaves it alone.
        IReadOnlyList<SignalPoint> fitSource = SourceCurve(
            source,
            request.SmoothingInverseOctaves,
            allPass.Count > 0 && source.IsGated
                ? new EqualizationCurve(allPass, preampDb: 0)
                : null);
        List<SignalPoint> target = fitSource
            .Select(point => new SignalPoint(
                point.X, targetSpec.Evaluate(point.X) + request.TargetLevelDb))
            .ToList();

        (double windowMinHz, double windowMaxHz) = Window(
            minHz ?? request.AutoTuneMinHz ?? WindowMinHz,
            maxHz ?? request.AutoTuneMaxHz ?? WindowMaxHz);

        // Max Filters is a budget for the BANK: the kept bands come off it.
        int bandLimit = Math.Clamp(
            policy.MaxBands - allPass.Count, 1, EqualizationCurve.MaxBandCount);
        // The preamp policy the wizard applies (CreateAutoTuneOptions): under Cuts
        // only the auto preamp may move within the field's range and the 0 dB
        // ceiling keeps the profile clip-free; otherwise the preamp is the user's
        // and stays where the bank had it.
        double seedPreamp = request.BankSeed.PreampDb;
        var options = new EqAutoTuner.Options
        {
            MaxBands = bandLimit,
            MinFrequencyHz = windowMinHz,
            MaxFrequencyHz = windowMaxHz,
            PreampMinDb = boostsAllowed ? seedPreamp : -PreampRangeDb,
            PreampMaxDb = boostsAllowed ? seedPreamp : PreampRangeDb,
            BandGainMinDb = policy.BandGainMinDb,
            BandGainMaxDb = policy.BandGainMaxDb,
            TotalGainMaxDb = boostsAllowed ? double.PositiveInfinity : 0,
            SampleRateHz = ProcessorRate(source),
            CutsOnlyMode = !boostsAllowed,
            QMin = PeqSlotControl.MinimumQ,
            QMax = policy.MaxQ,
            AllowShelves = shelves
        };

        return new EqHeadlessTuneInputs(
            fitSource, target, options, source.Coherence, allPass,
            windowMinHz, windowMaxHz, !boostsAllowed);
    }

    /// <summary>The fit, with the kept all-pass bands put back in front of it.</summary>
    public static EqualizationCurve Fit(EqHeadlessTuneInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        EqualizationCurve tuned = EqAutoTuner.Tune(
            inputs.Source, inputs.Target, inputs.Options, inputs.Coherence);
        return EqWizardPanel.WithAllPassBands(tuned, inputs.KeptAllPass);
    }

    /// <summary>
    /// The RMS of target minus curve over the window, the number the wizard's
    /// scoreboard shows before and after a fit; null with nothing to compare.
    /// </summary>
    public static double? RmsErrorDb(
        IReadOnlyList<SignalPoint> curve, IReadOnlyList<SignalPoint> target, double minHz, double maxHz)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(target);
        double sumSquares = 0;
        int valid = 0;
        int count = Math.Min(curve.Count, target.Count);
        for (int index = 0; index < count; index++)
        {
            double frequency = curve[index].X;
            double error = target[index].Y - curve[index].Y;
            // Paired by index on one grid; a point whose frequencies disagree is
            // not a comparison.
            if (frequency < minHz || frequency > maxHz || !double.IsFinite(error) ||
                Math.Abs(frequency - target[index].X) > frequency * 1e-6)
            {
                continue;
            }

            sumSquares += error * error;
            valid++;
        }

        return valid > 0 ? Math.Sqrt(sumSquares / valid) : null;
    }

    // The From/To fields' range, and nothing else: no reordering, no widening
    // — a lower edge stated above the upper is a window the review would have
    // refused, and one that slipped through is refused by the run, not read the
    // other way round.
    private static (double MinHz, double MaxHz) Window(double lowHz, double highHz) =>
        (Math.Clamp(lowHz, WindowMinHz, WindowMaxHz), Math.Clamp(highHz, WindowMinHz, WindowMaxHz));

    /// <summary>Whether a window is one the From/To fields could hold: ordered, the gap apart.</summary>
    public static bool IsUsableWindow(double minHz, double maxHz) =>
        minHz + MinWindowGapHz <= maxHz;

    // A handoff carries the project's processor, and the bank is realized at
    // its rate (EqProcessorSampleRate).
    private static int ProcessorRate(EqWizardCurveSource source) =>
        source.ProcessorProfile?.SampleRateHz
            ?? source.SampleRateHz
            ?? source.Measurement?.SampleRate
            ?? throw new ArgumentException("The handoff names no sample rate.", nameof(source));
}
