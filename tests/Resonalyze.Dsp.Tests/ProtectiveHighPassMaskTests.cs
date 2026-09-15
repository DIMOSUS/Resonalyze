using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

/// <summary>Where a protective-high-pass-corrected response stops carrying a measurement: magnitude curves carry NaN,
/// an impulse response is zeroed and a windowed spectrum refills those bins with window leakage.</summary>
public sealed class ProtectiveHighPassMaskTests
{
    private const double SampleRate = 96_000;
    private const double MaximumBoostDb = 40.0;

    public static TheoryData<CrossoverFilterFamily, double, int> Edges => new()
    {
        { CrossoverFilterFamily.Butterworth, 1_000, 48 },
        { CrossoverFilterFamily.Butterworth, 2_000, 24 },
        { CrossoverFilterFamily.LinkwitzRiley, 2_000, 24 },
        { CrossoverFilterFamily.LinkwitzRiley, 800, 36 }
    };

    [Theory]
    [MemberData(nameof(Edges))]
    public void TheLimitIsExactlyWhereTheMagnitudeCorrectionGivesUp(
        CrossoverFilterFamily family,
        double cornerHz,
        int slope)
    {
        var edge = new CrossoverEdge(family, cornerHz, slope);
        double limit = ProtectiveHighPassCompensation.LowestRecoverableFrequencyHz(
            edge, SampleRate, MaximumBoostDb);

        // Both paths are one rule; a mismatch hides a measured band or draws an unmeasured one.
        double[] correction = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
            edge,
            SampleRate,
            MaximumBoostDb,
            [limit * 0.999, limit * 1.001]);
        Assert.False(double.IsFinite(correction[0]));
        Assert.True(double.IsFinite(correction[1]));
    }

    [Fact]
    public void TheLimitSitsWhereTheSlopeSaysItShould()
    {
        // 1000 / 2^(40/48) = 561 Hz, pinned against the arithmetic; within a percent for bilinear warp.
        double limit = ProtectiveHighPassCompensation.LowestRecoverableFrequencyHz(
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 48),
            SampleRate,
            MaximumBoostDb);

        double asymptote = 1_000.0 / Math.Pow(2.0, MaximumBoostDb / 48.0);
        Assert.Equal(asymptote, limit, asymptote * 0.01);
    }

    [Fact]
    public void ADeeperCapReachesFurtherDown()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 2_000, 24);

        double shallow = ProtectiveHighPassCompensation.LowestRecoverableFrequencyHz(
            edge, SampleRate, maximumBoostDb: 20.0);
        double deep = ProtectiveHighPassCompensation.LowestRecoverableFrequencyHz(
            edge, SampleRate, MaximumBoostDb);

        Assert.True(deep < shallow, $"{deep:0.0} Hz should be below {shallow:0.0} Hz");
    }

    [Fact]
    public void ARefusedFamilyIsRefusedHereToo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProtectiveHighPassCompensation.LowestRecoverableFrequencyHz(
                new CrossoverEdge(CrossoverFilterFamily.Bessel, 2_000, 24),
                SampleRate,
                MaximumBoostDb));
    }

    [Fact]
    public void AGatedCurveDrawsTheWindowsLeakageWhereTheSignalEnded()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 48);
        IImpulseMeasurement corrected = CompensatedMeasurement(edge, limit: 0.0);

        AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
            corrected, new FrequencyResponseOptions(), calibration: null);

        // Unmasked, a real tweeter's curve reads a plausible rolloff 270 dB above the truth.
        SignalPoint drawn = curve.Points.MinBy(point => Math.Abs(point.X - 300.0));
        Assert.True(
            double.IsFinite(drawn.Y) && drawn.Y > -100.0,
            $"the window's leakage reads {drawn.Y:0.0} dB where nothing was measured");
    }

    [Fact]
    public void AMaskedCurveBreaksExactlyAtTheLimitAndNowhereElse()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 48);
        double limit = ProtectiveHighPassCompensation.LowestRecoverableFrequencyHz(
            edge, SampleRate, MaximumBoostDb);

        AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
            CompensatedMeasurement(edge, limit),
            new FrequencyResponseOptions(),
            calibration: null);

        Assert.NotEmpty(curve.Points);
        Assert.All(
            curve.Points,
            point => Assert.Equal(point.X >= limit, double.IsFinite(point.Y)));
    }

    [Theory]
    [InlineData(6.0)]
    [InlineData(48.0)]
    public void TheBreakDoesNotMoveWithTheSmoothingWidth(double inverseOctaves)
    {
        // The mask lands on the output grid after smoothing, or the break slides with the smoothing width.
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 48);
        double limit = ProtectiveHighPassCompensation.LowestRecoverableFrequencyHz(
            edge, SampleRate, MaximumBoostDb);

        AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
            CompensatedMeasurement(edge, limit),
            new FrequencyResponseOptions { SmoothingInverseOctaves = inverseOctaves },
            calibration: null);

        Assert.All(
            curve.Points,
            point => Assert.Equal(point.X >= limit, double.IsFinite(point.Y)));
    }

    [Fact]
    public void ACurveAlsoBreaksAboveWhereTheResponseStops()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 48);
        IImpulseMeasurement measurement = CompensatedMeasurement(edge, limit: 0.0);
        var bounded = new SyntheticMeasurement(
            measurement.ImpulseResponse!, measurement.SampleRate, measurement.PeakIndex)
        {
            HighestMeasuredFrequencyHz = 5_000.0
        };

        AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
            bounded, new FrequencyResponseOptions(), calibration: null);

        Assert.All(
            curve.Points,
            point => Assert.Equal(point.X <= 5_000.0, double.IsFinite(point.Y)));
    }

    [Fact]
    public void BothEndsBreakTogetherWhenBothAreSet()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 48);
        IImpulseMeasurement measurement = CompensatedMeasurement(edge, limit: 0.0);
        var bounded = new SyntheticMeasurement(
            measurement.ImpulseResponse!, measurement.SampleRate, measurement.PeakIndex)
        {
            LowestMeasuredFrequencyHz = 800.0,
            HighestMeasuredFrequencyHz = 5_000.0
        };

        AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
            bounded, new FrequencyResponseOptions(), calibration: null);

        Assert.All(
            curve.Points,
            point => Assert.Equal(
                point.X >= 800.0 && point.X <= 5_000.0, double.IsFinite(point.Y)));
    }

    [Fact]
    public void AResponseThatMeasuredEverythingIsLeftAlone()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 48);

        AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
            CompensatedMeasurement(edge, limit: 0.0),
            new FrequencyResponseOptions(),
            calibration: null);

        Assert.All(curve.Points, point => Assert.True(double.IsFinite(point.Y)));
    }

    private static IImpulseMeasurement CompensatedMeasurement(
        CrossoverEdge edge,
        double limit)
    {
        const int Length = 32_768;
        var spectrum = new Complex[Length];
        CrossoverSpec spec = new(CrossoverKind.HighPass, HighPassEdge: edge);
        for (int bin = 0; bin < spectrum.Length; bin++)
        {
            int signedBin = bin <= spectrum.Length / 2 ? bin : bin - spectrum.Length;
            spectrum[bin] = CrossoverFilter.Response(
                spec, signedBin * SampleRate / spectrum.Length, SampleRate);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        Complex[] corrected = ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
            spectrum, edge, SampleRate, MaximumBoostDb).ImpulseResponse;

        int peak = 0;
        for (int i = 1; i < corrected.Length; i++)
        {
            if (corrected[i].Magnitude > corrected[peak].Magnitude)
            {
                peak = i;
            }
        }

        return new SyntheticMeasurement(corrected, (int)SampleRate, peak)
        {
            LowestMeasuredFrequencyHz = limit
        };
    }
}
