namespace Resonalyze.Dsp.Tests;

public sealed class EqBoostabilityMaskTests
{
    private static readonly IReadOnlyList<double> Grid =
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 400);

    private static double[] Magnitude(Func<double, double> db) =>
        Grid.Select(db).ToArray();

    private static bool[] AllValid() => Enumerable.Repeat(true, Grid.Count).ToArray();

    private static int NearestIndex(double frequencyHz)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < Grid.Count; i++)
        {
            double distance = Math.Abs(Math.Log2(Grid[i] / frequencyHz));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    // A symmetric V-notch centred at centerHz: 0 dB outside +/-halfWidthOctaves,
    // dropping linearly to -depthDb at the centre.
    private static double Notch(double f, double centerHz, double depthDb, double halfWidthOctaves)
    {
        double octaves = Math.Abs(Math.Log2(f / centerHz));
        return octaves >= halfWidthOctaves ? 0.0 : -depthDb * (1.0 - octaves / halfWidthOctaves);
    }

    [Fact]
    public void NarrowDeepNull_IsNotBoostable()
    {
        // A 12 dB notch that recovers within +/-0.15 octave — narrower than the
        // default 0.25-octave window, so both sides climb the full depth.
        double[] magnitude = Magnitude(f => Notch(f, 1_000, 12, 0.15));

        bool[] allowed = EqBoostabilityMask.ComputeBoostAllowed(
            Grid, magnitude, AllValid(), coherence: null, new EqBoostabilityMask.Options());

        Assert.False(allowed[NearestIndex(1_000)]);
    }

    [Fact]
    public void FlatResponse_IsFullyBoostable()
    {
        double[] magnitude = Magnitude(_ => 0.0);

        bool[] allowed = EqBoostabilityMask.ComputeBoostAllowed(
            Grid, magnitude, AllValid(), coherence: null, new EqBoostabilityMask.Options());

        Assert.All(allowed, Assert.True);
    }

    [Fact]
    public void BroadDip_IsStillBoostable()
    {
        // A wide, shallow bowl (recovers only beyond +/-0.7 octave) is a correctable
        // trend, not an interference null: the mask must leave it boostable.
        double[] magnitude = Magnitude(f => Notch(f, 1_000, 8, 0.7));

        bool[] allowed = EqBoostabilityMask.ComputeBoostAllowed(
            Grid, magnitude, AllValid(), coherence: null, new EqBoostabilityMask.Options());

        Assert.True(allowed[NearestIndex(1_000)]);
    }

    [Fact]
    public void MonotonicRollOff_IsNotTreatedAsANull()
    {
        // A low-frequency roll-off recovers on the high side only; it must not be
        // masked (it is the boost-headroom cap's job, not the null detector's).
        double[] magnitude = Magnitude(f => f >= 100 ? 0.0 : -30.0 * Math.Log2(100 / f));

        bool[] allowed = EqBoostabilityMask.ComputeBoostAllowed(
            Grid, magnitude, AllValid(), coherence: null, new EqBoostabilityMask.Options());

        Assert.True(allowed[NearestIndex(40)]);
    }

    [Fact]
    public void LowCoherence_IsNotBoostable()
    {
        // Flat magnitude (nothing the null detector would flag), but the coherence
        // dips below the floor around 1 kHz — boosting there is disallowed.
        double[] magnitude = Magnitude(_ => 0.0);
        double[] coherence = Grid
            .Select(f => Math.Abs(Math.Log2(f / 1_000)) < 0.2 ? 0.2 : 0.95)
            .ToArray();

        bool[] allowed = EqBoostabilityMask.ComputeBoostAllowed(
            Grid, magnitude, AllValid(), coherence, new EqBoostabilityMask.Options());

        Assert.False(allowed[NearestIndex(1_000)]);
        Assert.True(allowed[NearestIndex(8_000)]);
    }

    [Fact]
    public void NonFiniteCoherence_IsTreatedAsReliable()
    {
        double[] magnitude = Magnitude(_ => 0.0);
        double[] coherence = Grid.Select(_ => double.NaN).ToArray();

        bool[] allowed = EqBoostabilityMask.ComputeBoostAllowed(
            Grid, magnitude, AllValid(), coherence, new EqBoostabilityMask.Options());

        Assert.All(allowed, Assert.True);
    }
}
