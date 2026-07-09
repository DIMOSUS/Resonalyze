using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class DataHelperImpulseTests
{
    private const int SampleRate = 48_000;

    private static SyntheticMeasurement WithPeakAt(int peakIndex, int length)
    {
        var ir = new Complex[length];
        // A clear dominant peak plus a smaller lobe so "the peak" is unambiguous.
        ir[peakIndex] = new Complex(1.0, 0.0);
        if (peakIndex + 40 < length)
        {
            ir[peakIndex + 40] = new Complex(0.25, 0.0);
        }

        return new SyntheticMeasurement(ir, SampleRate, peakIndex);
    }

    [Fact]
    public void GetImpulse_PlacesThePeakAtZeroWithTheExpectedLength()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 1_000, length: 8_192);
        var opt = new ImpulseResponseOptions { Length = 4_096, Logarithmic = false };

        AnalysisCurve curve = DataHelper.GetImpulse(measurement, opt);

        Assert.Equal(512 + opt.Length, curve.Points.Count);
        SignalPoint peak = curve.Points.MaxBy(p => Math.Abs(p.Y));
        Assert.Equal(0.0, peak.X, precision: 12);
        Assert.Equal(1.0, peak.Y, precision: 12);
    }

    [Fact]
    public void GetImpulse_LogarithmicNormalizesThePeakToZeroDecibels()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 1_000, length: 8_192);
        var opt = new ImpulseResponseOptions { Length = 4_096, Logarithmic = true };

        AnalysisCurve curve = DataHelper.GetImpulse(measurement, opt);

        SignalPoint loudest = curve.Points.MaxBy(p => p.Y);
        Assert.Equal(0.0, loudest.Y, precision: 9); // peak is the 0 dB reference
        Assert.Equal(0.0, loudest.X, precision: 12);
    }

    [Fact]
    public void GetImpulseFromStart_UsesAbsoluteSamplesAndClampsToTheAvailableLength()
    {
        // peakIndex + Length (1000 + 4096 = 5096) exceeds the 2000-sample response, so
        // the curve must clamp to the available length and keep the X axis absolute.
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 1_000, length: 2_000);
        var opt = new ImpulseResponseOptions { Length = 4_096, Logarithmic = false };

        AnalysisCurve curve = DataHelper.GetImpulseFromStart(measurement, opt);

        Assert.Equal(2_000, curve.Points.Count);
        Assert.Equal(0.0, curve.Points[0].X, precision: 12); // absolute samples start at 0
        SignalPoint peak = curve.Points.MaxBy(p => Math.Abs(p.Y));
        Assert.Equal(1_000.0, peak.X, precision: 12); // peak stays at its absolute index
    }

    [Fact]
    public void GetImpulseFromStart_EmptyResponseYieldsASingleSample()
    {
        var measurement = new SyntheticMeasurement(Array.Empty<Complex>(), SampleRate, 0);
        var opt = new ImpulseResponseOptions { Length = 4_096 };

        AnalysisCurve curve = DataHelper.GetImpulseFromStart(measurement, opt);

        Assert.Single(curve.Points);
    }

    [Theory]
    [InlineData(0.5, 4.0, 1.5, 1000.0 / 6.0)] // gate 6 ms -> ~166.67 Hz
    [InlineData(1.0, 1.0, 0.0, 500.0)]         // gate 2 ms -> 500 Hz
    public void GateMinReliableFrequencyHz_IsOneOverTheGateDuration(
        double leftMs, double plateauMs, double rightMs, double expected)
    {
        Assert.Equal(
            expected,
            FrequencyResponseOptions.GateMinReliableFrequencyHz(leftMs, plateauMs, rightMs),
            precision: 9);
    }

    [Fact]
    public void GateMinReliableFrequencyHz_ZeroDurationGateReturnsZero()
    {
        Assert.Equal(0.0, FrequencyResponseOptions.GateMinReliableFrequencyHz(0.0, 0.0, 0.0));
    }
}
