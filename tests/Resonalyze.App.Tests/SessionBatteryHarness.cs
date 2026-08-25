using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.History;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that reports the battery as SKIPPED, with a
/// reason, unless a folder of archived Virtual DSP sessions is named in the
/// environment. The measurements are field records that do not live in the
/// repository (and must not): CI has no folder to point at, so the runner is
/// skipped there and runs only on a machine that carries the cabins.
/// </summary>
public sealed class SessionBatteryFactAttribute : FactAttribute
{
    public const string RootVariable = "RESONALYZE_SESSION_BATTERY";

    public SessionBatteryFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(SessionBatteryHarness.RootDirectory))
        {
            Skip =
                $"Set {RootVariable} to the folder holding the archived cabins " +
                "(each session's measurements beside its session file) to run " +
                "the Auto delay battery.";
        }
    }
}

/// <summary>
/// The Auto delay battery: every archived cabin's session is loaded exactly as
/// the tool loads it, the Auto delay proposal is computed on the session's own
/// chains, and BOTH the proposal and the session's SAVED settings are judged by
/// the panel's own metric (<see cref="VirtualCrossoverMetrics.BuildCurves"/> +
/// <see cref="VirtualCrossoverMetrics.BuildEntries"/>: the per-junction average
/// summation loss and its dip, read through the session's own gate and
/// smoothing).
/// <para>
/// It exists because the engine's alignment rules cannot be judged by
/// |optimum − PHAT| on a low junction: that figure is noisy, and it answers a
/// question about one probe rather than about the read-out the tuner actually
/// looks at. The saved settings are the owner's own tuning, so a rule change
/// that moves the proposal TOWARD them on the metric is an improvement in the
/// only currency the panel reports.
/// </para>
/// <para>
/// Output goes to the test output and to a text file (see
/// <see cref="OutputVariable"/>), one fixed-format ROW line per junction, so
/// two builds can be diffed line by line.
/// </para>
/// <para>
/// This is a RUNNER, not a pinned expectation: it asserts only that every named
/// session was found, loaded and judged. What its numbers mean is a reading, and
/// the reading belongs in the branch's notes — not in an assert that would
/// freeze one build's arithmetic into the suite.
/// </para>
/// </summary>
public sealed class SessionBatteryHarness(ITestOutputHelper output)
{
    /// <summary>
    /// Where the report is written; defaults to a file in the temporary folder.
    /// The archive folder is the owner's measurement data — the battery reads
    /// it and writes nothing into it.
    /// </summary>
    public const string OutputVariable = "RESONALYZE_SESSION_BATTERY_OUT";

    /// <summary>
    /// A semicolon-separated list of session files to run instead of the
    /// default set, each either absolute or relative to the root.
    /// </summary>
    public const string SessionsVariable = "RESONALYZE_SESSION_BATTERY_SESSIONS";

    /// <summary>
    /// Set to judge every session through the gate dialog's AUTO placement
    /// (each set of settings windowed at its own earliest front) instead of
    /// through the offset the session pinned.
    /// <para>
    /// A pinned gate is one ABSOLUTE window, so it does not follow a proposal
    /// that moves a channel: on the archived v5 cabin the pin opens the window
    /// at 10.06 ms and the saved tuning puts the mid and tweeter fronts at
    /// 18.4/18.1 ms — inside the plateau — while the proposal brings them to
    /// 12.6/12.4 ms, inside the window's own fade-in. Their level in the sum is
    /// then set by the window, and the metric compares a windowing artifact.
    /// The pinned reading is what the panel shows before the gate is re-placed,
    /// so it stays the default; this switch is how the SAME comparison is read
    /// with the window off the scales.
    /// </para>
    /// </summary>
    public const string AutoGateVariable = "RESONALYZE_SESSION_BATTERY_AUTOGATE";

    // The archived cabins, in the order the branch's notes list them. Each entry
    // is a path under the root; the session file's own folder is what its
    // measurements are resolved against (VirtualCrossoverSourceLocator), so a
    // session whose stored absolute paths have gone stale still loads.
    // The reference car is v5_exp — the owner deleted the older v5 session of
    // the same car (2026-08-20) and distrusts v2's tuning, so a verdict that
    // rests on either of those alone is not a verdict.
    private static readonly string[] DefaultSessions =
    [
        @"3RC\virtual-dsp-session.json",
        @"Passat\virtual-dsp-session.json",
        // The same car re-measured and re-tuned by the owner (2026-08-20).
        // Its A/B junction is the conviction dead zone's field case: the
        // sub's band arrival latches 1.7 allowances late and only the comb
        // arbitration lands the pair on the owner's inverted earlier lobe.
        @"Passat v2\virtual-dsp-session-sq-v10-7-opt.json",
        @"v2\virtual-dsp-session.json",
        @"v2\head_90_grad\virtual-dsp-session.json",
        @"v3\virtual-dsp-session.json",
        @"v4\virtual-dsp-session.json",
        // manual-2 supersedes manual: the owner's 2026-08-20 re-tune, made
        // after auditioning the engine's proposal (it adopts the proposal's
        // bass/mid lobe and polarity structure and re-tunes around them).
        @"v5_exp\virtual-dsp-session-manual-2.json"
    ];

    internal static string? RootDirectory =>
        Environment.GetEnvironmentVariable(SessionBatteryFactAttribute.RootVariable);

    private static bool AutoGate =>
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(AutoGateVariable));

    [SessionBatteryFact]
    public void JudgeAutoDelayAgainstSavedSettings()
    {
        string root = RootDirectory!;
        // One locale for the report whatever the machine runs in: the tables
        // are compared between builds line by line, and a decimal comma on one
        // machine against a point on another is a diff on every row.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var report = new StringBuilder();
        var summary = new List<JunctionComparison>();
        // Two of the archived session files describe the SAME cabin (the v2
        // folder holds no measurements of its own: its session points at the
        // head_90_grad records, so the two files are the same session saved
        // twice). Judging both would count that cabin twice in the summary, so
        // a session whose resolved measurements and saved settings are already
        // in the battery is named and skipped.
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string session in ResolveSessions(root))
        {
            Assert.True(File.Exists(session), $"Session file not found: {session}");
            summary.AddRange(RunSession(session, report, seen));
        }

        Assert.NotEmpty(summary);
        WriteSummary(report, summary);
        string text = report.ToString();
        output.WriteLine(text);
        string reportPath = Environment.GetEnvironmentVariable(OutputVariable)
            ?? Path.Combine(Path.GetTempPath(), "resonalyze-session-battery.txt");
        File.WriteAllText(reportPath, text);
        output.WriteLine($"Report written to {reportPath}");
    }

    private static IEnumerable<string> ResolveSessions(string root)
    {
        string? configured = Environment.GetEnvironmentVariable(SessionsVariable);
        IEnumerable<string> names = string.IsNullOrWhiteSpace(configured)
            ? DefaultSessions
            : configured.Split(';', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        return names.Select(name =>
            Path.IsPathFullyQualified(name) ? name : Path.Combine(root, name));
    }

    // One cabin: load, propose, judge. Every number printed here is read off the
    // same code paths the panel runs — the chains through
    // VirtualCrossoverChannelSettings.ToChain, the proposal through
    // AutoAlignmentEngine, the metric through VirtualCrossoverMetrics — so a
    // disagreement with the tool on screen is a defect in one of them, not in
    // the harness's own arithmetic.
    private List<JunctionComparison> RunSession(
        string sessionPath,
        StringBuilder report,
        Dictionary<string, string> seen)
    {
        var stopwatch = Stopwatch.StartNew();
        VirtualCrossoverProjectFile project = VirtualCrossoverProjectFile.LoadFrom(sessionPath);
        bool rightSide = project.ActiveSideRight;
        List<VirtualCrossoverChannel> channels = LoadChannels(project, out string fingerprint);
        string name = Path.GetFileName(Path.GetDirectoryName(sessionPath)!);
        if (seen.TryGetValue(fingerprint, out string? original))
        {
            report.AppendLine();
            report.AppendLine($"=== {name}  ({sessionPath})");
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
        report.AppendLine();
        report.AppendLine($"=== {name}  ({sessionPath})");
        if (participants.Count < 2)
        {
            report.AppendLine(
                $"  skipped: {participants.Count} participating channel(s) resolved " +
                "on the active side.");
            return [];
        }

        VirtualCrossoverPhaseGateSettings gate = project.PhaseGateFor(rightSide);
        report.AppendLine(
            $"  side {(rightSide ? "R" : "L")}, {participants.Count} channels, " +
            $"{participants[0].SampleRate} Hz, gate " +
            (AutoGate && gate.OffsetMs is { } ignoredPin
                ? FormattableString.Invariant($"auto (pin {ignoredPin:0.00} ignored) ")
                : $"{gate.OffsetMs?.ToString(
                    "0.00", CultureInfo.InvariantCulture) ?? "auto"} ") +
            $"+ {project.PhaseGateLeftMs:0.#}/{project.PhaseGatePlateauMs:0.#}/" +
            $"{project.PhaseGateRightMs:0.#} ms, smoothing " +
            $"{OverlaySmoothing.GetLabel(project.SmoothingCode)}");

        var log = new StringBuilder();
        Dictionary<IAlignmentChannel, AlignmentOverride> proposal =
            ComputeProposal(participants, log);

        // The judged sets differ ONLY in delay and polarity: gains, filters and
        // PEQ stay the session's own, on both sides of the comparison, because
        // the proposal moves nothing else.
        List<ProcessedChannel> saved = Process(participants, _ => null);
        List<ProcessedChannel> proposed = Process(
            participants,
            channel => proposal.GetValueOrDefault(channel));
        foreach (VirtualCrossoverChannel channel in participants)
        {
            AlignmentOverride over = proposal.GetValueOrDefault(channel);
            report.AppendLine(
                $"    {channel.Name} {Describe(channel.Settings.DisplayName),-24} " +
                $"saved {channel.Settings.DelayMs,7:0.00} ms " +
                $"{(channel.Settings.InvertPolarity ? "INV" : "   ")}    " +
                $"proposed {over.DelayMs,7:0.00} ms " +
                $"{(over.InvertPolarity ? "INV" : "   ")}");
        }

        // Where each set's fronts land against the window they are judged
        // through. A PINNED gate is one absolute window for both sets, so a
        // proposal that moves a channel far enough can put its own front into
        // the window's left fade — and then the metric is reading the window,
        // not the alignment. Printed so that bias is visible instead of
        // silently deciding a comparison.
        double leftEdgeMs = !AutoGate && gate.OffsetMs is { } pinned
            ? pinned - project.PhaseGateLeftMs
            : double.NaN;
        report.AppendLine(
            "    fronts (ms): " + string.Join(", ", saved.Select((item, index) =>
                $"{item.Channel.Name} {FrontMs(item):0.00}->" +
                $"{FrontMs(proposed[index]):0.00}")) +
            (double.IsNaN(leftEdgeMs)
                ? "   (gate follows the earliest front)"
                : $"   (window opens {leftEdgeMs:0.00} ms, plateau to " +
                  $"{gate.OffsetMs!.Value + project.PhaseGatePlateauMs:0.00} ms)"));

        // The direct-sound whitened correlation of each junction, for the
        // saved tune and the proposal — the read the correlation view's
        // "PHAT direct" curve draws and the engine's direct-coherence witness
        // weighs. Per junction: the extremum nearest zero lag (the lobe the
        // setting sits on) and the curve's global extremum. A signed r:
        // negative means the coherent alignment is the INVERTED one.
        void DirectPhat(string label, List<ProcessedChannel> set)
        {
            foreach (AdjacentPair pair in ProcessedChannels.GetAdjacentPairs(
                ProcessedChannels.OrderByBand(set)))
            {
                JunctionCorrelationView view =
                    VirtualCrossoverPanel.BuildCorrelationView(pair, set);
                if (view.WhitenedDirect.Count == 0)
                {
                    continue;
                }

                SignalPoint best = view.WhitenedDirect
                    .MaxBy(point => Math.Abs(point.Y));
                SignalPoint near = view.WhitenedDirect
                    .Where(point => Math.Abs(point.X) <= 500.0 / pair.CrossoverHz)
                    .DefaultIfEmpty(best)
                    .MaxBy(point => Math.Abs(point.Y));
                // The runner already pinned the invariant culture.
                report.AppendLine(
                    $"    direct-PHAT {label,-8} " +
                    $"{pair.Lower.Channel.Name}/{pair.Upper.Channel.Name}: " +
                    $"on-lobe r {near.Y:+0.00;-0.00} @ {near.X:+0.00;-0.00} ms; " +
                    $"best r {best.Y:+0.00;-0.00} @ {best.X:+0.00;-0.00} ms");
            }
        }

        DirectPhat("saved", saved);
        DirectPhat("proposed", proposed);

        List<VirtualCrossoverMetric.Entry> savedEntries = Judge(project, saved);
        List<VirtualCrossoverMetric.Entry> proposedEntries = Judge(project, proposed);
        var comparisons = new List<JunctionComparison>();
        report.AppendLine(
            "    junction              band Hz        saved avg/dip     " +
            "proposed avg/dip      Δavg    Δdip");
        foreach (VirtualCrossoverMetric.Entry savedEntry in savedEntries)
        {
            // Matched by junction label: the two sets carry the same channels in
            // the same band order (only their delays differ), so the labels line
            // up one to one.
            VirtualCrossoverMetric.Entry? match = proposedEntries
                .Cast<VirtualCrossoverMetric.Entry?>()
                .FirstOrDefault(entry => entry!.Value.Junction == savedEntry.Junction);
            if (match is not { } proposedEntry)
            {
                continue;
            }

            var comparison = new JunctionComparison(
                name,
                savedEntry.Junction,
                savedEntry.IsTotal,
                savedEntry.LowHz,
                savedEntry.HighHz,
                savedEntry.AverageDb,
                savedEntry.DipDb,
                proposedEntry.AverageDb,
                proposedEntry.DipDb);
            comparisons.Add(comparison);
            report.AppendLine(
                $"    {savedEntry.Junction,-18} {savedEntry.LowHz,6:0} - " +
                $"{savedEntry.HighHz,-6:0}  {savedEntry.AverageDb,7:0.00} / " +
                $"{Format(savedEntry.DipDb),-7}  {proposedEntry.AverageDb,7:0.00} / " +
                $"{Format(proposedEntry.DipDb),-7}  {comparison.AverageDelta,7:+0.00;-0.00} " +
                $"{Format(comparison.DipDelta, "+0.00;-0.00"),7}");
            report.AppendLine(comparison.Row());
        }

        foreach (string line in log.ToString().Split('\n'))
        {
            if (line.Contains("latch") || line.Contains("veto") ||
                line.Contains("re-anchored") || line.Contains("lobe hop") ||
                line.Contains("direct coherence") || line.Contains("arbitration"))
            {
                report.AppendLine("    | " + line.Trim());
            }
        }

        report.AppendLine($"    ({stopwatch.Elapsed.TotalSeconds:0.0} s)");
        return comparisons;
    }

    private static double FrontMs(ProcessedChannel item) =>
        ProcessedChannels.StartAnchorIndex(
            item.ImpulseResponse, item.PeakIndex, item.Channel.SampleRate,
            item.ValidRange)
        * 1_000.0 / item.Channel.SampleRate;

    private static string Describe(string displayName) =>
        string.IsNullOrWhiteSpace(displayName)
            ? "(unnamed)"
            : displayName.Length > 24 ? displayName[..24] : displayName;

    private static string Format(double? value, string format = "0.00") =>
        value?.ToString(format, CultureInfo.InvariantCulture) ?? "-";

    // The panel's single-side Auto delay, verbatim: order along the spectrum by
    // band center, one shared direct-sound crop, crosstalk heads cleaned, then
    // the engine's cascade over the adjacent junctions. The session's own
    // delays and polarities are deliberately NOT fed in — the run ignores them,
    // exactly as the tool's own run does.
    private static Dictionary<IAlignmentChannel, AlignmentOverride> ComputeProposal(
        List<VirtualCrossoverChannel> participants,
        StringBuilder log)
    {
        List<VirtualCrossoverChannel> ordered = participants
            .OrderBy(channel => VirtualCrossoverJunctions.BandCenterHz(channel.Settings))
            .ToList();
        var reprocessor = new AlignmentReprocessor(
            CleanCrosstalkHeads(
                ordered.Select(channel => new AlignmentReprocessInput(
                    channel,
                    channel.TransferImpulseResponse!,
                    channel.SampleRate,
                    channel.Settings.ToChain())).ToList(),
                log));
        IReadOnlyList<AlignmentSnapshot> initial = reprocessor.Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        var snapshots = ordered
            .Select((channel, i) => (channel, snapshot: initial[i]))
            .ToDictionary(item => item.channel, item => item.snapshot);
        var junctions = new List<AlignmentJunction>();
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            double pairHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                ordered[i].Settings, ordered[i + 1].Settings);
            (double bandLowHz, double bandHighHz) =
                VirtualCrossoverJunctions.OverlapBand(pairHz);
            junctions.Add(new AlignmentJunction(
                snapshots[ordered[i]], snapshots[ordered[i + 1]],
                pairHz, bandLowHz, bandHighHz));
        }

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            ordered.Select(channel => snapshots[channel]).ToList(),
            junctions,
            reprocessor.Reprocess,
            alignment,
            log,
            new Dictionary<IAlignmentChannel, AlignmentDecision>());
        return alignment;
    }

    // The panel's head gate for the playback-crosstalk click (an electrical copy
    // of the playback ahead of any acoustic arrival, present in every record of
    // the v3 session): without it the battery would judge a search the tool
    // never runs.
    private static List<AlignmentReprocessInput> CleanCrosstalkHeads(
        List<AlignmentReprocessInput> inputs,
        StringBuilder log) =>
        inputs.Select(input =>
        {
            double[] real = Array.ConvertAll(
                input.MeasuredImpulseResponse, sample => sample.Real);
            if (TransferIrDiagnostics.DetectCrosstalkHead(real, input.SampleRate)
                is not { } convicted)
            {
                return input;
            }

            log.AppendLine(
                $"{input.Channel.Name}: playback-crosstalk click at " +
                $"{convicted.BurstTimeMs:0.00} ms removed before the search");
            return input with
            {
                MeasuredImpulseResponse = TransferIrDiagnostics.CleanCrosstalkHead(
                    input.MeasuredImpulseResponse, input.SampleRate, convicted)
            };
        }).ToList();

    // The panel's redraw, reduced to what the metric reads: each channel's
    // source run through its chain (the session's, or the session's with the
    // proposal's delay and polarity on top) by the same snapshot the tool
    // processes through, so the head crop and the FFT are the tool's.
    private static List<ProcessedChannel> Process(
        List<VirtualCrossoverChannel> participants,
        Func<VirtualCrossoverChannel, AlignmentOverride?> overrideFor)
    {
        var processed = new List<ProcessedChannel>(participants.Count);
        foreach (VirtualCrossoverChannel channel in participants)
        {
            VirtualCrossoverChannelState state = channel.SideState(channel.ActiveRight);
            DspChannelChain chain = channel.Settings.ToChain();
            if (overrideFor(channel) is { } over)
            {
                chain = chain with
                {
                    DelayMs = over.DelayMs,
                    InvertPolarity = over.InvertPolarity
                };
            }

            Complex[] response = state.ProcessingSource!.Apply(chain, state.SampleRate);
            processed.Add(new ProcessedChannel(
                channel,
                response,
                VirtualCrossoverAnalysis.FindPeakIndex(response),
                state.SampleRate,
                OxyColors.White,
                VirtualCrossoverAnalysis.ChainValidRange(
                    state.ProcessingSource.SampleCount,
                    chain,
                    state.SampleRate,
                    response.Length)));
        }

        return processed;
    }

    // The judge itself: the panel's metric, gated and smoothed the way THIS
    // session is (the pinned offset when it has one, its own Tukey shoulders,
    // its own smoothing code) — never a default invented here. Calibration is
    // null: it belongs to the machine that measured, not to the session, and
    // it applies identically to both sides of the comparison anyway.
    private static List<VirtualCrossoverMetric.Entry> Judge(
        VirtualCrossoverProjectFile project,
        List<ProcessedChannel> processed)
    {
        var template = new PhaseAnalysisSettings(
            // The magnitude reads the FIXED steady-state window, whatever the
            // session's gate says — the panel's own rule (see the magnitudeGate
            // rebuild in VirtualCrossoverPanel.RequestRedraw): the session gate
            // times junctions and shapes the phase/impulse views, while the
            // magnitude — and therefore this judge — reads the response the ear
            // hears. Only the session's OFFSET (its pin) still places the window.
            PhaseWindowMode.Fixed,
            project.PhaseFdwCycles,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: 0.0,
            FrequencyResponseOptions.SteadyStateLeftMs,
            FrequencyResponseOptions.SteadyStatePlateauMs,
            FrequencyResponseOptions.SteadyStateRightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);
        double? pinnedOffsetMs = AutoGate
            ? null
            : project.PhaseGateFor(project.ActiveSideRight).OffsetMs;
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(
            coordinator,
            (impulseResponse, anchorIndex, sampleRate) =>
            {
                PhaseAnalysisSettings gate = template with
                {
                    GateOffsetMs = pinnedOffsetMs
                        ?? anchorIndex * 1_000.0 / sampleRate
                };
                (AnalysisCurve display, AnalysisCurve unsmoothed) =
                    DataHelper.GetGatedPrimarySpectrumPair(
                        new ImpulseMeasurementView(impulseResponse, anchorIndex, sampleRate),
                        gate,
                        calibration: null,
                        project.SmoothingCode);
                return new GatedMagnitude(display, unsmoothed);
            });
        (_, _, List<SignalPoint>? loss, _) = metrics.BuildCurves(
            processed, project.SmoothingCode);
        return metrics.BuildEntries(processed, loss);
    }

    // The session's channels, resolved the way the tool resolves an imported
    // session: the stored path first, then the same measurement beside the
    // session file (VirtualCrossoverSourceLocator), which is what makes an
    // archived cabin load on a machine that never measured it.
    private static List<VirtualCrossoverChannel> LoadChannels(
        VirtualCrossoverProjectFile project,
        out string fingerprint)
    {
        var channels = new List<VirtualCrossoverChannel>();
        var identity = new StringBuilder();
        for (int i = 0; i < project.Pairs.Count; i++)
        {
            var channel = new VirtualCrossoverChannel(VirtualCrossoverSheet.ChannelName(i))
            {
                Pair = project.Pairs[i],
                ActiveRight = project.ActiveSideRight
            };
            channels.Add(channel);
            VirtualCrossoverChannelSettings settings =
                channel.SideSettings(channel.ActiveRight);
            if (!settings.HasSource ||
                VirtualCrossoverSourceLocator.Locate(
                    settings.SourceFilePath,
                    settings.SourceRelativePath,
                    project.ProjectDirectory) is not { } path)
            {
                continue;
            }

            ImpulseResponseFile file = ImpulseResponseFile.LoadAsync(path)
                .GetAwaiter().GetResult();
            ResolvedVirtualDspSource.FromSnapshot(
                MeasurementHistoryService.CreateSnapshot(file))
                ?.ApplyTo(channel.SideState(channel.ActiveRight));
            identity.Append(path).Append('|')
                .Append(settings.DelayMs.ToString("0.000", CultureInfo.InvariantCulture))
                .Append(settings.InvertPolarity ? "-inv;" : ";");
        }

        fingerprint = identity.ToString();
        return channels;
    }

    // The battery's bottom line: how the proposal stands against the owner's own
    // tuning across every junction of every cabin. Junction rows and the
    // per-session totals are counted apart — a total is not an independent
    // junction, it is the same bands read together.
    private static void WriteSummary(
        StringBuilder report, List<JunctionComparison> comparisons)
    {
        report.AppendLine();
        report.AppendLine("=== summary (proposal against the session's saved settings)");
        foreach (bool totals in new[] { false, true })
        {
            List<JunctionComparison> rows = comparisons
                .Where(item => item.IsTotal == totals)
                .ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            List<double> dips = rows
                .Where(item => item.DipDelta.HasValue)
                .Select(item => item.DipDelta!.Value)
                .ToList();
            report.AppendLine(
                $"  {(totals ? "totals  " : "junctions")} {rows.Count,3}: " +
                $"avg {rows.Average(item => item.AverageDelta),+7:+0.000;-0.000} dB " +
                $"({rows.Count(item => item.AverageDelta > 0.005)} better / " +
                $"{rows.Count(item => item.AverageDelta < -0.005)} worse), " +
                $"dip {(dips.Count > 0 ? dips.Average() : 0),+7:+0.000;-0.000} dB " +
                $"({dips.Count(value => value > 0.005)} better / " +
                $"{dips.Count(value => value < -0.005)} worse)");
        }
    }

    // One judged junction, both readings side by side. Positive deltas mean the
    // proposal loses LESS than the saved settings do.
    private sealed record JunctionComparison(
        string Session,
        string Junction,
        bool IsTotal,
        double LowHz,
        double HighHz,
        double SavedAverageDb,
        double? SavedDipDb,
        double ProposedAverageDb,
        double? ProposedDipDb)
    {
        public double AverageDelta => ProposedAverageDb - SavedAverageDb;

        public double? DipDelta => SavedDipDb.HasValue && ProposedDipDb.HasValue
            ? ProposedDipDb.Value - SavedDipDb.Value
            : null;

        // The line two builds are diffed on: fixed field order, invariant
        // formatting, six decimals — enough that a change of rule shows and a
        // rounding wobble does not.
        public string Row() => string.Join('\t',
            "ROW",
            Session,
            Junction,
            LowHz.ToString("0.0", CultureInfo.InvariantCulture),
            HighHz.ToString("0.0", CultureInfo.InvariantCulture),
            SavedAverageDb.ToString("0.000000", CultureInfo.InvariantCulture),
            SavedDipDb?.ToString("0.000000", CultureInfo.InvariantCulture) ?? "-",
            ProposedAverageDb.ToString("0.000000", CultureInfo.InvariantCulture),
            ProposedDipDb?.ToString("0.000000", CultureInfo.InvariantCulture) ?? "-");
    }
}
