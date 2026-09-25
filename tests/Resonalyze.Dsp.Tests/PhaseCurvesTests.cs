using System.Numerics;

namespace Resonalyze.Dsp.Tests;

// Pins the degrees-domain wrappers; the radian core is covered by GatedPhaseDataTests, MinimumPhaseTests, ExcessDelayTests.
public sealed class PhaseCurvesTests
{
    private const int SampleRate = 48_000;

    private static SyntheticMeasurement Delay(int sampleIndex)
    {
        var ir = new Complex[8_192];
        ir[sampleIndex] = Complex.One;
        return new SyntheticMeasurement(ir, SampleRate, sampleIndex);
    }

    private static SyntheticMeasurement MinimumPhaseFilterAt(int sampleIndex)
    {
        // Zeros inside the unit circle (|z| = sqrt(0.2)): minimum phase, excess ~0.
        var ir = new Complex[8_192];
        ir[sampleIndex] = new Complex(1.0, 0.0);
        ir[sampleIndex + 1] = new Complex(-0.7, 0.0);
        ir[sampleIndex + 2] = new Complex(0.2, 0.0);
        return new SyntheticMeasurement(ir, SampleRate, sampleIndex);
    }

    [Fact]
    public void GetPhase_IsTheGatedRadianPhaseConvertedToDegrees()
    {
        SyntheticMeasurement measurement = Delay(960);

        AnalysisCurve degrees = DataHelper.GetPhase(
            measurement, gateOffsetMs: 20.0, leftMs: 1.0, plateauMs: 5.0, rightMs: 10.0,
            detrendMilliseconds: 0.0, smoothingInverseOctaves: 0.0, unwrap: true);

        List<SignalPoint> radians = DataHelper.GetGatedPhaseData(
            measurement, 20.0, 1.0, 5.0, 10.0, referenceSamples: 0.0, unwrap: true);

        Assert.Equal(radians.Count, degrees.Points.Count);
        for (int i = 0; i < radians.Count; i++)
        {
            Assert.Equal(radians[i].X, degrees.Points[i].X, precision: 9);
            Assert.Equal(radians[i].Y / Math.PI * 180.0, degrees.Points[i].Y, precision: 9);
        }
    }

    [Fact]
    public void GetMinimumPhase_OfAFlatMagnitudeResponseIsNearZeroDegrees()
    {
        AnalysisCurve curve = DataHelper.GetMinimumPhase(
            Delay(960), gateOffsetMs: 20.0, leftMs: 1.0, plateauMs: 5.0, rightMs: 10.0,
            smoothingInverseOctaves: 0.0);

        Assert.Equal(AnalysisCurveKind.MinimumPhase, curve.Kind);
        Assert.NotEmpty(curve.Points);
        double previousX = double.NegativeInfinity;
        foreach (SignalPoint point in curve.Points)
        {
            Assert.True(point.X > previousX, "Frequency axis must be strictly ascending.");
            previousX = point.X;
        }
        foreach (SignalPoint point in curve.Points.Where(p => p.X is >= 200 and <= 5_000))
        {
            Assert.True(Math.Abs(point.Y) < 2.0, $"Minimum phase {point.Y:0.###} deg at {point.X:0.#} Hz.");
        }
    }

    [Fact]
    public void GetExcessPhase_OfAMinimumPhaseSystemIsNearZero()
    {
        // A sign flip or minimumPhase[j+1] vs [j] off-by-one would leave a residual.
        AnalysisCurve excess = DataHelper.GetExcessPhase(
            MinimumPhaseFilterAt(960), gateOffsetMs: 20.0, leftMs: 1.0, plateauMs: 5.0, rightMs: 10.0,
            detrendMilliseconds: 20.0, smoothingInverseOctaves: 0.0);

        Assert.Equal(AnalysisCurveKind.ExcessPhase, excess.Kind);
        foreach (SignalPoint point in excess.Points.Where(p => p.X is >= 200 and <= 5_000))
        {
            Assert.True(Math.Abs(point.Y) < 5.0, $"Excess phase {point.Y:0.###} deg at {point.X:0.#} Hz.");
        }
    }

    [Fact]
    public void GetExcessPhase_OfAPureDelayTracksTheMeasuredPhase()
    {
        // Pins the subtraction sign.
        var measurement = Delay(960);
        AnalysisCurve excess = DataHelper.GetExcessPhase(
            measurement, 20.0, 1.0, 5.0, 10.0,
            detrendMilliseconds: 0.0, smoothingInverseOctaves: 0.0);

        List<SignalPoint> measured = DataHelper.GetGatedPhaseData(
            measurement, 20.0, 1.0, 5.0, 10.0, referenceSamples: 0.0, unwrap: true);

        Assert.Equal(measured.Count, excess.Points.Count);
        foreach ((SignalPoint m, SignalPoint e) in measured.Zip(excess.Points))
        {
            if (m.X is < 200 or > 5_000)
            {
                continue;
            }
            Assert.Equal(m.Y / Math.PI * 180.0, e.Y, tolerance: 5.0);
        }
    }

    [Theory]
    [InlineData(192_000)]
    [InlineData(384_000)]
    public void PhaseCurves_AtAHighSampleRate_KeepTheirFrequencyAxis(int sampleRate)
    {
        var ir = new Complex[65_536];
        int peak = sampleRate / 50;
        ir[peak] = Complex.One;
        var measurement = new SyntheticMeasurement(ir, sampleRate, peak);
        var settings = new PhaseAnalysisSettings(
            PhaseWindowMode.Fixed, PhaseAnalysisSettings.DefaultFdwCycles, PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0, GateOffsetMs: 20.0, LeftMs: 1.0, PlateauMs: 40.0,
            RightMs: 10.0, Unwrap: false, SmoothingInverseOctaves: 0.0);

        AnalysisCurve[] curves =
        [
            DataHelper.GetPhase(measurement, settings),
            DataHelper.GetPhase(measurement, settings with { Unwrap = true }),
            DataHelper.GetMinimumPhase(measurement, settings),
            DataHelper.GetMinimumPhase(
                measurement, 20.0, 1.0, 40.0, 10.0, smoothingInverseOctaves: 0.0)
        ];

        foreach (AnalysisCurve curve in curves)
        {
            Assert.NotEmpty(curve.Points);
            double previousX = 0.0;
            foreach (SignalPoint point in curve.Points)
            {
                Assert.True(point.X > previousX && point.X < sampleRate / 2.0,
                    $"{curve.Name}: {point.X:0.#} Hz after {previousX:0.#} Hz");
                previousX = point.X;
            }
        }
    }

    [Fact]
    public void GetPhase_Wrapped_SmoothsAcrossTheWrapWithoutARamp()
    {
        // 1 ms left after the detrend: a linear phase wrapping every 1 kHz, which symmetric smoothing must leave as it is.
        var settings = new PhaseAnalysisSettings(
            PhaseWindowMode.Fixed, PhaseAnalysisSettings.DefaultFdwCycles, PhaseDetrendMode.Manual,
            ManualDetrendMilliseconds: 20.0, GateOffsetMs: 21.0, LeftMs: 1.0, PlateauMs: 5.0,
            RightMs: 10.0, Unwrap: false, SmoothingInverseOctaves: 0.0);
        SyntheticMeasurement measurement = Delay(1_008);

        AnalysisCurve raw = DataHelper.GetPhase(measurement, settings);
        AnalysisCurve smoothed = DataHelper.GetPhase(
            measurement, settings with { SmoothingInverseOctaves = 12 });

        Assert.Equal(raw.Points.Count, smoothed.Points.Count);
        foreach ((SignalPoint r, SignalPoint s) in raw.Points.Zip(smoothed.Points))
        {
            if (r.X is < 1_000 or > 10_000)
            {
                continue;
            }
            double difference = Math.IEEERemainder(s.Y - r.Y, 360.0);
            Assert.True(Math.Abs(difference) < 2.0,
                $"{r.X:0} Hz: {r.Y:0.0} deg smoothed to {s.Y:0.0} deg");
            Assert.InRange(s.Y, -180.0, 180.0);
        }
    }

    [Theory]
    [InlineData(1_440, 30.0)]
    [InlineData(960, 20.0)]
    public void EstimatePhaseDetrend_ReturnsTheAbsoluteArrivalTime(int sampleIndex, double expectedMs)
    {
        // Absolute to IR sample 0: dropping extractionStart would report a gate-relative time.
        (double slopeMs, double peakMs) = DataHelper.EstimatePhaseDetrend(
            Delay(sampleIndex),
            gateOffsetMs: expectedMs, leftMs: 1.0, plateauMs: 8.0, rightMs: 3.0);

        Assert.Equal(expectedMs, slopeMs, tolerance: 0.05);
        Assert.Equal(expectedMs, peakMs, tolerance: 0.05);
    }
}
