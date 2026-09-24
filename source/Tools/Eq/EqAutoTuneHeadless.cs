using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One Auto Tune's inputs assembled as the wizard does for a handoff, with no wizard on screen.</summary>
internal sealed record EqHeadlessTuneInputs(
    IReadOnlyList<SignalPoint> Source,
    IReadOnlyList<SignalPoint> Target,
    EqAutoTuner.Options Options,
    IReadOnlyList<SignalPoint>? Coherence,
    IReadOnlyList<PeqBand> Kept,
    double MinHz,
    double MaxHz,
    EqAutoTuneBoosts Boosts);

/// <summary>The wizard's current Auto Tune settings, so an import and the button fit the same bank.</summary>
internal sealed record EqAutoTunePolicy(
    int MaxBands,
    double BandGainMinDb,
    double BandGainMaxDb,
    double MaxQ,
    EqAutoTuneBoosts Boosts,
    bool AllowShelves,
    bool CrossoverInTarget)
{
    public static EqAutoTunePolicy Default { get; } = new(
        EqualizationCurve.MaxBandCount,
        EqAutoTuneHeadless.BandGainMinDb,
        EqAutoTuneHeadless.BandGainMaxDb,
        EqAutoTuneHeadless.MaxQ,
        EqAutoTuneBoosts.RefillOwnCuts,
        AllowShelves: false,
        CrossoverInTarget: true);
}

/// <summary>
/// Auto Tune without the wizard (AI import), built from the wizard's own constructions so tests can pin them
/// together; locked bands are kept as in the wizard, and all-pass bands too since a headless run cannot ask.
/// </summary>
internal static class EqAutoTuneHeadless
{
    public const double BandGainMinDb = -15;
    public const double BandGainMaxDb = 6;

    public const double MaxQ = 6.0;

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
        EqAutoTuneBoosts? boosts)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(targetSpec);
        ArgumentNullException.ThrowIfNull(policy);
        EqAutoTuneBoosts mode = boosts ?? policy.Boosts;
        bool shelves = allowShelves ?? policy.AllowShelves;

        EqWizardCurveSource source = request.Source;
        IReadOnlyList<PeqBand> kept = KeptBands(request.BankSeed);
        IReadOnlyList<SignalPoint> fitSource = SourceCurve(
            source,
            request.SmoothingInverseOctaves,
            EqWizardFit.KeptInSource(kept, source.IsGated));
        List<SignalPoint> target = fitSource
            .Select(point => new SignalPoint(
                point.X, targetSpec.Evaluate(point.X) + request.TargetLevelDb))
            .ToList();

        (double windowMinHz, double windowMaxHz) = Window(
            minHz ?? request.AutoTuneMinHz ?? WindowMinHz,
            maxHz ?? request.AutoTuneMaxHz ?? WindowMaxHz);

        // The wizard's goal for a handed-over channel, so an import fits what the screen shows: target inside the
        // passband, the crossover's slope outside it. See docs/tech/eq-auto-tuner.md#the-crossover-in-the-target.
        EqTargetSlope? crossover = policy.CrossoverInTarget ? EqTargetCrossover.Of(source) : null;
        int processorRate = ProcessorRate(source);
        if (crossover is { } slope)
        {
            // A stated window is the caller's; only the handoff's own passband is widened down the skirts.
            if (minHz == null && maxHz == null)
            {
                (windowMinHz, windowMaxHz) = EqTargetCrossover.SlopeWindow(
                    slope,
                    windowMinHz,
                    windowMaxHz,
                    processorRate,
                    source.Measurement?.LowestMeasuredFrequencyHz,
                    source.Measurement?.HighestMeasuredFrequencyHz);
            }

            target = target
                .Select(point => new SignalPoint(
                    point.X, point.Y + EqTargetCrossover.ShapeDb(slope, point.X, processorRate)))
                .ToList();
        }

        // Max Filters budgets the BANK; kept bands come off it.
        int bandLimit = RoomUnderMaxFilters(request, policy);
        if (bandLimit <= 0)
        {
            throw new InvalidOperationException(
                $"Keeping {EqWizardFit.DescribeKeptCount(kept)} leaves no room under Max Filters ({policy.MaxBands}).");
        }

        EqAutoTuner.Options options = EqWizardFit.Options(
            policy with { Boosts = mode, AllowShelves = shelves },
            bandLimit,
            windowMinHz,
            windowMaxHz,
            request.BankSeed.PreampDb,
            processorRate,
            crossover);

        return new EqHeadlessTuneInputs(
            fitSource, target, options, source.Coherence, kept,
            windowMinHz, windowMaxHz, mode);
    }

    /// <summary>The bands a headless fit keeps: the locked ones and every all-pass.</summary>
    public static IReadOnlyList<PeqBand> KeptBands(EqualizationCurve bank)
    {
        ArgumentNullException.ThrowIfNull(bank);
        return bank.Bands.Where(band => band.Locked || band.Type.IsAllPass()).ToList();
    }

    /// <summary>Max Filters less the kept bands; zero or less is a run the wizard refuses.</summary>
    public static int RoomUnderMaxFilters(VirtualDspEqHandoffRequest request, EqAutoTunePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);
        return Math.Min(policy.MaxBands, EqualizationCurve.MaxBandCount) - KeptBands(request.BankSeed).Count;
    }

    /// <summary>
    /// Why this fit must not run: its window holds no measured point, so the tuner would answer with an empty bank
    /// and Auto Tune would apply it over the channel's own. Null when there is something to fit.
    /// </summary>
    /// <remarks>
    /// The button's wording is <see cref="EqWizardFit.NoMeasuredDataRefusal"/>. Read on the ORDERED pair: an inverted
    /// window stated by a reply is taken as stated and refused by <see cref="IsUsableWindow"/>, not read backwards.
    /// </remarks>
    public static string? NoMeasuredDataRefusal(EqHeadlessTuneInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        double lowHz = Math.Min(inputs.MinHz, inputs.MaxHz);
        double highHz = Math.Max(inputs.MinHz, inputs.MaxHz);
        return inputs.Source.Where((point, index) => index < inputs.Target.Count &&
                point.X >= lowHz &&
                point.X <= highHz &&
                double.IsFinite(point.Y) &&
                double.IsFinite(inputs.Target[index].Y)).Any()
            ? null
            : $"the fit window ({lowHz:0} Hz - {highHz:0} Hz) holds no measured point";
    }

    public static EqualizationCurve Fit(EqHeadlessTuneInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        // Backstop, not the path: a caller that skips the refusal above would otherwise apply an empty bank.
        if (NoMeasuredDataRefusal(inputs) is { } refusal)
        {
            throw new InvalidOperationException($"Nothing to fit: {refusal}.");
        }

        EqualizationCurve tuned = EqAutoTuner.Tune(
            inputs.Source, inputs.Target, inputs.Options, inputs.Coherence);
        return EqWizardFit.Finish(tuned, inputs.Kept);
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
