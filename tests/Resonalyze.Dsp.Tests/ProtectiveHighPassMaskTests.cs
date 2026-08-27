using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// Where a response corrected for a protective high-pass stops carrying a
/// measurement, and what a curve drawn from it does there.
/// </summary>
/// <remarks>
/// The compensation ends two ways. A magnitude curve can carry NaN and say
/// "nothing here", so <see cref="ProtectiveHighPassCompensation.MagnitudeCorrectionDb"/>
/// does. An impulse response is a time series and cannot, so
/// <see cref="ProtectiveHighPassCompensation.RemoveFromImpulseResponse"/> zeroes
/// those bins — and a WINDOWED spectrum of the result then refills them with the
/// analysis window's own leakage, which is smooth, plausible, and entirely the
/// window's. These pin the frequency that breaks such a curve, and that all three
/// paths agree on it.
/// </remarks>
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

        // The two paths have to agree to the last hertz. They are the same rule read
        // two ways, and a curve broken anywhere other than where the correction
        // stops would either hide a measured band or draw an unmeasured one.
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
        // 40 dB down a 48 dB per octave slope is five sixths of an octave below the
        // corner: 1000 / 2^(40/48) = 561 Hz. Pinned against the arithmetic rather
        // than against the implementation, so a change of model has to argue with
        // the filter rather than with a recorded number. Within a percent, because
        // the bilinear-warped digital filter is not quite its analogue asymptote —
        // and it is the digital one the compensation actually inverts.
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

        // The measurement is exactly zero below the limit — the compensation put it
        // there — so the honest reading is minus infinity. Unmasked, the curve reads
        // a finite level instead, and on a real tweeter (1 kHz, 48 dB per octave)
        // a smooth, entirely plausible rolloff 270 dB above the truth. That is the
        // reason the mask exists: the leakage is not small and does not look wrong.
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
        // The mask lands on the OUTPUT grid, after the smoothing. Applied to the
        // oversampled spectrum that feeds it, a smoothing window straddling the
        // boundary would carry passband energy below the limit and NaN above it,
        // and the break would slide with the width the user picked.
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
    public void AResponseThatMeasuredEverythingIsLeftAlone()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 48);

        AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
            CompensatedMeasurement(edge, limit: 0.0),
            new FrequencyResponseOptions(),
            calibration: null);

        // The default, and what every response measured without a protective
        // high-pass gets: no break anywhere.
        Assert.All(curve.Points, point => Assert.True(double.IsFinite(point.Y)));
    }

    // A loudspeaker measured through the filter, with the filter divided back out —
    // the same two steps the application performs, so the zeroed stop band and its
    // leakage are the real ones rather than a stand-in.
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
