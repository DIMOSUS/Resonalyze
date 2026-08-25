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

        (List<double> perChannel, double offset) = Resolve(
            [missing, Flat(-9)], [reference, reference]);

        Assert.Equal(9, offset, 6);
        // Not counted as an offset of zero, which would read as a set disagreeing
        // by 9 dB when only one of its channels has anything to say.
        Assert.Equal([9.0], perChannel);
    }

    /// <summary>
    /// The set's own verdict on itself: how far its channels disagree about where
    /// the captures sit. A capture taken at a different input gain, or with a
    /// different frame length (which moves the noise-slope compensation), lands
    /// here — the detector does not care which, only that one offset can no longer
    /// serve the set.
    /// </summary>
    [Fact]
    public void TheSpread_IsTheDisagreementBetweenChannelsAndNotTheirDistanceFromTheIrs()
    {
        List<SignalPoint> reference = Flat(0);

        // Ninety dB away from the impulse responses, but in perfect agreement:
        // nothing is wrong with this set.
        (List<double> agreeing, _) = Resolve(
            [Flat(-90), Flat(-90), Flat(-90)], [reference, reference, reference]);
        Assert.Equal(0.0, Spread(agreeing), 6);

        // One capture eight dB out: the same distance, now disagreed upon.
        (List<double> mixed, _) = Resolve(
            [Flat(-90), Flat(-90), Flat(-82)], [reference, reference, reference]);
        Assert.Equal(8.0, Spread(mixed), 6);
    }

    /// <summary>
    /// The offsets come back IN CHANNEL ORDER, with a hole where a channel had
    /// nothing to compare. Packed, they silently shifted every figure below that
    /// channel onto the next driver's name in the spread read-out — a diagnostic
    /// blaming the wrong capture is worse than no diagnostic.
    /// </summary>
    [Fact]
    public void AChannelWithNothingToCompare_LeavesAHoleInPlaceAndDoesNotShiftTheRest()
    {
        List<SignalPoint> reference = Flat(0);
        List<SignalPoint> nothing = Flat(double.NaN);

        (double?[] offsets, _) = ResolvePositional(
            [Flat(-90), nothing, Flat(-82)],
            [reference, reference, reference]);

        Assert.Equal(3, offsets.Length);
        Assert.Equal(90.0, offsets[0]!.Value, 6);
        Assert.Null(offsets[1]);
        Assert.Equal(82.0, offsets[2]!.Value, 6);
    }

    private static double Spread(List<double> offsets) =>
        new HybridMagnitudes(
            [], [], offsets.Select(offset => (double?)offset).ToList(), 0).SpreadDb;

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
        IReadOnlyList<IReadOnlyList<SignalPoint>> references) =>
        Resolve(hybrids, references).SetOffsetDb;

    private static (List<double> PerChannel, double SetOffsetDb) Resolve(
        IReadOnlyList<IReadOnlyList<SignalPoint>> hybrids,
        IReadOnlyList<IReadOnlyList<SignalPoint>> references)
    {
        MethodInfo method = typeof(VirtualCrossoverPanel).GetMethod(
            "ResolveHybridOffsetsDb",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolveHybridOffsetsDb is gone.");
        object? result = method.Invoke(
            null,
            [
                hybrids,
                references
                    .Select(points => new AnalysisCurve("channel", points))
                    .ToList()
            ]);
        (double?[] positional, double setOffset) = ((double?[], double))result!;
        // The dense view these tests read: the offsets that could be resolved, in
        // order. The positional form — with a null where a channel had nothing to
        // compare — is what the spread read-out needs, and ResolvePositional returns
        // it untouched.
        return (
            positional.Where(offset => offset.HasValue)
                .Select(offset => offset!.Value)
                .ToList(),
            setOffset);
    }

    private static (double?[] PerChannel, double SetOffsetDb) ResolvePositional(
        IReadOnlyList<IReadOnlyList<SignalPoint>> hybrids,
        IReadOnlyList<IReadOnlyList<SignalPoint>> references)
    {
        MethodInfo method = typeof(VirtualCrossoverPanel).GetMethod(
            "ResolveHybridOffsetsDb",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolveHybridOffsetsDb is gone.");
        object? result = method.Invoke(
            null,
            [
                hybrids,
                references
                    .Select(points => new AnalysisCurve("channel", points))
                    .ToList()
            ]);
        return ((double?[], double))result!;
    }
}
