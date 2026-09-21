using System.Globalization;
using System.Numerics;
using System.Text;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

/// <summary>The acoustic-slope battery: every junction tuned per arm, the same EQ after, the sums read re-aligned.
/// A runner, not a pinned expectation. See docs/tech/crossover-auto-setup.md#measured-on-the-battery.</summary>
public sealed class AcousticTargetBattery(ITestOutputHelper output)
{
    public const string OutputVariable = "RESONALYZE_ACOUSTIC_TARGET_OUT";

    /// <summary>lr24-tune states the slope to the tune only, which tells the two halves of the mode apart.</summary>
    private static readonly Arm[] Arms =
    [
        new("plain", null, TellTheEq: false, CrossoverJunctionTuner.DefaultSumSlackDb),
        new("lr24", new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24),
            TellTheEq: true, CrossoverJunctionTuner.DefaultSumSlackDb),
        new("lr24-tune", new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24),
            TellTheEq: false, CrossoverJunctionTuner.DefaultSumSlackDb),
        new("lr24-slack1", new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24),
            TellTheEq: true, 1.0),
        new("lr48", new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 48),
            TellTheEq: true, CrossoverJunctionTuner.DefaultSumSlackDb)
    ];

    private static readonly PhaseAnalysisSettings GateTemplate = new(
        PhaseWindowMode.Fixed,
        PhaseAnalysisSettings.DefaultFdwCycles,
        PhaseDetrendMode.Off,
        ManualDetrendMilliseconds: 0.0,
        GateOffsetMs: 0.0,
        LeftMs: 2.0,
        PlateauMs: 12.0,
        RightMs: 5.0,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);

    [SessionBatteryFact]
    public void JudgeTheAcousticTargetAgainstThePlainJunctionTune()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var report = new StringBuilder();
        var rows = new List<Row>();
        List<string> sessions = SessionBatteryHarness.ResolveSessions(SessionBatteryHarness.RootDirectory!).ToList();
        (string Session, Arm Arm)[] runs = sessions
            .Where(File.Exists)
            .SelectMany(session => Arms.Select(arm => (session, arm)))
            .ToArray();
        // Every run loads its own copy of the session, so they are independent; the report keeps the serial order.
        var done = new (StringBuilder Text, List<Row> Rows)[runs.Length];
        Parallel.For(0, runs.Length, index =>
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var text = new StringBuilder();
            List<Row> judged = [];
            try
            {
                judged = RunArm(runs[index].Session, runs[index].Arm, text);
            }
            catch (Exception exception)
            {
                text.AppendLine($"  FAILED: {exception.GetType().Name}: {exception.Message}");
            }

            done[index] = (text, judged);
        });

        foreach (string session in sessions)
        {
            if (!File.Exists(session))
            {
                report.AppendLine($"missing: {session}");
                continue;
            }

            string name = Path.GetFileName(Path.GetDirectoryName(session)!);
            report.AppendLine();
            report.AppendLine($"=== {name}  ({session})");
            for (int index = 0; index < runs.Length; index++)
            {
                if (runs[index].Session == session)
                {
                    report.Append(done[index].Text);
                    rows.AddRange(done[index].Rows);
                }
            }
        }

        Summarise(report, rows);
        string text = report.ToString();
        output.WriteLine(text);
        string path = Environment.GetEnvironmentVariable(OutputVariable)
            ?? Path.Combine(Path.GetTempPath(), "resonalyze-acoustic-target.txt");
        File.WriteAllText(path, text);
        output.WriteLine($"Report written to {path}");
        Assert.NotEmpty(rows);
    }

    private static List<Row> RunArm(string sessionPath, Arm arm, StringBuilder report)
    {
        JunctionAcousticTarget? acousticTarget = arm.Asked;
        bool acoustic = acousticTarget != null;
        // Re-loaded per arm: tunes and fits write into the settings.
        VirtualCrossoverProjectFile project = VirtualCrossoverProjectFile.LoadFrom(sessionPath);
        List<VirtualCrossoverChannel> channels =
            SessionBatteryHarness.LoadChannels(project, out _, bothSides: true);
        List<VirtualCrossoverChannel> usable = channels
            .Where(channel => channel.Pair.Enabled && channel.TransferImpulseResponse != null)
            .OrderBy(channel => VirtualCrossoverJunctions.BandCenterHz(
                channel.SideSettings(channel.ActiveRight)))
            .ToList();
        if (usable.Count < 2)
        {
            report.AppendLine($"  {arm.Name}: fewer than two measured channels");
            return [];
        }

        DspProcessorProfile processor = project.ResolveDspProcessor(usable[0].SampleRate);
        IReadOnlyList<SignalPoint> targetCurve = TargetCurve(project);
        var rows = new List<Row>();
        var tuned = new List<Tuned>();
        for (int i = 0; i + 1 < usable.Count; i++)
        {
            VirtualCrossoverChannel lower = usable[i];
            VirtualCrossoverChannel upper = usable[i + 1];
            string label = $"{lower.Name}-{upper.Name}";
            (List<JunctionTuneSide> sides, string? refusal) =
                AgentProbeReader.JunctionTuneSides(lower, upper, rightSideOnly: null);
            if (refusal != null)
            {
                report.AppendLine($"  {arm.Name} {label}: skipped ({refusal})");
                continue;
            }

            double currentHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                lower.SideSettings(lower.ActiveRight), upper.SideSettings(upper.ActiveRight));
            if (!(currentHz > 0))
            {
                report.AppendLine($"  {arm.Name} {label}: skipped (no corner set)");
                continue;
            }

            (double minHz, double maxHz) = AgentProposalValidator.DefaultJunctionWindow(currentHz);
            var options = new JunctionTuneOptions(
                AgentProposalValidator.CurrentFamilies(
                    lower.SideSettings(lower.ActiveRight), upper.SideSettings(upper.ActiveRight)),
                null,
                minHz,
                maxHz,
                IndependentSlopes: false,
                processor.SampleRateHz,
                AcousticTarget: acousticTarget,
                TargetCurveDb: acoustic ? targetCurve : null,
                SumSlackDb: arm.SlackDb,
                OneAlignmentForAllSides: AgentProbeReader.SharesOneAlignment(lower, upper));
            JunctionTuneResult result = CrossoverJunctionTuner.Tune(sides, options);
            tuned.Add(new Tuned(label, lower, upper, result));
            if (result.Changed)
            {
                AgentJunctionTune.Write(result, lower, upper, arm.TellTheEq ? acousticTarget : null);
            }
        }

        int fitted = 0;
        int refusedFits = 0;
        var cost = new Dictionary<VirtualCrossoverChannelSettings, EqCost>();
        foreach (VirtualCrossoverChannel channel in usable)
        {
            foreach (bool rightSide in channel.Pair.Mono ? [false] : new[] { false, true })
            {
                if (channel.SideState(rightSide).TransferImpulseResponse == null)
                {
                    continue;
                }

                if (Equalise(channel, rightSide, project, processor) is { } spent)
                {
                    cost[channel.SideSettings(rightSide)] = spent;
                    fitted++;
                }
                else
                {
                    refusedFits++;
                }
            }
        }

        foreach ((string label, VirtualCrossoverChannel lower, VirtualCrossoverChannel upper,
            JunctionTuneResult result) in tuned)
        {
            List<EqCost> spentHere = new[] { false, true }
                .SelectMany(right => new[]
                {
                    lower.SideSettings(right && !lower.Pair.Mono),
                    upper.SideSettings(right && !upper.Pair.Mono)
                })
                .Distinct()
                .Where(cost.ContainsKey)
                .Select(settings => cost[settings])
                .ToList();
            foreach (JunctionSum sum in ReadSums(lower, upper, processor, result))
            {
                rows.Add(new Row(
                    Path.GetFileName(Path.GetDirectoryName(sessionPath)!),
                    label + ":" + sum.Side,
                    arm.Name,
                    sum.LossDb,
                    sum.DipDb,
                    sum.RippleDb,
                    result.Changed,
                    result.Best.AcousticCostDb,
                    result.ClosestAcousticCostDb,
                    result.Best.WorstAcousticCostDb,
                    spentHere.Count == 0 ? null : spentHere.Max(item => item.WorstBoostDb),
                    spentHere.Count == 0 ? null : spentHere.Sum(item => item.Bands)));
                report.AppendLine(
                    $"  {arm.Name,-12} {label + ":" + sum.Side,-16} loss {sum.LossDb,6:0.00} dip {sum.DipDb,6:0.00} " +
                    $"ripple {sum.RippleDb,5:0.00}" +
                    (result.Best.AcousticCostDb is { } chosen
                        ? $"  acoustic {chosen,5:0.00} (closest {result.ClosestAcousticCostDb,5:0.00}, " +
                          $"worst channel {result.Best.WorstAcousticCostDb,5:0.00}, " +
                          $"{(CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb) ? "reachable" : "out of reach")})" +
                          $" slopes {Slopes(result)}"
                        : string.Empty) +
                    (result.Changed ? "  applied" : "  kept"));
            }
        }

        report.AppendLine($"  {arm.Name}: {tuned.Count} junctions, {fitted} fits, {refusedFits} refused");
        return rows;
    }

    private static EqCost? Equalise(
        VirtualCrossoverChannel channel,
        bool rightSide,
        VirtualCrossoverProjectFile project,
        DspProcessorProfile processor)
    {
        VirtualDspEqHandoffRequest? request = VirtualDspEqHandoff.Build(
            channel, rightSide, withChain: true, processor, GateTemplate,
            pinnedGateOffsetMs: null, renderAnchorIndex: null, phaseContext: null,
            targetLevelDb: 0, targetLevelMinDb: -120, targetLevelMaxDb: 60,
            smoothingInverseOctaves: 0, calibration: null, calibrationName: null,
            SpatialAverageCalibration.Own, projectGeneration: 1,
            spatialAverage: null, spatialAverageOffsetDb: 0);
        if (request == null)
        {
            return null;
        }

        // Just under what the channel plays, so a cut-first fit has somewhere to go.
        IReadOnlyList<SignalPoint> source = EqAutoTuneHeadless.SourceCurve(request.Source, 0, null);
        List<double> levels = source
            .Where(point => double.IsFinite(point.Y))
            .Select(point => point.Y)
            .OrderBy(value => value)
            .ToList();
        if (levels.Count < 8)
        {
            return null;
        }

        VirtualCrossoverTargetSettings targetSettings =
            project.Target ?? new VirtualCrossoverTargetSettings();
        TargetCurveSpec spec = targetSettings.ToCurve().Normalized().Spec;
        VirtualDspEqHandoffRequest levelled = request with
        {
            TargetLevelDb = levels[(int)(levels.Count * 0.75)] - 1
        };
        EqHeadlessTuneInputs inputs;
        try
        {
            inputs = EqAutoTuneHeadless.Prepare(
                levelled, spec, EqAutoTunePolicy.Default, null, null, null, null);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }

        if (inputs.Source.Count < 2 ||
            !EqAutoTuneHeadless.IsUsableWindow(inputs.MinHz, inputs.MaxHz) ||
            EqAutoTuneHeadless.NoMeasuredDataRefusal(inputs) != null)
        {
            return null;
        }

        EqualizationCurve fitted = EqAutoTuneHeadless.Fit(inputs);
        VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
        settings.PeqBands = fitted.Bands.ToList();
        settings.PeqPreampDb = fitted.PreampDb;
        return new EqCost(
            fitted.Bands.Count,
            fitted.Bands.Count == 0 ? 0 : fitted.Bands.Max(band => band.GainDb),
            fitted.PreampDb);
    }

    private static List<JunctionSum> ReadSums(
        VirtualCrossoverChannel lower,
        VirtualCrossoverChannel upper,
        DspProcessorProfile processor,
        JunctionTuneResult result)
    {
        var sums = new List<JunctionSum>();
        (List<JunctionTuneSide> sides, string? refusal) =
            AgentProbeReader.JunctionTuneSides(lower, upper, rightSideOnly: null);
        if (refusal != null)
        {
            return sums;
        }

        // Re-aligned as Auto delay will after the tune, one shift where the upper block is mono.
        double cornerHz = Math.Sqrt(result.Best.BandLowHz * result.Best.BandHighHz);
        double halfWindowMs = CrossoverAutoSetup.PostCheckHalfWindowMs(cornerHz);
        var inputs = new List<JunctionAlignmentSide>(sides.Count);
        foreach (JunctionTuneSide side in sides)
        {
            Complex[] lowerResponse = VirtualCrossoverAnalysis.ApplyChain(
                side.LowerImpulseResponse, side.LowerChain, side.SampleRate, processor.SampleRateHz,
                out ValidSampleRange lowerRange);
            Complex[] upperResponse = VirtualCrossoverAnalysis.ApplyChain(
                side.UpperImpulseResponse, side.UpperChain, side.SampleRate, processor.SampleRateHz,
                out ValidSampleRange upperRange);
            inputs.Add(new JunctionAlignmentSide(upperResponse, lowerResponse, side.SampleRate, upperRange, lowerRange));
        }

        if (AgentProbeReader.SharesOneAlignment(lower, upper) && inputs.Count > 1)
        {
            if (VirtualCrossoverAnalysis.MeasureJointlyAlignedJunctionSpectra(
                    inputs, result.Best.BandLowHz, result.Best.BandHighHz, halfWindowMs) is { } joint)
            {
                for (int i = 0; i < sides.Count; i++)
                {
                    if (joint.Readings[i] is { } read)
                    {
                        sums.Add(new JunctionSum(sides[i].Name, read.LossDb, read.DipDb, read.RippleDb));
                    }
                }
            }

            return sums;
        }

        for (int i = 0; i < sides.Count; i++)
        {
            JunctionAlignmentSide input = inputs[i];
            if (VirtualCrossoverAnalysis.MeasureAlignedJunctionSpectrum(
                    input.VariableImpulseResponse, [input.FixedImpulseResponse], input.SampleRate,
                    result.Best.BandLowHz, result.Best.BandHighHz, halfWindowMs,
                    input.VariableValidRange, [input.FixedValidRange]) is { Reading: var reading })
            {
                sums.Add(new JunctionSum(sides[i].Name, reading.LossDb, reading.DipDb, reading.RippleDb));
            }
        }

        return sums;
    }

    private static string Slopes(JunctionTuneResult result)
    {
        JunctionAcousticFit? fit = result.Best.Sides.FirstOrDefault()?.Acoustic;
        JunctionDriverSlopes? plant = result.DriverSlopes.FirstOrDefault();
        return $"asked {fit?.TargetSlopeDbPerOctave,5:0.0} got {fit?.LowerSlopeDbPerOctave,5:0.0}/" +
            $"{fit?.UpperSlopeDbPerOctave,-5:0.0} plant {plant?.LowerDbPerOctave,5:0.0}/" +
            $"{plant?.UpperDbPerOctave,-5:0.0}";
    }

    private static IReadOnlyList<SignalPoint> TargetCurve(VirtualCrossoverProjectFile project)
    {
        TargetCurveSpec spec = (project.Target ?? new VirtualCrossoverTargetSettings())
            .ToCurve().Normalized().Spec;
        return EqualizationCurve.LogFrequencyGrid(20, 20_000, 400)
            .Select(hz => new SignalPoint(hz, spec.Evaluate(hz)))
            .ToList();
    }

    private static void Summarise(StringBuilder report, List<Row> rows)
    {
        report.AppendLine();
        report.AppendLine("=== summary (plain junction tune against a stated acoustic LR24, same EQ after both)");
        if (rows.Count == 0)
        {
            report.AppendLine("  nothing was judged");
            return;
        }

        foreach (Arm arm in Arms)
        {
            List<Row> read = rows.Where(row => row.Arm == arm.Name).ToList();
            if (read.Count == 0)
            {
                continue;
            }

            report.AppendLine(
                $"  {arm.Name,-12} rows {read.Count,3}  " +
                $"loss avg {read.Average(row => row.LossDb),6:0.00}  worst dip {read.Min(row => row.DipDb),6:0.00}  " +
                $"ripple avg {read.Average(row => row.RippleDb),5:0.00}  applied {read.Count(row => row.Changed),3}" +
                (arm.Asked != null
                    ? $"  acoustic cost avg {Average(read.Select(row => row.AcousticCostDb)),5:0.00}" +
                      $" (closest {Average(read.Select(row => row.ClosestCostDb)),5:0.00})"
                    : string.Empty));
        }

        List<Row> plainRows = rows.Where(row => row.Arm == "plain").ToList();
        foreach (Arm arm in Arms.Where(item => item.Asked != null))
        {
            var paired = plainRows
                .Join(
                    rows.Where(row => row.Arm == arm.Name),
                    row => (row.Session, row.Junction),
                    row => (row.Session, row.Junction),
                    (plain, acoustic) => (Plain: plain, Acoustic: acoustic))
                .ToList();
            report.AppendLine();
            report.AppendLine($"  --- {arm.Name} against plain, paired junctions: {paired.Count}");
            if (paired.Count == 0)
            {
                continue;
            }

            report.AppendLine(
                $"  loss  {paired.Average(pair => pair.Plain.LossDb),6:0.00} -> " +
                $"{paired.Average(pair => pair.Acoustic.LossDb),6:0.00}  " +
                $"better in {paired.Count(pair => pair.Acoustic.LossDb > pair.Plain.LossDb + 0.01),3}/{paired.Count}");
            report.AppendLine(
                $"  dip   {paired.Average(pair => pair.Plain.DipDb),6:0.00} -> " +
                $"{paired.Average(pair => pair.Acoustic.DipDb),6:0.00}  " +
                $"better in {paired.Count(pair => pair.Acoustic.DipDb > pair.Plain.DipDb + 0.01),3}/{paired.Count}");
            report.AppendLine(
                $"  boost {Average(paired.Select(pair => pair.Plain.WorstBoostDb)),6:0.00} -> " +
                $"{Average(paired.Select(pair => pair.Acoustic.WorstBoostDb)),6:0.00}   " +
                $"bands {Average(paired.Select(pair => (double?)pair.Plain.Bands)),5:0.0} -> " +
                $"{Average(paired.Select(pair => (double?)pair.Acoustic.Bands)),5:0.0}");
        }

        report.AppendLine();
        report.AppendLine(
            "  Stop criterion: no gain in the final sum and no cheaper EQ means the mode ships as the button and " +
            "the diagnostics only.");
    }

    private static double Average(IEnumerable<double?> values)
    {
        List<double> read = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return read.Count == 0 ? double.NaN : read.Average();
    }

    private sealed record Row(
        string Session,
        string Junction,
        string Arm,
        double LossDb,
        double DipDb,
        double RippleDb,
        bool Changed,
        double? AcousticCostDb,
        double? ClosestCostDb,
        double? WorstSideCostDb,
        double? WorstBoostDb,
        int? Bands);

    private sealed record Arm(
        string Name,
        JunctionAcousticTarget? Asked,
        bool TellTheEq,
        double SlackDb);

    private sealed record Tuned(
        string Label,
        VirtualCrossoverChannel Lower,
        VirtualCrossoverChannel Upper,
        JunctionTuneResult Result);

    private sealed record JunctionSum(string Side, double LossDb, double DipDb, double RippleDb);

    private sealed record EqCost(int Bands, double WorstBoostDb, double PreampDb);
}
