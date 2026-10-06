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
    public void Read_ASilentSide_ReadsNothing()
    {
        Assert.Null(StereoPairSum.Read(
            Impulses((10.0, 1.0)), new Complex[SampleRate / 2], SampleRate, 80, 175));
    }
}
