using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>Order-sensitive hash of the session manifest lines the panel writes. See docs/tech/agent-bridge.md#session-fingerprint.</summary>
internal static class AgentSessionFingerprint
{
    // Cached per array instance: the arrays hashed (impulse responses, coherence, imported targets) are replaced, never edited in place.
    private static readonly ConditionalWeakTable<Array, string> Digests = new();

    public static string Compute(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    /// <summary>Round-trip invariant; null as empty; negative zero reads as zero.</summary>
    public static string Number(double? value) =>
        value is { } number
            ? (number == 0 ? 0.0 : number).ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;

    public static string ContentDigest<T>(T[]? values) where T : unmanaged =>
        values == null
            ? string.Empty
            : Digests.GetValue(values, array => Hex(SHA256.HashData(MemoryMarshal.AsBytes<T>((T[])array))));

    /// <summary>Hashed on every call: a flattened list has no array instance to cache by.</summary>
    public static string ContentDigest(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        double[] flat = values.ToArray();
        return Hex(SHA256.HashData(MemoryMarshal.AsBytes<double>(flat)));
    }

    private static string Hex(byte[] hash) => Convert.ToHexStringLower(hash)[..16];
}
