using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Resonalyze.Dsp;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

/// <summary>Timing runner for the crossover wizard rethink: what the present ranked search costs, what a coherent
/// per-junction tune costs, and the marginal cost of one extra candidate once the chain responses are cached (the
/// term a split-corner search multiplies). Skipped without the archived cabins; not a pinned expectation.</summary>
public sealed class CrossoverCostHarness(ITestOutputHelper output)
{
    private static readonly CrossoverFilterFamily[] Families =
    [
        CrossoverFilterFamily.LinkwitzRiley,
        CrossoverFilterFamily.Butterworth,
        CrossoverFilterFamily.Bessel
    ];

    [SessionBatteryFact]
    public void MeasureCrossoverSearchCost()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var report = new StringBuilder();
        int measured = 0;
        foreach (string path in SessionBatteryHarness.ResolveSessions(
            SessionBatteryHarness.RootDirectory!))
        {
            if (!File.Exists(path))
            {
                report.AppendLine($"missing: {path}");
                continue;
            }

            try
            {
                if (MeasureSession(path, report))
                {
                    measured++;
                }
            }
            catch (Exception exception)
            {
                report.AppendLine($"  FAILED: {exception.GetType().Name}: {exception.Message}");
            }
        }

        output.WriteLine(report.ToString());
        Assert.True(measured > 0, "No session could be measured.");
    }

    private bool MeasureSession(string sessionPath, StringBuilder report)
    {
        VirtualCrossoverProjectFile project = VirtualCrossoverProjectFile.LoadFrom(sessionPath);
        List<VirtualCrossoverChannel> channels =
            SessionBatteryHarness.LoadChannels(project, out _, bothSides: true);
        string name = Path.GetFileName(Path.GetDirectoryName(sessionPath)!);
        report.AppendLine();
        report.AppendLine($"=== {name}");

        List<VirtualCrossoverChannel> usable = channels
            .Where(channel => channel.Pair.Enabled && channel.TransferImpulseResponse != null)
            .ToList();
        if (usable.Count < 2)
        {
            report.AppendLine("  fewer than two measured channels");
            return false;
        }

        int processorRate = project.ResolveDspProcessor(usable[0].SampleRate).SampleRateHz;
        report.AppendLine(
            $"  {usable.Count} channels, measured {usable[0].SampleRate} Hz, " +
            $"processor {processorRate} Hz");

        // The three curve builds the wizard could read: what it reads today, psychoacoustic, psychoacoustic on FDW-8.
        foreach ((string label, FrequencyResponseOptions curveOptions) in CurveModes())
        {
            var watch = Stopwatch.StartNew();
            foreach (VirtualCrossoverChannel channel in usable)
            {
                BuildCurve(channel, project, curveOptions);
            }

            watch.Stop();
            report.AppendLine(
                $"  curves {label,-22} {watch.Elapsed.TotalMilliseconds,8:0.0} ms " +
                $"({watch.Elapsed.TotalMilliseconds / usable.Count,6:0.0} ms per channel)");
        }

        // Grouped as the dialog groups; the biggest group is the chain worth timing.
        List<VirtualCrossoverChannel> group = usable
            .GroupBy(channel => VirtualCrossoverAlignmentStages.StageOf(channel.Pair.Zone))
            .OrderByDescending(entry => entry.Count())
            .First()
            .ToList();
        if (group.Count < 2)
        {
            report.AppendLine("  the biggest group has one channel");
            return false;
        }

        FrequencyResponseOptions wizardOptions = CurveModes()[0].Options;
        var ordered = new List<(VirtualCrossoverChannel Channel, double Center, AutoSetupSource Source)>();
        foreach (VirtualCrossoverChannel channel in group)
        {
            IReadOnlyList<SignalPoint> points = BuildCurve(channel, project, wizardOptions);
            DriverBandEstimate band = CrossoverAutoSetup.EstimateBand(
                points, null, channel.DistortionCurve);
            VirtualCrossoverChannelSettings settings =
                channel.SideSettings(project.ActiveSideRight);
            ordered.Add((
                channel,
                VirtualCrossoverAutoSetupOrder.CenterHz(
                    band, settings.EffectiveHighPassHz, settings.EffectiveLowPassHz),
                new AutoSetupSource(points, band.SuggestedType, null, channel.DistortionCurve)));
        }

        ordered.Sort((a, b) => a.Center.CompareTo(b.Center));
        var chain = ordered.Select(entry => entry.Channel).ToList();
        var sources = ordered.Select(entry => entry.Source).ToList();
        report.AppendLine(
            $"  chain: {string.Join(" -> ", chain.Select(channel => channel.Settings.DisplayName))}");

        var options = new CrossoverAutoSetupOptions(
            Families, 20, 20_000, IndependentSlopes: true,
            chain[0].SampleRate, processorRate);
        var impulseResponses = chain
            .Select(channel => channel.TransferImpulseResponse!)
            .ToList();
        var ranked = Stopwatch.StartNew();
        CrossoverAutoSetup.ProposeRanked(sources, options, impulseResponses);
        ranked.Stop();
        report.AppendLine(
            "  ProposeRanked (magnitude descent + achievability post-check): " +
            $"{ranked.Elapsed.TotalMilliseconds,9:0} ms  " +
            $"[{chain.Count} channels, {chain.Count - 1} junctions]");

        for (int j = 0; j < chain.Count - 1; j++)
        {
            MeasureJunction(chain[j], chain[j + 1], processorRate, report);
        }

        return true;
    }

    private static List<(string Label, FrequencyResponseOptions Options)> CurveModes() =>
    [
        ("1/3 oct, fixed 4096", new FrequencyResponseOptions { SmoothingInverseOctaves = 3 }),
        ("psycho, fixed 4096", new FrequencyResponseOptions
        {
            SmoothingInverseOctaves = SpectrumSmoothing.PsychoacousticCode
        }),
        ("psycho, FDW-8", new FrequencyResponseOptions
        {
            SmoothingInverseOctaves = SpectrumSmoothing.PsychoacousticCode,
            MagnitudeWindowMode = PhaseWindowMode.FrequencyDependent,
            MagnitudeFdwCycles = 8
        })
    ];

    private static IReadOnlyList<SignalPoint> BuildCurve(
        VirtualCrossoverChannel channel,
        VirtualCrossoverProjectFile project,
        FrequencyResponseOptions options)
    {
        VirtualCrossoverChannelState state = channel.SideState(project.ActiveSideRight);
        return DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(
                state.TransferImpulseResponse!, state.TransferPeakIndex, state.SampleRate)
            {
                LowestMeasuredFrequencyHz = state.MeasuredBand.LowEdgeHz,
                HighestMeasuredFrequencyHz = state.MeasuredBand.HighEdgeHz
            },
            options,
            null).Points;
    }

    // Matched and free slopes differ ONLY in the candidate count: the processed chain responses are the same set,
    // so the difference divides out to the marginal cost of one cached candidate, which is what a split corner buys.
    private void MeasureJunction(
        VirtualCrossoverChannel lower,
        VirtualCrossoverChannel upper,
        int processorRate,
        StringBuilder report)
    {
        var sides = new List<JunctionTuneSide>();
        foreach (bool right in new[] { false, true })
        {
            if (right && lower.Pair.Mono && upper.Pair.Mono)
            {
                continue;
            }

            VirtualCrossoverChannelState lowerState = lower.SideState(right && !lower.Pair.Mono);
            VirtualCrossoverChannelState upperState = upper.SideState(right && !upper.Pair.Mono);
            if (lowerState.TransferImpulseResponse == null ||
                upperState.TransferImpulseResponse == null ||
                lowerState.SampleRate != upperState.SampleRate)
            {
                continue;
            }

            sides.Add(new JunctionTuneSide(
                right ? "right" : "left",
                lowerState.TransferImpulseResponse,
                lower.SideSettings(right && !lower.Pair.Mono).ToChain(lower.Pair.Zone),
                upperState.TransferImpulseResponse,
                upper.SideSettings(right && !upper.Pair.Mono).ToChain(upper.Pair.Zone),
                lowerState.SampleRate));
        }

        string label = $"{lower.Settings.DisplayName}/{upper.Settings.DisplayName}";
        if (sides.Count == 0)
        {
            report.AppendLine($"  junction {label}: no side has both blocks measured");
            return;
        }

        double current = VirtualCrossoverJunctions.GetPairCrossoverHz(
            lower.Settings, upper.Settings);
        if (!(current > 0))
        {
            report.AppendLine($"  junction {label}: no corner set, skipped");
            return;
        }

        foreach (double halfSpanOctaves in new[] { 0.5, 1.0 })
        {
            double span = Math.Pow(2, halfSpanOctaves);
            double low = current / span;
            double high = current * span;
            double? previousTime = null;
            int previousCandidates = 0;
            foreach (bool free in new[] { false, true })
            {
                var tuneOptions = new JunctionTuneOptions(
                    Families, null, low, high, free, processorRate);
                var watch = Stopwatch.StartNew();
                JunctionTuneResult result = CrossoverJunctionTuner.Tune(sides, tuneOptions);
                watch.Stop();
                double ms = watch.Elapsed.TotalMilliseconds;
                string marginal = previousTime is { } before &&
                    result.CandidatesEvaluated > previousCandidates
                    ? $"  marginal {(ms - before) / (result.CandidatesEvaluated - previousCandidates),6:0.000} ms/cand"
                    : string.Empty;
                report.AppendLine(
                    $"  junction {label,-18} {low,6:0}-{high,-6:0} Hz " +
                    $"({2 * halfSpanOctaves:0.0} oct, {sides.Count} sides) " +
                    $"{(free ? "free   " : "matched")} slopes: " +
                    $"{ms,9:0} ms, {result.CandidatesEvaluated,6} candidates{marginal}");
                report.AppendLine($"      picked {Describe(result)}");
                previousTime = ms;
                previousCandidates = result.CandidatesEvaluated;
            }
        }
    }

    private static string Describe(JunctionTuneResult result) =>
        $"{Describe(result.Best.LowerLowPass)} / {Describe(result.Best.UpperHighPass)}" +
        $"  score {result.Best.RankingScoreDb:0.000} dB";

    private static string Describe(CrossoverEdge? edge) =>
        edge is { } value
            ? $"{value.Family} {value.FrequencyHz:0} Hz {value.SlopeDbPerOctave} dB/oct"
            : "none";
}
