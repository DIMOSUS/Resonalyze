using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The junction coherence view's band ladder
/// (<see cref="VirtualCrossoverAnalysis.ArrivalCoherenceLadder"/>): per band,
/// the envelope optimum of the direct cuts' band-limited GCC-PHAT — its lag
/// convention (a correction to the UPPER channel), the coherence it reaches
/// against what the applied alignment collects, and the level gate that drops
/// bands one channel no longer participates in.
/// </summary>
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

        // A quarter period of a 1500 Hz junction is 0.167 ms: the first two
        // bands sit inside it around 0.25 ms, the other two do not.
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
            // Right where the candidate is, and worthless: the ladder reports
            // a lag for every band it probes, coherent or not.
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
        // The shape the veto reads: the bands agree on one lobe, and the
        // opposite-polarity candidate half a period away collects almost none.
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
        // The upper channel arrives 0.15 ms EARLY: every band's optimum is
        // the same +0.15 ms correction to the upper channel — the ladder of a
        // dispersion-free junction is a flat line at the misalignment, in the
        // correlation view's own lag convention.
        List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> ladder =
            Ladder(Impulse(), Impulse(-0.15));

        Assert.NotEmpty(ladder);
        // The grid spans the pair band on sixth-octave steps.
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
    public void Ladder_ReadsAnInvertedPairAsCenteredAtLagZero()
    {
        // Time-aligned but INVERTED: the envelope is polarity-blind, so the
        // optimum stays at lag 0 and the band already collects its full
        // coherence there — CurrentR equals PeakR. The ladder reports no
        // polarity of its own (its probe band cannot separate opposite-signed
        // lobes); that an inversion does not move the optimum is exactly why.
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
    public void Ladder_DropsBandsWhereOneChannelStopsParticipating()
    {
        // 40 dB down, the upper channel is a crossover remnant everywhere in
        // the band: PHAT would still read "coherence" off it, so the level
        // gate must empty the ladder. 20 dB down it participates and the
        // ladder reads normally — the gate sits between, at the sum-loss
        // curve's own 25 dB.
        Assert.Empty(Ladder(Impulse(), Impulse(0, 0.01)));
        Assert.NotEmpty(Ladder(Impulse(), Impulse(0, 0.1)));
    }

    [Fact]
    public void Ladder_ResolvesADispersiveJunction()
    {
        // The upper channel's band content arrives in two pieces: its lower
        // part aligned with the lower channel, its upper part half a
        // millisecond EARLY — the two-path shape a tweeter with a different
        // acoustic path draws. One delay cannot reconcile them, and the
        // ladder must say so: low bands read ~0, high bands read ~+0.5 ms.
        // The paths are kept spectrally apart so the asserted probe bands
        // each see exactly one arrival; where a probe band straddles both,
        // the envelope reads their interference — real content, not a
        // defect, and not what this test pins.
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

    // A linear-phase band-limited pulse: UNIT spectral density over
    // [lowHz, highHz] (half-octave raised-cosine skirts) with its energy
    // centered at BasePosition + offsetMs. Unit density on purpose — it
    // matches the unit delta's density bin for bin, so the level gate sees
    // two equal participants and only the timing differs.
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
