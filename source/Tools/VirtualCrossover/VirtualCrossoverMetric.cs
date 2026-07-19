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
        double? LevelDeltaDb = null,
        // A latched side's full-band envelope timed the room's modal build-up,
        // not the direct rise (the alignment engine's cross-side detection,
        // rerun here): its number is real but compares a different feature, so
        // the row's Δ overstates the true skew and renders with a "~".
        bool LeftLatched = false,
        bool RightLatched = false)
    {
        public double? DeltaMs => LeftMs.HasValue && RightMs.HasValue
            ? LeftMs.Value - RightMs.Value
            : null;

        public bool AnyLatched => LeftLatched || RightLatched;
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

        // "~" marks a modal-latched read (see StereoDelta): the number is what
        // the envelope measured, but it timed the room build-up, not the
        // direct rise, so it must not read as a clean skew.
        static string Side(double? value, bool latched) =>
            value.HasValue
                ? latched
                    ? $"~{value.Value:0.00}".PadLeft(7)
                    : $"{value.Value,7:0.00}"
                : "      \u2014";

        var builder = new System.Text.StringBuilder(
            "Arrival (ms)\r\n         L      R  \u0394 L\u2212R\r\n");
        foreach (StereoDelta delta in deltas)
        {
            // Three sections, like the junction-phase figures: a negative delta
            // that rounds to zero must not render as "-+0.00".
            string deltaText = delta.DeltaMs.HasValue
                ? delta.AnyLatched
                    ? ("~" + delta.DeltaMs.Value.ToString("+0.00;-0.00;0.00"))
                        .PadLeft(7)
                    : $"{delta.DeltaMs.Value,7:+0.00;-0.00;0.00}"
                : "      \u2014";
            builder.AppendLine(
                $"{delta.Channel.PadRight(3)}" +
                $"{Side(delta.LeftMs, delta.LeftLatched)}" +
                $"{Side(delta.RightMs, delta.RightLatched)}{deltaText}");
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

        static string Side(double? value, bool latched) =>
            value.HasValue
                ? (latched ? "~" : string.Empty) + $"{value.Value:0.000}"
                : "\u2014";

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
                    ? (delta.AnyLatched ? "~" : string.Empty) +
                        $"{delta.DeltaMs.Value:+0.000;-0.000;0.000} ms"
                    : "\u2014";
                string levelText = delta.LevelDeltaDb.HasValue
                    ? $", level {delta.LevelDeltaDb.Value:+0.0;-0.0;0.0} dB"
                    : string.Empty;
                return $"{delta.Channel}: L {Side(delta.LeftMs, delta.LeftLatched)} / " +
                    $"R {Side(delta.RightMs, delta.RightLatched)} ms, " +
                    $"\u0394 {deltaText}{levelText} " +
                    $"({FrequencyText.Format(delta.LowHz)} \u2013 " +
                    $"{FrequencyText.Format(delta.HighHz)})";
            })) +
            (deltas.Any(delta => delta.AnyLatched)
                ? "\r\n~: the full-band envelope timed the room's modal " +
                    "build-up, not the direct rise\r\n(its upper half reads " +
                    "much earlier) \u2014 the sides compare different features," +
                    "\r\nso this \u0394 overstates the true skew. The alignment " +
                    "engine detects the same\r\nlatch and times such pairs by " +
                    "other means; trust its log over this row."
                : string.Empty) +
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
    /// Compact per-junction phase column for the host read-out panel: the phase
    /// at the crossover, the score-maximizing extra delay on the lower channel
    /// (with an "i" when flipping that channel's polarity scores clearly better,
    /// "~" when a flip nearly ties), and the same-polarity lobe margin ("!" when
    /// it is too small to rule out a whole-period hop).
    /// </summary>
    public static string FormatPhaseCompact(IReadOnlyList<PhaseEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(
            "Junction phase\r\n       φfc  fix ms   lobe\r\n");
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
            // Polarity slot: "i" recommends flipping the lower channel; "~"
            // keeps the current polarity but flags that flipping nearly ties
            // (an inversion and a half-period delay sum alike here — common at
            // a subwoofer junction), so the polarity is not settled by
            // summation; a space means the current polarity is clearly right.
            // This is distinct from the period-hop "!" on the lobe column.
            string polarity =
                result.BestInvert ? "i"
                : result.BestScore - result.OppositePolarityScore
                    < JunctionPhaseAlignment.PolarityFlipAdvantage ? "~"
                : " ";
            string lobe = result.LobeMargin.HasValue
                ? $"{result.LobeMargin.Value,5:0.00}"
                : "    —";
            string warning =
                result.LobeMargin is { } margin && margin < AmbiguousLobeMargin
                    ? " !"
                    : string.Empty;
            builder.AppendLine(
                $"{entry.Junction.PadRight(6)}{phase} {fix}{polarity} {lobe}{warning}");
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
                string flip = result.BestInvert
                    ? $", invert {entry.LowerChannel}"
                    : $" (flip scores {result.OppositePolarityScore:0.00})";
                return $"{entry.Junction} @ {FrequencyText.Format(entry.CrossoverHz)}: " +
                    $"{phaseNote}; " +
                    $"phase score {result.CurrentScore:0.00} now, " +
                    $"best {result.BestScore:0.00} at " +
                    $"{result.BestExtraDelayMs:+0.00;-0.00;0.00} ms " +
                    $"on {entry.LowerChannel}{flip};\r\n   {rival}; " +
                    $"fit Δτ {result.FitDelayMs:+0.00;-0.00;0.00} ms, " +
                    $"rms {result.FitRmsDeg:0}° " +
                    $"({FrequencyText.Format(entry.LowHz)} – " +
                    $"{FrequencyText.Format(entry.HighHz)})";
            })) +
            "\r\nphase score: Σw·cos(Δφ)/Σw over the band (−1..+1), a phase-" +
            "alignment score,\r\nnot the magnitude coherence γ². fix: the delay " +
            "to add to the LOWER channel\r\nthat best aligns the band. A " +
            "negative fix advances the lower channel — apply\r\nit as a +delay " +
            "on the UPPER one when the lower is already at 0. Polarity mark:" +
            "\r\n\"i\" (or \"invert\") = flipping the lower channel scores " +
            "clearly better; \"~\" = the\r\ncurrent polarity is kept but a flip " +
            "nearly ties (an inversion and a half-period\r\ndelay sum alike, " +
            "common at a sub), so summation cannot settle the polarity —\r\nφ " +
            "near ±180° never settles it either. φ is a narrow circular mean " +
            "around fc;\r\nR (0..1) is how much its bins agree — a low R dashes " +
            "it. lobe: how decisively\r\nthe best delay beats the nearest same-" +
            "polarity whole-period rival; small margin\r\n(!) = the band is too " +
            "narrow to rule that period hop out, so don't trust the fix.";
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
