namespace Resonalyze.Dsp.Tests;

/// <summary>
/// SPL conversion turns the loopback-referenced curves into an absolute dB SPL
/// axis: the primary shifts by K, and every fundamental-relative trace is lifted
/// by the primary's own SPL at its frequency so a ratio becomes a level.
/// </summary>
public sealed class SplConversionTests
{
    private static AnalysisCurve Curve(AnalysisCurveKind kind, params (double X, double Y)[] points) =>
        new(kind.ToString(), points.Select(p => new SignalPoint(p.X, p.Y)).ToArray(), kind);

    [Fact]
    public void Primary_ShiftsByOffsetAndKeepsIdentity()
    {
        AnalysisCurve primary = Curve(
            AnalysisCurveKind.Primary, (100, -30), (1_000, -10), (10_000, -20));

        AnalysisCurve result = SplConversion
            .ToSoundPressureLevel([primary], offsetDb: 100.0)[0];

        Assert.Equal(AnalysisCurveKind.Primary, result.Kind);
        Assert.Equal(primary.Name, result.Name);
        Assert.Equal([70.0, 90.0, 80.0], result.Points.Select(p => p.Y));
        Assert.Equal([100.0, 1_000.0, 10_000.0], result.Points.Select(p => p.X));
    }

    [Fact]
    public void Harmonic_LiftedByFlatPrimary()
    {
        // primary flat at -10 dBr, K = 100 -> primary SPL = 90 everywhere.
        // A -50 dBc harmonic becomes 90 + (-50) = 40 dB SPL.
        AnalysisCurve primary = Curve(AnalysisCurveKind.Primary, (100, -10), (10_000, -10));
        AnalysisCurve hd2 = Curve(AnalysisCurveKind.SecondHarmonic, (100, -50), (10_000, -50));

        IReadOnlyList<AnalysisCurve> result =
            SplConversion.ToSoundPressureLevel([primary, hd2], offsetDb: 100.0);

        Assert.All(result[1].Points, point => Assert.Equal(40.0, point.Y, 9));
    }

    [Fact]
    public void Harmonic_LiftedBySlopedPrimaryWithInterpolation()
    {
        // primary: 0 dBr at 100 Hz, -20 dBr at 1000 Hz. At 550 Hz (halfway in
        // frequency) the interpolated primary is -10 dBr. With K = 90 that point's
        // primary SPL is 80; a -40 dBc harmonic there reads 80 + (-40) = 40.
        AnalysisCurve primary = Curve(AnalysisCurveKind.Primary, (100, 0), (1_000, -20));
        AnalysisCurve hd3 = Curve(AnalysisCurveKind.ThirdHarmonic, (550, -40));

        AnalysisCurve result =
            SplConversion.ToSoundPressureLevel([primary, hd3], offsetDb: 90.0)[1];

        Assert.Equal(40.0, result.Points[0].Y, 9);
    }

    [Fact]
    public void EveryFundamentalRelativeTraceIsLifted()
    {
        AnalysisCurve primary = Curve(AnalysisCurveKind.Primary, (100, 0), (10_000, 0));
        AnalysisCurveKind[] relative =
        [
            AnalysisCurveKind.SecondHarmonic,
            AnalysisCurveKind.ThirdHarmonic,
            AnalysisCurveKind.FourthHarmonic,
            AnalysisCurveKind.ThdPlusNoise,
            AnalysisCurveKind.NoiseFloor
        ];
        var curves = new List<AnalysisCurve> { primary };
        curves.AddRange(relative.Select(kind => Curve(kind, (1_000, -60))));

        IReadOnlyList<AnalysisCurve> result =
            SplConversion.ToSoundPressureLevel(curves, offsetDb: 100.0);

        // primary SPL at 1 kHz is 0 + 100; each -60 dBc trace becomes 40 dB SPL.
        foreach (AnalysisCurve curve in result.Where(c => c.Kind != AnalysisCurveKind.Primary))
        {
            Assert.Equal(40.0, curve.Points[0].Y, 9);
        }
    }

    [Fact]
    public void MissingPrimary_LeavesFundamentalRelativeUnchanged()
    {
        // With nothing to anchor to, a ratio cannot become a level; leave it as-is
        // (the caller only enters SPL mode when a primary and a valid K exist).
        AnalysisCurve hd2 = Curve(AnalysisCurveKind.SecondHarmonic, (1_000, -60));

        AnalysisCurve result =
            SplConversion.ToSoundPressureLevel([hd2], offsetDb: 100.0)[0];

        Assert.Equal(-60.0, result.Points[0].Y, 9);
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        AnalysisCurve primary = Curve(AnalysisCurveKind.Primary, (100, 0));

        Assert.Throws<ArgumentNullException>(
            () => SplConversion.ToSoundPressureLevel(null!, 100.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SplConversion.ToSoundPressureLevel([primary], double.NaN));
    }
}
