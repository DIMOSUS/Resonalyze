using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

internal enum AgentChannelSide
{
    Left,
    Right,
    Mono
}

/// <summary>Channel ids like <c>A:left</c>, <c>C:mono</c>; stable while block order holds, which the expected current values also guard.</summary>
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

/// <summary>One physical channel. Settings is the LIVE object (the validator only mutates copies); Zone decides the junction group, since front and rear fill have no filter handing over.</summary>
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

    public string Label => $"{Block} {AgentChannelIds.SideName(Side)}";

    /// <summary>A mono block plays on both sides.</summary>
    public bool PlaysOn(AgentChannelSide side) =>
        Side == AgentChannelSide.Mono || Side == side;
}

/// <summary>Junction ids like <c>left:B-C</c>; a mono block appears under the side it was summed on.</summary>
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

/// <summary>What the validator knows about the session; built on the UI thread. A proposal over MaxDelayMs is flagged, not refused. SpatialAverageMode is the effective mode, not the stored one.</summary>
internal sealed record AgentSessionSnapshot(
    IReadOnlyList<AgentChannelSnapshot> Channels,
    int ProcessorSampleRateHz,
    double MaxDelayMs,
    string? LastPackageId,
    AgentAutoDelaySettings AutoDelay,
    VirtualCrossoverSpatialAverageMode SpatialAverageMode,
    bool HybridTicked,
    // An Auto-tune handoff is built for the shown side only, so the other side is refused at review.
    bool ActiveSideRight = false,
    // Unequal fingerprints mean the session changed since the copy; null (test sessions) skips the comparison.
    string? LastPackageFingerprint = null,
    string? Fingerprint = null)
{
    public AgentChannelSnapshot? Find(string channelId) =>
        Channels.FirstOrDefault(channel =>
            string.Equals(channel.Id, channelId, StringComparison.Ordinal));

    public bool HasCapture(VirtualCrossoverSpatialAverageMode mode) =>
        Channels.Any(channel => channel.SpatialAverageCaptures.Contains(
            mode.ToString(), StringComparer.Ordinal));
}

/// <summary>Layout-neutral magnitudes (the layout toggle owns signs); the gain balance opt-in is never stored.</summary>
internal sealed record AgentAutoDelaySettings(
    double SceneOffsetMs,
    bool RightHandDrive,
    bool AdjustGains,
    double NearSideCutDb,
    double RearFillOffsetMs);

/// <summary>12 hex digits of SHA-256 over bands in order (type, frequency, Q, gain, round-trip) then preamp: any edit or reorder changes it.</summary>
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
