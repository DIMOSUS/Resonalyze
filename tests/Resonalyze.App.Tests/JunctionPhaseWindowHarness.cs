using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Resonalyze.Dsp;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

/// <summary>
/// Reads <see cref="JunctionPhaseAlignment"/> through the steady-state window and the phase view's gated/FDW windows
/// over every archived cabin. A runner like <see cref="SessionBatteryHarness"/>: asserts only that every session was read.
/// </summary>
public sealed class JunctionPhaseWindowHarness(ITestOutputHelper output)
{
    public const string OutputVariable = "RESONALYZE_JUNCTION_PHASE_WINDOW_OUT";

    /// <summary>Null <see cref="Mode"/> is the production steady-state spectrum (full IR, origin at sample 0).</summary>
    /// <param name="Shared">One window for the whole set: separates window length from per-channel arrival estimates.</param>
    private sealed record Window(
        string Name, PhaseWindowMode? Mode, int Cycles, bool Shared = false);

    private static readonly Window[] Windows =
    [
        new("steady", null, 0),
        new("fixed", PhaseWindowMode.Fixed, 0),
        new("fdw4", PhaseWindowMode.FrequencyDependent, 4),
        new("fdw6", PhaseWindowMode.FrequencyDependent, 6),
        new("fdw8", PhaseWindowMode.FrequencyDependent, 8),
        new("fdw8s", PhaseWindowMode.FrequencyDependent, 8, Shared: true)
    ];

    // Only these windows are applied and judged by the summation-loss metric (run time).
    private static readonly string[] JudgedWindows = ["steady", "fdw8", "fdw8s"];

    private sealed record Reading(string Window, JunctionPhaseResult? Result);

    private sealed record JunctionRow(
        string Session,
        string Junction,
        double CrossoverHz,
        List<Reading> Readings,
        double SavedAverageDb,
        double? SavedDipDb,
        Dictionary<string, (double AverageDb, double? DipDb)> AfterFix);

    [SessionBatteryFact]
    public void CompareSteadyStateAgainstGatedWindows()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var report = new StringBuilder();
        var rows = new List<JunctionRow>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string session in SessionBatteryHarness.ResolveSessions(
            SessionBatteryHarness.RootDirectory!))
        {
            Assert.True(File.Exists(session), $"Session file not found: {session}");
            rows.AddRange(RunSession(session, report, seen));
        }

        Assert.NotEmpty(rows);
        WriteSummary(report, rows);
        string text = report.ToString();
        output.WriteLine(text);
        string path = Environment.GetEnvironmentVariable(OutputVariable)
            ?? Path.Combine(Path.GetTempPath(), "resonalyze-junction-phase-window.txt");
        File.WriteAllText(path, text);
        output.WriteLine($"Report written to {path}");
    }

    private static List<JunctionRow> RunSession(
        string sessionPath,
        StringBuilder report,
        Dictionary<string, string> seen)
    {
        VirtualCrossoverProjectFile project =
            VirtualCrossoverProjectFile.LoadFrom(sessionPath);
        List<VirtualCrossoverChannel> channels =
            SessionBatteryHarness.LoadChannels(project, out string fingerprint);
        string name = Path.GetFileName(Path.GetDirectoryName(sessionPath)!);
        report.AppendLine();
        report.AppendLine($"=== {name}  ({sessionPath})");
        if (seen.TryGetValue(fingerprint, out string? original))
        {
            report.AppendLine(
                $"  skipped: the same measurements and settings as {original}.");
            return [];
        }

        seen.Add(fingerprint, name);
        List<VirtualCrossoverChannel> participants = channels
            .Where(channel =>
                channel.Pair.Enabled &&
                !channel.Pair.Bypass &&
                channel.TransferImpulseResponse != null)
            .ToList();
        if (participants.Count < 2)
        {
            report.AppendLine(
                $"  skipped: {participants.Count} participating channel(s).");
            return [];
        }

        List<ProcessedChannel> saved = SessionBatteryHarness.Process(
            participants, _ => null);
        List<ProcessedChannel> ordered = ProcessedChannels.OrderByBand(saved);
        List<AdjacentPair> pairs = ProcessedChannels.GetAdjacentPairs(ordered);
        int sampleRate = ordered[0].SampleRate;
        VirtualCrossoverPhaseGateSettings gate =
            project.PhaseGateFor(project.ActiveSideRight);
        report.AppendLine(
            $"  side {(project.ActiveSideRight ? "R" : "L")}, {participants.Count} " +
            $"channels, {sampleRate} Hz, gate " +
            $"{gate.OffsetMs?.ToString("0.00", CultureInfo.InvariantCulture) ?? "auto"} " +
            $"+ {project.PhaseGateLeftMs:0.#}/{project.PhaseGatePlateauMs:0.#}/" +
            $"{project.PhaseGateRightMs:0.#} ms, " +
            $"steady window {JunctionPhaseAlignment.AnalysisSamplesFor(sampleRate) * 1_000.0 / sampleRate:0} ms " +
            $"/ FFT {JunctionPhaseAlignment.AnalysisLengthFor(sampleRate)}, " +
            $"gated FFT {DataHelper.GatedFftLength}");
        if (pairs.Count == 0)
        {
            report.AppendLine("  no junction in the set.");
            return [];
        }

        // Timed cold and warm: this runs on the UI thread inside the redraw.
        var spectra = new Dictionary<string, List<Complex[]>>(StringComparer.Ordinal);
        var cold = new Dictionary<string, double>(StringComparer.Ordinal);
        var warm = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (Window window in Windows)
        {
            var stopwatch = Stopwatch.StartNew();
            spectra[window.Name] = BuildSpectra(
                ordered, project, window, sampleRate, report);
            cold[window.Name] = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Restart();
            BuildSpectra(ordered, project, window, sampleRate, null);
            warm[window.Name] = stopwatch.Elapsed.TotalMilliseconds;
        }

        report.AppendLine(
            $"  spectra for {ordered.Count} channels, ms cold/warm: " +
            string.Join("  ", Windows.Select(window =>
                $"{window.Name} {cold[window.Name]:0.0}/{warm[window.Name]:0.0}")));

        using (var coordinator = new VirtualCrossoverProcessingCoordinator())
        {
            var metrics = new VirtualCrossoverMetrics(
                coordinator,
                (_, _, _, _, _) => throw new InvalidOperationException(
                    "the junction phase block reads no magnitude curve"));
            List<VirtualCrossoverMetric.PhaseEntry> shipped =
                metrics.BuildPhaseEntries(
                    saved,
                    channels => JunctionPhaseSpectra.Build(
                        channels,
                        sampleRate,
                        gate.OffsetMs,
                        project.PhaseGateLeftMs,
                        project.PhaseGatePlateauMs,
                        project.PhaseGateRightMs));
            report.AppendLine();
            foreach (string line in VirtualCrossoverMetric
                .FormatPhaseCompact(shipped).Split("\r\n"))
            {
                report.AppendLine("  | " + line);
            }
        }

        List<VirtualCrossoverMetric.Entry> savedEntries =
            SessionBatteryHarness.Judge(project, saved);
        var rows = new List<JunctionRow>();
        foreach (AdjacentPair pair in pairs)
        {
            string junction =
                $"{pair.Lower.Channel.Name}/{pair.Upper.Channel.Name}";
            // By reference: processed channels are records and equal values would collide.
            int lower = IndexOf(ordered, pair.Lower);
            int upper = IndexOf(ordered, pair.Upper);
            report.AppendLine();
            report.AppendLine(
                $"  {junction}  fc {pair.CrossoverHz:0.#} Hz  " +
                $"band {pair.BandLowHz:0.#}-{pair.BandHighHz:0.#} Hz");
            // PHAT lags correct the UPPER channel, so the junction fix (delay on the LOWER one) compares to the negated lag.
            JunctionCorrelationView correlation =
                JunctionViews.BuildCorrelationView(pair, ordered);
            if (correlation.WhitenedDirect.Count > 0)
            {
                SignalPoint best = correlation.WhitenedDirect
                    .MaxBy(point => Math.Abs(point.Y));
                SignalPoint near = correlation.WhitenedDirect
                    .Where(point => Math.Abs(point.X) <= 500.0 / pair.CrossoverHz)
                    .DefaultIfEmpty(best)
                    .MaxBy(point => Math.Abs(point.Y));
                report.AppendLine(
                    $"    direct-PHAT: on-lobe r {near.Y:+0.00;-0.00;0.00} @ " +
                    $"{near.X:+0.000;-0.000;0.000} ms (fix would be " +
                    $"{-near.X:+0.000;-0.000;0.000}); best r " +
                    $"{best.Y:+0.00;-0.00;0.00} @ {best.X:+0.000;-0.000;0.000} ms");
            }

            report.AppendLine(
                "    window       φfc    R    fix ms  pol   score    best  " +
                "opp    fit ms  rms°");

            var readings = new List<Reading>();
            foreach (Window window in Windows)
            {
                List<Complex[]> set = spectra[window.Name];
                JunctionPhaseResult? result =
                    JunctionPhaseAlignment.AnalyzeWindowedSpectra(
                        set[lower], set[upper], sampleRate,
                        pair.CrossoverHz, pair.BandLowHz, pair.BandHighHz);
                readings.Add(new Reading(window.Name, result));
                report.AppendLine("    " + Describe(window.Name, result));
            }

            VirtualCrossoverMetric.Entry? savedEntry = savedEntries
                .Cast<VirtualCrossoverMetric.Entry?>()
                .FirstOrDefault(entry =>
                    entry!.Value.Junction == junction && !entry.Value.IsTotal);
            if (savedEntry is not { } baseline)
            {
                continue;
            }

            var afterFix = new Dictionary<string, (double, double?)>(
                StringComparer.Ordinal);
            foreach (string windowName in JudgedWindows)
            {
                if (readings.First(item => item.Window == windowName).Result
                    is not { } advice)
                {
                    continue;
                }

                VirtualCrossoverChannel lowerChannel = pair.Lower.Channel;
                VirtualCrossoverChannelSettings settings =
                    lowerChannel.SideSettings(lowerChannel.ActiveRight);
                List<ProcessedChannel> fixedSet = SessionBatteryHarness.Process(
                    participants,
                    channel => ReferenceEquals(channel, lowerChannel)
                        ? new AlignmentOverride(
                            settings.DelayMs + advice.BestExtraDelayMs,
                            settings.InvertPolarity ^ advice.BestInvert)
                        : (AlignmentOverride?)null);
                VirtualCrossoverMetric.Entry? entry =
                    SessionBatteryHarness.Judge(project, fixedSet)
                        .Cast<VirtualCrossoverMetric.Entry?>()
                        .FirstOrDefault(item =>
                            item!.Value.Junction == junction && !item.Value.IsTotal);
                if (entry is { } judged)
                {
                    afterFix[windowName] = (judged.AverageDb, judged.DipDb);
                }
            }

            report.AppendLine(
                $"    sum loss avg/dip: saved {baseline.AverageDb,6:0.00} / " +
                $"{Format(baseline.DipDb),-6}" +
                string.Concat(JudgedWindows.Select(windowName =>
                    afterFix.TryGetValue(windowName, out (double Avg, double? Dip) after)
                        ? $"   {windowName}-fix {after.Avg,6:0.00} / " +
                          $"{Format(after.Dip),-6} " +
                          $"(Δ{after.Avg - baseline.AverageDb:+0.00;-0.00;0.00})"
                        : $"   {windowName}-fix —")));

            rows.Add(new JunctionRow(
                name, junction, pair.CrossoverHz, readings,
                baseline.AverageDb, baseline.DipDb,
                afterFix.ToDictionary(
                    item => item.Key, item => item.Value, StringComparer.Ordinal)));
        }

        return rows;
    }

    private static List<Complex[]> BuildSpectra(
        List<ProcessedChannel> ordered,
        VirtualCrossoverProjectFile project,
        Window window,
        int sampleRate,
        StringBuilder? report)
    {
        if (window.Mode is null)
        {
            return ordered
                .Select(item => JunctionPhaseAlignment.BuildAnalysisSpectrum(
                    item.ImpulseResponse, item.SampleRate))
                .ToList();
        }

        // The shipped window goes through the shipped builder, so this row measures the read-out itself.
        if (window is { Mode: PhaseWindowMode.FrequencyDependent, Shared: false } &&
            window.Cycles == JunctionPhaseSpectra.FdwCycles)
        {
            return JunctionPhaseSpectra.Build(
                ordered,
                sampleRate,
                project.PhaseGateFor(project.ActiveSideRight).OffsetMs,
                project.PhaseGateLeftMs,
                project.PhaseGatePlateauMs,
                project.PhaseGateRightMs);
        }

        double? pinned = project.PhaseGateFor(project.ActiveSideRight).OffsetMs;
        IReadOnlyList<PlacementChannel> placement = PlacementChannel.From(ordered);
        double sharedOffsetMs = PhaseGatePlacement.ResolveSharedOffsetMs(
            placement, sampleRate, pinned);
        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            placement,
            sharedOffsetMs,
            sampleRate,
            window.Shared ? sharedOffsetMs : pinned,
            project.PhaseGateLeftMs,
            project.PhaseGatePlateauMs,
            project.PhaseGateRightMs);
        var template = new PhaseAnalysisSettings(
            window.Mode.Value,
            window.Cycles == 0 ? PhaseAnalysisSettings.DefaultFdwCycles : window.Cycles,
            // No detrend: per-channel detrend would be the very answer being measured.
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: 0.0,
            project.PhaseGateLeftMs,
            project.PhaseGatePlateauMs,
            project.PhaseGateRightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

        var spectra = new List<Complex[]>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            Complex[] gated = DataHelper.GetPhaseAnalysisSpectrum(
                new ImpulseMeasurementView(ordered[i].ImpulseResponse, 0, sampleRate),
                template with { GateOffsetMs = offsets[i] },
                out int extractionStart);
            // Re-referenced to sample 0, or a per-curve window reads its own placement as the junction delay.
            spectra.Add(DataHelper.SumGatedSpectra([(gated, extractionStart)], 0));
        }

        if (report != null && (window.Name == Windows[1].Name || window.Shared))
        {
            report.AppendLine(
                $"  gate offsets ({window.Name}, ms): " + string.Join(", ", ordered.Select(
                    (item, index) =>
                        $"{item.Channel.Name} {offsets[index]:0.00}")) +
                (offsets.Distinct().Count() == 1
                    ? "   (one shared window)"
                    : "   (per curve)"));
        }

        return spectra;
    }

    private static string Describe(string window, JunctionPhaseResult? result)
    {
        if (result is not { } value)
        {
            return $"{window,-9}  (no reading: too few gated bins, or fc out of range)";
        }

        string phase = value.PhaseConsistency >=
            JunctionPhaseAlignment.MinimumPhaseConsistency
            ? $"{value.PhaseAtCrossoverDeg,5:+0;-0;0}°"
            : $"{value.PhaseAtCrossoverDeg,5:+0;-0;0}?";
        string polarity = value.BestInvert
            ? "INV"
            : value.BestScore - value.OppositePolarityScore <
                JunctionPhaseAlignment.PolarityFlipAdvantage ? " ~ " : "   ";
        return
            $"{window,-9} {phase} {value.PhaseConsistency,5:0.00} " +
            $"{value.BestExtraDelayMs,8:+0.000;-0.000;0.000} {polarity} " +
            $"{value.CurrentScore,7:0.000;-0.000;0.000} " +
            $"{value.BestScore,7:0.000;-0.000;0.000} " +
            $"{value.OppositePolarityScore,6:0.00;-0.00;0.00} " +
            $"{value.FitDelayMs,9:+0.000;-0.000;0.000} {value.FitRmsDeg,5:0}";
    }

    private static string Format(double? value) =>
        value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-";

    private static void WriteSummary(StringBuilder report, List<JunctionRow> rows)
    {
        report.AppendLine();
        report.AppendLine("=== per-junction summary (saved tuning)");
        report.AppendLine(
            "  session          junction   fc Hz  " +
            string.Concat(Windows.Select(window => $"{window.Name,8} φ/fix ")) +
            "   Δsum-loss steady / fdw8");
        foreach (JunctionRow row in rows)
        {
            report.AppendLine(
                $"  {row.Session,-16} {row.Junction,-10} {row.CrossoverHz,6:0} " +
                string.Concat(row.Readings.Select(reading =>
                    reading.Result is { } value
                        ? $"{value.PhaseAtCrossoverDeg,5:+0;-0;0}/" +
                          $"{value.BestExtraDelayMs,-8:+0.000;-0.000;0.000}"
                        : $"{"—",5}/{"—",-8}")) +
                string.Concat(JudgedWindows.Select(window =>
                    row.AfterFix.TryGetValue(window, out (double Avg, double? Dip) after)
                        ? $"  {after.Avg - row.SavedAverageDb,7:+0.00;-0.00;0.00}"
                        : $"  {"—",7}")));
        }

        report.AppendLine();
        report.AppendLine("=== how the windows disagree, across every junction");
        report.AppendLine(
            "  window     n   |Δφ vs steady|      |Δfix vs steady| ms      " +
            "median |fix|   R<0.5   polarity differs");
        foreach (Window window in Windows)
        {
            List<(JunctionPhaseResult Steady, JunctionPhaseResult Other)> paired = rows
                .Select(row => (
                    Steady: row.Readings.First(item => item.Window == "steady").Result,
                    Other: row.Readings.First(item => item.Window == window.Name).Result))
                .Where(item => item.Steady is not null && item.Other is not null)
                .Select(item => (item.Steady!, item.Other!))
                .ToList();
            if (paired.Count == 0)
            {
                report.AppendLine($"  {window.Name,-9}   0");
                continue;
            }

            List<double> phaseDeltas = paired
                .Select(item => Math.Abs(Wrap(
                    item.Other.PhaseAtCrossoverDeg - item.Steady.PhaseAtCrossoverDeg)))
                .ToList();
            List<double> fixDeltas = paired
                .Select(item => Math.Abs(
                    item.Other.BestExtraDelayMs - item.Steady.BestExtraDelayMs))
                .ToList();
            report.AppendLine(
                $"  {window.Name,-9} {paired.Count,3}   " +
                $"med {Median(phaseDeltas),5:0}°  max {phaseDeltas.Max(),5:0}°   " +
                $"med {Median(fixDeltas),7:0.000}  max {fixDeltas.Max(),7:0.000}   " +
                $"{Median(paired.Select(item => Math.Abs(item.Other.BestExtraDelayMs)).ToList()),8:0.000}   " +
                $"{paired.Count(item => item.Other.PhaseConsistency < JunctionPhaseAlignment.MinimumPhaseConsistency),5}   " +
                $"{paired.Count(item => item.Other.BestInvert != item.Steady.BestInvert),9}");
        }

        report.AppendLine();
        report.AppendLine(
            "=== the ceiling each window allows, split by junction band");
        report.AppendLine(
            "  BestScore is the highest in-phase score ANY delay reaches over the " +
            "band: a ceiling");
        report.AppendLine(
            "  well under 1 is the window's own decorrelation, not a misalignment " +
            "a fix can reach.");
        report.AppendLine(
            "  band              n  " +
            string.Concat(Windows.Select(window => $"{window.Name,8} med ")) +
            "  fdw8 better/same/worse vs steady");
        foreach ((string label, Func<double, bool> inBand) in new (string, Func<double, bool>)[]
        {
            ("fc < 500 Hz", hz => hz < 500.0),
            ("fc >= 1000 Hz", hz => hz >= 1_000.0)
        })
        {
            List<JunctionRow> band = rows.Where(row => inBand(row.CrossoverHz)).ToList();
            if (band.Count == 0)
            {
                continue;
            }

            List<double> steady = Ceilings(band, "steady");
            List<double> fdw8 = Ceilings(band, "fdw8");
            int better = 0, same = 0, worse = 0;
            for (int i = 0; i < Math.Min(steady.Count, fdw8.Count); i++)
            {
                if (fdw8[i] > steady[i] + 0.02) better++;
                else if (fdw8[i] < steady[i] - 0.02) worse++;
                else same++;
            }

            report.AppendLine(
                $"  {label,-16} {band.Count,2}  " +
                string.Concat(Windows.Select(window =>
                {
                    List<double> ceilings = Ceilings(band, window.Name);
                    return ceilings.Count == 0
                        ? $"{"—",8}     "
                        : $"{Median(ceilings),8:0.000}     ";
                })) +
                $"  {better} / {same} / {worse}");
        }

        report.AppendLine();
        report.AppendLine(
            "=== calibrating the fix guard: suppress the fix below a BestScore " +
            "threshold (fdw8)");
        report.AppendLine(
            "  A junction whose ceiling is low cannot be brought into phase by ANY " +
            "delay, so its");
        report.AppendLine(
            "  fix is not a recommendation. Judged against what applying that fix " +
            "did to the");
        report.AppendLine(
            "  panel's own sum loss: suppressing a fix that HURT is a catch, " +
            "suppressing one that");
        report.AppendLine("  HELPED is a loss.");
        report.AppendLine(
            "  threshold   suppressed   of them harmful / neutral / helpful");
        for (double threshold = 0.30; threshold <= 0.801; threshold += 0.05)
        {
            List<JunctionRow> suppressed = rows
                .Where(row => row.Readings
                    .First(reading => reading.Window == "fdw8").Result
                    is { } value && value.BestScore < threshold)
                .ToList();
            List<double> deltas = suppressed
                .Where(row => row.AfterFix.ContainsKey("fdw8"))
                .Select(row => row.AfterFix["fdw8"].AverageDb - row.SavedAverageDb)
                .ToList();
            report.AppendLine(
                $"  {threshold,9:0.00}   {suppressed.Count,10}   " +
                $"{deltas.Count(value => value < -0.01),9} / " +
                $"{deltas.Count(value => Math.Abs(value) <= 0.01),7} / " +
                $"{deltas.Count(value => value > 0.01),7}" +
                (suppressed.Count == 0
                    ? string.Empty
                    : "   " + string.Join(", ", suppressed.Select(row =>
                        $"{row.Session} {row.Junction}"))));
        }

        report.AppendLine();
        report.AppendLine(
            "=== what the panel's metric says about each judged window's advice");
        report.AppendLine(
            "  window     n   Δsum-loss avg (dB, + = better)      improved / unchanged / worse");
        foreach (string window in JudgedWindows)
        {
            List<double> deltas = rows
                .Where(row => row.AfterFix.ContainsKey(window))
                .Select(row => row.AfterFix[window].AverageDb - row.SavedAverageDb)
                .ToList();
            if (deltas.Count == 0)
            {
                report.AppendLine($"  {window,-9}   0");
                continue;
            }

            report.AppendLine(
                $"  {window,-9} {deltas.Count,3}   " +
                $"mean {deltas.Average(),6:+0.00;-0.00;0.00}  " +
                $"med {Median(deltas),6:+0.00;-0.00;0.00}  " +
                $"best {deltas.Max(),6:+0.00;-0.00;0.00}  " +
                $"worst {deltas.Min(),6:+0.00;-0.00;0.00}      " +
                $"{deltas.Count(value => value > 0.01),3} / " +
                $"{deltas.Count(value => Math.Abs(value) <= 0.01),3} / " +
                $"{deltas.Count(value => value < -0.01),3}");
        }
    }

    private static int IndexOf(List<ProcessedChannel> ordered, ProcessedChannel item)
    {
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ReferenceEquals(ordered[i], item))
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            "The junction names a channel that is not in the ordered set.");
    }

    private static List<double> Ceilings(List<JunctionRow> rows, string window) =>
        rows
            .Select(row => row.Readings
                .First(reading => reading.Window == window).Result?.BestScore)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

    private static double Wrap(double degrees) =>
        degrees - 360.0 * Math.Round(degrees / 360.0);

    private static double Median(List<double> values)
    {
        List<double> sorted = values.Order().ToList();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : 0.5 * (sorted[middle - 1] + sorted[middle]);
    }
}
