using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>Which physical side of a block a channel id names.</summary>
internal enum AgentChannelSide
{
    Left,
    Right,
    Mono
}

/// <summary>
/// The id a channel goes by in a package and a reply: the block's letter as the
/// panel and the tuning sheet print it, a colon, and the side — <c>A:left</c>,
/// <c>A:right</c>, <c>C:mono</c>. Stable for as long as the blocks keep their
/// order, which is exactly as long as the expected current values keep matching.
/// </summary>
internal static class AgentChannelIds
{
    public static string Format(string block, AgentChannelSide side) =>
        $"{block}:{SideName(side)}";

    public static string SideName(AgentChannelSide side) => side switch
    {
        AgentChannelSide.Left => "left",
        AgentChannelSide.Right => "right",
        _ => "mono"
    };
}

/// <summary>One physical channel as the bridge sees it: its id and its settings.</summary>
/// <param name="Settings">
/// The channel's LIVE settings object — what the review compares expected values
/// against, and what a commit writes to. The validator never mutates it: every
/// trial edit goes to a copy.
/// </param>
/// <param name="HasMeasurement">
/// Whether the side carries an impulse response. An engine asked to fit this
/// channel has nothing to fit without one.
/// </param>
/// <param name="SpatialAverageCaptures">
/// Every capture family the side holds, in the mode enum's own names — the same
/// list the package prints as <c>source.spatialAverageCaptures</c>. What the mode
/// currently READS is the session's business, not the channel's.
/// </param>
internal sealed record AgentChannelSnapshot(
    string Block,
    AgentChannelSide Side,
    VirtualCrossoverChannelSettings Settings,
    bool HasMeasurement,
    IReadOnlyList<string> SpatialAverageCaptures)
{
    public string Id => AgentChannelIds.Format(Block, Side);

    /// <summary>How the review names the channel: <c>B left</c>, <c>C mono</c>.</summary>
    public string Label => $"{Block} {AgentChannelIds.SideName(Side)}";
}

/// <summary>
/// Everything the validator needs to know about the session a reply is being
/// applied to. Built by the panel on the UI thread; read anywhere.
/// </summary>
/// <param name="ProcessorSampleRateHz">
/// The rate the processor builds its filters at — the Nyquist every proposed
/// corner and band frequency is gated by.
/// </param>
/// <param name="MaxDelayMs">
/// The processor's per-channel delay ceiling (<see cref="DspProcessorProfile.MaxDelayMs"/>);
/// a proposal above it is flagged, not refused — the manual fields keep their range.
/// </param>
/// <param name="LastPackageId">
/// The id of the package this session most recently copied, or null when it has
/// not copied one since it opened. A reply naming another id gets a warning.
/// </param>
/// <param name="AutoDelay">
/// What an Auto delay run would start from: the values the dialog would open
/// with. An engine request that leaves an input out is judged, and described in
/// the review, against these.
/// </param>
/// <param name="SpatialAverageMode">
/// The capture family in force — the panel's effective mode, not the raw stored
/// one, so a project that has not settled its choice yet is judged on what it
/// actually draws.
/// </param>
/// <param name="HybridTicked">Whether the Hybrid box under the plot is ticked.</param>
internal sealed record AgentSessionSnapshot(
    IReadOnlyList<AgentChannelSnapshot> Channels,
    int ProcessorSampleRateHz,
    double MaxDelayMs,
    string? LastPackageId,
    AgentAutoDelaySettings AutoDelay,
    VirtualCrossoverSpatialAverageMode SpatialAverageMode,
    bool HybridTicked,
    // The side on screen: an Auto-tune handoff is built for it alone (its gate
    // pin, its render anchor, its hybrid datum), so a request for the other
    // side's channel is refused at review rather than skipped at execution.
    bool ActiveSideRight = false)
{
    public AgentChannelSnapshot? Find(string channelId) =>
        Channels.FirstOrDefault(channel =>
            string.Equals(channel.Id, channelId, StringComparison.Ordinal));

    /// <summary>Whether any channel of the session holds a capture of that family.</summary>
    public bool HasCapture(VirtualCrossoverSpatialAverageMode mode) =>
        Channels.Any(channel => channel.SpatialAverageCaptures.Contains(
            mode.ToString(), StringComparer.Ordinal));
}

/// <summary>
/// The Auto delay inputs as the dialog would present them: the project's own
/// figures as layout-neutral magnitudes (the layout toggle owns every sign), and
/// the gain balance's opt-in, which the dialog opens unticked every time rather
/// than storing an answer.
/// </summary>
internal sealed record AgentAutoDelaySettings(
    double SceneOffsetMs,
    bool RightHandDrive,
    bool AdjustGains,
    double NearSideCutDb,
    double RearFillOffsetMs);

/// <summary>
/// A short fingerprint of one channel's PEQ bank, printed in the package and
/// echoed by a <c>replacePeqBank</c> operation in place of the whole current
/// bank. Twelve hex digits of SHA-256 over the bands in order, each as its type,
/// frequency, Q and gain in round-trip invariant form, then the preamp — so any
/// edit to any band, and a reorder, changes it.
/// </summary>
internal static class AgentPeqHash
{
    public static string Compute(double preampDb, IReadOnlyList<PeqBand> bands)
    {
        var text = new StringBuilder();
        foreach (PeqBand band in bands)
        {
            text.Append(band.Type).Append(';')
                .Append(band.FrequencyHz.ToString("R", CultureInfo.InvariantCulture)).Append(';')
                .Append(band.Q.ToString("R", CultureInfo.InvariantCulture)).Append(';')
                .Append(band.GainDb.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }
        text.Append("preamp;").Append(preampDb.ToString("R", CultureInfo.InvariantCulture));

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexStringLower(hash)[..12];
    }
}
