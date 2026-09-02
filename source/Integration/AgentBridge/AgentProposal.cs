namespace Resonalyze.Integration.AgentBridge;

/// <summary>
/// What an assistant proposed, as the parser understood it: the prose that is
/// shown, and a closed set of typed operations that may be applied. Nothing in
/// here can address anything but the five editable parameters of one physical
/// channel — there is no path from a reply to a file, a source or a setting.
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
/// One proposed change to one channel. Every operation carries what the
/// assistant believes the CURRENT value is, copied from the package it read; a
/// current value that no longer matches means the tune moved on since the
/// package was copied, and the operation is refused rather than applied to a
/// state it was not reasoned about.
/// </summary>
internal abstract record AgentOperation(string Id, string ChannelId, string Reason)
{
    /// <summary>
    /// The parameter family the operation edits, the unit conflicts are judged in:
    /// two operations on one channel's same family cannot both be right.
    /// </summary>
    public abstract string Parameter { get; }
}

internal sealed record SetGainOperation(
    string Id, string ChannelId, string Reason, double ExpectedCurrentDb, double ProposedDb)
    : AgentOperation(Id, ChannelId, Reason)
{
    public override string Parameter => "Gain";
}

internal sealed record SetDelayOperation(
    string Id, string ChannelId, string Reason, double ExpectedCurrentMs, double ProposedMs)
    : AgentOperation(Id, ChannelId, Reason)
{
    public override string Parameter => "Delay";
}

internal sealed record SetPolarityOperation(
    string Id, string ChannelId, string Reason, bool ExpectedCurrentInverted, bool ProposedInverted)
    : AgentOperation(Id, ChannelId, Reason)
{
    public override string Parameter => "Polarity";
}

internal sealed record SetCrossoverOperation(
    string Id, string ChannelId, string Reason, AgentCrossover ExpectedCurrent, AgentCrossover Proposed)
    : AgentOperation(Id, ChannelId, Reason)
{
    public override string Parameter => "Crossover";
}

/// <param name="ExpectedCurrentHash">
/// The bank's hash as the package printed it (see <see cref="AgentPeqHash"/>),
/// standing in for the whole current bank so the reply need not repeat it.
/// </param>
internal sealed record ReplacePeqBankOperation(
    string Id, string ChannelId, string Reason, string ExpectedCurrentHash, AgentPeqBank Proposed)
    : AgentOperation(Id, ChannelId, Reason)
{
    public override string Parameter => "PEQ bank";
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
