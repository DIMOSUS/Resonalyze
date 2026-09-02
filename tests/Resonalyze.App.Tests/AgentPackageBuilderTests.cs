using System.Text.Json;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>
/// The package builder turns what the panel gathered into the text a chat
/// assistant reads. Pinned here: the envelope and identity, the protocol grids
/// (fixed density, holes kept as holes, nothing invented past a curve's edge),
/// the read-outs mapped onto the right junction, determinism, and the fixed
/// order in which a too-large package sheds its optional series.
/// </summary>
public sealed class AgentPackageBuilderTests
{
    private static readonly Guid Id = Guid.Parse("b6bd73c2-997b-4fe0-814a-d123cc403b8a");
    private static readonly DateTimeOffset Clock = new(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void Build_WrapsAParsablePackageInTheEnvelope()
    {
        AgentPackageBuildResult result = AgentPackageBuilder.Build(Inputs(), Id, Clock);

        Assert.True(result.Succeeded, result.Error);
        string text = result.Text!;
        Assert.StartsWith(AgentProtocol.PackageHeader, text);
        Assert.Contains(AgentProtocol.InlineRules, text);
        Assert.Contains(AgentProtocol.GuideUrl, text);
        Assert.Empty(result.Omitted);

        JsonElement root = Json(text);
        Assert.Equal(AgentProtocol.PackageKind, root.GetProperty("kind").GetString());
        Assert.Equal(1, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(Id.ToString(), root.GetProperty("packageId").GetString());
        Assert.Equal("2026-09-02T07:00:00Z", root.GetProperty("createdAtUtc").GetString());
        Assert.Equal("Passat B8, LHD.", root.GetProperty("notes").GetString());
        Assert.Equal("1.2.3", root.GetProperty("application").GetProperty("version").GetString());
        Assert.Equal("Symmetric", root.GetProperty("processor").GetProperty("qConvention").GetString());
        Assert.Equal("catalog", root.GetProperty("processor").GetProperty("maxDelaySource").GetString());
        Assert.False(root.GetProperty("processor").TryGetProperty("peqBandsPerChannel", out _));
        Assert.Equal("FrontAndSub", root.GetProperty("analysis").GetProperty("groupView").GetString());
        Assert.Equal(6, root.GetProperty("analysis").GetProperty("fdwCycles").GetInt32());

        // Every convention a reader needs to use the numbers is stated in the package.
        JsonElement conventions = root.GetProperty("conventions");
        foreach (string key in new[] { "delay", "peqQ", "sumLoss", "sweep", "correlation", "stereo", "groups", "crossoverEdges" })
        {
            Assert.True(conventions.TryGetProperty(key, out _), key);
        }

        // Nothing that names a file, a folder or a machine.
        Assert.DoesNotContain("sourceFilePath", text);
        Assert.DoesNotContain("historyEntryId", text);
        Assert.DoesNotContain(@"D:\", text);
    }

    [Fact]
    public void Build_NamesChannelsByBlockAndSide_AndCarriesTheirChains()
    {
        JsonElement root = Json(AgentPackageBuilder.Build(Inputs(), Id, Clock).Text!);

        JsonElement channels = root.GetProperty("channels");
        Assert.Equal(["A:left", "A:right", "B:mono"], channels.EnumerateArray().Select(c => c.GetProperty("id").GetString()));
        JsonElement aLeft = channels[0];
        Assert.Equal("A", aLeft.GetProperty("block").GetString());
        Assert.Equal("left", aLeft.GetProperty("side").GetString());
        Assert.False(aLeft.GetProperty("mono").GetBoolean());
        Assert.Equal("Front", aLeft.GetProperty("zone").GetString());
        Assert.Equal("left mid.json", aLeft.GetProperty("displayName").GetString());
        JsonElement dsp = aLeft.GetProperty("dsp");
        Assert.Equal(-2.0, dsp.GetProperty("gainDb").GetDouble());
        Assert.Equal(1.42, dsp.GetProperty("delayMs").GetDouble());
        Assert.Equal("BandPass", dsp.GetProperty("crossover").GetProperty("kind").GetString());
        Assert.Equal("LinkwitzRiley", dsp.GetProperty("crossover").GetProperty("highPass").GetProperty("family").GetString());
        Assert.Equal(2800, dsp.GetProperty("crossover").GetProperty("lowPass").GetProperty("frequencyHz").GetDouble());
        JsonElement peq = dsp.GetProperty("peq");
        Assert.Equal(12, peq.GetProperty("hash").GetString()!.Length);
        Assert.Equal(AgentPeqHash.Compute(-1, [new PeqBand(820, 2.1, -2.4)]), peq.GetProperty("hash").GetString());
        Assert.Equal("Peaking", peq.GetProperty("bands")[0].GetProperty("type").GetString());
        Assert.Equal(2.1, peq.GetProperty("bands")[0].GetProperty("q").GetDouble());
        // A cut under a −1 dB preamp: the net response never rises above the preamp.
        Assert.Equal(-1.0, peq.GetProperty("peakDb").GetDouble());
        Assert.True(peq.TryGetProperty("peakHz", out _));

        JsonElement aRight = channels[1];
        Assert.False(aRight.GetProperty("source").GetProperty("available").GetBoolean());
        Assert.Equal("no measurement loaded", aRight.GetProperty("source").GetProperty("unavailableReason").GetString());
        Assert.False(aRight.TryGetProperty("curves", out _));

        JsonElement mono = channels[2];
        Assert.True(mono.GetProperty("mono").GetBoolean());
        Assert.Equal("Sub", mono.GetProperty("zone").GetString());
        Assert.Equal("MovingMic", mono.GetProperty("source").GetProperty("spatialAverage").GetString());
        Assert.Equal(
            ["frequencyHz", "preDspDb", "processedDb", "chainDb", "hybridPreDspDb", "hybridProcessedDb"],
            mono.GetProperty("curves").GetProperty("broadband").GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal([20, 200], mono.GetProperty("source").GetProperty("measuredBandHz").EnumerateArray().Select(v => v.GetDouble()));
    }

    [Fact]
    public void Build_SamplesCurvesOnTheProtocolGrid_AndKeepsHolesAsNull()
    {
        JsonElement root = Json(AgentPackageBuilder.Build(Inputs(), Id, Clock).Text!);

        JsonElement series = root.GetProperty("channels")[0].GetProperty("curves").GetProperty("broadband");
        Assert.Equal(
            ["frequencyHz", "preDspDb", "processedDb", "chainDb", "peqDb"],
            series.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));

        // 20 Hz to 20 kHz at 12 points per octave: 9.97 octaves → 120 points, plus
        // the 20 kHz endpoint; the source is 48 kHz and the processor 96 kHz, so
        // nothing lowers the top.
        List<JsonElement> rows = series.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(121, rows.Count);
        Assert.Equal(20, rows[0][0].GetDouble());
        Assert.Equal(20_000, rows[^1][0].GetDouble());
        // Twelve steps up is exactly one octave.
        Assert.Equal(40, rows[12][0].GetDouble());

        // The synthetic curves run 40 Hz .. 10 kHz; outside them the acoustic
        // columns are holes while the chain columns, which need no measurement,
        // are filled.
        Assert.Equal(JsonValueKind.Null, rows[0][1].ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[0][2].ValueKind);
        Assert.Equal(JsonValueKind.Number, rows[0][3].ValueKind);
        Assert.Equal(JsonValueKind.Number, rows[0][4].ValueKind);
        // Inside, the pre-DSP curve is the synthetic ramp (−0.5 dB per octave above 40 Hz).
        Assert.Equal(-0.5, rows[24][1].GetDouble(), 1);
        // The chain column is the chain alone: gain −2 dB plus preamp −1 dB where
        // the corners and the 820 Hz bell barely reach (320 Hz).
        Assert.InRange(rows[12 * 4][3].GetDouble(), -3.6, -2.9);
    }

    [Fact]
    public void Build_CapsTheGridAtTheLowerOfTheTwoNyquists()
    {
        AgentPackageInputs inputs = Inputs();
        AgentChannelInputs narrow = inputs.Channels[0] with
        {
            Source = inputs.Channels[0].Source! with { SampleRateHz = 32_000 }
        };
        inputs = inputs with { Channels = [narrow, inputs.Channels[1], inputs.Channels[2]] };

        JsonElement root = Json(AgentPackageBuilder.Build(inputs, Id, Clock).Text!);

        List<JsonElement> rows = root.GetProperty("channels")[0].GetProperty("curves")
            .GetProperty("broadband").GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(16_000, rows[^1][0].GetDouble());
    }

    [Fact]
    public void Build_ReadsEachJunctionOffItsOwnReadouts()
    {
        JsonElement root = Json(AgentPackageBuilder.Build(Inputs(), Id, Clock).Text!);

        JsonElement junction = Assert.Single(root.GetProperty("junctions").EnumerateArray());
        Assert.Equal("left:B-A", junction.GetProperty("id").GetString());
        Assert.Equal("B:mono", junction.GetProperty("lower").GetString());
        Assert.Equal("A:left", junction.GetProperty("upper").GetString());
        Assert.Equal(80, junction.GetProperty("crossoverHz").GetDouble());
        Assert.Equal(-1.3, junction.GetProperty("sumLoss").GetProperty("averageDb").GetDouble());
        Assert.Equal(-4.8, junction.GetProperty("sumLoss").GetProperty("dipDb").GetDouble());
        Assert.Equal(41.0, junction.GetProperty("phase").GetProperty("phaseAtCrossoverDeg").GetDouble());
        Assert.Equal(0.12, junction.GetProperty("phase").GetProperty("bestExtraDelayMs").GetDouble());
        Assert.False(junction.GetProperty("phase").TryGetProperty("rivalScore", out _));

        // Lobes: the two synthetic parabolas peak at +0.5 ms (normal) and −1.0 ms
        // (inverted); the normal one is the better of the two.
        JsonElement lobes = junction.GetProperty("lobes");
        Assert.Equal(2, lobes.GetArrayLength());
        Assert.Equal(0.5, lobes[0].GetProperty("extraDelayMs").GetDouble());
        Assert.False(lobes[0].GetProperty("invert").GetBoolean());
        Assert.Equal(-1.0, lobes[1].GetProperty("extraDelayMs").GetDouble());
        Assert.True(lobes[1].GetProperty("invert").GetBoolean());

        JsonElement sweep = junction.GetProperty("sweep");
        Assert.True(sweep.GetProperty("rows").GetArrayLength() <= 48);
        Assert.Equal(-3.0, sweep.GetProperty("rows")[0][0].GetDouble());

        JsonElement correlation = junction.GetProperty("correlation");
        Assert.Equal(0.5, correlation.GetProperty("fullRecordPeak").GetProperty("lagMs").GetDouble());
        Assert.Equal(0.9, correlation.GetProperty("fullRecordPeak").GetProperty("r").GetDouble());
        Assert.Equal(0.08, correlation.GetProperty("arrivalLagMs").GetDouble());

        JsonElement ladder = junction.GetProperty("coherenceLadder");
        Assert.Equal(2, ladder.GetProperty("rows").GetArrayLength());

        // The dense grid spans an octave each way of the crossover and includes it.
        JsonElement curves = junction.GetProperty("curves");
        List<double> grid = curves.GetProperty("rows").EnumerateArray().Select(row => row[0].GetDouble()).ToList();
        Assert.Equal(40, grid[0]);
        Assert.Equal(160, grid[^1]);
        Assert.Contains(80, grid);
        Assert.True(grid.Count >= 48);
    }

    [Fact]
    public void Build_ReportsSidesStereoAndGroups()
    {
        JsonElement root = Json(AgentPackageBuilder.Build(Inputs(), Id, Clock).Text!);

        JsonElement left = root.GetProperty("sides")[0];
        Assert.Equal("left", left.GetProperty("side").GetString());
        Assert.Equal(["A:left", "B:mono"], left.GetProperty("channels").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(-2.1, left.GetProperty("totalSumLoss").GetProperty("averageDb").GetDouble());
        JsonElement right = root.GetProperty("sides")[1];
        Assert.Equal("no channel with a source on this side", right.GetProperty("unavailableReason").GetString());
        Assert.False(right.TryGetProperty("sumDb", out _));

        JsonElement stereo = Assert.Single(root.GetProperty("stereo").EnumerateArray());
        Assert.Equal("A", stereo.GetProperty("block").GetString());
        Assert.Equal(-0.25, stereo.GetProperty("deltaMs").GetDouble());
        Assert.Equal(1.5, stereo.GetProperty("levelDeltaDb").GetDouble());

        JsonElement group = Assert.Single(root.GetProperty("groups").EnumerateArray());
        Assert.Equal("Rear", group.GetProperty("zone").GetString());
        Assert.Equal(6.5, group.GetProperty("delayMs").GetDouble());
    }

    [Fact]
    public void Build_SaysWhereTheTuneStandsWithSpatialAverages()
    {
        // The fixture: the left view shows A:left (measured, no capture) and
        // B:mono (a moving-microphone capture, hybrid curves in the package). One
        // of two shown channels drawn from its average: partial.
        AgentPackageInputs inputs = Inputs();
        JsonElement status = Json(AgentPackageBuilder.Build(inputs, Id, Clock).Text!)
            .GetProperty("analysis").GetProperty("spatialAverage");
        Assert.Equal("MovingMic", status.GetProperty("mode").GetString());
        Assert.True(status.GetProperty("hybridTicked").GetBoolean());
        Assert.True(status.GetProperty("hybridDrawn").GetBoolean());
        Assert.Equal("partial", status.GetProperty("status").GetString());
        Assert.Equal(2, status.GetProperty("channelsShown").GetInt32());
        Assert.Equal(1, status.GetProperty("channelsWithCapture").GetInt32());
        Assert.Equal(1, status.GetProperty("channelsDrawn").GetInt32());

        // Each channel lists what it holds, and only when it holds something.
        JsonElement channels = Json(AgentPackageBuilder.Build(inputs, Id, Clock).Text!).GetProperty("channels");
        Assert.False(channels[0].GetProperty("source").TryGetProperty("spatialAverageCaptures", out _));
        Assert.Equal(["MovingMic"], channels[2].GetProperty("source").GetProperty("spatialAverageCaptures").EnumerateArray().Select(c => c.GetString()));

        // Every shown channel drawn from its average.
        AgentPackageInputs everywhere = inputs with
        {
            Channels = inputs.Channels
                .Select(channel => channel.Source == null
                    ? channel
                    : channel with { Source = channel.Source with { SpatialAverage = "MovingMic", SpatialAverageCaptures = ["MovingMic", "MicArray"], HybridPreDsp = Ramp(-2), HybridProcessed = Ramp(-4) } })
                .ToList()
        };
        Assert.Equal("active", Status(everywhere));

        // "Drawn" is the hybrid curves actually in the package, not the
        // attachment: captures the view did not turn into curves — the box off,
        // the mode reading another family, a group view — are captured, not shown.
        AgentPackageInputs notDrawn = everywhere with
        {
            Analysis = everywhere.Analysis with { HybridDrawn = false },
            Channels = everywhere.Channels
                .Select(channel => channel.Source == null
                    ? channel
                    : channel with { Source = channel.Source with { HybridPreDsp = null, HybridProcessed = null } })
                .ToList()
        };
        Assert.Equal("capturedNotShown", Status(notDrawn));

        // A channel the view leaves out has curves of its own but no hybrid ones,
        // whatever it holds; a muted one has no curves at all. Neither is counted:
        // the status describes the channels the diagnostics are built from.
        AgentPackageInputs outsideTheView = everywhere with
        {
            Channels =
            [
                .. everywhere.Channels,
                new AgentChannelInputs("E", AgentChannelSide.Left, VirtualCrossoverZone.Rear,
                    "rear left.json", true, false, new VirtualCrossoverChannelSettings(), 96_000,
                    new AgentSourceInputs(48_000, new MeasuredBand(60, 20_000), "MovingMic", ["MovingMic"], Ramp(0), Ramp(-1), null, null, null, null)),
                new AgentChannelInputs("F", AgentChannelSide.Left, VirtualCrossoverZone.Front,
                    "muted.json", false, false, new VirtualCrossoverChannelSettings(), 96_000,
                    new AgentSourceInputs(48_000, new MeasuredBand(60, 20_000), null, [], null, null, null, null, null, "channel muted"))
            ]
        };
        JsonElement outside = Json(AgentPackageBuilder.Build(outsideTheView, Id, Clock).Text!)
            .GetProperty("analysis").GetProperty("spatialAverage");
        Assert.Equal("active", outside.GetProperty("status").GetString());
        Assert.Equal(2, outside.GetProperty("channelsShown").GetInt32());

        // Nothing to draw from at all: the case the assistant is told to press.
        AgentPackageInputs none = notDrawn with
        {
            Channels = notDrawn.Channels
                .Select(channel => channel.Source == null
                    ? channel
                    : channel with { Source = channel.Source with { SpatialAverage = null, SpatialAverageCaptures = [] } })
                .ToList()
        };
        Assert.Equal("none", Status(none));

        static string Status(AgentPackageInputs inputs) =>
            Json(AgentPackageBuilder.Build(inputs, Id, Clock).Text!)
                .GetProperty("analysis").GetProperty("spatialAverage").GetProperty("status").GetString()!;
    }

    [Fact]
    public void Build_ReadsTheSidesLevelAgainstTheTarget()
    {
        // The left sum is Ramp(2): 2 dB at 40 Hz falling half a dB per octave,
        // against a flat target at -4 dB. On the 20 Hz..20 kHz grid the sum has
        // points from 40 Hz to 10 kHz; the median of sum - target lands in the
        // middle of that span, near 640 Hz: 2 - 0.5 * log2(16) + 4 = 4 dB.
        JsonElement root = Json(AgentPackageBuilder.Build(Inputs(), Id, Clock).Text!);

        Assert.Equal(4.0, root.GetProperty("sides")[0].GetProperty("sumVsTargetDb").GetDouble(), 1);
        // The hybrid sum, Ramp(3), reads one dB higher — its own datum, so the
        // assistant is not sent to move the target by a point measurement while
        // the tune is judged on averages.
        Assert.Equal(5.0, root.GetProperty("sides")[0].GetProperty("hybridSumVsTargetDb").GetDouble(), 1);
        Assert.False(root.GetProperty("sides")[1].TryGetProperty("sumVsTargetDb", out _));
        Assert.False(root.GetProperty("sides")[1].TryGetProperty("hybridSumVsTargetDb", out _));
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        string first = AgentPackageBuilder.Build(Inputs(), Id, Clock).Text!;
        string second = AgentPackageBuilder.Build(Inputs(), Id, Clock).Text!;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Build_ShedsOptionalSeriesInAFixedOrder_AndSaysWhichWent()
    {
        AgentPackageInputs inputs = Inputs();
        int full = AgentPackageBuilder.Build(inputs, Id, Clock).JsonBytes;

        // Just under the full size as the TARGET: only the first optional series
        // has to go, and the ceiling is not what decides.
        AgentPackageBuildResult trimmed = AgentPackageBuilder.Build(inputs, Id, Clock, targetBytes: full - 1);
        Assert.True(trimmed.Succeeded, trimmed.Error);
        Assert.Equal(["junctions[].sweep"], trimmed.Omitted);
        JsonElement junction = Json(trimmed.Text!).GetProperty("junctions")[0];
        Assert.False(junction.TryGetProperty("sweep", out _));
        Assert.True(junction.TryGetProperty("lobes", out _));
        Assert.Equal(["junctions[].sweep"], Json(trimmed.Text!).GetProperty("omitted").EnumerateArray().Select(o => o.GetString()));

        // Over the target every optional series goes, and the mandatory payload
        // may still grow up to the ceiling.
        AgentPackageBuildResult mandatory = AgentPackageBuilder.Build(inputs, Id, Clock, targetBytes: 100, maxBytes: full);
        Assert.True(mandatory.Succeeded, mandatory.Error);
        Assert.Equal(5, mandatory.Omitted.Count);
        Assert.True(mandatory.JsonBytes < full);

        // Nothing optional is enough: the failure names the size, and no text is handed out.
        AgentPackageBuildResult failed = AgentPackageBuilder.Build(inputs, Id, Clock, targetBytes: 100, maxBytes: 100);
        Assert.False(failed.Succeeded);
        Assert.Null(failed.Text);
        Assert.Equal(5, failed.Omitted.Count);
        Assert.Contains("limit is 0 KB", failed.Error);
    }

    [Fact]
    public void Limits_NameTheOperationsThisBuildCanRun()
    {
        JsonElement limits = Json(AgentPackageBuilder.Build(Inputs(), Id, Clock).Text!).GetProperty("limits");

        string[] operations = limits.GetProperty("operations")
            .EnumerateArray().Select(operation => operation.GetString()!).ToArray();

        Assert.Equal(AgentProtocol.Operations, operations);
        Assert.Contains("useSpatialAverage", operations);
        Assert.Contains("runAutoCrossover", operations);
        Assert.Contains("runAutoDelay", operations);
        // The protocol describes this one; this build reviews it and refuses.
        Assert.DoesNotContain("autoTunePeq", operations);
    }

    [Fact]
    public void Limits_RestateWhatTheProjectValidatorEnforces()
    {
        JsonElement limits = Json(AgentPackageBuilder.Build(Inputs(), Id, Clock).Text!).GetProperty("limits");
        double lowHz = limits.GetProperty("crossoverHz")[0].GetDouble();
        double highHz = limits.GetProperty("crossoverHz")[1].GetDouble();
        double preamp = limits.GetProperty("peqPreampDb").GetDouble();
        Assert.Equal(EqualizationCurve.MaxBandCount, limits.GetProperty("peqBands").GetInt32());
        Assert.Equal([12, 24, 36, 48], limits.GetProperty("slopes").GetProperty("LinkwitzRiley").EnumerateArray().Select(s => s.GetInt32()));

        // The package's numbers are the file validator's: one hertz past the edge fails it.
        Validates(new VirtualCrossoverChannelSettings { LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, highHz, 12) }, valid: true);
        Validates(new VirtualCrossoverChannelSettings { LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, highHz + 1, 12) }, valid: false);
        Validates(new VirtualCrossoverChannelSettings { LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, lowHz, 12) }, valid: true);
        Validates(new VirtualCrossoverChannelSettings { LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, lowHz - 1, 12) }, valid: false);
        Validates(new VirtualCrossoverChannelSettings { PeqPreampDb = preamp }, valid: true);
        Validates(new VirtualCrossoverChannelSettings { PeqPreampDb = preamp + 1 }, valid: false);
    }

    [Fact]
    public void Sampling_GridsInterpolationThinningAndLobes()
    {
        List<double> grid = AgentCurveSampling.LogGrid(20, 20_000, 12);
        Assert.Equal(121, grid.Count);
        Assert.Equal(20, grid[0]);
        Assert.Equal(20_000, grid[^1]);
        Assert.Empty(AgentCurveSampling.LogGrid(100, 100, 12));
        Assert.Empty(AgentCurveSampling.LogGrid(200, 100, 12));

        List<double> junction = AgentCurveSampling.JunctionGrid(3_000, 20, 5_000);
        Assert.Equal(1_500, junction[0]);
        Assert.Equal(5_000, junction[^1]);
        Assert.Contains(3_000, junction);

        // Log-linear between neighbours; a NaN neighbour makes a hole; nothing outside.
        SignalPoint[] curve = [new(100, 0), new(200, 6), new(400, double.NaN), new(800, 12)];
        Assert.Equal(3, AgentCurveSampling.Sample(curve, 141.4213562)!.Value, 3);
        Assert.Equal(6.0, AgentCurveSampling.Sample(curve, 200));
        Assert.Null(AgentCurveSampling.Sample(curve, 300));
        Assert.Null(AgentCurveSampling.Sample(curve, 400));
        Assert.Null(AgentCurveSampling.Sample(curve, 600));
        Assert.Null(AgentCurveSampling.Sample(curve, 99));
        Assert.Null(AgentCurveSampling.Sample(curve, 801));

        List<int> thinned = AgentCurveSampling.Thin(Enumerable.Range(0, 100).ToList(), 10);
        Assert.Equal(10, thinned.Count);
        Assert.Equal(0, thinned[0]);
        Assert.Equal(99, thinned[^1]);

        // Lobes are interior local maxima; a sweep climbing into its edge has none there.
        SignalPoint[] climbing = [new(-1, -6), new(0, -4), new(1, -2)];
        SignalPoint[] peaked = [new(-1, -6), new(0, -1), new(1, -3), new(2, -2), new(3, -5)];
        List<AgentLobe> lobes = AgentCurveSampling.Lobes(peaked, climbing, 5);
        Assert.Equal([0.0, 2.0], lobes.Select(lobe => lobe.DelayMs));
        Assert.All(lobes, lobe => Assert.False(lobe.Invert));

        Assert.Equal(1235, AgentCurveSampling.Frequency(1234.5));
        Assert.Equal(20.03, AgentCurveSampling.Frequency(20.034));
        Assert.Equal(12_350, AgentCurveSampling.Frequency(12_345));
    }

    private static void Validates(VirtualCrossoverChannelSettings settings, bool valid)
    {
        if (valid)
        {
            settings.Validate();
        }
        else
        {
            Assert.Throws<InvalidDataException>(settings.Validate);
        }
    }

    private static JsonElement Json(string envelope)
    {
        int begin = envelope.IndexOf(AgentProtocol.PackageJsonBegin, StringComparison.Ordinal) +
            AgentProtocol.PackageJsonBegin.Length;
        int end = envelope.IndexOf(AgentProtocol.PackageJsonEnd, StringComparison.Ordinal);
        return JsonDocument.Parse(envelope[begin..end].Trim()).RootElement;
    }

    // A −0.5 dB/octave ramp from 40 Hz to 10 kHz, with a hole (NaN) at 1 kHz.
    private static List<SignalPoint> Ramp(double offsetDb)
    {
        var points = new List<SignalPoint>();
        for (double frequency = 40; frequency <= 10_000; frequency *= 1.02)
        {
            double value = offsetDb - 0.5 * Math.Log2(frequency / 40);
            points.Add(new SignalPoint(frequency, Math.Abs(frequency - 1_000) < 10 ? double.NaN : value));
        }

        return points;
    }

    private static List<SignalPoint> Parabola(double centerMs, double topDb, double fromMs, double toMs)
    {
        var points = new List<SignalPoint>();
        for (double delay = fromMs; delay <= toMs + 1e-9; delay += 0.05)
        {
            points.Add(new SignalPoint(Math.Round(delay, 2), topDb - 2 * (delay - centerMs) * (delay - centerMs)));
        }

        return points;
    }

    private static AgentPackageInputs Inputs()
    {
        var aLeftSettings = new VirtualCrossoverChannelSettings
        {
            DisplayName = "left mid.json",
            SourceFilePath = @"D:\hobby\AMP\left mid.json",
            GainDb = -2.0,
            DelayMs = 1.42,
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_800, 24),
            PeqPreampDb = -1,
            PeqBands = [new PeqBand(820, 2.1, -2.4)]
        };
        var aRightSettings = new VirtualCrossoverChannelSettings { DisplayName = "right mid.json" };
        var bSettings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)
        };

        List<SignalPoint> aLeftProcessed = Ramp(-1.5);
        List<SignalPoint> bProcessed = Ramp(-3);
        var aLeft = new AgentChannelInputs("A", AgentChannelSide.Left, VirtualCrossoverZone.Front,
            "left mid.json", true, false, aLeftSettings, 96_000,
            new AgentSourceInputs(48_000, new MeasuredBand(40, 20_000), null, [], Ramp(0), aLeftProcessed, null, null, null, null));
        var aRight = new AgentChannelInputs("A", AgentChannelSide.Right, VirtualCrossoverZone.Front,
            "right mid.json", true, false, aRightSettings, 96_000, null);
        var b = new AgentChannelInputs("B", AgentChannelSide.Mono, VirtualCrossoverZone.Sub,
            string.Empty, true, false, bSettings, 96_000, null);
        // B has curves on the left side (it sums there) but no source of its own
        // listed: the side's channel list is what says it played.
        var bWithCurves = b with
        {
            Source = new AgentSourceInputs(48_000, new MeasuredBand(20, 200), "MovingMic", ["MovingMic"], Ramp(-3), bProcessed, Ramp(-2), Ramp(-4), null, null)
        };

        var correlation = new JunctionCorrelationView(
            "B-A", "A", 80, 40, 160,
            Whitened: [new(-3, 0.1), new(-1, -0.6), new(0.5, 0.9), new(3, 0.0)],
            WhitenedDirect: [new(-3, 0.0), new(0.5, 0.8), new(3, 0.0)],
            ScoreNormal: Parabola(0.5, -0.4, -3, 3),
            ScoreInverted: Parabola(-1.0, -1.1, -3, 3),
            ArrivalLagMs: 0.08);
        var coherence = new JunctionCoherenceView(
            "B-A", "A", 80, 40, 160,
            [
                new VirtualCrossoverAnalysis.ArrivalCoherencePoint(56, 0.2, 0.9, 0.7, 8.9),
                new VirtualCrossoverAnalysis.ArrivalCoherencePoint(113, 0.1, 0.8, 0.6, 4.4)
            ]);
        var phase = new JunctionPhaseResult(
            CurrentScore: 0.71, PhaseAtCrossoverDeg: 41, PhaseConsistency: 0.82,
            BestExtraDelayMs: 0.12, BestInvert: false, BestScore: 0.93, OppositePolarityScore: 0.31,
            RivalExtraDelayMs: null, RivalScore: null, LobeMargin: 0.05, FitDelayMs: 0.1, FitRmsDeg: 12);

        var leftSide = new AgentSideInputs(
            AgentChannelSide.Left,
            ["A:left", "B:mono"],
            Ramp(2),
            Ramp(3),
            Ramp(-1).Select(point => new SignalPoint(point.X, double.IsNaN(point.Y) ? point.Y : -Math.Abs(point.Y) / 4)).ToList(),
            [
                new VirtualCrossoverMetric.Entry("B/A", -1.3, -4.8, 40, 160, IsTotal: false),
                new VirtualCrossoverMetric.Entry("total", -2.1, -4.8, 40, 2_800, IsTotal: true)
            ],
            [new VirtualCrossoverMetric.PhaseEntry("B/A", "B", 80, 40, 160, phase)],
            [new AgentJunctionInputs("B", "A", 80, 40, 160, bProcessed, aLeftProcessed, correlation, coherence)],
            null);
        var rightSide = new AgentSideInputs(
            AgentChannelSide.Right, [], null, null, null, [], [], [], "no channel with a source on this side");

        return new AgentPackageInputs(
            "1.2.3",
            "Passat B8, LHD.",
            new AgentProcessorInputs("helix-dsp-ultra-s", "HELIX DSP ULTRA S", false, 96_000, false,
                PeqQConvention.Symmetric, 10, MaxDelayFromCatalog: true),
            new AgentAnalysisInputs(
                VirtualCrossoverGroupView.FrontAndSub, false, 6, true,
                VirtualCrossoverSpatialAverageMode.MovingMic, HybridTicked: true, HybridDrawn: true,
                PhaseWindowMode.FrequencyDependent, 6, PhaseDetrendMode.Auto,
                5, 100, 20, null, null, null, 8.66, "ECM8000_90deg.txt", 0.25, false, -1, 0),
            new AgentTargetInputs(-4, TargetPreset.Flat, TargetCurveSpec.FromPreset(TargetPreset.Flat), 3, null),
            [aLeft, aRight, bWithCurves],
            [leftSide, rightSide],
            [new VirtualCrossoverMetric.StereoDelta("A", 1.0, 1.25, 300, 3_000, 1.5)],
            [new VirtualCrossoverMetric.GroupDelta(VirtualCrossoverZone.Rear, 6.5, -3.2, 300, 3_000)]);
    }
}
