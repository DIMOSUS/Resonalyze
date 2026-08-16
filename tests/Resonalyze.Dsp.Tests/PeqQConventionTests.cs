using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The conversion is only worth anything if it is exact, so these tests do not check
/// the scale factor against a hand-copied constant — they build the Zölzer section
/// independently and compare its magnitude response against the library's RBJ one.
/// </summary>
public class PeqQConventionTests
{
    private const double SampleRateHz = 48_000;

    // A converted band must reproduce the ORIGINAL response on the target device, not
    // merely something close: the whole point is that a user can type these numbers in
    // and measure the curve Resonalyze drew.
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

    // Without the conversion the device is materially wrong, so the test above cannot be
    // passing by accident (e.g. if ToConvention silently returned its input).
    [Fact]
    public void Symmetric_WithoutConversion_IsMuchWiderThanRbj()
    {
        var band = new PeqBand(2_000, 5.8, -15.0);

        // 4500 Hz sits between the skirts of the two bands of the field case that
        // exposed this: RBJ leaves it essentially untouched, Zölzer does not.
        double rbj = DigitalEqualizationResponse.MagnitudeDbAt(band, 4_500, SampleRateHz);
        double symmetric = ZoelzerMagnitudeDb(band, 4_500, SampleRateHz);

        Assert.True(Math.Abs(rbj) < 0.25, $"RBJ skirt was {rbj:F2} dB");
        Assert.True(Math.Abs(symmetric) > 0.8, $"Symmetric skirt was {symmetric:F2} dB");
    }

    // REW publishes the bandwidth between the half-gain points for each convention as
    // BW = m * f0 / Q. Reproducing its worked figures for f0 = 1 kHz, Q 4, ±12 dB pins
    // our scale factors against an outside authority rather than against our own algebra,
    // and it is the case that separates Classic from Symmetric: only Classic is
    // asymmetric, coming out wide on a boost and narrow on a cut.
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
        // What the device realizes when Q 4 is dialled into it.
        PeqBand realized = PeqQConventions.ToRbj(
            new PeqBand(1_000, 4.0, gainDb), convention);

        Assert.Equal(expectedBandwidthHz, HalfGainBandwidthHz(realized), 1);
    }

    // Generalises the rows above off the hardcoded figures: for every convention, the
    // band a device realizes from a dialled-in Q must have the half-gain bandwidth that
    // convention DEFINES. The multiplier here is written the way REW writes it — the
    // square root of the LINEAR gain — rather than as the exponent PeqQConventions uses,
    // so the test checks the reading of the definition and not just its arithmetic. This
    // is what covers Classic at the realized-response level, where Symmetric has an
    // independent section to compare against and Classic has none.
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
                        // Loosen with the width itself: a 4 kHz-wide band cannot be
                        // pinned to the same absolute hertz as a 60 Hz one.
                        Math.Max(0.05, expected * 1e-3));
                }
            }
        }
    }

    // REW states each convention's half-gain bandwidth as a multiple of Fc/Q:
    //   RBJ Q        centre frequency/Q
    //   Classic Q    sqrt(gain)*centre frequency/Q          — the signed linear gain
    //   Symmetric Q  sqrt(absgain)*centre frequency/Q       — "always >= 1"
    private static double RewBandwidthMultiplier(PeqQConvention convention, double gainDb) =>
        convention switch
        {
            PeqQConvention.Symmetric => Math.Sqrt(Math.Pow(10.0, Math.Abs(gainDb) / 20.0)),
            PeqQConvention.Classic => Math.Sqrt(Math.Pow(10.0, gainDb / 20.0)),
            _ => 1.0
        };

    // The two proportional conventions agree on boosts and disagree on cuts, so a build
    // that treated Classic as an alias of Symmetric would still pass the boost rows above.
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

    // A half-filled PEQ slot has no gain, so it has no bandwidth to restate; scaling its
    // placeholder Q would make an empty slot look edited on the sheet.
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

    // Boost and cut of the same depth scale identically — the factor is 10^(|G|/40), and
    // an implementation that dropped the absolute value would turn Symmetric into
    // Classic, narrowing every cut instead of widening it.
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

    // The three descriptions a chooser shows are what a user decides on, so a switch
    // that quietly fell through to the RBJ default would mislabel a whole sheet. Each
    // convention must describe ITSELF, in every one of the three texts.
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

    // The width factors quoted in the blurb are the ones a user checks their own DSP
    // against, so they are pinned to the conversion itself rather than trusted: a
    // change to Scale that left the prose behind would print a lie.
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

    // Distance between the points at half the band's gain, the quantity every one of
    // these conventions defines Q through. Bisection on each skirt rather than a scan,
    // so the resolution does not limit what the bandwidth assertions can pin down.
    private static double HalfGainBandwidthHz(PeqBand band)
    {
        // Well above audio, to keep the bilinear warp from moving the skirts: the
        // conventions are analog definitions and REW's figures carry no warp.
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

    // Bisects between a frequency known to be inside the half-gain region and one known
    // to be outside it, in log space so both skirts converge alike.
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

    // Frequencies spanning three octaves either side of the band, plus the centre and
    // points just inside the skirts where the two conventions differ most.
    private static IEnumerable<double> Probes(double centreHz)
    {
        foreach (double ratio in new[]
                 { 0.125, 0.25, 0.5, 0.71, 0.9, 1.0, 1.1, 1.41, 2.0, 4.0 })
        {
            double probe = centreHz * ratio;
            // Above Nyquist there is no response to compare.
            if (probe > 0 && probe < SampleRateHz / 2.2)
            {
                yield return probe;
            }
        }
    }

    /// <summary>
    /// Zölzer/DAFX peak filter, written out independently of anything in the library so
    /// the comparison is a real cross-check rather than the same code twice. Boost and
    /// cut are separate branches: the gain V0 = 10^(G/20) rides on the numerator for a
    /// boost and on the denominator for a cut.
    /// </summary>
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
