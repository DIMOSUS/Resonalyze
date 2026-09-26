using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

/// <summary>Band ladder of <see cref="VirtualCrossoverAnalysis.ArrivalCoherenceLadder"/>: lag is a correction to the UPPER channel.</summary>
public sealed class ArrivalCoherenceLadderTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 16_384;
    private const int BasePosition = 2_048;
    private const double CrossoverHz = 1_500;
    private const double BandLowHz = 750;
    private const double BandHighHz = 3_000;

    private static VirtualCrossoverAnalysis.ArrivalCoherencePoint Band(
        double frequencyHz, double lagMs, double peakR) =>
        new(frequencyHz, lagMs, peakR, CurrentR: 0, HalfPeriodMs: 0.5);

    [Fact]
    public void CountLadderAgreement_CountsTheBandsWithinAQuarterPeriod()
    {
        var ladder = new[]
        {
            Band(1_000, lagMs: 0.20, peakR: 0.9),
            Band(1_200, lagMs: 0.30, peakR: 0.9),
            Band(1_500, lagMs: 0.55, peakR: 0.9),
            Band(2_000, lagMs: -0.40, peakR: 0.9)
        };

        // A quarter period at 1500 Hz is 0.167 ms.
        Assert.Equal(
            2,
            VirtualCrossoverAnalysis.CountLadderAgreement(
                ladder, delayMs: 0.25, quarterPeriodMs: 0.167, minPeakR: 0.6));
    }

    [Fact]
    public void CountLadderAgreement_LeavesTheIncoherentBandsOutOfTheVote()
    {
        var ladder = new[]
        {
            Band(1_000, lagMs: 0.25, peakR: 0.9),
            Band(1_200, lagMs: 0.25, peakR: 0.2)
        };

        Assert.Equal(
            1,
            VirtualCrossoverAnalysis.CountLadderAgreement(
                ladder, delayMs: 0.25, quarterPeriodMs: 0.167, minPeakR: 0.6));
    }

    [Fact]
    public void CountLadderAgreement_SeparatesTwoCandidatesAHalfPeriodApart()
    {
        var ladder = new[]
        {
            Band(1_000, lagMs: 0.24, peakR: 0.9),
            Band(1_200, lagMs: 0.28, peakR: 0.9),
            Band(1_500, lagMs: 0.31, peakR: 0.9),
            Band(2_000, lagMs: -0.05, peakR: 0.9)
        };

        int onTheLobe = VirtualCrossoverAnalysis.CountLadderAgreement(
            ladder, delayMs: 0.28, quarterPeriodMs: 0.167, minPeakR: 0.6);
        int halfAPeriodEarly = VirtualCrossoverAnalysis.CountLadderAgreement(
            ladder, delayMs: -0.05, quarterPeriodMs: 0.167, minPeakR: 0.6);
        Assert.Equal(3, onTheLobe);
        Assert.Equal(1, halfAPeriodEarly);
    }

    [Fact]
    public void CountLadderAgreement_RefusesAWindowlessQuarterPeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VirtualCrossoverAnalysis.CountLadderAgreement(
                [], delayMs: 0, quarterPeriodMs: 0, minPeakR: 0.6));
    }
    private static Complex[] Impulse(double offsetMs = 0, double amplitude = 1.0)
    {
        var ir = new Complex[IrLength];
        ir[BasePosition + (int)Math.Round(offsetMs / 1000.0 * SampleRate)] =
            amplitude;
        return ir;
    }

    private static List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> Ladder(
        Complex[] lower, Complex[] upper) =>
        VirtualCrossoverAnalysis.ArrivalCoherenceLadder(
            lower, upper, SampleRate, BandLowHz, BandHighHz, CrossoverHz);

    [Fact]
    public void Ladder_ReadsAPureDelayFlatAcrossTheBand()
    {
        List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> ladder =
            Ladder(Impulse(), Impulse(-0.15));

        Assert.NotEmpty(ladder);
        Assert.Equal(BandLowHz, ladder[0].FrequencyHz, 6);
        Assert.True(
            ladder[^1].FrequencyHz > BandHighHz / Math.Pow(2, 1.0 / 6) - 1,
            "the ladder must reach the band's top");
        foreach (VirtualCrossoverAnalysis.ArrivalCoherencePoint point in ladder)
        {
            Assert.True(
                Math.Abs(point.LagMs - 0.15) < 0.05,
                $"band {point.FrequencyHz:0} Hz read {point.LagMs:0.000} ms " +
                "instead of the +0.15 ms correction");
            Assert.True(
                point.PeakR > 0.8,
                $"band {point.FrequencyHz:0} Hz peak r {point.PeakR:0.00}");
            Assert.True(point.PeakR <= 1.0 && point.CurrentR <= 1.0);
            Assert.Equal(500.0 / point.FrequencyHz, point.HalfPeriodMs, 9);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Ladder_ReadsAnInvertedPairAsCenteredAtLagZero()
    {
        // The envelope is polarity-blind: an inversion leaves the optimum at 0 and CurrentR equals PeakR.
        List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> ladder =
            Ladder(Impulse(), Impulse(0, -1.0));

        Assert.NotEmpty(ladder);
        foreach (VirtualCrossoverAnalysis.ArrivalCoherencePoint point in ladder)
        {
            Assert.True(
                Math.Abs(point.LagMs) < 0.05,
                $"band {point.FrequencyHz:0} Hz optimum at {point.LagMs:0.000} ms");
            Assert.True(
                point.PeakR - point.CurrentR < 0.05,
                $"band {point.FrequencyHz:0} Hz leaves " +
                $"{point.PeakR - point.CurrentR:0.00} r on the table while " +
                "sitting on its optimum");
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Ladder_DropsBandsWhereOneChannelStopsParticipating()
    {
        // The level gate sits at the sum-loss curve's 25 dB: -40 dB empties the ladder, -20 dB participates.
        Assert.Empty(Ladder(Impulse(), Impulse(0, 0.01)));
        Assert.NotEmpty(Ladder(Impulse(), Impulse(0, 0.1)));
    }

    [Fact]
    public void Ladder_ResolvesADispersiveJunction()
    {
        // Two paths kept spectrally apart so each asserted probe band sees one arrival.
        Complex[] lower = Impulse();
        Complex[] upper = BandPulse(600, 1_200, 0)
            .Zip(BandPulse(1_900, 3_600, -0.5), (a, b) => a + b)
            .ToArray();

        List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> ladder =
            Ladder(lower, upper);

        List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> low = ladder
            .Where(point => point.FrequencyHz <= 850)
            .ToList();
        List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> high = ladder
            .Where(point => point.FrequencyHz >= 2_600)
            .ToList();
        Assert.NotEmpty(low);
        Assert.NotEmpty(high);
        Assert.All(low, point => Assert.True(
            Math.Abs(point.LagMs) < 0.12,
            $"band {point.FrequencyHz:0} Hz read {point.LagMs:0.000} ms " +
            "for the aligned low path"));
        Assert.All(high, point => Assert.True(
            Math.Abs(point.LagMs - 0.5) < 0.12,
            $"band {point.FrequencyHz:0} Hz read {point.LagMs:0.000} ms " +
            "for the +0.5 ms high path"));
    }

    // Unit spectral density matches the unit delta bin for bin, so only timing differs.
    private static Complex[] BandPulse(double lowHz, double highHz, double offsetMs)
    {
        var spectrum = new Complex[IrLength];
        double center = BasePosition + offsetMs / 1000.0 * SampleRate;
        for (int k = 0; k < IrLength; k++)
        {
            double frequency = (double)k / IrLength * SampleRate;
            if (frequency > SampleRate / 2.0)
            {
                continue;
            }

            double weight = SkirtedBandWeight(frequency, lowHz, highHz);
            if (weight <= 0)
            {
                continue;
            }

            double phase = -Math.Tau * frequency * center / SampleRate;
            spectrum[k] = weight * Complex.FromPolarCoordinates(1.0, phase);
            if (k > 0 && k < IrLength / 2)
            {
                spectrum[IrLength - k] = Complex.Conjugate(spectrum[k]);
            }
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return spectrum
            .Select(sample => (Complex)sample.Real)
            .ToArray();
    }

    private static double SkirtedBandWeight(
        double frequency, double lowHz, double highHz)
    {
        if (frequency <= 0)
        {
            return 0;
        }

        double skirt = 0.5; // octaves
        double Edge(double edgeHz, bool rising)
        {
            double octaves = Math.Log2(frequency / edgeHz);
            double position = rising ? octaves / skirt : -octaves / skirt;
            return position switch
            {
                >= 0 => 1.0,
                <= -1 => 0.0,
                _ => 0.5 + 0.5 * Math.Cos(Math.PI * position)
            };
        }

        return Edge(lowHz, rising: true) * Edge(highHz, rising: false);
    }
}
