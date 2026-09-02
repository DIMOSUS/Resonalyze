using System.Globalization;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

public sealed class AgentSessionFingerprintTests
{
    [Fact]
    public void Compute_IsStable_AndReadsEveryLineInOrder()
    {
        string[] lines = ["processor;x;96000", "A;left;Front;True;False;;a.json"];
        string baseline = AgentSessionFingerprint.Compute(lines);

        Assert.Equal(16, baseline.Length);
        Assert.Matches("^[0-9a-f]{16}$", baseline);
        Assert.Equal(baseline, AgentSessionFingerprint.Compute(lines));
        // The block order is part of what the channel ids mean.
        Assert.NotEqual(baseline, AgentSessionFingerprint.Compute(lines.Reverse()));
        Assert.NotEqual(baseline, AgentSessionFingerprint.Compute([lines[0], lines[1] + ";1"]));
        Assert.NotEqual(baseline, AgentSessionFingerprint.Compute([lines[0]]));
    }

    [Fact]
    public void ContentDigest_ReadsTheSamples_NotTheArray()
    {
        double[] a = [0.1, 0.2, 0.3];
        double[] same = [0.1, 0.2, 0.3];
        double[] other = [0.1, 0.2, 0.30000001];
        string digest = AgentSessionFingerprint.ContentDigest(a);

        Assert.Equal(16, digest.Length);
        Assert.Equal(digest, AgentSessionFingerprint.ContentDigest(a));
        Assert.Equal(digest, AgentSessionFingerprint.ContentDigest(same));
        Assert.NotEqual(digest, AgentSessionFingerprint.ContentDigest(other));
        Assert.NotEqual(digest, AgentSessionFingerprint.ContentDigest(a[..2]));
        Assert.Equal(string.Empty, AgentSessionFingerprint.ContentDigest<double>(null));
        // The uncached twin, for a curve flattened on the way: same reading.
        Assert.Equal(digest, AgentSessionFingerprint.ContentDigest(a.AsEnumerable()));
        Assert.NotEqual(digest, AgentSessionFingerprint.ContentDigest(other.AsEnumerable()));
        // A complex array hashes its real and imaginary parts alike.
        Assert.NotEqual(
            AgentSessionFingerprint.ContentDigest([new System.Numerics.Complex(1, 0)]),
            AgentSessionFingerprint.ContentDigest([new System.Numerics.Complex(1, 1)]));
    }

    [Fact]
    public void Number_IsCultureInvariant_AndRoundTrips()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.42", AgentSessionFingerprint.Number(1.42));
            Assert.Equal("0.1", AgentSessionFingerprint.Number(0.1));
            Assert.Equal(string.Empty, AgentSessionFingerprint.Number(null));
            Assert.Equal("0", AgentSessionFingerprint.Number(-0.0));
            // Two values a display would print alike must not hash alike.
            Assert.NotEqual(
                AgentSessionFingerprint.Number(0.30000000000000004),
                AgentSessionFingerprint.Number(0.3));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
