using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The status report of a read as styled segments; the panel draws them, table segments in its monospaced font.</summary>
internal sealed class TimeAlignmentReport
{
    private readonly List<TimeAlignmentReportSegment> segments = [];

    private TimeAlignmentReport()
    {
    }

    public static IReadOnlyList<TimeAlignmentReportSegment> ForMessage(string text)
    {
        var report = new TimeAlignmentReport();
        report.AppendStatusText(text, UiPalette.TextSecondary);
        return report.segments;
    }

    public static IReadOnlyList<TimeAlignmentReportSegment> ForResult(
        TimeAlignmentBandMode bandMode,
        TimeAlignmentOutcome outcome)
    {
        var report = new TimeAlignmentReport();
        report.AppendMeasurementResult(
            bandMode, "Main", outcome.MainSource.Levels, outcome.MainResult, outcome.MainProbe, outcome.MainCrosstalk);
        report.AppendCompareResult(
            bandMode,
            outcome.MainResult,
            outcome.Compare,
            outcome.CompareProbe,
            outcome.CompareCrosstalk,
            outcome.CompareWarning);
        return report.segments;
    }

    private void AppendStatusText(string text, Color color, bool table = false) =>
        segments.Add(new TimeAlignmentReportSegment(text, color, table));

    private void AppendCompareResult(
        TimeAlignmentBandMode bandMode,
        TimeAlignmentAnalysisResult mainResult,
        TimeAlignmentCompareAnalysis? compareAnalysis,
        TimeAlignmentArrivalProbe? compareProbe,
        CrosstalkHeadGate? compareCrosstalk,
        string? warning)
    {
        if (compareAnalysis == null && warning == null)
        {
            return;
        }

        if (warning != null)
        {
            AppendStatusText("\r\nCompare: ", UiPalette.TextDefault, table: true);
            AppendStatusText(warning + "\r\n", UiPalette.Warning);
            return;
        }

        AppendStatusText("\r\n", UiPalette.TextDefault);
        AppendMeasurementResult(
            bandMode,
            "Compare",
            compareAnalysis!.Value.Source.Levels,
            compareAnalysis.Value.Result,
            compareProbe,
            compareCrosstalk,
            mainResult);
    }

    private void AppendMeasurementResult(
        TimeAlignmentBandMode bandMode,
        string title,
        InputLevelMeterSnapshot levels,
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? honestyProbe,
        CrosstalkHeadGate? crosstalk,
        TimeAlignmentAnalysisResult? reference = null)
    {
        AppendSignalQuality(title, result);
        AppendAlignmentConfidence(result);
        AppendArrivalHonesty(bandMode, result, honestyProbe);
        AppendCrosstalkFlag(bandMode, crosstalk);
        AppendLevelsLine(levels);
        AppendSeparator();
        TimeAlignmentDelayRow? recommended = TimeAlignmentRecommendation.RecommendedRow(
            result, honestyProbe, bandMode, crosstalk != null);
        AppendDelayTable(result, reference, recommended);
        if (recommended is { } row)
        {
            AppendStatusText("Recommended for alignment: ", UiPalette.TextDefault);
            AppendStatusText(TimeAlignmentRecommendation.RowLabel(row) + "\r\n", UiPalette.Success);
            AppendStrongestPeakHint(result);
        }
    }

    // Field failure (v3): an electrical playback copy at a fixed early sample is timed by the full-band first arrival instead of the sound.
    private void AppendCrosstalkFlag(
        TimeAlignmentBandMode bandMode,
        CrosstalkHeadGate? crosstalk)
    {
        if (crosstalk is not { } gate)
        {
            return;
        }

        // The mode the READ was taken in, not the controls' current one: a bypass read must not claim a cleaning.
        if (bandMode == TimeAlignmentBandMode.FullBand)
        {
            AppendStatusText(
                $"⚠ Playback crosstalk at {gate.BurstTimeMs:0.00} ms " +
                $"({gate.BurstPeakDbReMax:0.0} dB re max) — an electrical copy of\r\n" +
                "the playback, not the driver's sound; the full-band First Arrival\r\n" +
                "may be timing it. Switch to Auto band (analyzed with it removed).\r\n",
                UiPalette.Error);
            return;
        }

        AppendStatusText(
            $"⚠ Playback crosstalk at {gate.BurstTimeMs:0.00} ms " +
            $"({gate.BurstPeakDbReMax:0.0} dB re max) removed from this analysis\r\n",
            UiPalette.Warning);
    }

    // Engine's arrival honesty probe: a full-band arrival far LATER than its upper half is a modal latch (times a room mode, not the front).
    private void AppendArrivalHonesty(
        TimeAlignmentBandMode bandMode,
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? probe)
    {
        if (bandMode == TimeAlignmentBandMode.FullBand)
        {
            return;
        }

        AppendStatusText("Arrival probe: ", UiPalette.TextDefault);
        if (probe == null)
        {
            AppendStatusText(
                "pass band too narrow for the upper-half check\r\n",
                UiPalette.TextSecondary);
            return;
        }

        TimeAlignmentArrivalProbe probeValue = probe.Value;
        switch (probeValue.Certificate)
        {
            case AutoAlignmentEngine.ArrivalCertificate.Verified:
                AppendStatusText(
                    $"verified — the {probeValue.ProbeLowHz:0}-{probeValue.ProbeHighHz:0} Hz " +
                    "upper half agrees " +
                    $"({probeValue.ProbeResult.FirstArrivalDelayMilliseconds:0.000} ms)\r\n",
                    UiPalette.Success);
                break;
            case AutoAlignmentEngine.ArrivalCertificate.Latched:
                // Upper-half figure is diagnostic only: in the engine's field case it walked a woofer 6 ms off.
                AppendStatusText(
                    $"MODAL LATCH — full band {result.FirstArrivalDelayMilliseconds:0.000} ms " +
                    $"vs upper half {probeValue.ProbeResult.FirstArrivalDelayMilliseconds:0.000} ms\r\n",
                    UiPalette.Error);
                AppendStatusText(
                    "⚠ Not the direct front (modal build-up) — do not align " +
                    "from this arrival;\r\nchange the analysis band or check " +
                    "the measurement.\r\n",
                    UiPalette.Error);
                break;
            default:
                AppendStatusText(
                    "not certified — the upper half is unmeasurable or does not " +
                    "show the front\r\n",
                    UiPalette.TextSecondary);
                break;
        }
    }

    private void AppendStrongestPeakHint(TimeAlignmentAnalysisResult result)
    {
        if (!result.StrongestPeakIsSeparateArrival)
        {
            return;
        }

        AppendStatusText(
            $"⚠ Strongest peak is ~{result.StrongestPeakSeparationMilliseconds:0.0} ms " +
            "after first arrival — likely a room mode or reflection.\r\n",
            UiPalette.Warning);
    }

    // SNR grades the recording, prominence grades the pick; kept apart because a woofer's broad edge gives low prominence on a good recording.
    private void AppendSignalQuality(string title, TimeAlignmentAnalysisResult result)
    {
        AppendStatusText($"{title} Signal: ", UiPalette.TextDefault);

        // Below the engine's SNR floor the arrival is a noise bump (independent noise reads ~8 dB): shown, but graded not-evidence.
        if (result.SignalToNoiseDecibels < AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            AppendStatusText(
                $"Unmeasurable ({result.SignalToNoiseDecibels:0.0} dB SNR, below " +
                $"the {AutoAlignmentEngine.MinimumArrivalSnrDb:0} dB floor)\r\n",
                UiPalette.Error);
            AppendStatusText(
                "⚠ The arrival is not distinguishable from the record's noise\r\n" +
                "floor — the delay figures below are noise, not measurements.\r\n",
                UiPalette.Error);
            return;
        }

        string signalGrade = FormatConfidence(result.SignalToNoiseDecibels);
        AppendStatusText(
            $"{signalGrade} ({result.SignalToNoiseDecibels:0.0} dB SNR)\r\n",
            GetConfidenceColor(signalGrade));

        double prominence = result.FirstArrivalProminenceDecibels;
        AppendStatusText("First arrival: ", UiPalette.TextDefault);
        if (prominence >= -1.0)
        {
            AppendStatusText(
                "coincides with the strongest peak\r\n",
                UiPalette.Success);
            return;
        }

        string hint = prominence <= BroadRiseProminenceDb
            ? " — broad rise, normal for low-frequency drivers"
            : string.Empty;
        Color color = prominence >= BroadRiseProminenceDb
            ? UiPalette.Success
            : UiPalette.TextSecondary;
        AppendStatusText(
            $"{prominence:0.0} dB re strongest peak{hint}\r\n",
            color);
    }

    // Typical of band-limited LF drivers whose envelope rises over milliseconds.
    private const double BroadRiseProminenceDb = -12.0;

    // RefinedByPhat=false: whitened peak too weak, envelope parabola set the position (coarse alignment only).
    private void AppendAlignmentConfidence(TimeAlignmentAnalysisResult result)
    {
        // Near-noise already declared non-evidence; a confident percentage would contradict it.
        if (result.SignalToNoiseDecibels < AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            return;
        }

        int percent = (int)Math.Round(
            Math.Clamp(result.FirstArrivalConfidence, 0.0, 1.0) * 100.0);
        string method = result.FirstArrivalRefinedByPhat
            ? "GCC-PHAT"
            : "envelope fallback";
        Color color = !result.FirstArrivalRefinedByPhat
            ? UiPalette.TextSecondary
            : result.FirstArrivalConfidence >= 0.6
                ? UiPalette.Success
                : result.FirstArrivalConfidence >= 0.4
                    ? UiPalette.Success
                    : UiPalette.Warning;
        AppendStatusText("Alignment: ", UiPalette.TextDefault);
        AppendStatusText($"{percent}% ({method})\r\n", color);
    }

    private void AppendSeparator()
    {
        AppendStatusText(
            new string('_', 54) + "\r\n",
            UiPalette.TextSecondary,
            table: true);
    }

    // Compare cells show the delta against Main in parentheses; the recommended row is bright and named, others dimmed.
    private void AppendDelayTable(
        TimeAlignmentAnalysisResult result,
        TimeAlignmentAnalysisResult? reference,
        TimeAlignmentDelayRow? recommended)
    {
        AppendStatusText(
            DelayTableText.FormatHeader() + "\r\n",
            UiPalette.TextDefault,
            table: true);
        AppendDelayRow(
            TimeAlignmentDelayRow.FirstArrival,
            UiPalette.MarkerFirstArrival,
            result.FirstArrivalDelayMilliseconds,
            result.FirstArrivalPeakSample,
            reference?.FirstArrivalDelayMilliseconds,
            reference?.FirstArrivalPeakSample,
            recommended);
        AppendDelayRow(
            TimeAlignmentDelayRow.StrongestPeak,
            UiPalette.MarkerStrongestPeak,
            result.StrongestDelayMilliseconds,
            result.StrongestPeakSample,
            reference?.StrongestDelayMilliseconds,
            reference?.StrongestPeakSample,
            recommended);
        AppendDelayRow(
            TimeAlignmentDelayRow.EnergyOnset,
            UiPalette.MarkerEnergyOnset,
            result.EnergyOnsetDelayMilliseconds,
            result.EnergyOnsetSample,
            reference?.EnergyOnsetDelayMilliseconds,
            reference?.EnergyOnsetSample,
            recommended);
    }

    private void AppendDelayRow(
        TimeAlignmentDelayRow row,
        Color labelColor,
        double milliseconds,
        double samples,
        double? referenceMilliseconds,
        double? referenceSamples,
        TimeAlignmentDelayRow? recommended)
    {
        bool isRecommended = recommended == row;
        // Cells stay one segment so click-to-copy columns remain exact.
        AppendStatusText(
            TimeAlignmentRecommendation.RowLabel(row).PadRight(DelayTableText.MillisecondsColumn),
            labelColor,
            table: true);
        AppendStatusText(
            DelayTableText.FormatCells(
                FormatValueWithDelta(milliseconds, referenceMilliseconds, "0.000"),
                FormatValueWithDelta(samples, referenceSamples, "0.0"),
                FormatValueWithDelta(
                    DelayMeters(milliseconds),
                    referenceMilliseconds is { } referenceMs ? DelayMeters(referenceMs) : null,
                    "0.000")),
            isRecommended ? UiPalette.TextDefault : UiPalette.TextSecondary,
            table: true);
        if (isRecommended)
        {
            AppendStatusText(
                DelayTableText.RecommendedMarker,
                UiPalette.Success,
                table: true);
        }

        AppendStatusText("\r\n", UiPalette.TextDefault, table: true);
    }

    private static double DelayMeters(double delayMilliseconds) =>
        Math.Abs(delayMilliseconds) * Acoustics.SpeedOfSoundAt20CMetersPerSecond / 1000.0;

    private static string FormatValueWithDelta(
        double value,
        double? reference,
        string valueFormat) =>
        DelayTableText.FormatValueWithDelta(value, reference, valueFormat);

    private void AppendLevelsLine(InputLevelMeterSnapshot levels)
    {
        AppendStatusText("Levels (peak/RMS dBFS): ", UiPalette.TextDefault);
        AppendLevelSegment("mic", levels.Microphone);
        AppendStatusText(", ", UiPalette.TextDefault);
        AppendLevelSegment("loop", levels.Loopback);
        AppendStatusText("\r\n", UiPalette.TextDefault);
    }

    private void AppendLevelSegment(string label, InputLevelMeterEntry entry)
    {
        if (!entry.Available)
        {
            AppendStatusText($"{label} unavailable", UiPalette.TextSecondary);
            return;
        }

        AppendStatusText(
            $"{label} {entry.PeakDbFs:0.0}/{entry.RmsDbFs:0.0}",
            UiPalette.TextDefault);
        if (entry.Clipped)
        {
            AppendStatusText(" CLIP", UiPalette.Error);
        }
        else if (entry.FullScaleReference)
        {
            AppendStatusText(" FULL SCALE", UiPalette.TextSecondary);
        }
    }

    private static string FormatConfidence(double confidenceDecibels)
    {
        if (confidenceDecibels >= 45)
        {
            return "Excellent";
        }
        if (confidenceDecibels >= 34)
        {
            return "Good";
        }
        if (confidenceDecibels >= 23)
        {
            return "Fair";
        }

        return "Poor";
    }

    private static Color GetConfidenceColor(string confidence) =>
        confidence switch
        {
            "Excellent" => UiPalette.Success,
            "Good" => UiPalette.Success,
            "Fair" => UiPalette.Warning,
            _ => UiPalette.Error
        };
}

internal readonly record struct TimeAlignmentReportSegment(string Text, Color Color, bool Table);
