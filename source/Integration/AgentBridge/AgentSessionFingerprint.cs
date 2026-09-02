using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>
/// A short hash of everything a package vouches for: the blocks and their
/// order, each side's measurement and captures, every chain, and the project
/// figures the diagnostics were computed under. Taken when a package is copied
/// and again when a reply is reviewed; a difference means the reply describes
/// a session this one no longer is, whichever way it changed — a measurement
/// replaced, an import undone, a gate moved — and the review says so without
/// anyone having to remember to forget the package on every such path.
/// </summary>
/// <remarks>
/// The lines are written by the panel, which is the one thing that knows the
/// whole session; this class only fixes the hashing so a test can pin that two
/// manifests differing in one field differ in the hash. Order-sensitive: the
/// block order is part of what the channel ids mean.
/// </remarks>
internal static class AgentSessionFingerprint
{
    // One digest per array INSTANCE, kept as long as the array is: a measurement
    // is hashed once when first fingerprinted, not on every review. The arrays
    // this is asked about — a loaded impulse response, its coherence, an
    // imported target curve — are replaced, never edited in place, which is
    // what makes the cache honest.
    private static readonly ConditionalWeakTable<Array, string> Digests = new();

    /// <summary>Sixteen hex digits of SHA-256 over the lines, newline-joined.</summary>
    public static string Compute(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    /// <summary>
    /// A number in round-trip invariant form; <c>null</c> as the empty string.
    /// Negative zero reads as zero: a gain typed as 0 and one arrived at by
    /// arithmetic are the same setting.
    /// </summary>
    public static string Number(double? value) =>
        value is { } number
            ? (number == 0 ? 0.0 : number).ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>
    /// The CONTENT of an array, as sixteen hex digits — so a measurement saved
    /// over its own file, same name, same length, same rate, still reads as the
    /// different measurement it is. <c>null</c> as the empty string.
    /// </summary>
    public static string ContentDigest<T>(T[]? values) where T : unmanaged =>
        values == null
            ? string.Empty
            : Digests.GetValue(values, array => Hex(SHA256.HashData(MemoryMarshal.AsBytes<T>((T[])array))));

    /// <summary>
    /// The content of a short curve — a calibration's points — as sixteen hex
    /// digits, hashed on every call: the points are read off a list the caller
    /// flattens, so there is no array instance to cache by.
    /// </summary>
    public static string ContentDigest(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        double[] flat = values.ToArray();
        return Hex(SHA256.HashData(MemoryMarshal.AsBytes<double>(flat)));
    }

    private static string Hex(byte[] hash) => Convert.ToHexStringLower(hash)[..16];
}
