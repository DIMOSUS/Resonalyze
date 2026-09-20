using System.Globalization;
using System.Numerics;
using System.Text;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

/// <summary>
/// The measurement that decides the acoustic-slope mode: on each archived cabin, walk every junction with the plain
/// junction tune and again with a stated acoustic slope, equalise both the same way afterwards, and read what the
/// junctions actually sum to. A runner, not a pinned expectation — it asserts only that cabins were judged, and the
/// ROW lines are for diffing two builds. Stop criterion: docs/specs/acoustic-crossover-target.md#6.
/// </summary>
/// <remarks>
/// Plants are read off the gated impulse responses here rather than from spatial averages, so every cabin is judged
/// the same way and the corridor is the only thing that differs between the arms.
/// </remarks>
public sealed class AcousticTargetBattery(ITestOutputHelper output)
{
    public const string OutputVariable = "RESONALYZE_ACOUSTIC_TARGET_OUT";

    /// <summary>What the arms ask for; the default a tuner would state.</summary>
    private static readonly JunctionAcousticTarget Asked =
        new(CrossoverFilterFamily.LinkwitzRiley, 24);

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
        foreach (string session in SessionBatteryHarness.ResolveSessions(
            SessionBatteryHarness.RootDirectory!))
        {
            if (!File.Exists(session))
            {
                report.AppendLine($"missing: {session}");
                continue;
            }

            string name = Path.GetFileName(Path.GetDirectoryName(session)!);
            report.AppendLine();
            report.AppendLine($"=== {name}  ({session})");
            try
            {
                foreach (bool acoustic in new[] { false, true })
                {
                    rows.AddRange(RunArm(session, acoustic, report));
                }
            }
            catch (Exception exception)
            {
                report.AppendLine($"  FAILED: {exception.GetType().Name}: {exception.Message}");
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

    /// <summary>One whole pass over a cabin: tune every junction, then equalise every channel, then read the sums.</summary>
    private static List<Row> RunArm(string sessionPath, bool acoustic, StringBuilder report)
    {
        // Re-loaded per arm: a tune and a fit both write into the settings, and the two arms must not see each other.
        VirtualCrossoverProjectFile project = VirtualCrossoverProjectFile.LoadFrom(sessionPath);
        List<VirtualCrossoverChannel> channels =
            SessionBatteryHarness.LoadChannels(project, out _, bothSides: true);
        List<VirtualCrossoverChannel> usable = channels
            .Where(channel => channel.Pair.Enabled && channel.TransferImpulseResponse != null)
            .OrderBy(channel => VirtualCrossoverJunctions.BandCenterHz(
                channel.SideSettings(channel.ActiveRight)))
            .ToList();
        string arm = acoustic ? "acoustic" : "plain";
        if (usable.Count < 2)
        {
            report.AppendLine($"  {arm}: fewer than two measured channels");
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
                report.AppendLine($"  {arm} {label}: skipped ({refusal})");
                continue;
            }

            double currentHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                lower.SideSettings(lower.ActiveRight), upper.SideSettings(upper.ActiveRight));
            if (!(currentHz > 0))
            {
                report.AppendLine($"  {arm} {label}: skipped (no corner set)");
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
                AcousticTarget: acoustic ? Asked : null,
                TargetCurveDb: acoustic ? targetCurve : null);
            JunctionTuneResult result = CrossoverJunctionTuner.Tune(sides, options);
            tuned.Add(new Tuned(label, lower, upper, result));
            if (result.Changed)
            {
                AgentJunctionTune.Write(result, lower, upper, acoustic ? Asked : null);
            }
        }

        // One equalisation stage for both arms, exactly the wizard's defaults, after every junction is settled.
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
            // The EQ cost of THIS junction: the two banks that meet in it, not the cabin's total.
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
                    acoustic,
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
                    $"  {arm,-8} {label + ":" + sum.Side,-16} loss {sum.LossDb,6:0.00} dip {sum.DipDb,6:0.00} " +
                    $"ripple {sum.RippleDb,5:0.00}" +
                    (result.Best.AcousticCostDb is { } chosen
                        ? $"  acoustic {chosen,5:0.00} (closest {result.ClosestAcousticCostDb,5:0.00}, " +
                          $"worst side {result.Best.WorstAcousticCostDb,5:0.00}, " +
                          $"{(CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb) ? "reachable" : "out of reach")})" +
                          $" slopes {Slopes(result)}"
                        : string.Empty) +
                    (result.Changed ? "  applied" : "  kept"));
            }
        }

        report.AppendLine($"  {arm}: {tuned.Count} junctions, {fitted} fits, {refusedFits} refused");
        return rows;
    }

    /// <summary>The wizard's own Auto Tune on one channel side, written into the settings; null when it refuses.</summary>
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

        // The level the wizard's numeric would be set to: just under what the channel plays, so a cut-first fit has
        // somewhere to go. Identical rule in both arms.
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

    /// <summary>Every measured side's sum on the junction's own band, through the chains as they now stand.</summary>
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

        foreach (JunctionTuneSide side in sides)
        {
            Complex[] lowerResponse = VirtualCrossoverAnalysis.ApplyChain(
                side.LowerImpulseResponse, side.LowerChain, side.SampleRate, processor.SampleRateHz,
                out ValidSampleRange lowerRange);
            Complex[] upperResponse = VirtualCrossoverAnalysis.ApplyChain(
                side.UpperImpulseResponse, side.UpperChain, side.SampleRate, processor.SampleRateHz,
                out ValidSampleRange upperRange);
            if (VirtualCrossoverAnalysis.MeasureJunctionSpectrum(
                    upperResponse, [lowerResponse], side.SampleRate,
                    result.Best.BandLowHz, result.Best.BandHighHz,
                    upperRange, [lowerRange]) is { } reading)
            {
                sums.Add(new JunctionSum(side.Name, reading.LossDb, reading.DipDb, reading.RippleDb));
            }
        }

        return sums;
    }

    /// <summary>What the winner achieved against what was asked, and what the channels do by themselves, in dB/oct.</summary>
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

        foreach (bool acoustic in new[] { false, true })
        {
            List<Row> arm = rows.Where(row => row.Acoustic == acoustic).ToList();
            if (arm.Count == 0)
            {
                continue;
            }

            report.AppendLine(
                $"  {(acoustic ? "acoustic" : "plain"),-8} rows {arm.Count,3}  " +
                $"loss avg {arm.Average(row => row.LossDb),6:0.00}  worst dip {arm.Min(row => row.DipDb),6:0.00}  " +
                $"ripple avg {arm.Average(row => row.RippleDb),5:0.00}  applied {arm.Count(row => row.Changed),3}" +
                (acoustic
                    ? $"  acoustic cost avg {Average(arm.Select(row => row.AcousticCostDb)),5:0.00}" +
                      $" (closest {Average(arm.Select(row => row.ClosestCostDb)),5:0.00})"
                    : string.Empty));
        }

        // Paired by junction and side: the same cabin's same junction under the two arms.
        var paired = rows
            .Where(row => !row.Acoustic)
            .Join(
                rows.Where(row => row.Acoustic),
                row => (row.Session, row.Junction),
                row => (row.Session, row.Junction),
                (plain, acoustic) => (Plain: plain, Acoustic: acoustic))
            .ToList();
        report.AppendLine();
        report.AppendLine($"  paired junctions: {paired.Count}");
        if (paired.Count == 0)
        {
            return;
        }

        report.AppendLine(
            $"  loss  plain {paired.Average(pair => pair.Plain.LossDb),6:0.00} -> " +
            $"acoustic {paired.Average(pair => pair.Acoustic.LossDb),6:0.00}  " +
            $"better in {paired.Count(pair => pair.Acoustic.LossDb > pair.Plain.LossDb + 0.01),3}/{paired.Count}");
        report.AppendLine(
            $"  dip   plain {paired.Average(pair => pair.Plain.DipDb),6:0.00} -> " +
            $"acoustic {paired.Average(pair => pair.Acoustic.DipDb),6:0.00}  " +
            $"better in {paired.Count(pair => pair.Acoustic.DipDb > pair.Plain.DipDb + 0.01),3}/{paired.Count}");
        report.AppendLine(
            $"  worst boost  plain {Average(paired.Select(pair => pair.Plain.WorstBoostDb)),5:0.00} -> " +
            $"acoustic {Average(paired.Select(pair => pair.Acoustic.WorstBoostDb)),5:0.00}");
        report.AppendLine(
            $"  bands        plain {Average(paired.Select(pair => (double?)pair.Plain.Bands)),6:0.0} -> " +
            $"acoustic {Average(paired.Select(pair => (double?)pair.Acoustic.Bands)),6:0.0}");
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
        bool Acoustic,
        double LossDb,
        double DipDb,
        double RippleDb,
        bool Changed,
        double? AcousticCostDb,
        double? ClosestCostDb,
        double? WorstSideCostDb,
        double? WorstBoostDb,
        int? Bands);

    private sealed record Tuned(
        string Label,
        VirtualCrossoverChannel Lower,
        VirtualCrossoverChannel Upper,
        JunctionTuneResult Result);

    private sealed record JunctionSum(string Side, double LossDb, double DipDb, double RippleDb);

    private sealed record EqCost(int Bands, double WorstBoostDb, double PreampDb);
}
