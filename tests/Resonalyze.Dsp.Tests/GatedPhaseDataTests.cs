using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class GatedPhaseDataTests
{
    private const int SampleRate = 48_000;

    private static Complex[] StructuredImpulse()
    {
        var ir = new Complex[8_192];
        ir[480] = Complex.One;
        ir[600] = new Complex(-0.4, 0);
        ir[900] = new Complex(0.2, 0);
        return ir;
    }

    private static int MillisecondsToSamples(double milliseconds) =>
        (int)Math.Round(Math.Max(0.0, milliseconds) * SampleRate / 1_000.0);

    [Fact]
    public void GetGatedPhaseData_MatchesTheManualWindowConstruction()
    {
        // Pins GetGatedPhaseData against the former manual gate construction (whole-sample reference + fractional-τ correction).
        Complex[] ir = StructuredImpulse();
        const double gateOffsetMs = 10.0;
        const double leftMs = 2.0;
        const double plateauMs = 8.0;
        const double rightMs = 30.0;
        const double detrendMs = 10.021; // deliberately off the sample grid

        var view = new SyntheticMeasurement(ir, SampleRate, 0);
        List<SignalPoint> actual = DataHelper.GetGatedPhaseData(
            view, gateOffsetMs, leftMs, plateauMs, rightMs,
            referenceSamples: detrendMs / 1_000.0 * SampleRate,
            unwrap: false);

        int length = DataHelper.GatedFftLength;
        int gateOffset = (int)Math.Round(gateOffsetMs / 1_000.0 * SampleRate);
        int left = MillisecondsToSamples(leftMs);
        int plateau = MillisecondsToSamples(plateauMs);
        int right = MillisecondsToSamples(rightMs);
        int gate = Math.Clamp(left + plateau + right, 1, length);
        left = Math.Min(left, gate);
        right = Math.Min(right, gate - left);
        double[] tukey = Windowing.TukeyWindow(
            gate, (double)left / gate * 2.0, (double)right / gate * 2.0);
        double[] window = new double[length];
        Array.Copy(tukey, window, gate);
        int gateStart = gateOffset - left;
        double detrendSamples = detrendMs / 1_000.0 * SampleRate;
        int referenceSample = Math.Clamp(
            (int)Math.Round(detrendSamples), 0, ir.Length - 1);
        double residualSeconds = (detrendSamples - referenceSample) / SampleRate;
        var referenceView = new SyntheticMeasurement(ir, SampleRate, referenceSample);
        List<SignalPoint> expected = DataHelper.GetPhaseData(
            referenceView, gateStart - referenceSample, length, window, unwrap: false);

        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].X, actual[i].X, 9);
            double corrected =
                expected[i].Y + Math.Tau * expected[i].X * residualSeconds;
            corrected = Math.Atan2(Math.Sin(corrected), Math.Cos(corrected));
            // Values near ±π may wrap to opposite sides.
            double delta = Math.IEEERemainder(corrected - actual[i].Y, Math.Tau);
            Assert.True(
                Math.Abs(delta) < 1e-9,
                $"Phase mismatch at {actual[i].X:0.##} Hz: {delta:e}.");
        }
    }

    [Fact]
    public void GetGatedPhaseData_WholeSampleReferenceFlattensAPureDelay()
    {
        var ir = new Complex[8_192];
        ir[960] = Complex.One; // 20 ms
        var view = new SyntheticMeasurement(ir, SampleRate, 0);

        List<SignalPoint> phase = DataHelper.GetGatedPhaseData(
            view,
            gateOffsetMs: 20.0,
            leftMs: 1.0,
            plateauMs: 5.0,
            rightMs: 10.0,
            referenceSamples: 960,
            unwrap: false);

        foreach (SignalPoint point in phase.Where(p => p.X is >= 100 and <= 10_000))
        {
            Assert.True(
                Math.Abs(point.Y) < 1e-6,
                $"Residual phase {point.Y:e} at {point.X:0.##} Hz.");
        }
    }
}
