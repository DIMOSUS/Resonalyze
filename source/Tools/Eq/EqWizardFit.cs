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

    /// <summary>Mirrors the fields; bands held back (kept all-pass) come off Max Filters, which budgets the whole BANK.</summary>
    public static EqAutoTuner.Options Options(EqWizardSession session, int reservedBands)
    {
        ArgumentNullException.ThrowIfNull(session);
        // A reserve eating the budget is refused before the fit; this only clamps.
        int bandLimit = session.BandLimit - reservedBands;
        (double minHz, double maxHz) = session.FrequencyWindow;

        // Preamp policy: a bank that may not lift the curve lets it move with a 0 dB ceiling; with boosts it is pinned to
        // the user's value. See docs/tech/eq-auto-tuner.md#wizard-preamp-policy.
        bool lifts = session.Boosts == EqAutoTuneBoosts.Allowed;
        double pinnedPreampDb = session.Bank.PreampDb;

        return new EqAutoTuner.Options
        {
            MaxBands = Math.Clamp(bandLimit, 1, EqWizardLimits.MaxBands),
            MinFrequencyHz = minHz,
            MaxFrequencyHz = maxHz,
            PreampMinDb = lifts ? pinnedPreampDb : (double)EqWizardLimits.Preamp.Minimum,
            PreampMaxDb = lifts ? pinnedPreampDb : (double)EqWizardLimits.Preamp.Maximum,
            BandGainMinDb = (double)session.GainMinDb,
            BandGainMaxDb = (double)session.GainMaxDb,
            TotalGainMaxDb = lifts ? double.PositiveInfinity : 0,
            SampleRateHz = session.ProcessorSampleRateHz,
            Boosts = session.Boosts,
            // Widest Q is the strips' limit (available with an empty bank); narrowest is the user's Max Q, below what strips accept.
            QMin = (double)EqWizardLimits.BandQ.Minimum,
            QMax = (double)session.AutoTuneMaxQ,
            // Shelves are opt-in: they change the SHAPE returned, and Max Q says nothing about a shelf's knee.
            AllowShelves = session.AllowShelves,
            // Down a crossover skirt the target's fall is the filter's doing: cut onto it, never lift it.
            NoBoostBands = session.CrossoverInTarget && session.TargetCrossover is { } slope
                ? EqTargetCrossover.NoBoostBands(slope, minHz, maxHz, session.ProcessorSampleRateHz)
                : Array.Empty<EqNoBoostBand>()
        };
    }

    /// <summary>The all-pass bands in the bank, which the fit cannot place and may keep.</summary>
    public static IReadOnlyList<PeqBand> AllPassBands(EqWizardSession session) =>
        session.Bank.Bands.Where(band => band.Type.IsAllPass()).ToList();

    /// <summary>
    /// The curve the fit corrects. When kept all-pass bands meet a GATED source they are applied first: through a window
    /// an all-pass is not flat (see <see cref="EqWizardGatedPreview"/>).
    /// </summary>
    public static IReadOnlyList<DataPoint> FitSource(
        EqWizardSession session,
        EqWizardCurve source,
        IReadOnlyList<PeqBand> keptAllPass)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        if (keptAllPass.Count == 0 ||
            session.Source is not { IsGated: true } gated)
        {
            return source.Points;
        }

        // Same conversion as the source curve, so the tuner's index pairing with the target holds.
        return EqWizardSourceCurve.ToPlotPoints(
            EqWizardGatedPreview.Render(
                EqWizardSourceCurve.GatedPreviewRequest(
                    session, gated, new EqualizationCurve(keptAllPass, preampDb: 0))),
            EqWizardSourceCurve.KeepsGaps(gated));
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
    /// Carries the replaced bank's all-pass bands over into a tuned bank (the tuner emits bells and shelves only). On
    /// overflow the FITTED bands give way: they can be regenerated, a hand-aligned all-pass cannot.
    /// </summary>
    public static EqualizationCurve WithAllPassBands(
        EqualizationCurve tuned,
        IReadOnlyList<PeqBand> allPass)
    {
        ArgumentNullException.ThrowIfNull(tuned);
        ArgumentNullException.ThrowIfNull(allPass);
        if (allPass.Count == 0)
        {
            return tuned;
        }

        return new EqualizationCurve(
            tuned.Bands
                .Take(Math.Max(0, EqWizardLimits.MaxBands - allPass.Count))
                .Concat(allPass),
            tuned.PreampDb);
    }

    /// <summary>
    /// The bank a fit leaves: the kept all-pass bands carried over (<see cref="WithAllPassBands"/>, so an overflow still
    /// drops the fit's least important bands, which it returns last), then every band in ascending frequency, the order
    /// a tuner reads a bank in. Bands at one frequency keep the fit's order.
    /// </summary>
    public static EqualizationCurve Finish(
        EqualizationCurve tuned,
        IReadOnlyList<PeqBand> keptAllPass)
    {
        EqualizationCurve carried = WithAllPassBands(tuned, keptAllPass);
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
}
