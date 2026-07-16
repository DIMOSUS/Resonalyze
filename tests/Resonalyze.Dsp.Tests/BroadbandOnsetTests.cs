using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class BroadbandOnsetTests
{
    private const int Rate = 48_000;

    private static Complex[] Silence(int length) => new Complex[length];

    [Fact]
    public void EstimateBroadbandOnset_SilenceIsInvalid()
    {
        BroadbandOnsetEstimate estimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(Silence(4096), Rate);

        Assert.False(estimate.IsValid);
    }

    [Fact]
    public void EstimateBroadbandOnset_DeltaOnsetSitsAtTheDeltaWithTinySpread()
    {
        // A delta's Hilbert envelope carries a short ~1/t skirt, so the low
        // thresholds cross a couple of samples before the peak — the crossings
        // must still hug the delta within a few samples, with a spread far
        // below any crossover period the lock would gate on.
        Complex[] impulseResponse = Silence(4096);
        const int DeltaIndex = 1_000;
        impulseResponse[DeltaIndex] = Complex.One;

        BroadbandOnsetEstimate estimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(impulseResponse, Rate);

        double deltaMs = DeltaIndex * 1_000.0 / Rate;
        double sampleMs = 1_000.0 / Rate;
        Assert.True(estimate.IsValid);
        Assert.InRange(estimate.OnsetMs, deltaMs - 4 * sampleMs, deltaMs + sampleMs);
        Assert.InRange(estimate.LateMs - estimate.EarlyMs, 0, 6 * sampleMs);
    }

    [Fact]
    public void EstimateBroadbandOnset_CrossingsAreMonotonicInTheThreshold()
    {
        // A slow Hann-windowed tone burst: the envelope rises over many samples,
        // so the crossings must order early <= onset <= late and all sit before
        // the envelope peak at the burst's center.
        Complex[] impulseResponse = Silence(8192);
        const int Start = 2_000;
        const int Length = 2_048;
        for (int i = 0; i < Length; i++)
        {
            double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (Length - 1)));
            impulseResponse[Start + i] = new Complex(
                window * Math.Sin(2 * Math.PI * 1_000.0 * i / Rate), 0);
        }

        BroadbandOnsetEstimate estimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(impulseResponse, Rate);

        double centerMs = (Start + Length / 2.0) * 1_000.0 / Rate;
        Assert.True(estimate.IsValid);
        Assert.True(estimate.EarlyMs <= estimate.OnsetMs);
        Assert.True(estimate.OnsetMs <= estimate.LateMs);
        Assert.True(estimate.LateMs < centerMs);
        Assert.True(estimate.EarlyMs >= Start * 1_000.0 / Rate - 1.0);
    }

    [Fact]
    public void EstimateBroadbandOnset_DelayShiftsTheOnsetOneToOne()
    {
        // The onset difference between an IR and its delayed copy is the delay —
        // the invariant the auto-delay onset anchor rests on.
        Complex[] original = Silence(8192);
        const int Start = 1_500;
        for (int i = 0; i < 512; i++)
        {
            double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / 511.0));
            original[Start + i] = new Complex(
                window * Math.Sin(2 * Math.PI * 2_000.0 * i / Rate), 0);
        }

        const int ShiftSamples = 733;
        Complex[] delayed = Silence(8192);
        Array.Copy(original, 0, delayed, ShiftSamples, 8192 - ShiftSamples);

        BroadbandOnsetEstimate first =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(original, Rate);
        BroadbandOnsetEstimate second =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(delayed, Rate);

        double expectedMs = ShiftSamples * 1_000.0 / Rate;
        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.InRange(
            second.OnsetMs - first.OnsetMs,
            expectedMs - 0.05,
            expectedMs + 0.05);
    }
}
