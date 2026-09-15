namespace Resonalyze.Integration.AgentBridge;

/// <summary>A parsed reply: prose plus a closed set of typed operations. No path leads from a reply to a file, source or setting beyond channel parameters and panel engines. PackageId is a correlation hint, never a gate.</summary>
internal sealed record AgentProposal(
    string? PackageId,
    string? Summary,
    IReadOnlyList<string> Advice,
    IReadOnlyList<AgentSource> Sources,
    IReadOnlyList<AgentOperation> Operations,
    IReadOnlyList<AgentRejectedOperation> Rejected);

/// <summary>Shown as text, never opened.</summary>
internal sealed record AgentSource(string Url, string? Title, IReadOnlyList<string> FactsUsed);

internal sealed record AgentRejectedOperation(string? Id, string? Op, string Problem);

/// <summary>Settings operations carry the expected current value (a mismatch refuses them); engine operations carry inputs, since what an engine writes is unknown until it runs.</summary>
internal abstract record AgentOperation(string Id, string? Reason)
{
    public abstract string Op { get; }

    /// <summary>Conflict unit: two operations on one channel's same family cannot both be right.</summary>
    public abstract string Parameter { get; }
}

internal abstract record AgentChannelOperation(string Id, string ChannelId, string? Reason)
    : AgentOperation(Id, Reason);

/// <summary>Writes one channel's settings directly; the only operations the importer applies itself.</summary>
internal abstract record AgentSettingsOperation(string Id, string ChannelId, string? Reason)
    : AgentChannelOperation(Id, ChannelId, Reason);

internal sealed record SetGainOperation(
    string Id, string ChannelId, string? Reason, double ExpectedCurrentDb, double ProposedDb)
    : AgentSettingsOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.SetGainDb;

    public override string Parameter => "Gain";
}

internal sealed record SetDelayOperation(
    string Id, string ChannelId, string? Reason, double ExpectedCurrentMs, double ProposedMs)
    : AgentSettingsOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.SetDelayMs;

    public override string Parameter => "Delay";
}

internal sealed record SetPolarityOperation(
    string Id, string ChannelId, string? Reason, bool ExpectedCurrentInverted, bool ProposedInverted)
    : AgentSettingsOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.SetPolarity;

    public override string Parameter => "Polarity";
}

internal sealed record SetCrossoverOperation(
    string Id, string ChannelId, string? Reason, AgentCrossover ExpectedCurrent, AgentCrossover Proposed)
    : AgentSettingsOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.SetCrossover;

    public override string Parameter => "Crossover";
}

/// <param name="ExpectedCurrentHash">The package's <see cref="AgentPeqHash"/>, standing in for the whole current bank.</param>
internal sealed record ReplacePeqBankOperation(
    string Id, string ChannelId, string? Reason, string ExpectedCurrentHash, AgentPeqBank Proposed)
    : AgentSettingsOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.ReplacePeqBank;

    public override string Parameter => "PEQ bank";
}

/// <summary>Run Auto delay; omitted inputs mean the project's current values. Stereo vs single-sided is decided as the button decides. NearSideCutDb is a magnitude: the layout toggle owns the sign.</summary>
internal sealed record RunAutoDelayOperation(
    string Id,
    string? Reason,
    double? SceneOffsetMs,
    bool? RightHandDrive,
    bool? AdjustGains,
    double? NearSideCutDb,
    double? RearFillOffsetMs) : AgentOperation(Id, Reason)
{
    public override string Op => AgentProtocol.RunAutoDelay;

    public override string Parameter => "Auto delay";
}

internal sealed record RunAutoCrossoverOperation(string Id, string? Reason)
    : AgentOperation(Id, Reason)
{
    public override string Op => AgentProtocol.RunAutoCrossover;

    public override string Parameter => "Auto crossover";
}

/// <summary>A read-only question; the answer goes to the clipboard for pasting back. Variants state changes exactly as settings operations do, so a good variant converts word for word.</summary>
internal sealed record ProbeOperation(
    string Id,
    string? Reason,
    string Probe,
    string? JunctionId,
    IReadOnlyList<AgentProbeVariant>? Variants,
    IReadOnlyList<string>? Series = null,
    IReadOnlyList<string>? ChannelIds = null,
    int? PointsPerOctave = null,
    int? Rows = null) : AgentOperation(Id, Reason)
{
    public override string Op => AgentProtocol.Probe;

    public override string Parameter => "Probe";
}

internal sealed record AgentProbeVariant(string? Label, IReadOnlyList<AgentProbeChange> Changes);

/// <summary>One channel read as if it held these settings; omitted fields keep the channel's. An empty <see cref="Peq"/> bank means the bank cleared.</summary>
internal sealed record AgentProbeChange(
    string ChannelId,
    double? GainDb,
    double? DelayMs,
    bool? InvertPolarity,
    AgentCrossover? Crossover,
    AgentPeqBank? Peq)
{
    public bool StatesNothing =>
        GainDb == null && DelayMs == null && InvertPolarity == null &&
        Crossover == null && Peq == null;
}

/// <summary>Tune one junction's facing low-pass/high-pass on the pair's coherent sum at current delays and polarity, writing one crossover to both sides; nothing else moves. Omitted inputs: half-octave window, current families, every practical slope, one slope for both edges.</summary>
internal sealed record TuneJunctionOperation(
    string Id,
    string? Reason,
    string JunctionId,
    double? MinHz,
    double? MaxHz,
    IReadOnlyList<string>? Families,
    IReadOnlyList<int>? Slopes,
    bool? IndependentSlopes) : AgentOperation(Id, Reason)
{
    public override string Op => AgentProtocol.TuneJunction;

    public override string Parameter => "Junction tune";
}

/// <summary>Fit a PEQ bank on one channel; omitted inputs mean the wizard's own answer. Null Source leaves the choice with the panel.</summary>
internal sealed record AutoTunePeqOperation(
    string Id,
    string ChannelId,
    string? Reason,
    double? TargetLevelDb,
    double? MinHz,
    double? MaxHz,
    bool? AllowShelves,
    bool? CutsOnly,
    string? Source) : AgentChannelOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.AutoTunePeq;

    public override string Parameter => "Auto-tune";
}

/// <summary>Read the named capture family and tick Hybrid (project-wide). Only Hybrid=true is accepted.</summary>
internal sealed record UseSpatialAverageOperation(
    string Id, string? Reason, string Mode, bool Hybrid) : AgentOperation(Id, Reason)
{
    public override string Op => AgentProtocol.UseSpatialAverage;

    public override string Parameter => "Spatial average";
}

/// <summary>Kind and families by their published enum names; edges the kind does not use may be omitted.</summary>
internal sealed record AgentCrossover(string Kind, AgentCrossoverEdge? HighPass, AgentCrossoverEdge? LowPass);

/// <param name="RippleDb">Chebyshev only; null keeps the stored value and skips the expected comparison.</param>
internal sealed record AgentCrossoverEdge(string Family, double FrequencyHz, int SlopeDbPerOctave, double? RippleDb);

internal sealed record AgentPeqBank(double PreampDb, IReadOnlyList<AgentPeqBand> Bands);

/// <summary>Q is RBJ cookbook Q.</summary>
internal sealed record AgentPeqBand(string Type, double FrequencyHz, double Q, double GainDb);
