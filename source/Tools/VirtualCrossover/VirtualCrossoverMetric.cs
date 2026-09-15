using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Pure formatting of the Virtual DSP read-outs. See docs/tech/virtual-dsp-analysis.md.</summary>
internal static class VirtualCrossoverMetric
{
    internal readonly record struct Entry(
        string Junction,
        double AverageDb,
        double? DipDb,
        double LowHz,
        double HighHz,
        bool IsTotal);

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

    /// <summary>Per-junction "avg / dip" column. Two decimals: genuine lobe alternatives differ by hundredths of a dB.</summary>
    /// <param name="direct">Read through <see cref="SumLossWindow.Direct"/>; the header says so, the two families are not comparable.</param>
    public static string FormatCompact(IReadOnlyList<Entry> entries, bool direct = false)
    {
        string header = (direct ? "Sum loss (direct, dB)" : "Sum loss (dB)") +
            "\r\n         avg /   dip\r\n\r\n";
        if (entries.Count == 0)
        {
            return header + "—";
        }

        var builder = new System.Text.StringBuilder(header);
        foreach (Entry entry in entries)
        {
            string name = (entry.IsTotal ? "Total" : entry.Junction).PadRight(6);
            string dip = entry.DipDb.HasValue ? $"{entry.DipDb.Value,6:0.00}" : "     —";
            builder.AppendLine($"{name}{entry.AverageDb,6:0.00} /{dip}");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>One pair's final L/R read-out. Delay and level deltas are left minus right: positive delay = RIGHT leads
    /// (the Auto delay scene-offset sign), positive level = LEFT louder. Null when unreliable, unless the level is spatial.</summary>
    internal readonly record struct StereoDelta(
        string Channel,
        double? LeftMs,
        double? RightMs,
        double LowHz,
        double HighHz,
        double? LevelDeltaDb = null,
        // Latched: the full-band envelope timed the modal build-up, so the delta renders with "~".
        bool LeftLatched = false,
        bool RightLatched = false,
        // Level from the spatial averages through the chains; timing stays on the IRs.
        bool LevelFromSpatialAverage = false,
        // Arrivals are band energy onsets (engine cross-side link rule for low pairs), not first envelope peaks.
        bool EnergyOnset = false,
        // Band qualifies for onsets but SNR is short, so both sides fell back to peaks.
        bool EnergyOnsetWithheld = false)
    {
        public double? DeltaMs => LeftMs.HasValue && RightMs.HasValue
            ? LeftMs.Value - RightMs.Value
            : null;

        public bool AnyLatched => LeftLatched || RightLatched;
    }

    public static string FormatStereoDeltasCompact(IReadOnlyList<StereoDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

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
            // Three format sections: a negative value rounding to zero must not render "-+0.00".
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

        // Mixed lists are legitimate (a channel without an array); point-measured rows are marked in place.
        bool anySpatialLevel = deltas.Any(delta => delta.LevelFromSpatialAverage);
        bool anyPointLevel = deltas.Any(delta =>
            delta.LevelDeltaDb.HasValue && !delta.LevelFromSpatialAverage);

        return "Final envelope arrivals of the processed sides (ms from the " +
            "IR start, delays\r\nincluded) and \u0394 L\u2212R (positive: right leads; " +
            "after a stereo Auto delay every\r\nchannel should read the scene " +
            "offset)\r\n" +
            string.Join("\r\n", deltas.Select(delta =>
            {
                string levelText = delta.LevelDeltaDb.HasValue
                    ? $", level {delta.LevelDeltaDb.Value:+0.0;-0.0;0.0} dB" +
                        (anySpatialLevel && !delta.LevelFromSpatialAverage
                            ? " (point mic)"
                            : string.Empty)
                    : string.Empty;
                if (!delta.LeftMs.HasValue && !delta.RightMs.HasValue)
                {
                    // A spatial level outlives the arrivals, so the row keeps it.
                    return $"{delta.Channel}: \u2014 (no measurable arrival)" +
                        levelText;
                }

                string deltaText = delta.DeltaMs.HasValue
                    ? (delta.AnyLatched ? "~" : string.Empty) +
                        $"{delta.DeltaMs.Value:+0.000;-0.000;0.000} ms"
                    : "\u2014";
                return $"{delta.Channel}: L {Side(delta.LeftMs, delta.LeftLatched)} / " +
                    $"R {Side(delta.RightMs, delta.RightLatched)} ms, " +
                    $"\u0394 {deltaText}{levelText} " +
                    $"({FrequencyText.Format(delta.LowHz)} \u2013 " +
                    $"{FrequencyText.Format(delta.HighHz)}" +
                    (delta.EnergyOnset
                        ? ", energy onsets"
                        : delta.EnergyOnsetWithheld
                            ? ", first peaks: a side is under the " +
                                $"{AutoAlignmentEngine.EnergyOnsetMinimumSnrDb:0} dB " +
                                "an energy onset needs"
                            : string.Empty) + ")";
            })) +
            (deltas.Any(delta => delta.EnergyOnset)
                ? "\r\nEnergy onsets: a pair whose shared band is centred " +
                    $"below {AutoAlignmentEngine.EnergyOnsetBandCenterHz:0} Hz is " +
                    "timed by the instant a tenth\r\nof the band's energy has " +
                    "arrived, not by its first envelope peak \u2014 a slow " +
                    "low-frequency\r\nenvelope's first hump is a coin toss " +
                    "(a fraction of a dB decides whether it peaks),\r\nand the " +
                    "stereo Auto delay's cross-side target picks its instrument " +
                    "by the same rule\r\n(on the band it reads, which may be " +
                    $"narrower). Both sides need {AutoAlignmentEngine.EnergyOnsetMinimumSnrDb:0} dB " +
                    "of SNR,\r\nor the pair reads first peaks."
                : string.Empty) +
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
            (anySpatialLevel
                ? "\r\nLevel \u0394 compares the sides' spatial averages " +
                    "through their chains \u2014 the levels\r\nthe hybrid " +
                    "view draws \u2014 so one microphone position's dips " +
                    "have no say in it\r\n(positive: LEFT louder)." +
                    (anyPointLevel
                        ? " (point mic): that pair's captures cannot produce " +
                            "the figure — a side\r\nwithout one, or no shared " +
                            "data in this band — so its row still reads " +
                            "the\r\ngated processed responses."
                        : string.Empty)
                : "\r\nLevel \u0394 is the gated band level of the processed " +
                    "sides (positive: LEFT louder).") +
            "\r\nTrim the louder side's gain by ear " +
            "to center the image alongside the timing \u2014\r\na single " +
            "microphone underestimates the binaural difference (no head " +
            "shadow).";
    }

    /// <summary>A listening group against the front stage in their shared band, replacing sum loss in multi-group views.
    /// <see cref="DelayMs"/> positive = later; <see cref="LevelDb"/> negative = quieter; null without a reliable arrival.</summary>
    internal readonly record struct GroupDelta(
        VirtualCrossoverZone Zone,
        double? DelayMs,
        double? LevelDb,
        double LowHz,
        double HighHz,
        bool LevelFromSpatialAverage = false);

    public static string FormatGroupDeltasCompact(IReadOnlyList<GroupDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

        const string Header = "vs Front\r\n       Δt ms    ΔdB\r\n\r\n";
        var builder = new System.Text.StringBuilder(Header);
        foreach (GroupDelta delta in deltas)
        {
            string name = VirtualCrossoverZones.DisplayName(delta.Zone).PadRight(7);
            string time = delta.DelayMs is { } ms ? $"{ms,7:+0.00;-0.00;0.00}" : "      —";
            string level = delta.LevelDb is { } db ? $"{db,7:+0.0;-0.0;0.0}" : "      —";
            builder.AppendLine($"{name}{time}{level}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatGroupDeltasDetail(IReadOnlyList<GroupDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return string.Empty;
        }

        bool anySpatialLevel = deltas.Any(delta => delta.LevelFromSpatialAverage);
        var builder = new System.Text.StringBuilder(
            "Each group against the front stage, measured on their summed " +
            "responses in the band they share. No summation loss is quoted " +
            "across groups: they play the same band from different places with " +
            "no crossover between them, so their sum combs however well each " +
            "one is tuned.");
        foreach (GroupDelta delta in deltas)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(VirtualCrossoverZones.DisplayName(delta.Zone));
            builder.Append($" ({delta.LowHz:0}-{delta.HighHz:0} Hz): ");
            builder.Append(delta.DelayMs is { } ms
                ? ms >= 0
                    ? $"arrives {ms:0.00} ms after the front"
                    : $"arrives {-ms:0.00} ms BEFORE the front"
                : "no reliable arrival in this band");
            if (delta.LevelDb is { } db)
            {
                builder.Append($", {Math.Abs(db):0.0} dB ");
                builder.Append(db < 0 ? "quieter" : "louder");
                if (anySpatialLevel && !delta.LevelFromSpatialAverage)
                {
                    builder.Append(" (point mic)");
                }
            }

            builder.Append('.');
        }

        if (anySpatialLevel)
        {
            builder.AppendLine();
            builder.Append(
                "Levels compare the groups' spatial averages through their " +
                "chains (each group power-summed — an average carries no " +
                "phase), so one microphone position's dips have no say in " +
                "them; the arrivals keep reading the impulse responses. A row " +
                "marked (point mic) could not be read from the captures — a " +
                "member without one, or no shared capture data across the " +
                "band — and still reads the gated processed responses.");
        }

        return builder.ToString();
    }

    internal readonly record struct PhaseEntry(
        string Junction,
        string LowerChannel,
        double CrossoverHz,
        double LowHz,
        double HighHz,
        JunctionPhaseResult Result);

    // Below this lobe margin the column shows "!": a whole-period hop cannot be ruled out (~0.19 healthy, ~0.04 narrowed).
    private const double AmbiguousLobeMargin = 0.10;

    // 10° at fc costs the sum 0.03 dB, below audibility and repeatability; phase, so it scales per junction.
    private const double SignificantFixDegrees = 10.0;

    // Fixes under 0.005 ms take a third decimal so they keep their sign.
    private static string FixText(double milliseconds) =>
        Math.Abs(milliseconds) < 0.005
            ? milliseconds.ToString("+0.000;-0.000;0.000")
            : milliseconds.ToString("+0.00;-0.00;0.00");

    /// <summary>Per-junction phase column: φ at fc, the fix on the lower channel ("i" flip, "~" flip near-tie, "!" lobe hop,
    /// "·" too small) and the current score. See docs/tech/virtual-dsp-analysis.md#junction-phase-read-out-formatting.</summary>
    public static string FormatPhaseCompact(IReadOnlyList<PhaseEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(
            "Junction phase\r\n       φfc  fix ms  score\r\n");
        foreach (PhaseEntry entry in entries)
        {
            JunctionPhaseResult result = entry.Result;
            // Three sections (.NET Core 3.0 signed zero renders "-+0.00"); φ with inconsistent bins is dashed.
            string phase =
                result.PhaseConsistency >= JunctionPhaseAlignment.MinimumPhaseConsistency
                    ? $"{result.PhaseAtCrossoverDeg,4:+0;-0;0}°"
                    : "    —";
            // Below MinimumAlignableScore the best delay is the least bad of bad alignments, so fix and polarity are dashed.
            bool alignable =
                result.BestScore >= JunctionPhaseAlignment.MinimumAlignableScore;
            double significantMs =
                SignificantFixDegrees / 360.0 * 1_000.0 / entry.CrossoverHz;
            string fix = !alignable
                ? "     —"
                : Math.Abs(result.BestExtraDelayMs) >= significantMs
                    ? $"{FixText(result.BestExtraDelayMs),6}"
                    : "     ·";
            string polarity =
                !alignable ? " "
                : result.BestInvert ? "i"
                : result.BestScore - result.OppositePolarityScore
                    < JunctionPhaseAlignment.PolarityFlipAdvantage ? "~"
                : " ";
            string score = $"{result.CurrentScore,5:0.00;-0.00;0.00}";
            // No fix on the row, no period-hop warning.
            string warning =
                alignable &&
                result.LobeMargin is { } margin && margin < AmbiguousLobeMargin
                    ? " !"
                    : string.Empty;
            builder.AppendLine(
                $"{entry.Junction.PadRight(6)}{phase} {fix}{polarity} {score}{warning}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatPhaseDetail(IReadOnlyList<PhaseEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        return "Junction phase (through the phase gate, 8-cycle window)\r\n" +
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
                string advice =
                    result.BestScore >= JunctionPhaseAlignment.MinimumAlignableScore
                        ? $"best {result.BestScore:0.00} at " +
                            $"{FixText(result.BestExtraDelayMs)} ms " +
                            $"on {entry.LowerChannel}{flip}"
                        : $"no delay aligns this band (ceiling " +
                            $"{result.BestScore:0.00}) — the two paths do not " +
                            "correlate here, so no fix is offered";
                return $"{entry.Junction} @ {FrequencyText.Format(entry.CrossoverHz)}: " +
                    $"{phaseNote}; " +
                    $"phase score {result.CurrentScore:0.00} now, " +
                    $"{advice};\r\n   {rival}; " +
                    $"fit Δτ {result.FitDelayMs:+0.00;-0.00;0.00} ms, " +
                    $"rms {result.FitRmsDeg:0}° " +
                    $"({FrequencyText.Format(entry.LowHz)} – " +
                    $"{FrequencyText.Format(entry.HighHz)})";
            })) +
            "\r\nscore: Σw·cos(Δφ)/Σw over the band as the junction stands " +
            "(−1..+1), a phase-\r\nalignment score, not the magnitude coherence " +
            "γ² — 1.00 is aligned across the\r\nband, 0 a wash, negative means " +
            "the overlap subtracts; it is what the fix\r\nmaximizes, so it " +
            "moves while a delay is dragged. fix: the delay " +
            "to add to the LOWER channel\r\nthat best aligns the band. A " +
            "negative fix advances the lower channel — apply\r\nit as a +delay " +
            "on the UPPER one when the lower is already at 0. A fix worth less " +
            "than\r\n10° of phase at fc (0.03 dB in the sum) shows as \"·\": " +
            "there is nothing to apply.\r\nA fix shows as \"—\" where even " +
            "the best delay leaves the band out of phase: the two paths do\r\n" +
            "not correlate over it, and moving one of them cannot help.\r\n" +
            "Polarity mark: " +
            "\"i\" (or \"invert\") = flipping the lower channel scores " +
            "clearly better; \"~\" = the\r\ncurrent polarity is kept but a flip " +
            "nearly ties (an inversion and a half-period\r\ndelay sum alike, " +
            "common at a sub), so summation cannot settle the polarity —\r\nφ " +
            "near ±180° never settles it either. φ is a narrow circular mean " +
            "around fc;\r\nR (0..1) is how much its bins agree — a low R dashes " +
            "it. The lobe margin above\r\nis how decisively the best delay beats " +
            "the nearest same-polarity whole-period\r\nrival; a small one (!) " +
            "means the band is too narrow to rule that period hop out,\r\nso " +
            "don't trust the fix — read the junction's coherence ladder instead.";
    }

    public static string FormatDetail(IReadOnlyList<Entry> entries, bool direct = false)
    {
        string title = direct ? "Sum loss (direct) avg" : "Sum loss avg";
        if (entries.Count == 0)
        {
            return title + ": —";
        }

        return title + "\r\n" + string.Join("\r\n", entries.Select(entry =>
        {
            string name = entry.IsTotal ? "Total" : entry.Junction;
            string dip = entry.DipDb.HasValue ? $", dip {entry.DipDb.Value:0.00} dB" : "";
            return $"{name}: {entry.AverageDb:0.00} dB avg{dip} " +
                $"({FrequencyText.Format(entry.LowHz)} – {FrequencyText.Format(entry.HighHz)})";
        })) + (direct
            ? "\r\nDirect: each channel through the Junction phase block's 8-cycle\r\n" +
              "window at its own front — the loss of the direct sound, not of the\r\n" +
              "sum the cabin hears. Deeper and more seat-sensitive than the Full\r\n" +
              "read; the two are not comparable."
            : string.Empty);
    }
}
