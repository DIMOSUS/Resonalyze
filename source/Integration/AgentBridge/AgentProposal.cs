namespace Resonalyze.Integration.AgentBridge;

/// <summary>
/// What an assistant proposed, as the parser understood it: the prose that is
/// shown, and a closed set of typed operations that may be applied. Nothing in
/// here can address anything but the five editable parameters of one physical
/// channel and the four engines the panel already runs from its own buttons —
/// there is no path from a reply to a file, a source or a setting.
/// </summary>
/// <param name="PackageId">
/// The id of the package the assistant says it answered, echoed from that
/// package; null when the reply did not carry one. A correlation hint, never a
/// gate: the stale-state guard is every operation's expected current value.
/// </param>
/// <param name="Rejected">
/// Operation objects the parser could not turn into a typed operation — unknown
/// <c>op</c>, missing fields, out-of-limit strings, duplicate ids. Kept so the
/// review can list them, greyed out, with the reason; never applied.
/// </param>
internal sealed record AgentProposal(
    string? PackageId,
    string Summary,
    IReadOnlyList<string> Advice,
    IReadOnlyList<AgentSource> Sources,
    IReadOnlyList<AgentOperation> Operations,
    IReadOnlyList<AgentRejectedOperation> Rejected);

/// <summary>A page the assistant cited; shown as text, never opened.</summary>
internal sealed record AgentSource(string Url, string? Title, IReadOnlyList<string> FactsUsed);

/// <summary>An operation object the parser refused, and why.</summary>
internal sealed record AgentRejectedOperation(string? Id, string? Op, string Problem);

/// <summary>
/// One thing a reply asks for. Two families sit under this: an operation that
/// WRITES one channel's settings, which carries what the assistant believes the
/// current value is — a current value that no longer matches means the tune
/// moved on since the package was copied, and the operation is refused rather
/// than applied to a state it was not reasoned about — and an operation that
/// asks for one of the panel's own ENGINES to be run, which carries the
/// engine's inputs instead, because what an engine will write is not knowable
/// until it has run.
/// </summary>
internal abstract record AgentOperation(string Id, string Reason)
{
    /// <summary>The protocol's name for the operation: what its <c>op</c> said.</summary>
    public abstract string Op { get; }

    /// <summary>
    /// The parameter family the operation edits, the unit conflicts are judged in:
    /// two operations on one channel's same family cannot both be right.
    /// </summary>
    public abstract string Parameter { get; }
}

/// <summary>An operation addressed at one physical channel, by the package's id for it.</summary>
internal abstract record AgentChannelOperation(string Id, string ChannelId, string Reason)
    : AgentOperation(Id, Reason);

/// <summary>
/// An operation that writes one channel's editable settings directly — the five
/// the bridge has always had. These are the only operations the importer applies
/// itself; everything else it asks an engine to do.
/// </summary>
internal abstract record AgentSettingsOperation(string Id, string ChannelId, string Reason)
    : AgentChannelOperation(Id, ChannelId, Reason);

internal sealed record SetGainOperation(
    string Id, string ChannelId, string Reason, double ExpectedCurrentDb, double ProposedDb)
    : AgentSettingsOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.SetGainDb;

    public override string Parameter => "Gain";
}

internal sealed record SetDelayOperation(
    string Id, string ChannelId, string Reason, double ExpectedCurrentMs, double ProposedMs)
    : AgentSettingsOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.SetDelayMs;

    public override string Parameter => "Delay";
}

internal sealed record SetPolarityOperation(
    string Id, string ChannelId, string Reason, bool ExpectedCurrentInverted, bool ProposedInverted)
    : AgentSettingsOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.SetPolarity;

    public override string Parameter => "Polarity";
}

internal sealed record SetCrossoverOperation(
    string Id, string ChannelId, string Reason, AgentCrossover ExpectedCurrent, AgentCrossover Proposed)
    : AgentSettingsOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.SetCrossover;

    public override string Parameter => "Crossover";
}

/// <param name="ExpectedCurrentHash">
/// The bank's hash as the package printed it (see <see cref="AgentPeqHash"/>),
/// standing in for the whole current bank so the reply need not repeat it.
/// </param>
internal sealed record ReplacePeqBankOperation(
    string Id, string ChannelId, string Reason, string ExpectedCurrentHash, AgentPeqBank Proposed)
    : AgentSettingsOperation(Id, ChannelId, Reason)
{
    public override string Op => AgentProtocol.ReplacePeqBank;

    public override string Parameter => "PEQ bank";
}

/// <summary>
/// Run Auto delay. Every input is optional, and a missing one means "what the
/// project holds now" — which is what the dialog would open with — so a reply
/// that wants only the scene offset changed states only that. Whether the run is
/// stereo or single-sided the panel decides exactly as its button does; nothing
/// in a reply can.
/// </summary>
/// <param name="NearSideCutDb">
/// How much quieter the near side plays, as the dialog edits it. The layout
/// toggle owns the sign, so this magnitude is never negative.
/// </param>
internal sealed record RunAutoDelayOperation(
    string Id,
    string Reason,
    double? SceneOffsetMs,
    bool? RightHandDrive,
    bool? AdjustGains,
    double? NearSideCutDb,
    double? RearFillOffsetMs) : AgentOperation(Id, Reason)
{
    public override string Op => AgentProtocol.RunAutoDelay;

    public override string Parameter => "Auto delay";
}

/// <summary>
/// Open the Auto crossover wizard. It takes no inputs: what it proposes comes
/// from the drivers' own bands, and the choices it does offer are made in its
/// own dialog, in front of the user.
/// </summary>
internal sealed record RunAutoCrossoverOperation(string Id, string Reason)
    : AgentOperation(Id, Reason)
{
    public override string Op => AgentProtocol.RunAutoCrossover;

    public override string Parameter => "Auto crossover";
}

/// <summary>
/// Fit a PEQ bank to the target on one channel. A missing input means the
/// wizard's own answer for it.
/// </summary>
/// <param name="Source">
/// <c>point</c> or <c>spatialAverage</c>: which curve the fit reads. Null leaves
/// the choice where it is, with the channel and the panel.
/// </param>
internal sealed record AutoTunePeqOperation(
    string Id,
    string ChannelId,
    string Reason,
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

/// <summary>
/// Judge the tune on spatial averages: read the capture family named here and
/// tick Hybrid. The one operation addressed at the whole project rather than a
/// channel, because the mode and the tick are the project's.
/// </summary>
/// <param name="Hybrid">
/// Only <c>true</c> is accepted. Turning the hybrid OFF is not something a reply
/// has a reason to ask for, and refusing it costs nothing.
/// </param>
internal sealed record UseSpatialAverageOperation(
    string Id, string Reason, string Mode, bool Hybrid) : AgentOperation(Id, Reason)
{
    public override string Op => AgentProtocol.UseSpatialAverage;

    public override string Parameter => "Spatial average";
}

/// <summary>
/// A crossover as the reply states it: the kind by its <see cref="Dsp.CrossoverKind"/>
/// name and each edge by its <see cref="Dsp.CrossoverFilterFamily"/> name — the
/// enum names the package published, so nothing is translated on the way in. An
/// edge the kind does not use may be omitted; the edges it does use may not.
/// </summary>
internal sealed record AgentCrossover(string Kind, AgentCrossoverEdge? HighPass, AgentCrossoverEdge? LowPass);

/// <param name="RippleDb">
/// Chebyshev passband ripple; null leaves the stored value alone (and, on the
/// expected side, skips the comparison), since every other family ignores it.
/// </param>
internal sealed record AgentCrossoverEdge(string Family, double FrequencyHz, int SlopeDbPerOctave, double? RippleDb);

internal sealed record AgentPeqBank(double PreampDb, IReadOnlyList<AgentPeqBand> Bands);

/// <summary>One band; <paramref name="Q"/> is RBJ cookbook Q, as everywhere inside.</summary>
internal sealed record AgentPeqBand(string Type, double FrequencyHz, double Q, double GainDb);
