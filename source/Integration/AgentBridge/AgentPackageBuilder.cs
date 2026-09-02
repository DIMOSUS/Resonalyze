using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>What Copy for AI produced: the clipboard text, or one sentence why not.</summary>
internal sealed record AgentPackageBuildResult(
    string? Text,
    int JsonBytes,
    IReadOnlyList<string> Omitted,
    string? Error)
{
    public bool Succeeded => Text != null;
}

/// <summary>
/// Turns the panel's gathered inputs into the text that goes on the clipboard:
/// the envelope with the inline rules, and inside it one compact JSON package.
/// Pure — the same inputs, id and clock give the same bytes — and sized for a
/// chat: over the ceiling it drops optional series in a fixed order and says
/// which in <c>omitted</c>, so a large installation is trimmed the same way
/// every time rather than differently per run.
/// </summary>
internal static class AgentPackageBuilder
{
    private const int MaxLobes = 5;
    private const int MaxSweepRows = 48;
    private const int MaxCorrelationRows = 48;

    // Which optional series are in, in the order they go out. Each name is what
    // `omitted` reports; the reader can tell what it is not seeing.
    private static readonly string[] OmissionOrder =
    [
        "junctions[].sweep",
        "junctions[].coherenceLadder",
        "channels[].curves.broadband.coherence",
        "junctions[].correlation.curve",
        "junctions[].curves"
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.Strict
    };

    // The units and signs a reader has to know to use the numbers. Stated in the
    // package itself because the guide may be out of reach.
    private static readonly IReadOnlyDictionary<string, string> Conventions =
        new Dictionary<string, string>
        {
            ["frequency"] = "Hz",
            ["level"] = "dB",
            ["delay"] = "ms; positive delay = the channel plays later",
            ["phase"] = "degrees",
            ["coherence"] = "gamma squared, 0..1",
            ["peqQ"] = "RBJ cookbook Q; the processor's own Q convention is applied only when numbers leave for the device",
            ["peqPeak"] = "peakDb/peakHz = the highest point of the bank's NET response (preamp + all bands); above 0 dB the device is asked for more than unity there and a full-scale signal clips — lower the preamp by that much or trim the boost; a boost inside a wider cut or under a negative preamp is not a headroom problem",
            ["crossoverEdges"] = "both edges are stored; kind says which act: LowPass uses lowPass, HighPass uses highPass, BandPass both, Off none",
            ["curves"] = "preDspDb = measured response before the chain (Raw); processedDb = through the chain (Processed); chainDb = the chain alone; peqDb = the PEQ alone; hybridPreDspDb = the spatial average before the chain and hybridProcessedDb through it, both placed on the same level axis as the impulse-response curves (the hybrid datum applied) so all columns compare directly; null = not measured there",
            ["sumLoss"] = "dB <= 0: how far the coherent sum falls short of the magnitude sum over the junction band; averageDb over the band, dipDb its worst point",
            ["phase"] = "junction phase read-out: bestExtraDelayMs and bestInvert are applied to the LOWER channel; scores in -1..1, higher is better",
            ["sweep"] = "summation score vs extra delay applied to the UPPER channel, both polarities; scoreDb <= 0, 0 = perfect; lobes are its local maxima",
            ["correlation"] = "GCC-PHAT between the pair: lagMs is the delay that, added to the UPPER channel, aligns it with the lower; a positive peak is a normal-polarity alignment, a negative trough the same with the upper channel inverted; arrivalLagMs = lower arrival minus upper arrival",
            ["coherenceLadder"] = "per band: lagMs = the upper channel's arrival relative to the lower at that frequency, peakR the best coherence found, currentR the coherence at the current alignment",
            ["stereo"] = "per block: deltaMs = left arrival minus right arrival (positive = the right side leads); levelDeltaDb = left minus right",
            ["groups"] = "each zone against the front stage: delayMs = the zone's arrival minus the front's; levelDb = the zone's level minus the front's"
        };

    /// <param name="targetBytes">
    /// What the builder aims under: optional series go, in order, until the JSON
    /// fits it. The size a chat with a modest context takes comfortably.
    /// </param>
    /// <param name="maxBytes">
    /// The ceiling: once every optional series is gone, the mandatory payload may
    /// grow up to here; beyond it nothing is copied.
    /// </param>
    public static AgentPackageBuildResult Build(
        AgentPackageInputs inputs,
        Guid packageId,
        DateTimeOffset createdAtUtc,
        int targetBytes = AgentProtocol.TargetPackageBytes,
        int maxBytes = AgentProtocol.MaxPackageBytes)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var omitted = new List<string>();
        string json = string.Empty;
        int bytes = 0;
        for (int level = 0; level <= OmissionOrder.Length; level++)
        {
            AgentPackage package = Assemble(inputs, packageId, createdAtUtc, omitted);
            json = JsonSerializer.Serialize(package, Options);
            bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes <= targetBytes)
            {
                return new AgentPackageBuildResult(Envelope(json), bytes, omitted, null);
            }
            if (level < OmissionOrder.Length)
            {
                omitted.Add(OmissionOrder[level]);
            }
        }

        if (bytes <= maxBytes)
        {
            return new AgentPackageBuildResult(Envelope(json), bytes, omitted, null);
        }

        return new AgentPackageBuildResult(
            null, bytes, omitted,
            $"The package is {bytes / 1024} KB even without its optional series; " +
            $"the limit is {maxBytes / 1024} KB. Try a view with fewer channels.");
    }

    private static string Envelope(string json) =>
        AgentProtocol.PackageHeader + "\r\n\r\n" +
        AgentProtocol.InlineRules + "\r\n\r\n" +
        AgentProtocol.PackageJsonBegin + "\r\n" +
        json + "\r\n" +
        AgentProtocol.PackageJsonEnd + "\r\n";

    private static AgentPackage Assemble(
        AgentPackageInputs inputs,
        Guid packageId,
        DateTimeOffset createdAtUtc,
        IReadOnlyList<string> omitted)
    {
        bool keepSweep = !omitted.Contains(OmissionOrder[0]);
        bool keepLadder = !omitted.Contains(OmissionOrder[1]);
        bool keepCoherence = !omitted.Contains(OmissionOrder[2]);
        bool keepCorrelationCurve = !omitted.Contains(OmissionOrder[3]);
        bool keepJunctionCurves = !omitted.Contains(OmissionOrder[4]);

        var junctions = new List<AgentPackageJunction>();
        var sides = new List<AgentPackageSide>();
        foreach (AgentSideInputs side in inputs.Sides)
        {
            sides.Add(BuildSide(side, inputs));
            foreach (AgentJunctionInputs junction in side.Junctions)
            {
                junctions.Add(BuildJunction(
                    side, junction, inputs,
                    keepSweep, keepLadder, keepCorrelationCurve, keepJunctionCurves));
            }
        }

        return new AgentPackage(
            AgentProtocol.PackageKind,
            AgentProtocol.Version,
            packageId.ToString("D"),
            createdAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
            new AgentPackageApplication("Resonalyze", inputs.ApplicationVersion),
            Conventions,
            inputs.Notes,
            BuildProcessor(inputs.Processor),
            BuildLimits(),
            BuildAnalysis(inputs.Analysis),
            BuildTarget(inputs.Target),
            inputs.Channels.Select(channel => BuildChannel(channel, inputs, keepCoherence)).ToList(),
            sides,
            junctions,
            inputs.Stereo.Select(BuildStereo).ToList(),
            inputs.Groups.Select(BuildGroup).ToList(),
            omitted);
    }

    private static AgentPackageProcessor BuildProcessor(AgentProcessorInputs processor) =>
        new(
            processor.ModelId,
            processor.DisplayName,
            processor.IsCustom,
            processor.SampleRateHz,
            processor.FollowsMeasurements,
            processor.QConvention.ToString(),
            processor.MaxDelayMs,
            processor.MaxDelayFromCatalog ? "catalog" : "default (device not looked up)",
            // The catalog knows no per-device PEQ count; saying so beats guessing.
            PeqBandsPerChannel: null);

    // The ranges the import will hold a reply to. The crossover corner range and
    // the preamp cap restate VirtualCrossoverChannelSettings.Validate, pinned by
    // AgentPackageBuilderTests so the two cannot drift.
    private static AgentPackageLimits BuildLimits() =>
        new(
            [AgentProposalValidator.MinimumGainDb, AgentProposalValidator.MaximumGainDb],
            AgentProposalValidator.GainStepDb,
            [AgentProposalValidator.MinimumDelayMs, AgentProposalValidator.MaximumDelayMs],
            AgentProposalValidator.DelayStepMs,
            EqualizationCurve.MaxBandCount,
            60,
            [10, 24_000],
            Enum.GetValues<CrossoverFilterFamily>().ToDictionary(
                family => family.ToString(),
                family => CrossoverFilter.SupportedSlopes(family).ToArray()),
            [0, CrossoverFilter.MaximumChebyshevRippleDb]);

    private static AgentPackageAnalysis BuildAnalysis(AgentAnalysisInputs analysis) =>
        new(
            analysis.GroupView.ToString(),
            analysis.ActiveSideRight ? "right" : "left",
            analysis.SmoothingInverseOctaves,
            analysis.PsychoacousticSmoothing,
            analysis.SpatialAverageMode?.ToString(),
            analysis.HybridOn,
            analysis.PhaseWindowMode.ToString(),
            analysis.FdwCycles,
            analysis.DetrendMode.ToString(),
            new AgentPackageGateShape(analysis.GateLeftMs, analysis.GatePlateauMs, analysis.GateRightMs),
            new AgentPackageGate(analysis.LeftGateOffsetMs, analysis.LeftDetrendMs),
            new AgentPackageGate(analysis.RightGateOffsetMs, analysis.RightDetrendMs),
            analysis.CalibrationName,
            analysis.StereoSceneOffsetMs,
            analysis.RightHandDrive,
            analysis.StereoLevelDifferenceDb,
            analysis.RearFillOffsetMs);

    private static AgentPackageTarget BuildTarget(AgentTargetInputs target)
    {
        TargetCurveSpec spec = target.Spec;
        List<double> grid = AgentCurveSampling.LogGrid(
            AgentCurveSampling.BroadbandLowHz, AgentCurveSampling.BroadbandHighHz,
            AgentCurveSampling.BroadbandPointsPerOctave);
        var rows = grid
            .Select(frequency => new double?[]
            {
                AgentCurveSampling.Frequency(frequency),
                AgentCurveSampling.Round(target.LevelDb + spec.Evaluate(frequency), 1)
            })
            .ToList();
        return new AgentPackageTarget(
            target.LevelDb,
            target.Preset.ToString(),
            spec.TiltDbPerOctave,
            new AgentPackageShelf(spec.BassShelfGainDb, spec.BassShelfFrequencyHz, spec.BassShelfWidthOctaves),
            new AgentPackageShelf(spec.TrebleShelfGainDb, spec.TrebleShelfFrequencyHz, spec.TrebleShelfWidthOctaves),
            new AgentPackageShelf(spec.PresenceGainDb, spec.PresenceFrequencyHz, spec.PresenceWidthOctaves),
            target.ToleranceDb,
            target.ImportedName,
            new AgentSeries(["frequencyHz", "targetDb"], rows));
    }

    private static AgentPackageChannel BuildChannel(
        AgentChannelInputs channel, AgentPackageInputs inputs, bool keepCoherence)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        AgentSourceInputs? source = channel.Source;
        (double PeakDb, double PeakHz) peak = AgentPeqHeadroom.Peak(
            settings.PeqPreampDb, settings.PeqBands, channel.ProcessorSampleRateHz);
        var dsp = new AgentPackageDsp(
            settings.GainDb,
            settings.DelayMs,
            settings.InvertPolarity,
            new AgentPackageCrossover(
                settings.CrossoverKind.ToString(),
                Edge(settings.HighPassEdge),
                Edge(settings.LowPassEdge)),
            new AgentPackagePeq(
                settings.PeqPreampDb,
                AgentPeqHash.Compute(settings.PeqPreampDb, settings.PeqBands),
                Math.Round(peak.PeakDb, 1),
                AgentCurveSampling.Frequency(peak.PeakHz),
                settings.PeqBands
                    .Select(band => new AgentPackageBand(
                        band.Type.ToString(), band.FrequencyHz, band.Q, band.GainDb))
                    .ToList()));

        var packageSource = new AgentPackageSource(
            source != null,
            source?.SampleRateHz,
            source == null ? null : [source.MeasuredBand.LowEdgeHz, source.MeasuredBand.HighEdgeHz],
            source?.SpatialAverage,
            source?.UnavailableReason ?? (source == null ? "no measurement loaded" : null));

        return new AgentPackageChannel(
            channel.Id,
            channel.Block,
            AgentChannelIds.SideName(channel.Side),
            channel.Side == AgentChannelSide.Mono,
            channel.Zone.ToString(),
            channel.DisplayName,
            channel.Enabled,
            channel.Bypass,
            packageSource,
            dsp,
            BuildChannelCurves(channel, keepCoherence));
    }

    private static AgentPackageEdge Edge(CrossoverEdge edge) =>
        new(edge.Family.ToString(), edge.FrequencyHz, edge.SlopeDbPerOctave, edge.RippleDb);

    // One row per grid point: the acoustic columns from the screen's curves, the
    // chain columns from the filters alone (built at the PROCESSOR's rate, as the
    // simulation builds them). A column the channel has nothing for is left out
    // of the series rather than filled with nulls.
    private static AgentPackageChannelCurves? BuildChannelCurves(
        AgentChannelInputs channel, bool keepCoherence)
    {
        AgentSourceInputs? source = channel.Source;
        if (source == null || (source.PreDsp == null && source.Processed == null))
        {
            return null;
        }

        double highHz = Math.Min(
            AgentCurveSampling.BroadbandHighHz,
            Math.Min(source.SampleRateHz, channel.ProcessorSampleRateHz) / 2.0);
        List<double> grid = AgentCurveSampling.LogGrid(
            AgentCurveSampling.BroadbandLowHz, highHz, AgentCurveSampling.BroadbandPointsPerOctave);

        DspChannelChain chain = channel.Bypass ? DspChannelChain.Identity : channel.Settings.ToChain();
        bool hasChain = !channel.Bypass &&
            (channel.Settings.CrossoverKind != CrossoverKind.Off ||
             channel.Settings.PeqBands.Count > 0 || channel.Settings.PeqPreampDb != 0 ||
             channel.Settings.GainDb != 0);
        bool hasPeq = !channel.Bypass &&
            (channel.Settings.PeqBands.Count > 0 || channel.Settings.PeqPreampDb != 0);
        PreparedDspResponse? chainResponse = hasChain
            ? PreparedDspResponse.Create(chain, channel.ProcessorSampleRateHz)
            : null;
        PreparedDspResponse? peqResponse = hasPeq
            ? PreparedDspResponse.Create(
                new DspChannelChain(
                    0, 0, false, CrossoverSpec.Off,
                    new EqualizationCurve(channel.Settings.PeqBands, channel.Settings.PeqPreampDb)),
                channel.ProcessorSampleRateHz)
            : null;
        bool coherence = keepCoherence && source.Coherence != null;

        var columns = new List<string> { "frequencyHz" };
        if (source.PreDsp != null) columns.Add("preDspDb");
        if (source.Processed != null) columns.Add("processedDb");
        if (chainResponse != null) columns.Add("chainDb");
        if (peqResponse != null) columns.Add("peqDb");
        if (source.HybridPreDsp != null) columns.Add("hybridPreDspDb");
        if (source.HybridProcessed != null) columns.Add("hybridProcessedDb");
        if (coherence) columns.Add("coherence");

        var rows = new List<double?[]>(grid.Count);
        foreach (double frequency in grid)
        {
            var row = new List<double?> { AgentCurveSampling.Frequency(frequency) };
            if (source.PreDsp != null) row.Add(AgentCurveSampling.Round(AgentCurveSampling.Sample(source.PreDsp, frequency), 1));
            if (source.Processed != null) row.Add(AgentCurveSampling.Round(AgentCurveSampling.Sample(source.Processed, frequency), 1));
            if (chainResponse != null) row.Add(AgentCurveSampling.Round(Decibels(chainResponse, frequency), 1));
            if (peqResponse != null) row.Add(AgentCurveSampling.Round(Decibels(peqResponse, frequency), 1));
            if (source.HybridPreDsp != null) row.Add(AgentCurveSampling.Round(AgentCurveSampling.Sample(source.HybridPreDsp, frequency), 1));
            if (source.HybridProcessed != null) row.Add(AgentCurveSampling.Round(AgentCurveSampling.Sample(source.HybridProcessed, frequency), 1));
            if (coherence) row.Add(AgentCurveSampling.Round(AgentCurveSampling.Sample(source.Coherence!, frequency), 2));
            rows.Add(row.ToArray());
        }

        return new AgentPackageChannelCurves(new AgentSeries(columns, rows));
    }

    private static double Decibels(PreparedDspResponse response, double frequencyHz) =>
        DataHelper.AmplitudeToDecibels(response.Response(frequencyHz).Magnitude);

    private static AgentPackageSide BuildSide(AgentSideInputs side, AgentPackageInputs inputs)
    {
        string sideName = AgentChannelIds.SideName(side.Side);
        // The ids the capture says went into this side's sum — not every channel
        // with curves, since channels outside the view get curves of their own.
        List<string> channels = side.ChannelIds.ToList();

        AgentSeries? sum = null;
        if (side.Sum != null)
        {
            List<double> grid = AgentCurveSampling.LogGrid(
                AgentCurveSampling.BroadbandLowHz, AgentCurveSampling.BroadbandHighHz,
                AgentCurveSampling.BroadbandPointsPerOctave);
            var rows = grid
                .Select(frequency => new double?[]
                {
                    AgentCurveSampling.Frequency(frequency),
                    AgentCurveSampling.Round(AgentCurveSampling.Sample(side.Sum, frequency), 1)
                })
                .Where(row => row[1] != null)
                .ToList();
            sum = new AgentSeries(["frequencyHz", "sumDb"], rows);
        }

        VirtualCrossoverMetric.Entry? total = side.Entries
            .Where(entry => entry.IsTotal)
            .Select(entry => (VirtualCrossoverMetric.Entry?)entry)
            .FirstOrDefault();
        return new AgentPackageSide(
            sideName,
            channels,
            sum,
            total is { } t ? new AgentPackageLoss(Round1(t.AverageDb), AgentCurveSampling.Round(t.DipDb, 1)) : null,
            side.UnavailableReason ??
                (total == null && side.Sum != null
                    ? "no total: the chain is not continuous, or the view spans more than one listening group"
                    : null));
    }

    private static AgentPackageJunction BuildJunction(
        AgentSideInputs side,
        AgentJunctionInputs junction,
        AgentPackageInputs inputs,
        bool keepSweep,
        bool keepLadder,
        bool keepCorrelationCurve,
        bool keepJunctionCurves)
    {
        string sideName = AgentChannelIds.SideName(side.Side);
        string name = $"{junction.LowerBlock}/{junction.UpperBlock}";
        string lowerId = ChannelIdOn(inputs, junction.LowerBlock, side.Side);
        string upperId = ChannelIdOn(inputs, junction.UpperBlock, side.Side);

        VirtualCrossoverMetric.Entry? loss = side.Entries
            .Where(entry => !entry.IsTotal && entry.Junction == name)
            .Select(entry => (VirtualCrossoverMetric.Entry?)entry)
            .FirstOrDefault();
        VirtualCrossoverMetric.PhaseEntry? phase = side.PhaseEntries
            .Where(entry => entry.Junction == name)
            .Select(entry => (VirtualCrossoverMetric.PhaseEntry?)entry)
            .FirstOrDefault();

        JunctionCorrelationView? correlation = junction.Correlation;
        List<AgentPackageLobe>? lobes = null;
        AgentSeries? sweep = null;
        AgentPackageCorrelation? phat = null;
        if (correlation != null)
        {
            lobes = AgentCurveSampling.Lobes(correlation.ScoreNormal, correlation.ScoreInverted, MaxLobes)
                .Select(lobe => new AgentPackageLobe(
                    Round2(lobe.DelayMs), lobe.Invert, Round1(lobe.ScoreDb)))
                .ToList();
            if (keepSweep)
            {
                sweep = Sweep(correlation);
            }
            phat = Correlation(correlation, keepCorrelationCurve);
        }

        AgentSeries? ladder = null;
        if (keepLadder && junction.Coherence != null)
        {
            ladder = new AgentSeries(
                ["frequencyHz", "lagMs", "peakR", "currentR", "halfPeriodMs"],
                junction.Coherence.Ladder
                    .Select(point => new double?[]
                    {
                        AgentCurveSampling.Frequency(point.FrequencyHz),
                        Round2(point.LagMs),
                        Round2(point.PeakR),
                        Round2(point.CurrentR),
                        Round2(point.HalfPeriodMs)
                    })
                    .ToList());
        }

        AgentSeries? curves = null;
        if (keepJunctionCurves && (junction.LowerMagnitude != null || junction.UpperMagnitude != null))
        {
            List<double> grid = AgentCurveSampling.JunctionGrid(
                junction.CrossoverHz, AgentCurveSampling.BroadbandLowHz, AgentCurveSampling.BroadbandHighHz);
            curves = new AgentSeries(
                ["frequencyHz", "lowerDb", "upperDb", "sumDb", "lossDb"],
                grid.Select(frequency => new double?[]
                {
                    AgentCurveSampling.Frequency(frequency),
                    Sample1(junction.LowerMagnitude, frequency),
                    Sample1(junction.UpperMagnitude, frequency),
                    Sample1(side.Sum, frequency),
                    Sample1(side.Loss, frequency)
                }).ToList());
        }

        string? unavailable = null;
        if (loss == null && phase == null && correlation == null)
        {
            unavailable = "no junction read-out: the pair's responses could not be compared " +
                "(no overlap, mismatched sample rates, or no source on one of them)";
        }
        else if (phase == null && loss != null)
        {
            unavailable = "phase read-out withheld: the pair's phase is not consistent enough across the band";
        }

        return new AgentPackageJunction(
            $"{sideName}:{junction.LowerBlock}-{junction.UpperBlock}",
            sideName,
            lowerId,
            upperId,
            AgentCurveSampling.Frequency(junction.CrossoverHz),
            [AgentCurveSampling.Frequency(junction.BandLowHz), AgentCurveSampling.Frequency(junction.BandHighHz)],
            loss is { } l ? new AgentPackageLoss(Round1(l.AverageDb), AgentCurveSampling.Round(l.DipDb, 1)) : null,
            phase is { } p ? Phase(p.Result) : null,
            lobes,
            sweep,
            phat,
            ladder,
            curves,
            unavailable);
    }

    private static AgentPackagePhase Phase(JunctionPhaseResult result) =>
        new(
            Round1(result.PhaseAtCrossoverDeg),
            Round2(result.PhaseConsistency),
            Round2(result.CurrentScore),
            Round2(result.BestExtraDelayMs),
            result.BestInvert,
            Round2(result.BestScore),
            Round2(result.OppositePolarityScore),
            AgentCurveSampling.Round(result.RivalExtraDelayMs, 2),
            AgentCurveSampling.Round(result.RivalScore, 2),
            AgentCurveSampling.Round(result.LobeMargin, 2),
            Round2(result.FitDelayMs),
            Round1(result.FitRmsDeg));

    private static AgentSeries Sweep(JunctionCorrelationView view)
    {
        // The two polarities are swept on one delay grid, so one row holds both.
        List<SignalPoint> normal = AgentCurveSampling.Thin(view.ScoreNormal, MaxSweepRows);
        List<SignalPoint> inverted = AgentCurveSampling.Thin(view.ScoreInverted, MaxSweepRows);
        var rows = new List<double?[]>(normal.Count);
        for (int index = 0; index < normal.Count; index++)
        {
            double? invertedValue = index < inverted.Count &&
                Math.Abs(inverted[index].X - normal[index].X) < 1e-6
                    ? inverted[index].Y
                    : AgentCurveSampling.Sample(view.ScoreInverted, normal[index].X);
            rows.Add([Round2(normal[index].X), Round1(normal[index].Y), AgentCurveSampling.Round(invertedValue, 1)]);
        }

        return new AgentSeries(["extraDelayMs", "scoreNormalDb", "scoreInvertedDb"], rows);
    }

    private static AgentPackageCorrelation? Correlation(JunctionCorrelationView view, bool keepCurve)
    {
        (double X, double Y)? peak = AgentCurveSampling.Extremum(view.Whitened, maximum: true);
        (double X, double Y)? trough = AgentCurveSampling.Extremum(view.Whitened, maximum: false);
        if (peak == null || trough == null)
        {
            return null;
        }

        (double X, double Y)? directPeak = AgentCurveSampling.Extremum(view.WhitenedDirect, maximum: true);
        (double X, double Y)? directTrough = AgentCurveSampling.Extremum(view.WhitenedDirect, maximum: false);
        double range = view.Whitened.Count > 0
            ? Math.Max(Math.Abs(view.Whitened[0].X), Math.Abs(view.Whitened[^1].X))
            : 0;

        List<SignalPoint> full = AgentCurveSampling.Thin(view.Whitened, keepCurve ? MaxCorrelationRows : 0);
        var rows = new List<double?[]>(full.Count);
        if (keepCurve)
        {
            foreach (SignalPoint point in full)
            {
                rows.Add([
                    Round2(point.X),
                    Round2(point.Y),
                    AgentCurveSampling.Round(AgentCurveSampling.Sample(view.WhitenedDirect, point.X), 2)
                ]);
            }
        }

        return new AgentPackageCorrelation(
            Round2(range),
            new AgentPackagePeak(Round2(peak.Value.X), Round2(peak.Value.Y)),
            new AgentPackagePeak(Round2(trough.Value.X), Round2(trough.Value.Y)),
            directPeak is { } dp ? new AgentPackagePeak(Round2(dp.X), Round2(dp.Y)) : null,
            directTrough is { } dt ? new AgentPackagePeak(Round2(dt.X), Round2(dt.Y)) : null,
            Round2(view.ArrivalLagMs),
            new AgentSeries(["lagMs", "fullRecordR", "directR"], rows));
    }

    private static AgentPackageStereo BuildStereo(VirtualCrossoverMetric.StereoDelta delta) =>
        new(
            delta.Channel,
            AgentCurveSampling.Round(delta.LeftMs, 2),
            AgentCurveSampling.Round(delta.RightMs, 2),
            delta.LeftMs is { } left && delta.RightMs is { } right
                ? AgentCurveSampling.Round(left - right, 2)
                : null,
            [AgentCurveSampling.Frequency(delta.LowHz), AgentCurveSampling.Frequency(delta.HighHz)],
            AgentCurveSampling.Round(delta.LevelDeltaDb, 1),
            delta.LeftLatched,
            delta.RightLatched,
            delta.LevelFromSpatialAverage);

    private static AgentPackageGroup BuildGroup(VirtualCrossoverMetric.GroupDelta delta) =>
        new(
            delta.Zone.ToString(),
            AgentCurveSampling.Round(delta.DelayMs, 2),
            AgentCurveSampling.Round(delta.LevelDb, 1),
            [AgentCurveSampling.Frequency(delta.LowHz), AgentCurveSampling.Frequency(delta.HighHz)],
            delta.LevelFromSpatialAverage);

    // A junction on a side names the channel that side holds for the block: a
    // mono block sits on both sides under its one id.
    private static string ChannelIdOn(AgentPackageInputs inputs, string block, AgentChannelSide side)
    {
        AgentChannelInputs? channel = inputs.Channels.FirstOrDefault(candidate =>
            candidate.Block == block && (candidate.Side == side || candidate.Side == AgentChannelSide.Mono));
        return channel?.Id ?? AgentChannelIds.Format(block, side);
    }

    private static double? Sample1(IReadOnlyList<SignalPoint>? curve, double frequency) =>
        curve == null ? null : AgentCurveSampling.Round(AgentCurveSampling.Sample(curve, frequency), 1);

    private static double Round1(double value) => Math.Round(value, 1);

    private static double Round2(double value) => Math.Round(value, 2);
}
