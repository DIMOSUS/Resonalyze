using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class GroupDelayCurvesTests
{
    private const int SampleRate = 48_000;
    private const int TransformLength = 4096;

    [Fact]
    public void PureDelay_MinimumIsFlatZeroAndExcessCarriesTheDelay()
    {
        // A delayed delta has a flat magnitude, so the whole measured delay is
        // all-pass: the minimum-phase curve must sit at 0 and the excess must
        // read the full arrival time.
        const int delaySamples = 24;
        var response = new Complex[TransformLength];
        response[delaySamples] = Complex.One;
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: 0);

        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            measurement,
            gateOffsetMs: 0,
            leftMs: 0,
            plateauMs: TransformLength * 1000.0 / SampleRate,
            rightMs: 0,
            smoothingInverseOctaves: 96,
            includeMinimumPhase: true);

        Assert.NotNull(curves.Minimum);
        Assert.NotNull(curves.Excess);
        AnalysisCurve minimum = curves.Minimum!;
        AnalysisCurve excess = curves.Excess!;
        Assert.Equal(
            curves.Measured.Points.Select(point => point.X),
            minimum.Points.Select(point => point.X));
        Assert.Equal(
            curves.Measured.Points.Select(point => point.X),
            excess.Points.Select(point => point.X));

        double delayMilliseconds = delaySamples * 1000.0 / SampleRate;
        List<int> band = AnalysisBandIndices(curves.Measured, 1_000, 18_000);
        Assert.NotEmpty(band);
        Assert.All(band, i => Assert.InRange(
            curves.Measured.Points[i].Y,
            delayMilliseconds - 1e-9,
            delayMilliseconds + 1e-9));
        Assert.All(band, i => Assert.InRange(
            minimum.Points[i].Y, -1e-6, 1e-6));
        Assert.All(band, i => Assert.InRange(
            excess.Points[i].Y,
            delayMilliseconds - 1e-6,
            delayMilliseconds + 1e-6));
    }

    [Fact]
    public void MinimumPhaseSystem_HasNearZeroExcess()
    {
        // h[n] = 0.9ⁿ is a one-pole system, minimum-phase by construction (the
        // pole sits inside the unit circle; the truncation zeros sit at radius
        // 0.9 too). Its measured group delay is fully explained by the
        // magnitude, so the excess must read ≈ 0 across the band — this is the
        // curve pair agreeing about a system an EQ could actually correct.
        var response = new Complex[TransformLength];
        for (int i = 0; i < 1_000; i++)
        {
            response[i] = new Complex(Math.Pow(0.9, i), 0);
        }
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: 0);

        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            measurement,
            gateOffsetMs: 0,
            leftMs: 0,
            plateauMs: TransformLength * 1000.0 / SampleRate,
            rightMs: 0,
            smoothingInverseOctaves: 96,
            includeMinimumPhase: true);

        List<int> band = AnalysisBandIndices(curves.Measured, 100, 18_000);
        Assert.NotEmpty(band);
        // The system's own delay reaches ~0.19 ms at the low end; the excess
        // must be an order of magnitude under that everywhere.
        Assert.All(band, i => Assert.InRange(
            curves.Excess!.Points[i].Y, -0.01, 0.01));
    }

    [Fact]
    public void AllPass_DispersionLandsInExcessNotMinimum()
    {
        // A second-order all-pass at 1 kHz (Q = 2): |H| = 1 everywhere, so the
        // minimum-phase curve must stay flat ≈ 0 while the group-delay pile-up
        // at the corner (τ ≈ 4Q/ω₀ ≈ 1.27 ms) lands entirely in the excess.
        IReadOnlyList<BiquadCoefficients> sections = AllPassFilter.BuildSections(
            new AllPassSpec(AllPassType.SecondOrder, 1_000.0, Q: 2.0),
            SampleRate);
        Assert.NotEmpty(sections);

        double[] impulse = new double[TransformLength];
        impulse[0] = 1.0;
        foreach (BiquadCoefficients biquad in sections)
        {
            impulse = FilterAdditiveFeedback(biquad, impulse);
        }
        var response = new Complex[TransformLength];
        for (int i = 0; i < TransformLength; i++)
        {
            response[i] = new Complex(impulse[i], 0);
        }
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: 0);

        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            measurement,
            gateOffsetMs: 0,
            leftMs: 0,
            plateauMs: TransformLength * 1000.0 / SampleRate,
            rightMs: 0,
            smoothingInverseOctaves: 96,
            includeMinimumPhase: true);

        List<int> band = AnalysisBandIndices(curves.Measured, 100, 18_000);
        Assert.NotEmpty(band);
        Assert.All(band, i => Assert.InRange(
            curves.Minimum!.Points[i].Y, -0.05, 0.05));

        double excessAtCorner = InterpolateY(curves.Excess!, 1_000.0);
        double excessFarAbove = InterpolateY(curves.Excess!, 10_000.0);
        Assert.True(
            excessAtCorner - excessFarAbove > 0.8,
            $"the all-pass dispersion never reached the excess curve " +
            $"({excessAtCorner:0.000} ms at 1 kHz vs {excessFarAbove:0.000} ms at 10 kHz)");
    }

    [Fact]
    public void ValidityGate_BlanksTheSameBinsInEveryCurve()
    {
        // A differencer's low end falls below the −60 dB global backstop, so
        // some bins gate to NaN. The three curves must agree bin-exactly: a
        // reader overlaying them must never see an excess value whose measured
        // or minimum operand was blanked.
        var response = new Complex[TransformLength];
        response[0] = Complex.One;
        response[1] = -Complex.One;
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: 0);

        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            measurement,
            gateOffsetMs: 0,
            leftMs: 0,
            plateauMs: TransformLength * 1000.0 / SampleRate,
            rightMs: 0,
            smoothingInverseOctaves: 0,
            includeMinimumPhase: true);

        Assert.Equal(curves.Measured.Points.Count, curves.Minimum!.Points.Count);
        Assert.Equal(curves.Measured.Points.Count, curves.Excess!.Points.Count);
        for (int i = 0; i < curves.Measured.Points.Count; i++)
        {
            bool measuredIsNaN = double.IsNaN(curves.Measured.Points[i].Y);
            Assert.Equal(measuredIsNaN, double.IsNaN(curves.Minimum.Points[i].Y));
            Assert.Equal(measuredIsNaN, double.IsNaN(curves.Excess.Points[i].Y));
        }
        Assert.Contains(curves.Measured.Points, point => double.IsNaN(point.Y));
        Assert.Contains(curves.Measured.Points, point => double.IsFinite(point.Y));
    }

    [Fact]
    public void GetGroupDelay_IsTheMeasuredCurveOfTheSet()
    {
        const int delaySamples = 24;
        var response = new Complex[TransformLength];
        response[delaySamples] = Complex.One;
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: 0);
        double plateauMs = TransformLength * 1000.0 / SampleRate;

        AnalysisCurve wrapper = DataHelper.GetGroupDelay(
            measurement,
            gateOffsetMs: 0,
            leftMs: 0,
            plateauMs: plateauMs,
            rightMs: 0,
            smoothingInverseOctaves: 96);
        GroupDelayCurveSet set = DataHelper.GetGroupDelayCurves(
            measurement,
            gateOffsetMs: 0,
            leftMs: 0,
            plateauMs: plateauMs,
            rightMs: 0,
            smoothingInverseOctaves: 96);

        // The default set carries no minimum-phase work at all — the wrapper
        // must not silently pay for curves nobody asked for.
        Assert.Null(set.Minimum);
        Assert.Null(set.Excess);
        Assert.Equal(wrapper.Points, set.Measured.Points);
    }

    private static List<int> AnalysisBandIndices(
        AnalysisCurve curve,
        double lowHz,
        double highHz)
    {
        var indices = new List<int>();
        for (int i = 0; i < curve.Points.Count; i++)
        {
            if (curve.Points[i].X >= lowHz && curve.Points[i].X <= highHz)
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    // Reads the curve at the frequency nearest to the target (the grids are
    // dense — 32768-point FFT — so nearest is as good as interpolation here).
    private static double InterpolateY(AnalysisCurve curve, double frequencyHz)
    {
        SignalPoint nearest = curve.Points
            .Where(point => double.IsFinite(point.Y))
            .MinBy(point => Math.Abs(point.X - frequencyHz));
        return nearest.Y;
    }

    // y[n] = b0·x[n] + b1·x[n−1] + b2·x[n−2] + a1·y[n−1] + a2·y[n−2] — the
    // additive-feedback convention BiquadCoefficients documents.
    private static double[] FilterAdditiveFeedback(
        BiquadCoefficients biquad,
        double[] input)
    {
        double[] output = new double[input.Length];
        double x1 = 0.0, x2 = 0.0, y1 = 0.0, y2 = 0.0;
        for (int i = 0; i < input.Length; i++)
        {
            double x = input[i];
            double y = biquad.B0 * x + biquad.B1 * x1 + biquad.B2 * x2 +
                biquad.A1 * y1 + biquad.A2 * y2;
            output[i] = y;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
        }

        return output;
    }
}
