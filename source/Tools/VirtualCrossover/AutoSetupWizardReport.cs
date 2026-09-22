using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What the crossover wizard says: each channel's band, the preview card's lines and each junction's verdict.</summary>
internal static class AutoSetupWizardReport
{
    public static string BandText(AutoSetupWizardChannel channel) =>
        $"{FormatHz(channel.Band.LowHz)} – {FormatHz(channel.Band.HighHz)}";

    // The band shown is measured, but the chain is ordered by the effective band after the channel's own corners.
    public static string BandTooltip(AutoSetupWizardChannel channel)
    {
        const string measured = "The usable band read from the raw response — what\r\n" +
            "bounds where this driver may be crossed.";
        (double low, double high) = VirtualCrossoverAutoSetupOrder.EffectiveBand(
            channel.Band, channel.HighPassHz, channel.LowPassHz);
        bool narrowed = low > channel.Band.LowHz || high < channel.Band.HighHz;
        return narrowed
            ? measured + "\r\n\r\nIts crossover already narrows it to " +
                $"{FormatHz(low)} – {FormatHz(high)},\r\nwhich is what puts it here in the chain."
            : measured;
    }

    public static string JunctionName(AutoSetupWizardJunction junction) =>
        $"{junction.Lower.Name.Split(' ')[0]} → {junction.Upper.Name.Split(' ')[0]}";

    /// <summary>From structure, not current text (which may be a one-line error).</summary>
    public static int PreviewLineCount(AutoSetupWizardSession session) =>
        session.Rows.Count + (2 * session.GroupsInOrder().Count());

    public static IEnumerable<string> PreviewLines(AutoSetupWizardSession session, AutoSetupPreview preview)
    {
        IReadOnlyList<AutoSetupGroupFit> fits = preview.Fits;
        bool headers = fits.Count > 1;
        VirtualCrossoverAlignmentStage primary =
            fits.FirstOrDefault(fit => fit.Plan.IsPrimary)?.Plan.Group
            ?? VirtualCrossoverAlignmentStage.FrontChain;
        string anchor = primary == VirtualCrossoverAlignmentStage.FrontChain
            ? "front stage"
            : LowerFirst(VirtualCrossoverAlignmentStages.DisplayName(primary));
        for (int g = 0; g < fits.Count; g++)
        {
            AutoSetupGroupFit fit = fits[g];
            if (headers)
            {
                yield return VirtualCrossoverAlignmentStages.DisplayName(fit.Plan.Group) + ":";
            }

            for (int i = 0; i < fit.Plan.InitIndices.Count; i++)
            {
                AutoSetupWizardRow row = session.Rows.First(
                    candidate => candidate.InitIndex == fit.Plan.InitIndices[i]);
                yield return FormatProposal(row, fit.Proposals[i], headers);
            }

            yield return FormatSummary(session, fit, preview.Summaries[g], headers, anchor);
        }
    }

    /// <summary>The late half of each junction row: what the fit made of it.</summary>
    public static List<(AutoSetupWizardJunction Junction, string Verdict)> JunctionVerdicts(
        AutoSetupWizardSession session, IReadOnlyList<AutoSetupGroupFit> fits)
    {
        var verdicts = new List<(AutoSetupWizardJunction, string)>();
        foreach (AutoSetupGroupFit fit in fits)
        {
            List<AutoSetupWizardJunction> junctions = session.Junctions()
                .Where(junction => junction.Group == fit.Plan.Group)
                .OrderBy(junction => junction.IndexInGroup)
                .ToList();
            for (int j = 0; j < junctions.Count && j + 1 < fit.Proposals.Count; j++)
            {
                verdicts.Add((junctions[j], DescribeJunction(fit.Proposals[j], fit.Proposals[j + 1])));
            }
        }

        return verdicts;
    }

    /// <summary>What the junction ended up as. Split corners print both, so the row shows the split rather than
    /// hiding it behind one number.</summary>
    public static string DescribeJunction(CrossoverProposal lower, CrossoverProposal upper)
    {
        if (lower.LowPassEdge is not { } lowPass || upper.HighPassEdge is not { } highPass)
        {
            return "—";
        }

        string family = lowPass.Family == highPass.Family
            ? FamilyName(lowPass.Family)
            : $"{FamilyName(lowPass.Family)}/{FamilyName(highPass.Family)}";
        string corners = Math.Abs(lowPass.FrequencyHz - highPass.FrequencyHz) < 0.5
            ? FormatHz(lowPass.FrequencyHz)
            : $"{FormatHz(lowPass.FrequencyHz)} ↓ / {FormatHz(highPass.FrequencyHz)} ↑";
        string polarity = upper.InvertPolarity == lower.InvertPolarity ? string.Empty : ", inverted";
        return $"{family} {corners} · " +
            $"{lowPass.SlopeDbPerOctave}/{highPass.SlopeDbPerOctave} dB/oct{polarity}";
    }

    private static string FamilyName(CrossoverFilterFamily family) => family switch
    {
        CrossoverFilterFamily.LinkwitzRiley => "LR",
        CrossoverFilterFamily.Butterworth => "BW",
        CrossoverFilterFamily.Bessel => "Bessel",
        _ => family.ToString()
    };

    private static string LowerFirst(string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    // Target-curve gains make the sum an intentional downslope, so report its span, not a defect.
    private static string FormatSummary(
        AutoSetupWizardSession session,
        AutoSetupGroupFit fit,
        AutoSetupGroupSummary summary,
        bool indent,
        string anchor)
    {
        string prefix = indent ? "   " : string.Empty;
        string levelled = fit.Plan.IsPrimary
            ? string.Empty
            : $"  ·  levelled to the {anchor}";
        if (fit.Plan.Sources.Count == 1)
        {
            return prefix + (fit.Plan.IsPrimary
                ? "One driver, so nothing to cross: a protective high-pass only."
                : $"Protective high-pass, levelled to the {anchor} — balance by ear.");
        }

        // The span comes from the worker with the fit: reading it here would mean a second summed response on the
        // UI thread, which is the bulk of what the preview costs.
        string elevation = fit.Plan.IsPrimary && session.SubElevationInitialized
            ? $"  ·  bass +{(double)session.SubElevationDb:0.0} dB over mid/treble"
            : string.Empty;
        return $"{prefix}Predicted sum spans {summary.SpanDb:0.0} dB over " +
            $"{FormatHz(summary.LowHz)}–{FormatHz(summary.HighHz)}{elevation}{levelled}";
    }

    private static string FormatProposal(AutoSetupWizardRow row, CrossoverProposal proposal, bool indent)
    {
        var parts = new List<string>();
        if (proposal.HighPassEdge is { } highPass)
        {
            parts.Add($"HP {FormatHz(highPass.FrequencyHz)} {FormatFamily(highPass)}");
        }
        if (proposal.LowPassEdge is { } lowPass)
        {
            parts.Add($"LP {FormatHz(lowPass.FrequencyHz)} {FormatFamily(lowPass)}");
        }
        parts.Add($"gain {proposal.GainDb:0.0} dB");
        return $"{(indent ? "   " : string.Empty)}{row.Source.Name}:  {string.Join(",  ", parts)}";
    }

    private static string FormatFamily(CrossoverEdge edge)
    {
        string family = edge.Family switch
        {
            CrossoverFilterFamily.LinkwitzRiley => "LR",
            CrossoverFilterFamily.Butterworth => "BW",
            _ => "BE"
        };
        return $"{family}{edge.SlopeDbPerOctave}";
    }

    private static string FormatHz(double frequencyHz) =>
        FrequencyText.Format(frequencyHz);
}
