using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The Virtual DSP sum-loss read-outs: one entry per junction pair (or the
/// total across the crossover window) and the three text renderings — the
/// single-line sheet subtitle, the compact host-panel column and the full
/// tooltip/log breakdown. Pure formatting, unit-tested.
/// </summary>
internal static class VirtualCrossoverMetric
{
    /// <summary>
    /// One sum-loss read-out: a junction pair (or the total across the crossover
    /// window), its average and dip in dB, and the band it was measured over.
    /// (The polarity-flip null depth used to be a third column; it proved
    /// uninformative in practice and was dropped along with its computation.)
    /// </summary>
    internal readonly record struct Entry(
        string Junction,
        double AverageDb,
        double? DipDb,
        double LowHz,
        double HighHz,
        bool IsTotal);

    /// <summary>
    /// Compact single line for the sheet subtitle: no frequency ranges, so it
    /// stays short with many channels.
    /// </summary>
    public static string FormatLabel(IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0)
        {
            return "Sum loss avg: —";
        }

        IEnumerable<string> parts = entries.Select(entry =>
        {
            string body = $"{entry.AverageDb:0.0} dB" +
                (entry.DipDb.HasValue ? $", dip {entry.DipDb.Value:0.0} dB" : "");
            return entry.IsTotal ? "total " + body : $"{entry.Junction} {body}";
        });
        return "Sum loss avg: " + string.Join("   ", parts);
    }

    /// <summary>
    /// Compact per-junction column for the narrow host read-out panel: a
    /// monospace "name  avg / dip" line each, no frequency ranges (those are
    /// on hover).
    /// </summary>
    public static string FormatCompact(IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0)
        {
            return "Sum loss (dB)\r\n  avg / dip\r\n\r\n—";
        }

        var builder = new System.Text.StringBuilder(
            "Sum loss (dB)\r\n  avg / dip\r\n\r\n");
        foreach (Entry entry in entries)
        {
            string name = (entry.IsTotal ? "Total" : entry.Junction).PadRight(6);
            string dip = entry.DipDb.HasValue ? $"{entry.DipDb.Value,5:0.0}" : "    —";
            builder.AppendLine($"{name}{entry.AverageDb,5:0.0} /{dip}");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// One channel pair's final inter-side read-out: the two sides'
    /// band-limited envelope arrivals (their fully processed responses,
    /// delays included) in the pair's shared band, in ms from the transfer
    /// IR start, plus the sides' gated band-level difference in dB. A side's
    /// arrival is null when it is unmeasurable or unreliable (a silent band,
    /// a near-noise record); the level delta is null under the same gate.
    /// The timing delta is left minus right, so positive means the RIGHT
    /// side leads — the same sign convention as the Auto delay scene offset,
    /// so after a stereo run every row should read the offset. The level
    /// delta is also left minus right: positive = the LEFT side is louder
    /// at the microphone.
    /// </summary>
    internal readonly record struct StereoDelta(
        string Channel,
        double? LeftMs,
        double? RightMs,
        double LowHz,
        double HighHz,
        double? LevelDeltaDb = null)
    {
        public double? DeltaMs => LeftMs.HasValue && RightMs.HasValue
            ? LeftMs.Value - RightMs.Value
            : null;
    }

    /// <summary>
    /// Compact per-channel arrival block for the host read-out panel,
    /// appended below the sum-loss column: one L / R / delta row per pair,
    /// then the pairs' level asymmetry (the ILD companion of the timing
    /// delta).
    /// </summary>
    public static string FormatStereoDeltasCompact(IReadOnlyList<StereoDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

        static string Side(double? value) =>
            value.HasValue ? $"{value.Value,7:0.00}" : "      \u2014";

        var builder = new System.Text.StringBuilder(
            "Arrival (ms)\r\n         L      R  \u0394 L\u2212R\r\n");
        foreach (StereoDelta delta in deltas)
        {
            // Three sections, like the junction-phase figures: a negative delta
            // that rounds to zero must not render as "-+0.00".
            string deltaText = delta.DeltaMs.HasValue
                ? $"{delta.DeltaMs.Value,7:+0.00;-0.00;0.00}"
                : "      \u2014";
            builder.AppendLine(
                $"{delta.Channel.PadRight(3)}" +
                $"{Side(delta.LeftMs)}{Side(delta.RightMs)}{deltaText}");
        }

        builder.AppendLine();
        builder.AppendLine("Level \u0394 L\u2212R (dB)");
        foreach (StereoDelta delta in deltas)
        {
            string levelText = delta.LevelDeltaDb.HasValue
                ? $"{delta.LevelDeltaDb.Value,7:+0.0;-0.0;0.0}"
                : "      \u2014";
            builder.AppendLine($"{delta.Channel.PadRight(3)}{levelText}");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Full L / R / delta breakdown for the tooltip, with the measured bands.
    /// </summary>
    public static string FormatStereoDeltasDetail(IReadOnlyList<StereoDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

        static string Side(double? value) =>
            value.HasValue ? $"{value.Value:0.000}" : "\u2014";

        return "Final envelope arrivals of the processed sides (ms from the " +
            "IR start, delays\r\nincluded) and \u0394 L\u2212R (positive: right leads; " +
            "after a stereo Auto delay every\r\nchannel should read the scene " +
            "offset)\r\n" +
            string.Join("\r\n", deltas.Select(delta =>
            {
                if (!delta.LeftMs.HasValue && !delta.RightMs.HasValue)
                {
                    return $"{delta.Channel}: \u2014 (no measurable arrival)";
                }

                string deltaText = delta.DeltaMs.HasValue
                    ? $"{delta.DeltaMs.Value:+0.000;-0.000;0.000} ms"
                    : "\u2014";
                string levelText = delta.LevelDeltaDb.HasValue
                    ? $", level {delta.LevelDeltaDb.Value:+0.0;-0.0;0.0} dB"
                    : string.Empty;
                return $"{delta.Channel}: L {Side(delta.LeftMs)} / " +
                    $"R {Side(delta.RightMs)} ms, \u0394 {deltaText}{levelText} " +
                    $"({FrequencyText.Format(delta.LowHz)} \u2013 " +
                    $"{FrequencyText.Format(delta.HighHz)})";
            })) +
            "\r\nLow-band envelopes rise slowly, so the lowest rows carry " +
            "extra tolerance (a fraction of a millisecond is noise there)." +
            "\r\nLevel \u0394 is the gated band level of the processed sides " +
            "(positive: LEFT louder).\r\nTrim the louder side's gain by ear " +
            "to center the image alongside the timing \u2014\r\na single " +
            "microphone underestimates the binaural difference (no head " +
            "shadow).";
    }

    /// <summary>
    /// One junction's phase read-out for the label: the pair name, the lower
    /// channel's name (the one the recommended extra delay applies to), the
    /// crossover and overlap band it was read over, and the analysis result.
    /// </summary>
    internal readonly record struct PhaseEntry(
        string Junction,
        string LowerChannel,
        double CrossoverHz,
        double LowHz,
        double HighHz,
        JunctionPhaseResult Result);

    // Below this lobe margin the compact line gets a "!" — the overlap band is
    // too narrow to rule out a whole-period hop, so the recommendation is
    // ambiguous. The field probe read ~0.19 on a healthy octave-wide junction
    // and ~0.04 on a deliberately narrowed one.
    private const double AmbiguousLobeMargin = 0.10;

    /// <summary>
    /// Compact per-junction phase column for the host read-out panel: the
    /// fitted phase at the crossover, the coherence-maximizing extra delay on
    /// the lower channel, and the lobe margin ("—" when the sweep window holds
    /// no rival lobe; "!" when the margin is too small to trust the fix).
    /// </summary>
    public static string FormatPhaseCompact(IReadOnlyList<PhaseEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(
            "Junction phase\r\n       φfc  fix ms  lobe\r\n");
        foreach (PhaseEntry entry in entries)
        {
            JunctionPhaseResult result = entry.Result;
            // Signed values format through THREE sections on purpose: since the
            // .NET Core 3.0 signed-zero change, a negative value that rounds to
            // zero renders through the two-section form as "-+0.00" (the minus
            // is prepended to the positive section's literal plus).
            // A φ whose window bins do not agree (a notch or spectral gap at
            // the handover) is dashed instead of shown as a confident number.
            string phase =
                result.PhaseConsistency >= JunctionPhaseAlignment.MinimumPhaseConsistency
                    ? $"{result.PhaseAtCrossoverDeg,4:+0;-0;0}°"
                    : "    —";
            string fix = $"{result.BestExtraDelayMs,6:+0.00;-0.00;0.00}";
            string lobe = result.LobeMargin.HasValue
                ? $"{result.LobeMargin.Value,5:0.00}"
                : "    —";
            string warning =
                result.LobeMargin is { } margin && margin < AmbiguousLobeMargin
                    ? " !"
                    : string.Empty;
            builder.AppendLine(
                $"{entry.Junction.PadRight(6)}{phase} {fix} {lobe}{warning}");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Full junction-phase breakdown for the tooltip: every fitted figure per
    /// junction plus the legend that explains how to act on the numbers.
    /// </summary>
    public static string FormatPhaseDetail(IReadOnlyList<PhaseEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        return "Junction phase (steady state — what sustained bass sums to)\r\n" +
            string.Join("\r\n", entries.Select(entry =>
            {
                JunctionPhaseResult result = entry.Result;
                string rival = result.RivalScore.HasValue
                    ? $"rival lobe {result.RivalScore.Value:0.00} at " +
                        $"{result.RivalExtraDelayMs!.Value:+0.00;-0.00;0.00} ms " +
                        $"(margin {result.LobeMargin!.Value:0.00})"
                    : "no rival lobe in the sweep window";
                string phaseNote =
                    result.PhaseConsistency >= JunctionPhaseAlignment.MinimumPhaseConsistency
                        ? $"φ {result.PhaseAtCrossoverDeg:+0;-0;0}° at fc " +
                            $"(R {result.PhaseConsistency:0.00})"
                        : $"φ unreliable (R {result.PhaseConsistency:0.00} — " +
                            "a notch or gap sits at the handover)";
                return $"{entry.Junction} @ {FrequencyText.Format(entry.CrossoverHz)}: " +
                    $"{phaseNote}; " +
                    $"coherence {result.CurrentScore:0.00} now, " +
                    $"best {result.BestScore:0.00} at " +
                    $"{result.BestExtraDelayMs:+0.00;-0.00;0.00} ms " +
                    $"on {entry.LowerChannel};\r\n   {rival}; " +
                    $"fit Δτ {result.FitDelayMs:+0.00;-0.00;0.00} ms, " +
                    $"rms {result.FitRmsDeg:0}° " +
                    $"({FrequencyText.Format(entry.LowHz)} – " +
                    $"{FrequencyText.Format(entry.HighHz)})";
            })) +
            "\r\nfix: extra delay on the LOWER channel (add to its Delay) that " +
            "maximizes the\r\nband coherence. φ near ±180°: flip a " +
            "polarity instead of chasing a delay.\r\nφ is measured in a narrow " +
            "window around fc; R (0..1) is how much its bins\r\nagree — a low R " +
            "dashes the column instead of showing a mush number.\r\nlobe: how " +
            "decisively the optimum beats the nearest whole-period rival;" +
            "\r\nsmall margin (!) " +
            "= the overlap band is too narrow to rule the rival out.";
    }

    /// <summary>
    /// Full multi-line breakdown for the tooltip and the Auto delay log, one
    /// read-out per line, including the band each was measured over.
    /// </summary>
    public static string FormatDetail(IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0)
        {
            return "Sum loss avg: —";
        }

        return "Sum loss avg\r\n" + string.Join("\r\n", entries.Select(entry =>
        {
            string name = entry.IsTotal ? "Total" : entry.Junction;
            string dip = entry.DipDb.HasValue ? $", dip {entry.DipDb.Value:0.0} dB" : "";
            return $"{name}: {entry.AverageDb:0.0} dB avg{dip} " +
                $"({FrequencyText.Format(entry.LowHz)} – {FrequencyText.Format(entry.HighHz)})";
        }));
    }
}
