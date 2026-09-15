using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.History;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

/// <summary>Skipped unless a folder of archived field sessions (not in the repository) is named in the environment.</summary>
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
/// Auto delay battery: loads archived sessions as the tool does and judges both the proposal and the saved tune
/// by the panel's own summation-loss metric. A runner, not a pinned expectation: it asserts only that sessions
/// were found, loaded and judged; ROW lines are for diffing two builds.
/// </summary>
public sealed class SessionBatteryHarness(ITestOutputHelper output)
{
    public const string OutputVariable = "RESONALYZE_SESSION_BATTERY_OUT";

    /// <summary>Semicolon-separated session files, absolute or relative to the root.</summary>
    public const string SessionsVariable = "RESONALYZE_SESSION_BATTERY_SESSIONS";

    /// <summary>Judge through AUTO gate placement: a pinned absolute gate can put a proposal's moved fronts into its fade-in.</summary>
    public const string AutoGateVariable = "RESONALYZE_SESSION_BATTERY_AUTOGATE";

    // Reference car is v5_exp; the older v5 session was deleted and v2's tuning is distrusted.
    private static readonly string[] DefaultSessions =
    [
        @"3RC\virtual-dsp-session.json",
        @"Passat\virtual-dsp-session.json",
        @"Passat v2\virtual-dsp-session-sq-v10-7-opt.json",
        @"v2\virtual-dsp-session.json",
        @"v2\head_90_grad\virtual-dsp-session.json",
        @"v3\virtual-dsp-session.json",
        @"v4\virtual-dsp-session.json",
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
        // Invariant culture so reports diff cleanly between machines.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var report = new StringBuilder();
        var summary = new List<JunctionComparison>();
        // v2 and head_90_grad are the same cabin saved twice; duplicates are named and skipped.
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

    internal static IEnumerable<string> ResolveSessions(string root)
    {
        string? configured = Environment.GetEnvironmentVariable(SessionsVariable);
        IEnumerable<string> names = string.IsNullOrWhiteSpace(configured)
            ? DefaultSessions
            : configured.Split(';', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        return names.Select(name =>
            Path.IsPathFullyQualified(name) ? name : Path.Combine(root, name));
    }

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

        // Signed r: negative means the coherent alignment is the inverted one.
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

    // Mirrors the panel's single-side Auto delay; saved delays and polarities are deliberately not fed in.
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
                    channel.ProcessorSampleRate,
                    channel.Settings.ToChain(channel.Pair.Zone))).ToList(),
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

    internal static List<ProcessedChannel> Process(
        List<VirtualCrossoverChannel> participants,
        Func<VirtualCrossoverChannel, AlignmentOverride?> overrideFor)
    {
        var processed = new List<ProcessedChannel>(participants.Count);
        foreach (VirtualCrossoverChannel channel in participants)
        {
            VirtualCrossoverChannelState state = channel.SideState(channel.ActiveRight);
            DspChannelChain chain = channel.Settings.ToChain(channel.Pair.Zone);
            if (overrideFor(channel) is { } over)
            {
                chain = chain with
                {
                    DelayMs = over.DelayMs,
                    InvertPolarity = over.InvertPolarity
                };
            }

            Complex[] response = state.ProcessingSource!.Apply(
                chain, state.SampleRate, channel.ProcessorSampleRate);
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
                    channel.ProcessorSampleRate,
                    response.Length)));
        }

        return processed;
    }

    // Calibration is null: it belongs to the measuring machine and applies equally to both sides.
    internal static List<VirtualCrossoverMetric.Entry> Judge(
        VirtualCrossoverProjectFile project,
        List<ProcessedChannel> processed)
    {
        var template = new PhaseAnalysisSettings(
            // Magnitude reads the FIXED steady-state window regardless of the session gate, as the panel does; only the pin places it.
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
            (impulseResponse, anchorIndex, sampleRate, measuredBand, _) =>
            {
                PhaseAnalysisSettings gate = template with
                {
                    GateOffsetMs = pinnedOffsetMs
                        ?? anchorIndex * 1_000.0 / sampleRate
                };
                (AnalysisCurve display, AnalysisCurve unsmoothed) =
                    DataHelper.GetGatedPrimarySpectrumPair(
                        new ImpulseMeasurementView(impulseResponse, anchorIndex, sampleRate)
                        {
                            LowestMeasuredFrequencyHz = measuredBand.LowEdgeHz,
                            HighestMeasuredFrequencyHz = measuredBand.HighEdgeHz
                        },
                        gate,
                        calibration: null,
                        project.SmoothingCode);
                return new GatedMagnitude(display, unsmoothed);
            });
        (_, _, List<SignalPoint>? loss) = metrics.BuildCurves(
            processed, project.SmoothingCode);
        return metrics.BuildEntries(processed, loss);
    }

    internal static List<VirtualCrossoverChannel> LoadChannels(
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

    // Positive deltas mean the proposal loses LESS than the saved settings.
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
