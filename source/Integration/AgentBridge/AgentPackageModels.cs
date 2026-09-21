namespace Resonalyze.Integration.AgentBridge;

// Package wire shape (normative: docs/agent/PROTOCOL.md). Null properties are not written: absent means unavailable or not applicable, with `unavailableReason` where a reason is needed.

internal sealed record AgentPackage(
    string Kind,
    int ProtocolVersion,
    string GuideVersion,
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
    AgentSampling Sampling,
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
    double[] ChebyshevRippleDb,
    IReadOnlyList<string> Operations,
    IReadOnlyList<string> Probes,
    int ProbeVariantsPerImport,
    int ProbeChanges,
    int SeriesPointsPerOctave,
    int SeriesRows,
    int SeriesProbesPerImport);

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

/// <summary>Spatial-average status as one word (none, capturedNotShown, partial, active) over the shown channels. See docs/tech/agent-bridge.md#package-smoothing.</summary>
internal sealed record AgentPackageSpatialAverage(
    string? Mode,
    bool HybridTicked,
    bool HybridDrawn,
    int SmoothingInverseOctaves,
    string Status,
    int ChannelsShown,
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
    AgentPackagePeq Peq,
    // Absent where not dialled in; stated at the channel's own crossover (low-pass on a sub, else high-pass).
    double? PhaseRotationDeg = null,
    // Read-only, and already inside every curve.
    AgentPackageFir? Fir = null);

/// <summary>A loaded FIR kernel. PeakMs is a peak position, NOT a group delay (see docs/tech/agent-bridge.md#fir-in-the-package); Crossover is present only for FIR Constructor kernels.</summary>
internal sealed record AgentPackageFir(
    string File,
    int Taps,
    double PeakMs,
    AgentPackageFirCrossover? Crossover = null);

internal sealed record AgentPackageFirCrossover(
    string Kind,
    AgentPackageEdge? HighPass,
    AgentPackageEdge? LowPass,
    string Method,
    string Window,
    int DesignedAtHz,
    double LatencyMs);

internal sealed record AgentPackageCrossover(
    string Kind,
    AgentPackageEdge HighPass,
    AgentPackageEdge LowPass,
    AgentPackageAcousticGoal? AcousticHighPass = null,
    AgentPackageAcousticGoal? AcousticLowPass = null);

internal sealed record AgentPackageAcousticGoal(string Family, int SlopeDbPerOctave);

internal sealed record AgentPackageEdge(
    string Family,
    double FrequencyHz,
    int SlopeDbPerOctave,
    double RippleDb);

/// <param name="PeakDb">Net response maximum, preamp included; above 0 dB is a headroom problem.</param>
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
    AgentPackageLoss? TotalSumLossDirect,
    // Median of sum minus target over the broadband grid; positive = the side plays above the target.
    double? SumVsTargetDb,
    double? HybridSumVsTargetDb,
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
    // Direct-sound (FDW-8) loss beside the full one; the two families are never compared with each other.
    AgentPackageLoss? SumLossDirect,
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
    bool LevelFromSpatialAverage,
    bool EnergyOnset,
    bool EnergyOnsetWithheld);

internal sealed record AgentPackageGroup(
    string Zone,
    double? DelayMs,
    double? LevelDb,
    double[] BandHz,
    bool LevelFromSpatialAverage);

internal sealed record AgentSeries(IReadOnlyList<string> Columns, IReadOnlyList<double?[]> Rows);
