using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The one offset that puts a whole spatial-average set on the impulse responses'
/// axis. Its hard part is not the statistic but WHERE it is read: a channel spends
/// most of the drawn range in its stopband, where the impulse response shows what
/// the room and the noise floor left of a filtered driver while the hybrid shows
/// the filter's own analytic slope. Read across the whole range, a real four-way
/// set came out with its channels 73 dB apart, ordered by band.
/// </summary>
public sealed class VirtualCrossoverHybridOffsetTests
{
    /// <summary>
    /// The stopband, where nothing real is being compared, must not reach the
    /// answer — not even when it holds most of the points.
    /// </summary>
    [Fact]
    public void TheOffset_IsReadInTheChannelsOwnBandAndNotItsStopband()
    {
        // A woofer: 60 points of passband, then 940 of rolloff. The measured curve
        // floors on the room and the noise; the hybrid keeps falling with the
        // filter, so in the stopband the two part by more than 100 dB.
        var reference = new List<SignalPoint>();
        var hybrid = new List<SignalPoint>();
        for (int i = 0; i < 1_000; i++)
        {
            double hz = 20 * Math.Pow(10, 3.0 * i / 999);
            bool passband = i < 60;
            double measured = passband ? -3 + Math.Sin(i / 4.0) : Math.Max(-70, -3 - i);
            double analytic = passband ? measured - 7 : -3 - 7 - i * 1.5;
            reference.Add(new SignalPoint(hz, measured));
            hybrid.Add(new SignalPoint(hz, analytic));
        }

        double offset = ResolveOffset([hybrid], [reference]);

        Assert.Equal(7, offset, 6);
    }

    /// <summary>
    /// One offset for the SET, never one per channel: the captures were taken in a
    /// single analyzer session at a fixed gain, so their relative levels are honest
    /// measurements and normalizing each channel separately would throw that away.
    /// A channel that disagrees is left disagreeing — visibly.
    /// </summary>
    [Fact]
    public void TheSetsOffset_IsTheMedianAcrossChannelsAndDoesNotLevelThemSeparately()
    {
        List<SignalPoint> reference = Flat(0);
        List<List<SignalPoint>> hybrids = [Flat(-4), Flat(-5), Flat(-30)];

        double offset = ResolveOffset(
            hybrids.Cast<IReadOnlyList<SignalPoint>>().ToList(),
            [reference, reference, reference]);

        // The outlier moved nothing: the median is the middle channel's own figure,
        // so the odd one out still draws 25 dB away from where it claims to be.
        Assert.Equal(5, offset, 6);
    }

    /// <summary>
    /// A channel whose curves never overlap contributes nothing rather than voting
    /// with a fabricated number.
    /// </summary>
    [Fact]
    public void AChannelWithNothingToCompare_IsSkipped()
    {
        List<SignalPoint> reference = Flat(0);
        List<SignalPoint> missing = Flat(double.NaN);

        double offset = ResolveOffset(
            [missing, Flat(-9)], [reference, reference]);

        Assert.Equal(9, offset, 6);
    }

    private static List<SignalPoint> Flat(double db)
    {
        var points = new List<SignalPoint>();
        for (int i = 0; i < 200; i++)
        {
            points.Add(new SignalPoint(20 * Math.Pow(10, 3.0 * i / 199), db));
        }

        return points;
    }

    private static double ResolveOffset(
        IReadOnlyList<IReadOnlyList<SignalPoint>> hybrids,
        IReadOnlyList<IReadOnlyList<SignalPoint>> references)
    {
        MethodInfo method = typeof(VirtualCrossoverPanel).GetMethod(
            "ResolveHybridOffsetDb",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolveHybridOffsetDb is gone.");
        object? result = method.Invoke(
            null,
            [
                hybrids,
                references
                    .Select(points => new AnalysisCurve("channel", points))
                    .ToList()
            ]);
        return Assert.IsType<double>(result);
    }
}
