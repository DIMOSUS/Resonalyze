using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What Auto Tune is given for a session, and how its result lands in the bank. See docs/tech/eq-auto-tuner.md.</summary>
internal static class EqWizardFit
{
    /// <summary>The Auto Tune settings, for a fit run elsewhere (AI import) that must match the button.</summary>
    public static EqAutoTunePolicy Policy(EqWizardSession session) => new(
        session.BandLimit,
        (double)session.GainMinDb,
        (double)session.GainMaxDb,
        (double)session.AutoTuneMaxQ,
        session.Boosts,
        session.AllowShelves,
        session.CrossoverInTarget);

    /// <summary>Mirrors the fields; bands held back (locked, kept all-pass) come off Max Filters, which budgets the whole BANK.</summary>
    public static EqAutoTuner.Options Options(EqWizardSession session, int reservedBands)
    {
        ArgumentNullException.ThrowIfNull(session);
        // A reserve eating the budget is refused before the fit; this only clamps.
        (double minHz, double maxHz) = session.FrequencyWindow;
        return Options(
            Policy(session),
            Math.Clamp(session.BandLimit - reservedBands, 1, EqWizardLimits.MaxBands),
            minHz,
            maxHz,
            session.Bank.PreampDb,
            session.ProcessorSampleRateHz,
            session.TargetCrossover);
    }

    /// <summary>The fit's options from the Auto Tune settings: the button's and a fit run elsewhere (AI import) alike.</summary>
    /// <param name="pinnedPreampDb">The preamp a fit that may lift the curve keeps.</param>
    /// <param name="crossover">The handed-over channel's crossover; read only while the policy puts it in the target.</param>
    public static EqAutoTuner.Options Options(
        EqAutoTunePolicy policy,
        int maxBands,
        double minHz,
        double maxHz,
        double pinnedPreampDb,
        int processorSampleRateHz,
        EqTargetSlope? crossover)
    {
        ArgumentNullException.ThrowIfNull(policy);
        // Preamp policy: a bank that may not lift the curve lets it move with a 0 dB ceiling; with boosts it is pinned to
        // the user's value. See docs/tech/eq-auto-tuner.md#wizard-preamp-policy.
        bool lifts = policy.Boosts == EqAutoTuneBoosts.Allowed;
        return new EqAutoTuner.Options
        {
            MaxBands = maxBands,
            MinFrequencyHz = minHz,
            MaxFrequencyHz = maxHz,
            PreampMinDb = lifts ? pinnedPreampDb : (double)EqWizardLimits.Preamp.Minimum,
            PreampMaxDb = lifts ? pinnedPreampDb : (double)EqWizardLimits.Preamp.Maximum,
            BandGainMinDb = policy.BandGainMinDb,
            BandGainMaxDb = policy.BandGainMaxDb,
            TotalGainMaxDb = lifts ? double.PositiveInfinity : 0,
            SampleRateHz = processorSampleRateHz,
            Boosts = policy.Boosts,
            // Widest Q is the strips' limit (available with an empty bank); narrowest is the user's Max Q, below what strips accept.
            QMin = (double)EqWizardLimits.BandQ.Minimum,
            QMax = policy.MaxQ,
            // Shelves are opt-in: they change the SHAPE returned, and Max Q says nothing about a shelf's knee.
            AllowShelves = policy.AllowShelves,
            // Down a crossover skirt the target's fall is the filter's doing: cut onto it, never lift it.
            NoBoostBands = policy.CrossoverInTarget && crossover is { } slope
                ? EqTargetCrossover.NoBoostBands(slope, minHz, maxHz, processorSampleRateHz)
                : Array.Empty<EqNoBoostBand>()
        };
    }

    /// <summary>
    /// Why the fit must not run: the window holds no point the fit could read. Auto Tune would return an empty bank
    /// and replace the user's with it, and a window outside the record is missing data, not a fit worth making.
    /// </summary>
    public static string? NoMeasuredDataRefusal(
        EqWizardSession session,
        IReadOnlyList<SignalPoint> source,
        IReadOnlyList<SignalPoint> target)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        (double minHz, double maxHz) = session.FrequencyWindow;
        int count = Math.Min(source.Count, target.Count);
        for (int i = 0; i < count; i++)
        {
            if (source[i].X >= minHz &&
                source[i].X <= maxHz &&
                double.IsFinite(source[i].Y) &&
                double.IsFinite(target[i].Y))
            {
                return null;
            }
        }

        string window = $"{minHz:0} Hz - {maxHz:0} Hz";
        string measured = source.Count > 0 &&
            source.Where(point => double.IsFinite(point.Y)).ToList() is { Count: > 0 } measuredPoints
                ? $"{measuredPoints.Min(point => point.X):0} Hz - {measuredPoints.Max(point => point.X):0} Hz"
                : "nothing";
        return $"The fit window ({window}) holds no measured point: the source covers {measured}." +
            Environment.NewLine + Environment.NewLine +
            "Widen From / To onto the measured range, or untick Crossover in target, which set them from " +
            "the channel's crossover.";
    }

    /// <summary>The bands the user locked: Auto Tune keeps them as they are and fits the rest around them.</summary>
    public static IReadOnlyList<PeqBand> LockedBands(EqWizardSession session) =>
        session.Bank.Bands.Where(band => band.Locked).ToList();

    /// <summary>The unlocked all-pass bands, which the fit cannot place and may keep.</summary>
    public static IReadOnlyList<PeqBand> AllPassBands(EqWizardSession session) =>
        session.Bank.Bands.Where(band => band.Type.IsAllPass() && !band.Locked).ToList();

    /// <summary>
    /// The curve the fit corrects: the source through the kept bands, drawn as the wizard draws Source + EQ. Through a
    /// GATED window an all-pass is not flat (see <see cref="EqWizardGatedPreview"/>); elsewhere only gain bands count.
    /// </summary>
    public static IReadOnlyList<DataPoint> FitSource(
        EqWizardSession session,
        EqWizardCurve source,
        IReadOnlyList<PeqBand> kept)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        if (KeptInSource(kept, session.Source is { IsGated: true }) is not { } bank)
        {
            return source.Points;
        }

        // Each path is the source's own builder, so the tuner's index pairing with the target holds.
        if (session.Source is { IsGated: true } gated)
        {
            return EqWizardSourceCurve.ToPlotPoints(
                EqWizardGatedPreview.Render(EqWizardSourceCurve.GatedPreviewRequest(session, gated, bank)),
                EqWizardSourceCurve.KeepsGaps(gated));
        }

        if (session.Source is { SpatialAverage: not null } average)
        {
            return EqWizardSourceCurve.ToPlotPoints(
                EqWizardSourceCurve.SpatialAverageCurve(session, average, bank),
                EqWizardSourceCurve.KeepsGaps(average));
        }

        return source.Points
            .Select(point => new DataPoint(
                point.X,
                point.Y + DigitalEqualizationResponse.MagnitudeDbAt(bank, point.X, session.ProcessorSampleRateHz)))
            .ToList();
    }

    /// <summary>The kept bands the source is rendered through; null when none of them moves it.</summary>
    public static EqualizationCurve? KeptInSource(IReadOnlyList<PeqBand> kept, bool gated)
    {
        ArgumentNullException.ThrowIfNull(kept);
        List<PeqBand> moving = gated ? kept.ToList() : kept.Where(band => !band.Type.IsAllPass()).ToList();
        return moving.Count > 0 ? new EqualizationCurve(moving, preampDb: 0) : null;
    }

    /// <summary>A wrong datum is fitted faithfully (whole window boosted or cut), so the user is asked before, not told after.</summary>
    public static string? LevelWarning(
        EqWizardSession session,
        IReadOnlyList<SignalPoint> fitSource,
        IReadOnlyList<SignalPoint> fitTarget)
    {
        (double minHz, double maxHz) = session.FrequencyWindow;
        return EqTargetLevelCheck.Warning(
            EqTargetLevelCheck.TargetAboveSourceDb(fitSource, fitTarget, minHz, maxHz),
            session.Boosts != EqAutoTuneBoosts.Allowed,
            minHz,
            maxHz);
    }

    /// <summary>
    /// Carries the replaced bank's kept bands (locked, and all-pass the tuner cannot emit) over into a tuned bank. On
    /// overflow the FITTED bands give way: they can be regenerated, a hand-placed band cannot.
    /// </summary>
    public static EqualizationCurve WithKeptBands(
        EqualizationCurve tuned,
        IReadOnlyList<PeqBand> kept)
    {
        ArgumentNullException.ThrowIfNull(tuned);
        ArgumentNullException.ThrowIfNull(kept);
        if (kept.Count == 0)
        {
            return tuned;
        }

        return new EqualizationCurve(
            tuned.Bands
                .Take(Math.Max(0, EqWizardLimits.MaxBands - kept.Count))
                .Concat(kept),
            tuned.PreampDb);
    }

    /// <summary>
    /// The bank a fit leaves: the kept bands carried over (<see cref="WithKeptBands"/>, so an overflow still drops the
    /// fit's least important bands, which it returns last), then every band in ascending frequency, the order a tuner
    /// reads a bank in. Bands at one frequency keep the fit's order.
    /// </summary>
    public static EqualizationCurve Finish(
        EqualizationCurve tuned,
        IReadOnlyList<PeqBand> kept)
    {
        EqualizationCurve carried = WithKeptBands(tuned, kept);
        return new EqualizationCurve(
            carried.Bands.OrderBy(band => band.FrequencyHz),
            carried.PreampDb);
    }

    public static string DescribeBoosts(EqAutoTuneBoosts boosts) => boosts switch
    {
        EqAutoTuneBoosts.Off => "cuts only",
        EqAutoTuneBoosts.RefillOwnCuts => "boosts only refilling its own cuts",
        _ => "cuts and boosts"
    };

    public static string DescribeAllPassCount(int count) =>
        count == 1 ? "an all-pass filter" : $"{count} all-pass filters";

    public static string DescribeKeptCount(IReadOnlyList<PeqBand> kept)
    {
        int locked = kept.Count(band => band.Locked);
        int allPass = kept.Count - locked;
        string lockedText = locked == 1 ? "a locked filter" : $"{locked} locked filters";
        return (locked, allPass) switch
        {
            (0, _) => DescribeAllPassCount(allPass),
            (_, 0) => lockedText,
            _ => $"{lockedText} and {DescribeAllPassCount(allPass)}"
        };
    }
}
