using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>Builds the Zölzer section independently and compares against the library's RBJ response, not a copied constant.</summary>
public class PeqQConventionTests
{
    private const double SampleRateHz = 48_000;

    [Theory]
    [InlineData(2_000, 5.8, -15.0)]
    [InlineData(10_000, 5.8, -15.0)]
    [InlineData(1_000, 3.0, -6.0)]
    [InlineData(1_000, 3.0, 6.0)]
    [InlineData(120, 1.2, 12.0)]
    [InlineData(45, 8.0, -18.0)]
    [InlineData(6_300, 0.7, -1.0)]
    public void ToConvention_Symmetric_ReproducesTheRbjResponseOnTheDevice(
        double frequencyHz,
        double q,
        double gainDb)
    {
        var designed = new PeqBand(frequencyHz, q, gainDb);
        PeqBand typedIn = PeqQConventions.ToConvention(designed, PeqQConvention.Symmetric);

        foreach (double probe in Probes(frequencyHz))
        {
            double resonalyze = DigitalEqualizationResponse.MagnitudeDbAt(
                designed, probe, SampleRateHz);
            double device = ZoelzerMagnitudeDb(typedIn, probe, SampleRateHz);

            Assert.Equal(resonalyze, device, 9);
        }
    }

    // Guards the test above from passing if ToConvention returned its input.
    [Fact]
    public void Symmetric_WithoutConversion_IsMuchWiderThanRbj()
    {
        var band = new PeqBand(2_000, 5.8, -15.0);

        // 4500 Hz lies between the skirts of the field case's two bands.
        double rbj = DigitalEqualizationResponse.MagnitudeDbAt(band, 4_500, SampleRateHz);
        double symmetric = ZoelzerMagnitudeDb(band, 4_500, SampleRateHz);

        Assert.True(Math.Abs(rbj) < 0.25, $"RBJ skirt was {rbj:F2} dB");
        Assert.True(Math.Abs(symmetric) > 0.8, $"Symmetric skirt was {symmetric:F2} dB");
    }

    // REW's published BW = m * f0 / Q figures (f0 1 kHz, Q 4, ±12 dB): only Classic is asymmetric.
    [Theory]
    [InlineData(PeqQConvention.Rbj, 12.0, 250.0)]
    [InlineData(PeqQConvention.Rbj, -12.0, 250.0)]
    [InlineData(PeqQConvention.Symmetric, 12.0, 498.8)]
    [InlineData(PeqQConvention.Symmetric, -12.0, 498.8)]
    [InlineData(PeqQConvention.Classic, 12.0, 498.8)]
    [InlineData(PeqQConvention.Classic, -12.0, 125.3)]
    public void ToRbj_MatchesRewPublishedHalfGainBandwidth(
        PeqQConvention convention,
        double gainDb,
        double expectedBandwidthHz)
    {
        PeqBand realized = PeqQConventions.ToRbj(
            new PeqBand(1_000, 4.0, gainDb), convention);

        Assert.Equal(expectedBandwidthHz, HalfGainBandwidthHz(realized), 1);
    }

    // The multiplier is written REW's way (sqrt of linear gain), checking the definition rather than our exponent.
    [Fact]
    public void ToRbj_RealizesTheBandwidthEachConventionDefines()
    {
        foreach (PeqQConvention convention in
                 new[] { PeqQConvention.Rbj, PeqQConvention.Symmetric, PeqQConvention.Classic })
        {
            foreach (double gainDb in new[] { -15.0, -12.0, -6.0, 6.0, 12.0 })
            {
                foreach (double deviceQ in new[] { 1.0, 4.0, 8.0 })
                {
                    PeqBand realized = PeqQConventions.ToRbj(
                        new PeqBand(1_000, deviceQ, gainDb), convention);

                    double expected =
                        RewBandwidthMultiplier(convention, gainDb) * 1_000 / deviceQ;
                    double actual = HalfGainBandwidthHz(realized);

                    Assert.Equal(
                        expected,
                        actual,
                        Math.Max(0.05, expected * 1e-3));
                }
            }
        }
    }

    // REW half-gain bandwidths: RBJ Fc/Q; Classic sqrt(gain)*Fc/Q (signed linear gain); Symmetric sqrt(absgain)*Fc/Q.
    private static double RewBandwidthMultiplier(PeqQConvention convention, double gainDb) =>
        convention switch
        {
            PeqQConvention.Symmetric => Math.Sqrt(Math.Pow(10.0, Math.Abs(gainDb) / 20.0)),
            PeqQConvention.Classic => Math.Sqrt(Math.Pow(10.0, gainDb / 20.0)),
            _ => 1.0
        };

    // Classic and Symmetric agree on boosts, so an alias would pass the boost rows.
    [Fact]
    public void Classic_DiffersFromSymmetric_OnCutsOnly()
    {
        var boost = new PeqBand(1_000, 4.0, 9.0);
        var cut = new PeqBand(1_000, 4.0, -9.0);

        Assert.Equal(
            PeqQConventions.ToConvention(boost, PeqQConvention.Classic).Q,
            PeqQConventions.ToConvention(boost, PeqQConvention.Symmetric).Q,
            12);

        double classicCut = PeqQConventions.ToConvention(cut, PeqQConvention.Classic).Q;
        double symmetricCut = PeqQConventions.ToConvention(cut, PeqQConvention.Symmetric).Q;
        Assert.True(classicCut < cut.Q, $"Classic cut Q was {classicCut:F3}");
        Assert.True(symmetricCut > cut.Q, $"Symmetric cut Q was {symmetricCut:F3}");
    }

    [Theory]
    [InlineData(PeqQConvention.Rbj)]
    [InlineData(PeqQConvention.Symmetric)]
    [InlineData(PeqQConvention.Classic)]
    public void ToRbj_UndoesToConvention(PeqQConvention convention)
    {
        var band = new PeqBand(2_000, 5.8, -15.0);

        PeqBand roundTripped = PeqQConventions.ToRbj(
            PeqQConventions.ToConvention(band, convention), convention);

        Assert.Equal(band.FrequencyHz, roundTripped.FrequencyHz);
        Assert.Equal(band.GainDb, roundTripped.GainDb);
        Assert.Equal(band.Q, roundTripped.Q, 12);
    }

    [Fact]
    public void ToConvention_Rbj_IsIdentity()
    {
        var band = new PeqBand(2_000, 5.8, -15.0);

        Assert.Equal(band, PeqQConventions.ToConvention(band, PeqQConvention.Rbj));
    }

    // Scaling a transparent slot's placeholder Q would make it look edited.
    [Theory]
    [InlineData(0.0, 5.8, -15.0)]
    [InlineData(2_000, 0.0, -15.0)]
    [InlineData(2_000, 5.8, 0.0)]
    public void ToConvention_LeavesTransparentBandsAlone(
        double frequencyHz,
        double q,
        double gainDb)
    {
        var band = new PeqBand(frequencyHz, q, gainDb);

        Assert.Equal(band, PeqQConventions.ToConvention(band, PeqQConvention.Symmetric));
        Assert.Equal(band, PeqQConventions.ToConvention(band, PeqQConvention.Classic));
    }

    // Factor 10^(|G|/40): dropping the absolute value turns Symmetric into Classic.
    [Fact]
    public void ToConvention_Symmetric_ScalesBoostAndCutTheSameWay()
    {
        var cut = new PeqBand(1_000, 3.0, -12.0);
        var boost = new PeqBand(1_000, 3.0, 12.0);

        double cutQ = PeqQConventions.ToConvention(cut, PeqQConvention.Symmetric).Q;
        double boostQ = PeqQConventions.ToConvention(boost, PeqQConvention.Symmetric).Q;

        Assert.Equal(cutQ, boostQ, 12);
        Assert.True(cutQ > cut.Q);
    }

    [Fact]
    public void EveryConventionIsDescribedDistinctly()
    {
        PeqQConvention[] conventions =
            [PeqQConvention.Rbj, PeqQConvention.Symmetric, PeqQConvention.Classic];

        foreach (Func<PeqQConvention, string> describe in new Func<PeqQConvention, string>[]
        {
            PeqQConventions.Describe,
            PeqQConventions.DescribeShort,
            PeqQConventions.DescribeBandwidth,
            PeqQConventions.DescribeDevices
        })
        {
            string[] texts = conventions.Select(describe).ToArray();

            Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
            Assert.Equal(texts.Length, texts.Distinct().Count());
        }
    }

    // The quoted width factors are pinned to the conversion so the prose cannot drift.
    [Theory]
    [InlineData(PeqQConvention.Symmetric, 3.0)]
    [InlineData(PeqQConvention.Symmetric, 12.0)]
    [InlineData(PeqQConvention.Symmetric, 15.0)]
    [InlineData(PeqQConvention.Classic, 12.0)]
    [InlineData(PeqQConvention.Classic, -12.0)]
    public void DescribeBandwidth_QuotesTheFactorsTheConversionRealizes(
        PeqQConvention convention,
        double gainDb)
    {
        var band = new PeqBand(1_000, 1.0, gainDb);
        double factor = PeqQConventions.ToConvention(band, convention).Q / band.Q;

        Assert.Contains(
            factor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            PeqQConventions.DescribeBandwidth(convention));
    }

    private static double HalfGainBandwidthHz(PeqBand band)
    {
        // Well above audio: the conventions are analog definitions, free of bilinear warp.
        const double WideRateHz = 768_000;
        double half = band.GainDb / 2.0;

        bool Reached(double frequencyHz)
        {
            double value = DigitalEqualizationResponse.MagnitudeDbAt(
                band, frequencyHz, WideRateHz);
            return band.GainDb < 0 ? value <= half : value >= half;
        }

        return Edge(band.FrequencyHz, band.FrequencyHz * 8, Reached) -
            Edge(band.FrequencyHz, band.FrequencyHz / 8, Reached);
    }

    private static double Edge(double inside, double outside, Func<double, bool> reached)
    {
        for (int i = 0; i < 200; i++)
        {
            double middle = Math.Sqrt(inside * outside);
            if (reached(middle))
            {
                inside = middle;
            }
            else
            {
                outside = middle;
            }
        }

        return Math.Sqrt(inside * outside);
    }

    private static IEnumerable<double> Probes(double centreHz)
    {
        foreach (double ratio in new[]
                 { 0.125, 0.25, 0.5, 0.71, 0.9, 1.0, 1.1, 1.41, 2.0, 4.0 })
        {
            double probe = centreHz * ratio;
            if (probe > 0 && probe < SampleRateHz / 2.2)
            {
                yield return probe;
            }
        }
    }

    private static double ZoelzerMagnitudeDb(PeqBand band, double frequencyHz, double sampleRateHz)
    {
        double v0 = Math.Pow(10.0, band.GainDb / 20.0);
        double k = Math.Tan(Math.PI * band.FrequencyHz / sampleRateHz);
        double kk = k * k;

        double a0, b0, b1, b2, a1, a2;
        if (band.GainDb >= 0)
        {
            a0 = 1 + (k / band.Q) + kk;
            b0 = 1 + (v0 * k / band.Q) + kk;
            b2 = 1 - (v0 * k / band.Q) + kk;
            a2 = 1 - (k / band.Q) + kk;
        }
        else
        {
            a0 = 1 + (k / (v0 * band.Q)) + kk;
            b0 = 1 + (k / band.Q) + kk;
            b2 = 1 - (k / band.Q) + kk;
            a2 = 1 - (k / (v0 * band.Q)) + kk;
        }

        b1 = 2 * (kk - 1);
        a1 = b1;

        // BiquadCoefficients stores a1/a2 negated for the additive-feedback form.
        var coefficients = new BiquadCoefficients(
            b0 / a0, b1 / a0, b2 / a0, -(a1 / a0), -(a2 / a0));
        return 20.0 * Math.Log10(
            BiquadResponse.Evaluate(coefficients, frequencyHz, sampleRateHz).Magnitude);
    }
}
