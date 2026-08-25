using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The hybrid view's sum. Spatial averages hold no phase, so their channels cannot
/// be summed as vectors; the sum is their magnitudes added as amplitudes with the
/// summation loss the honest impulse responses measure laid on top. These pin the
/// arithmetic that makes that substitution legitimate — and the two ways it is
/// allowed to break.
/// </summary>
public sealed class VirtualCrossoverHybridSumTests
{
    /// <summary>
    /// The formula's own inverse: fed the honest channel magnitudes, it has to give
    /// back the honest sum, to the last decimal. That is what makes the loss curve a
    /// legitimate stand-in for the phase the average threw away — the hybrid sum
    /// differs from the measured one ONLY where the hybrid channels differ from the
    /// measured ones, never because of the reconstruction itself.
    /// </summary>
    [Fact]
    public void FedTheHonestChannels_ItReproducesTheHonestSum()
    {
        // Two channels that genuinely cancel in places, so the loss curve carries
        // real dips rather than a flat zero that any formula would reproduce.
        var low = new List<SignalPoint>();
        var high = new List<SignalPoint>();
        var sum = new List<SignalPoint>();
        for (int i = 0; i < 200; i++)
        {
            double hz = 20 * Math.Pow(10, 3.0 * i / 199);
            double lowDb = -6 + 4 * Math.Sin(i / 11.0);
            double highDb = -9 + 3 * Math.Cos(i / 7.0);
            // A complex sum with a phase that walks, so the cancellation is deep in
            // places and constructive in others.
            double phase = i / 5.0;
            double amplitude = Math.Abs(
                DataHelper.DecibelsToAmplitude(lowDb) +
                DataHelper.DecibelsToAmplitude(highDb) * Math.Cos(phase));
            low.Add(new SignalPoint(hz, lowDb));
            high.Add(new SignalPoint(hz, highDb));
            sum.Add(new SignalPoint(hz, DataHelper.AmplitudeToDecibels(amplitude)));
        }

        List<SignalPoint> loss = VirtualCrossoverAnalysis.SumLossCurve(sum, [low, high]);
        // The reconstruction has real work to do here: without a loss curve that
        // genuinely dips, any formula would reproduce a magnitude sum.
        Assert.Contains(loss, point => point.Y < -6);

        List<SignalPoint> rebuilt = BuildHybridSum([low, high], offsetDb: 0, sum, loss);

        Assert.Equal(sum.Count, rebuilt.Count);
        for (int i = 0; i < sum.Count; i++)
        {
            Assert.Equal(sum[i].X, rebuilt[i].X);
            Assert.Equal(sum[i].Y, rebuilt[i].Y, 10);
        }
    }

    /// <summary>
    /// The set's common offset reaches the sum as the same number of dB it moves the
    /// channels by — a shared gain factors straight out of a magnitude sum, so the
    /// hybrid sum cannot drift away from the hybrid channels when the set is levelled.
    /// </summary>
    [Fact]
    public void TheSetsOffset_MovesTheSumByExactlyTheSameDecibels()
    {
        (List<SignalPoint> low, List<SignalPoint> high, List<SignalPoint> sum,
            List<SignalPoint> loss) = SimpleSet();

        List<SignalPoint> at0 = BuildHybridSum([low, high], offsetDb: 0, sum, loss);
        List<SignalPoint> at7 = BuildHybridSum([low, high], offsetDb: 7.5, sum, loss);

        for (int i = 0; i < at0.Count; i++)
        {
            Assert.Equal(at0[i].Y + 7.5, at7[i].Y, 10);
        }
    }

    /// <summary>
    /// Where the loss curve breaks, the sum breaks with it. The tempting fallback —
    /// summing the magnitudes and calling it a system — draws its most confident
    /// picture (no cancellation anywhere) exactly where the measurement is weakest,
    /// which is the one place a tune must not be trusted.
    /// </summary>
    [Fact]
    public void WhereTheLossCurveBreaks_TheSumBreaksToo()
    {
        (List<SignalPoint> low, List<SignalPoint> high, List<SignalPoint> sum,
            List<SignalPoint> loss) = SimpleSet();
        loss[3] = new SignalPoint(loss[3].X, double.NaN);

        List<SignalPoint> hybrid = BuildHybridSum([low, high], offsetDb: 0, sum, loss);

        Assert.True(double.IsNaN(hybrid[3].Y));
        Assert.True(double.IsFinite(hybrid[2].Y));
        Assert.True(double.IsFinite(hybrid[4].Y));
    }

    /// <summary>
    /// A channel whose own capture stops — below its protective high-pass, or past
    /// the end of its grid — drops out of that point's sum instead of taking the
    /// whole sum with it. Its output there is far under the others and its own
    /// crossover removes it anyway.
    /// </summary>
    [Fact]
    public void AChannelThatBreaks_DropsOutOfThatPointOnly()
    {
        (List<SignalPoint> low, List<SignalPoint> high, List<SignalPoint> sum,
            List<SignalPoint> loss) = SimpleSet();
        high[3] = new SignalPoint(high[3].X, double.NaN);

        List<SignalPoint> hybrid = BuildHybridSum([low, high], offsetDb: 0, sum, loss);

        // The surviving channel alone, plus the point's loss.
        Assert.Equal(low[3].Y + loss[3].Y, hybrid[3].Y, 10);
    }

    /// <summary>
    /// With every channel broken at a point there is nothing left to sum, and the
    /// sum says so rather than reporting silence as a level.
    /// </summary>
    [Fact]
    public void WithEveryChannelBrokenAtAPoint_TheSumBreaks()
    {
        (List<SignalPoint> low, List<SignalPoint> high, List<SignalPoint> sum,
            List<SignalPoint> loss) = SimpleSet();
        low[3] = new SignalPoint(low[3].X, double.NaN);
        high[3] = new SignalPoint(high[3].X, double.NaN);

        List<SignalPoint> hybrid = BuildHybridSum([low, high], offsetDb: 0, sum, loss);

        Assert.True(double.IsNaN(hybrid[3].Y));
    }

    // Two smooth channels, their honest complex sum and the loss it implies. Smooth
    // enough that the loss gate never fires, so a NaN in these tests is one the test
    // itself put there.
    private static (List<SignalPoint> Low, List<SignalPoint> High, List<SignalPoint> Sum,
        List<SignalPoint> Loss) SimpleSet()
    {
        var low = new List<SignalPoint>();
        var high = new List<SignalPoint>();
        var sum = new List<SignalPoint>();
        for (int i = 0; i < 40; i++)
        {
            double hz = 50 * Math.Pow(10, 2.0 * i / 39);
            double lowDb = -4 + Math.Sin(i / 6.0);
            double highDb = -7 + Math.Cos(i / 9.0);
            double amplitude =
                DataHelper.DecibelsToAmplitude(lowDb) +
                DataHelper.DecibelsToAmplitude(highDb) * 0.6;
            low.Add(new SignalPoint(hz, lowDb));
            high.Add(new SignalPoint(hz, highDb));
            sum.Add(new SignalPoint(hz, DataHelper.AmplitudeToDecibels(amplitude)));
        }

        return (low, high, sum, VirtualCrossoverAnalysis.SumLossCurve(sum, [low, high]));
    }

    // The panel's own builder, reached directly: the arithmetic is what is under
    // test, and routing it through a live panel would test the plot instead.
    private static List<SignalPoint> BuildHybridSum(
        IReadOnlyList<IReadOnlyList<SignalPoint>> channels,
        double offsetDb,
        IReadOnlyList<SignalPoint> reference,
        IReadOnlyList<SignalPoint> loss)
    {
        MethodInfo method = typeof(VirtualCrossoverPanel).GetMethod(
            "BuildHybridSumCurve",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildHybridSumCurve is gone.");
        object? result = method.Invoke(
            null,
            [new HybridMagnitudes(channels, [], offsetDb), reference, loss]);
        return Assert.IsType<List<SignalPoint>>(result);
    }
}
