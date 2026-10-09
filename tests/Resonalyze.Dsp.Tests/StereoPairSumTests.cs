using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>The split a low pair's own sum asks for: the strongest optimum of the two sides' sum over the pair's band.</summary>
public sealed class StereoPairSumTests
{
    private const int SampleRate = 48_000;

    private static Complex[] Impulses(params (double Ms, double Amplitude)[] taps)
    {
        var ir = new Complex[SampleRate / 2];
        foreach ((double ms, double amplitude) in taps)
        {
            ir[(int)Math.Round(ms / 1000.0 * SampleRate)] += amplitude;
        }

        return ir;
    }

    [Fact]
    public void Read_TwoFronts_SumBestWithTheEarlierSideDelayedOntoTheLater()
    {
        StereoPairSumReading? reading = StereoPairSum.Read(
            Impulses((10.0, 1.0)), Impulses((11.5, 1.0)), SampleRate, 80, 175);

        Assert.NotNull(reading);
        Assert.Equal(1.5, reading.LeftLaterMs, 2);
        Assert.InRange(reading.GainDb, 2.9, 3.02);
    }

    [Fact]
    public void Read_FollowsTheSideWhereTheEnergyIs_NotItsFirstArrival()
    {
        // Four fifths of the right side's energy arrives 2.5 ms behind its front: the sum stands near the energy,
        // pulled a little toward the front, and keeps a weaker runner-up.
        StereoPairSumReading? reading = StereoPairSum.Read(
            Impulses((10.0, 1.0)), Impulses((11.5, 1.0), (14.0, 4.0)), SampleRate, 80, 175);

        Assert.NotNull(reading);
        Assert.InRange(reading.LeftLaterMs, 3.4, 4.1);
        Assert.NotNull(reading.RunnerUpMs);
        Assert.True(reading.RunnerUpGainDb < reading.GainDb);
    }

    [Fact]
    public void Read_AnInvertedRightSide_SumsBestFlipped()
    {
        StereoPairSumReading? plain = StereoPairSum.Read(
            Impulses((10.0, 1.0)), Impulses((11.5, -1.0)), SampleRate, 80, 175);
        StereoPairSumReading? flipped = StereoPairSum.Read(
            Impulses((10.0, 1.0)), Impulses((11.5, -1.0)), SampleRate, 80, 175, invertRight: true);

        Assert.NotNull(flipped);
        Assert.Equal(1.5, flipped.LeftLaterMs, 2);
        Assert.True(plain == null || Math.Abs(plain.LeftLaterMs - 1.5) > 1.0);
    }

    [Fact]
    public void Read_ScansAroundTheRecordsRelation_NotTheRendersOne()
    {
        // The left side comes rendered 8 ms late (a descent's delay): the physical split, 1.5 ms, is 6.5 ms off the
        // render's, past the scan's reach, so the scan is centred back on the records.
        Complex[] left = Impulses((18.0, 1.0));
        Complex[] right = Impulses((11.5, 1.0));

        StereoPairSumReading? centred = StereoPairSum.Read(left, right, SampleRate, 80, 175, centreMs: -8.0);
        StereoPairSumReading? rendered = StereoPairSum.Read(left, right, SampleRate, 80, 175);

        Assert.NotNull(centred);
        Assert.Equal(-6.5, centred.LeftLaterMs, 2);
        Assert.True(rendered == null || Math.Abs(rendered.LeftLaterMs + 6.5) > 1.0);
    }

    [Fact]
    public void Read_TheCabinsGeometryPicksTheLobe_NotTheStrongestOptimum()
    {
        // Most of the right side's energy comes 3.5 ms behind its front: the strongest sum is a lobe off where the
        // cabin's geometry puts the pair, and the geometry sets it aside for the lobe at the front.
        Complex[] left = Impulses((10.0, 1.0));
        Complex[] right = Impulses((11.5, 0.6), (15.0, 1.0));

        StereoPairSumReading? strongest = StereoPairSum.Read(left, right, SampleRate, 80, 300);
        StereoPairSumReading? onGeometry = StereoPairSum.Read(left, right, SampleRate, 80, 300, geometryMs: 1.3);

        Assert.NotNull(strongest);
        Assert.InRange(strongest.LeftLaterMs, 4.8, 5.3);
        Assert.Null(strongest.SetAsideMs);
        Assert.NotNull(onGeometry);
        Assert.InRange(onGeometry.LeftLaterMs, 1.0, 1.8);
        Assert.Equal(strongest.LeftLaterMs, onGeometry.SetAsideMs);
        Assert.True(onGeometry.SetAsideGainDb > onGeometry.GainDb);
        Assert.True(onGeometry.IsDecisive);
    }

    [Fact]
    public void Read_AGeometryOnASidelobe_IsNotDecisive()
    {
        // Half a period off the pair's only lobe, the geometry finds a sidelobe the sides cancel on.
        StereoPairSumReading? reading = StereoPairSum.Read(
            Impulses((10.0, 1.0)), Impulses((11.5, 1.0)), SampleRate, 80, 300, geometryMs: -3.0);

        Assert.Equal(0.5 * 1000.0 / Math.Sqrt(80.0 * 300.0), StereoPairSum.GeometryLobeReachMs(80, 300), 6);
        Assert.NotNull(reading);
        Assert.Equal(1.5, reading.SetAsideMs!.Value, 2);
        Assert.False(reading.IsDecisive);
    }

    [Fact]
    public void Read_TwoUnrelatedSides_FindAnOptimumThatIsNotDecisive()
    {
        // Two noise records sum to a few hundredths of a dB on a lobe no better than its neighbours: an optimum
        // exists, as it always does, and it may not hold the pair.
        var random = new Random(7);
        Complex[] Noise()
        {
            var ir = new Complex[SampleRate / 2];
            for (int i = 0; i < ir.Length; i++)
            {
                ir[i] = random.NextDouble() - 0.5;
            }

            return ir;
        }

        StereoPairSumReading? reading = StereoPairSum.Read(Noise(), Noise(), SampleRate, 80, 175);

        Assert.NotNull(reading);
        Assert.False(reading.IsDecisive);
        Assert.True(StereoPairSum.Read(
            Impulses((10.0, 1.0)), Impulses((11.5, 1.0), (14.0, 4.0)), SampleRate, 80, 175)!.IsDecisive);
    }

    [Fact]
    public void Read_ASilentSide_ReadsNothing()
    {
        Assert.Null(StereoPairSum.Read(
            Impulses((10.0, 1.0)), new Complex[SampleRate / 2], SampleRate, 80, 175));
    }
}
