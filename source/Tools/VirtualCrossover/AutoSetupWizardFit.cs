using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed record AutoSetupGroupFit(
    AutoSetupGroupPlan Plan,
    IReadOnlyList<CrossoverProposal> Proposals);

/// <summary>The summed span one group is predicted to have, and the band it was read over.</summary>
internal sealed record AutoSetupGroupSummary(double SpanDb, double LowHz, double HighHz);

/// <summary>Everything the preview needs that costs real time.</summary>
internal sealed record AutoSetupPreview(
    IReadOnlyList<AutoSetupGroupFit> Fits,
    IReadOnlyList<AutoSetupGroupSummary> Summaries,
    decimal ElevationCeiling,
    decimal? ElevationValue);

/// <summary>What one preview run reads, taken from the session on the UI thread before the run leaves it.</summary>
internal sealed record AutoSetupPreviewInputs(
    IReadOnlyList<AutoSetupGroupPlan> Plan,
    IReadOnlyDictionary<VirtualCrossoverAlignmentStage, CrossoverAutoSetupOptions> Options,
    double SampleRateHz,
    double ProcessorSampleRateHz,
    bool ElevationInitialized,
    decimal ElevationDb)
{
    public static AutoSetupPreviewInputs Of(AutoSetupWizardSession session)
    {
        List<AutoSetupGroupPlan> plan = AutoSetupWizardPlan.Groups(session, withImpulseResponses: false);
        return new AutoSetupPreviewInputs(
            plan,
            AutoSetupWizardPlan.Snapshot(session, plan),
            session.SampleRateHz,
            session.ProcessorSampleRateHz,
            session.SubElevationInitialized,
            session.SubElevationDb);
    }
}

/// <summary>The crossover wizard's fit: each group as its own chain, the primary first and the others levelled onto
/// it. Pure, so it runs off the UI thread. See docs/tech/crossover-auto-setup.md#groups-outside-the-chain.</summary>
internal static class AutoSetupWizardFit
{
    public static List<AutoSetupGroupFit> Fit(
        IReadOnlyList<AutoSetupGroupPlan> plan,
        Func<AutoSetupGroupPlan, CrossoverAutoSetupOptions> options,
        double sampleRateHz)
    {
        var fitted = new IReadOnlyList<CrossoverProposal>[plan.Count];
        double? reference = null;
        // Primary fitted first (others level onto it); stable sort keeps plan order for the rest.
        foreach (int index in Enumerable.Range(0, plan.Count)
                     .OrderByDescending(index => plan[index].IsPrimary))
        {
            AutoSetupGroupPlan group = plan[index];
            CrossoverAutoSetupOptions groupOptions = options(group);
            IReadOnlyList<CrossoverProposal> proposals = group.Sources.Count == 1
                ? [CrossoverAutoSetup.ProposeSingle(group.Sources[0], groupOptions)]
                : group.ImpulseResponses != null
                    ? CrossoverAutoSetup.ProposeRanked(
                        group.Sources, groupOptions, group.ImpulseResponses)[0].Proposals
                    : CrossoverAutoSetup.Propose(group.Sources, groupOptions);

            if (group.IsPrimary)
            {
                reference = CrossoverAutoSetup.ReferenceLevelDb(
                    group.Sources, proposals, sampleRateHz);
            }
            else if (reference is { } level)
            {
                proposals = CrossoverAutoSetup.OffsetToReferenceLevel(
                    group.Sources, proposals, sampleRateHz, level);
            }

            fitted[index] = proposals;
        }

        return plan.Select((group, index) => new AutoSetupGroupFit(group, fitted[index])).ToList();
    }

    /// <summary>One proposal per channel, in the order the wizard was opened on.</summary>
    public static CrossoverProposal[] InInitOrder(IReadOnlyList<AutoSetupGroupFit> fits, int count)
    {
        var result = new CrossoverProposal[count];
        foreach (AutoSetupGroupFit fit in fits)
        {
            for (int i = 0; i < fit.Plan.InitIndices.Count; i++)
            {
                result[fit.Plan.InitIndices[i]] = fit.Proposals[i];
            }
        }

        return result;
    }

    /// <summary>The magnitude-only fit of the session as it stands; null when nothing fits.</summary>
    public static List<AutoSetupGroupFit>? TryFit(AutoSetupWizardSession session)
    {
        if (session.SelectedFamilies().Count == 0)
        {
            return null;
        }

        try
        {
            return Fit(
                AutoSetupWizardPlan.Groups(session, withImpulseResponses: false),
                group => AutoSetupWizardPlan.OptionsFor(session, group),
                session.SampleRateHz);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A run that moves the elevation ceiling fits a second time here rather than bouncing back through the
    /// UI to do it.</summary>
    public static AutoSetupPreview? Preview(AutoSetupPreviewInputs inputs)
    {
        List<AutoSetupGroupFit>? fits = TryFit(inputs.Plan, inputs.Options, inputs.SampleRateHz);
        if (fits == null)
        {
            return null;
        }

        decimal ceiling = 0;
        decimal? value = null;
        AutoSetupGroupFit? primary = fits.FirstOrDefault(fit => fit.Plan.IsPrimary);
        if (primary != null && primary.Plan.Sources.Count > 1)
        {
            ceiling = (decimal)Math.Max(0, Math.Round(
                CrossoverAutoSetup.MeasuredSubElevationDb(
                    primary.Plan.Sources, primary.Proposals, inputs.SampleRateHz),
                1));
            if (!inputs.ElevationInitialized && ceiling != inputs.ElevationDb)
            {
                value = ceiling;
                // The first fit used a default elevation the user never sees; refit to the one about to be shown.
                fits = TryFit(
                    inputs.Plan,
                    inputs.Options.ToDictionary(
                        entry => entry.Key,
                        entry => entry.Key == primary.Plan.Group
                            ? entry.Value with { SubElevationDb = (double)ceiling }
                            : entry.Value),
                    inputs.SampleRateHz) ?? fits;
            }
        }

        var summaries = new List<AutoSetupGroupSummary>(fits.Count);
        foreach (AutoSetupGroupFit fit in fits)
        {
            summaries.Add(Summarize(fit, inputs.SampleRateHz, inputs.ProcessorSampleRateHz));
        }

        return new AutoSetupPreview(fits, summaries, ceiling, value);
    }

    private static List<AutoSetupGroupFit>? TryFit(
        IReadOnlyList<AutoSetupGroupPlan> plan,
        IReadOnlyDictionary<VirtualCrossoverAlignmentStage, CrossoverAutoSetupOptions> options,
        double sampleRateHz)
    {
        try
        {
            return Fit(plan, group => options[group.Group], sampleRateHz);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static AutoSetupGroupSummary Summarize(
        AutoSetupGroupFit fit,
        double sampleRateHz,
        double processorSampleRateHz)
    {
        IReadOnlyList<AutoSetupSource> sources = fit.Plan.Sources;
        if (sources.Count < 2)
        {
            return new AutoSetupGroupSummary(0, 0, 0);
        }

        DriverBandEstimate low = CrossoverAutoSetup.EstimateBand(
            sources[0].MagnitudeDb, sources[0].Coherence);
        DriverBandEstimate high = CrossoverAutoSetup.EstimateBand(
            sources[^1].MagnitudeDb, sources[^1].Coherence);
        double trim = Math.Pow(2.0, 0.5);
        var window = CrossoverAutoSetup
            .SummedResponseDb(sources, fit.Proposals, sampleRateHz, processorSampleRateHz)
            .Where(point => point.X >= low.LowHz * trim && point.X <= high.HighHz / trim)
            .Select(point => point.Y)
            .ToList();
        return new AutoSetupGroupSummary(
            window.Count > 0 ? window.Max() - window.Min() : 0, low.LowHz, high.HighHz);
    }
}
