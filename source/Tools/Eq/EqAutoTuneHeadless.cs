using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One Auto Tune's inputs assembled as the wizard does for a handoff, with no wizard on screen.</summary>
internal sealed record EqHeadlessTuneInputs(
    IReadOnlyList<SignalPoint> Source,
    IReadOnlyList<SignalPoint> Target,
    EqAutoTuner.Options Options,
    IReadOnlyList<SignalPoint>? Coherence,
    IReadOnlyList<PeqBand> KeptAllPass,
    double MinHz,
    double MaxHz,
    bool CutsOnly);

/// <summary>The wizard's current Auto Tune settings, so an import and the button fit the same bank.</summary>
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
/// Auto Tune without the wizard (AI import), built from the wizard's own constructions so tests can pin them
/// together; all-pass bands are always kept since a headless run cannot ask.
/// </summary>
internal static class EqAutoTuneHeadless
{
    public const double BandGainMinDb = -15;
    public const double BandGainMaxDb = 6;

    public const double MaxQ = 6.0;

    public const double PreampRangeDb = 80;

    public const double WindowMinHz = 20;
    public const double WindowMaxHz = 20_000;
    public const double MinWindowGapHz = 1;

    /// <summary>The wizard's Source for a handoff; <paramref name="appliedBank"/> runs in the preview chain (null = none).</summary>
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
            // An average keeps NaN gaps: frequencies the capture did not cover.
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
        return gated
            .Where(point => double.IsFinite(point.X) && point.X > 0 && double.IsFinite(point.Y))
            .ToList();
    }

    /// <summary>Fit inputs for a handoff; the window is taken as stated (the review already held it to the wizard's fields).</summary>
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
        // Through a window an all-pass is not flat, so a gated source is rendered WITH kept all-pass bands.
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

        // Max Filters budgets the BANK; kept bands come off it.
        int bandLimit = RoomUnderMaxFilters(request, policy);
        if (bandLimit <= 0)
        {
            throw new InvalidOperationException(
                $"Keeping {allPass.Count} all-pass bands leaves no room under Max Filters ({policy.MaxBands}).");
        }
        // Wizard preamp policy (CreateAutoTuneOptions); see docs/tech/eq-auto-tuner.md#wizard-preamp-policy.
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
            QMin = (double)EqWizardLimits.BandQ.Minimum,
            QMax = policy.MaxQ,
            AllowShelves = shelves
        };

        return new EqHeadlessTuneInputs(
            fitSource, target, options, source.Coherence, allPass,
            windowMinHz, windowMaxHz, !boostsAllowed);
    }

    /// <summary>Max Filters less kept all-pass bands; zero or less is a run the wizard refuses.</summary>
    public static int RoomUnderMaxFilters(VirtualDspEqHandoffRequest request, EqAutoTunePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);
        int kept = request.BankSeed.Bands.Count(band => band.Type.IsAllPass());
        return Math.Min(policy.MaxBands, EqualizationCurve.MaxBandCount) - kept;
    }

    public static EqualizationCurve Fit(EqHeadlessTuneInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        EqualizationCurve tuned = EqAutoTuner.Tune(
            inputs.Source, inputs.Target, inputs.Options, inputs.Coherence);
        return EqWizardFit.Finish(tuned, inputs.KeptAllPass);
    }

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
            // Paired by index on one grid; mismatched frequencies are not a comparison.
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

    // Clamped only, never reordered: an inverted window is refused by the run, not read the other way round.
    private static (double MinHz, double MaxHz) Window(double lowHz, double highHz) =>
        (Math.Clamp(lowHz, WindowMinHz, WindowMaxHz), Math.Clamp(highHz, WindowMinHz, WindowMaxHz));

    public static bool IsUsableWindow(double minHz, double maxHz) =>
        minHz + MinWindowGapHz <= maxHz;

    private static int ProcessorRate(EqWizardCurveSource source) =>
        source.ProcessorProfile?.SampleRateHz
            ?? source.SampleRateHz
            ?? source.Measurement?.SampleRate
            ?? throw new ArgumentException("The handoff names no sample rate.", nameof(source));
}
