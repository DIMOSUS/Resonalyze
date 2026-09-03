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
/// <param name="Zone">
/// The block's zone, which decides the group it can hold a junction in: a front
/// channel and a rear fill are neighbours along the spectrum with no filter
/// handing anything between them.
/// </param>
/// <param name="Enabled">Whether the block is in the sum at all.</param>
/// <param name="Bypass">Whether the block's chain is bypassed — no crossover to tune.</param>
internal sealed record AgentChannelSnapshot(
    string Block,
    AgentChannelSide Side,
    VirtualCrossoverChannelSettings Settings,
    bool HasMeasurement,
    IReadOnlyList<string> SpatialAverageCaptures,
    VirtualCrossoverZone Zone = VirtualCrossoverZone.Front,
    bool Enabled = true,
    bool Bypass = false)
{
    public string Id => AgentChannelIds.Format(Block, Side);

    /// <summary>How the review names the channel: <c>B left</c>, <c>C mono</c>.</summary>
    public string Label => $"{Block} {AgentChannelIds.SideName(Side)}";

    /// <summary>
    /// Whether the channel plays on the given side: its own side, or either
    /// for a mono block, which the panel routes to both.
    /// </summary>
    public bool PlaysOn(AgentChannelSide side) =>
        Side == AgentChannelSide.Mono || Side == side;
}

/// <summary>
/// The id a junction goes by in a package (<c>left:B-C</c>): the side it was
/// read on, a colon, and the lower and upper blocks joined by a dash. A mono
/// block appears under the side it was summed on, as the package prints it.
/// </summary>
internal static class AgentJunctionIds
{
    public static string Format(AgentChannelSide side, string lowerBlock, string upperBlock) =>
        $"{AgentChannelIds.SideName(side)}:{lowerBlock}-{upperBlock}";

    public static bool TryParse(
        string? id, out AgentChannelSide side, out string lowerBlock, out string upperBlock)
    {
        side = AgentChannelSide.Left;
        lowerBlock = string.Empty;
        upperBlock = string.Empty;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        int colon = id.IndexOf(':', StringComparison.Ordinal);
        int dash = id.IndexOf('-', StringComparison.Ordinal);
        if (colon <= 0 || dash <= colon + 1 || dash == id.Length - 1)
        {
            return false;
        }

        side = id[..colon] switch
        {
            "left" => AgentChannelSide.Left,
            "right" => AgentChannelSide.Right,
            _ => AgentChannelSide.Mono
        };
        if (side == AgentChannelSide.Mono)
        {
            return false;
        }

        lowerBlock = id[(colon + 1)..dash];
        upperBlock = id[(dash + 1)..];
        return lowerBlock.Length > 0 && upperBlock.Length > 0 &&
            !lowerBlock.Contains(':') && !upperBlock.Contains('-') && !upperBlock.Contains(':');
    }
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
/// not copied one since it opened. A reply naming another id gets a warning,
/// and its engine requests are refused.
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
    bool ActiveSideRight = false,
    // The fingerprint the last copied package was taken at, and the session's
    // own now (AgentSessionFingerprint): unequal, the session has changed since
    // the copy, whichever way. Null when the panel took none — a test session —
    // and then the two are not compared.
    string? LastPackageFingerprint = null,
    string? Fingerprint = null)
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
