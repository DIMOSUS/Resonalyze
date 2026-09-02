namespace Resonalyze.Integration.AgentBridge;

// The wire shape of a package, one record per JSON object. Property names go
// out camelCase through the serializer; a null property is not written, which is
// how "unavailable" and "not applicable" both read: the field is simply absent,
// and where the absence needs a reason, a sibling `unavailableReason` says it.
// Normative description with a full example: docs/agent/PROTOCOL.md.

internal sealed record AgentPackage(
    string Kind,
    int ProtocolVersion,
    string PackageId,
    string CreatedAtUtc,
    AgentPackageApplication Application,
    IReadOnlyDictionary<string, string> Conventions,
    string? Notes,
    AgentPackageProcessor Processor,
    AgentPackageLimits Limits,
    AgentPackageAnalysis Analysis,
    AgentPackageTarget Target,
    IReadOnlyList<AgentPackageChannel> Channels,
    IReadOnlyList<AgentPackageSide> Sides,
    IReadOnlyList<AgentPackageJunction> Junctions,
    IReadOnlyList<AgentPackageStereo> Stereo,
    IReadOnlyList<AgentPackageGroup> Groups,
    IReadOnlyList<string> Omitted);

internal sealed record AgentPackageApplication(string Name, string Version);

internal sealed record AgentPackageProcessor(
    string ModelId,
    string DisplayName,
    bool Custom,
    int SampleRateHz,
    bool FollowsMeasurements,
    string QConvention,
    double MaxDelayMs,
    string MaxDelaySource,
    int? PeqBandsPerChannel);

internal sealed record AgentPackageLimits(
    double[] GainDb,
    double GainStepDb,
    double[] DelayMs,
    double DelayStepMs,
    int PeqBands,
    double PeqPreampDb,
    double[] CrossoverHz,
    IReadOnlyDictionary<string, int[]> Slopes,
    double[] ChebyshevRippleDb);

internal sealed record AgentPackageAnalysis(
    string GroupView,
    string ActiveSide,
    int SmoothingInverseOctaves,
    bool PsychoacousticSmoothing,
    AgentPackageSpatialAverage SpatialAverage,
    string PhaseWindowMode,
    int FdwCycles,
    string PhaseDetrendMode,
    AgentPackageGateShape GateShapeMs,
    AgentPackageGate GateLeft,
    AgentPackageGate GateRight,
    string? Calibration,
    double StereoSceneOffsetMs,
    bool RightHandDrive,
    double StereoLevelDifferenceDb,
    double RearFillOffsetMs);

/// <summary>
/// Whether the tune is being judged on spatial averages, and if not, why not —
/// stated as one word so the assistant does not have to count captures across
/// the channels: <c>none</c> (no channel carries one), <c>capturedNotShown</c>
/// (captures exist, the hybrid view is not drawn), <c>partial</c> (drawn for
/// some measured channels), <c>active</c>.
/// </summary>
internal sealed record AgentPackageSpatialAverage(
    string? Mode,
    bool HybridTicked,
    bool HybridDrawn,
    string Status,
    int ChannelsMeasured,
    int ChannelsWithCapture,
    int ChannelsDrawn);

internal sealed record AgentPackageGateShape(double Left, double Plateau, double Right);

internal sealed record AgentPackageGate(double? OffsetMs, double? DetrendMs);

internal sealed record AgentPackageTarget(
    double LevelDb,
    string Preset,
    double TiltDbPerOctave,
    AgentPackageShelf BassShelf,
    AgentPackageShelf TrebleShelf,
    AgentPackageShelf Presence,
    double ToleranceDb,
    string? ImportedName,
    AgentSeries Curve);

internal sealed record AgentPackageShelf(double GainDb, double FrequencyHz, double WidthOctaves);

internal sealed record AgentPackageChannel(
    string Id,
    string Block,
    string Side,
    bool Mono,
    string Zone,
    string DisplayName,
    bool Enabled,
    bool Bypass,
    AgentPackageSource Source,
    AgentPackageDsp Dsp,
    AgentPackageChannelCurves? Curves);

internal sealed record AgentPackageSource(
    bool Available,
    int? SampleRateHz,
    double[]? MeasuredBandHz,
    string? SpatialAverage,
    IReadOnlyList<string>? SpatialAverageCaptures,
    string? UnavailableReason);

internal sealed record AgentPackageDsp(
    double GainDb,
    double DelayMs,
    bool InvertPolarity,
    AgentPackageCrossover Crossover,
    AgentPackagePeq Peq);

internal sealed record AgentPackageCrossover(
    string Kind,
    AgentPackageEdge HighPass,
    AgentPackageEdge LowPass);

internal sealed record AgentPackageEdge(
    string Family,
    double FrequencyHz,
    int SlopeDbPerOctave,
    double RippleDb);

/// <param name="PeakDb">The net response's highest point, preamp included; above 0 dB is a headroom problem.</param>
internal sealed record AgentPackagePeq(
    double PreampDb,
    string Hash,
    double PeakDb,
    double PeakHz,
    IReadOnlyList<AgentPackageBand> Bands);

internal sealed record AgentPackageBand(string Type, double FrequencyHz, double Q, double GainDb);

internal sealed record AgentPackageChannelCurves(AgentSeries Broadband);

internal sealed record AgentPackageSide(
    string Side,
    IReadOnlyList<string> Channels,
    AgentSeries? SumDb,
    AgentPackageLoss? TotalSumLoss,
    // The median of sum minus target over the broadband grid: where the target
    // level datum sits against what the side actually plays. Sign: positive =
    // the side plays above the target.
    double? SumVsTargetDb,
    string? UnavailableReason);

internal sealed record AgentPackageLoss(double AverageDb, double? DipDb);

internal sealed record AgentPackageJunction(
    string Id,
    string Side,
    string Lower,
    string Upper,
    double CrossoverHz,
    double[] BandHz,
    AgentPackageLoss? SumLoss,
    AgentPackagePhase? Phase,
    IReadOnlyList<AgentPackageLobe>? Lobes,
    AgentSeries? Sweep,
    AgentPackageCorrelation? Correlation,
    AgentSeries? CoherenceLadder,
    AgentSeries? Curves,
    string? UnavailableReason);

internal sealed record AgentPackagePhase(
    double PhaseAtCrossoverDeg,
    double Consistency,
    double CurrentScore,
    double BestExtraDelayMs,
    bool BestInvert,
    double BestScore,
    double OppositePolarityScore,
    double? RivalExtraDelayMs,
    double? RivalScore,
    double? LobeMargin,
    double FitDelayMs,
    double FitRmsDeg);

internal sealed record AgentPackageLobe(double ExtraDelayMs, bool Invert, double ScoreDb);

internal sealed record AgentPackageCorrelation(
    double SearchRangeMs,
    AgentPackagePeak FullRecordPeak,
    AgentPackagePeak FullRecordTrough,
    AgentPackagePeak? DirectPeak,
    AgentPackagePeak? DirectTrough,
    double ArrivalLagMs,
    AgentSeries Curve);

internal sealed record AgentPackagePeak(double LagMs, double R);

internal sealed record AgentPackageStereo(
    string Block,
    double? LeftMs,
    double? RightMs,
    double? DeltaMs,
    double[] BandHz,
    double? LevelDeltaDb,
    bool LeftLatched,
    bool RightLatched,
    bool LevelFromSpatialAverage);

internal sealed record AgentPackageGroup(
    string Zone,
    double? DelayMs,
    double? LevelDb,
    double[] BandHz,
    bool LevelFromSpatialAverage);

/// <summary>A columnar series: the column names once, then one row per frequency or lag.</summary>
internal sealed record AgentSeries(IReadOnlyList<string> Columns, IReadOnlyList<double?[]> Rows);
