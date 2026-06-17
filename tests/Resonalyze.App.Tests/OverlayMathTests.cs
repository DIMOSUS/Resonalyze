namespace Resonalyze.App.Tests;

public sealed class OverlayMathTests
{
    public static TheoryData<OverlayOperation, double[]> OperationCases => new()
    {
        { OverlayOperation.AMinusB, [2, 2, 4] },
        { OverlayOperation.BMinusA, [-2, -2, -4] },
        { OverlayOperation.Sum, [18, 26, 36] },
        { OverlayOperation.Average, [9, 13, 18] },
        { OverlayOperation.AbsoluteDifference, [2, 2, 4] }
    };

    [Theory]
    [MemberData(nameof(OperationCases))]
    public void CalculateOperation_InterpolatesAndAppliesSelectedMode(
        OverlayOperation operation,
        double[] expectedValues)
    {
        OverlayPoint[] a =
        [
            new OverlayPoint(1, 10),
            new OverlayPoint(2, 14),
            new OverlayPoint(3, 20)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1, 8),
            new OverlayPoint(3, 16)
        ];

        OverlayPoint[] result =
            OverlayMath.CalculateOperation(a, b, operation);

        Assert.Equal(expectedValues, result.Select(point => point.Y));
    }

    [Fact]
    public void CalculateOperation_BlendCrossfadesAroundCenterFrequency()
    {
        OverlayPoint[] a =
        [
            new OverlayPoint(1, 10),
            new OverlayPoint(2, 14),
            new OverlayPoint(3, 20)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1, 8),
            new OverlayPoint(3, 16)
        ];

        OverlayPoint[] result = OverlayMath.CalculateOperation(
            a,
            b,
            OverlayOperation.Blend,
            blendFrequencyHz: 2,
            blendWidthOctaves: 1);

        Assert.Equal(3, result.Length);
        Assert.Equal(10, result[0].Y, precision: 12);
        Assert.Equal(13, result[1].Y, precision: 12);
        Assert.Equal(16, result[2].Y, precision: 12);
    }

    [Fact]
    public void CalculateOperation_BlendStaysBetweenSourceCurves()
    {
        OverlayPoint[] a =
        [
            new OverlayPoint(1, 10),
            new OverlayPoint(2, 14),
            new OverlayPoint(3, 20)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1, 8),
            new OverlayPoint(3, 16)
        ];

        OverlayPoint[] result = OverlayMath.CalculateOperation(
            a,
            b,
            OverlayOperation.Blend,
            blendFrequencyHz: 2,
            blendWidthOctaves: 1);

        for (int i = 0; i < result.Length; i++)
        {
            OverlayPoint point = result[i];
            double min = Math.Min(a[i].Y, b[0].Y + (b[1].Y - b[0].Y) * ((point.X - b[0].X) / (b[1].X - b[0].X)));
            double max = Math.Max(a[i].Y, b[0].Y + (b[1].Y - b[0].Y) * ((point.X - b[0].X) / (b[1].X - b[0].X)));
            Assert.InRange(point.Y, min, max);
        }
    }

    [Fact]
    public void CalculateOperation_UsesAmplitudeSpaceWhenRequested()
    {
        double halfAmplitudeDb = Resonalyze.Dsp.DataHelper.AmplitudeToDecibels(0.5);
        OverlayPoint[] a =
        [
            new OverlayPoint(1, halfAmplitudeDb),
            new OverlayPoint(2, halfAmplitudeDb)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1, halfAmplitudeDb),
            new OverlayPoint(2, halfAmplitudeDb)
        ];

        OverlayPoint[] result = OverlayMath.CalculateOperation(
            a,
            b,
            OverlayOperation.Sum,
            useAmplitudeSpace: true);

        Assert.Equal(2, result.Length);
        Assert.All(result, point => Assert.Equal(0, point.Y, precision: 12));
    }

    [Fact]
    public void CalculateOperation_UsesOnlyOverlappingRange()
    {
        OverlayPoint[] a =
        [
            new OverlayPoint(0, 10),
            new OverlayPoint(1, 10),
            new OverlayPoint(2, 10),
            new OverlayPoint(3, 10)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1, 7),
            new OverlayPoint(2, 8)
        ];

        OverlayPoint[] result = OverlayMath.CalculateOperation(
            a,
            b,
            OverlayOperation.AMinusB);

        Assert.Equal(
            [
                new OverlayPoint(1, 3),
                new OverlayPoint(2, 2)
            ],
            result);
    }

    [Fact]
    public void SmoothByOctaves_PreservesConstantCurve()
    {
        OverlayPoint[] points = CreateLogarithmicPoints(
            index => 7.5);

        OverlayPoint[] result = OverlayMath.SmoothByOctaves(points, 3);

        Assert.All(
            result,
            point => Assert.Equal(7.5, point.Y, precision: 12));
    }

    [Fact]
    public void SmoothByOctaves_ReducesNarrowPeak()
    {
        OverlayPoint[] points = CreateLogarithmicPoints(
            index => index == 24 ? 12 : 0);

        OverlayPoint[] result = OverlayMath.SmoothByOctaves(points, 3);

        Assert.InRange(result[24].Y, 0, 12);
        Assert.True(result[23].Y > 0);
        Assert.True(result[25].Y > 0);
    }

    [Fact]
    public void SmoothByOctaves_IsFrequencyScaleInvariant()
    {
        OverlayPoint[] lowBand = CreateLogarithmicPoints(
            index => index == 24 ? 12 : 0,
            startFrequency: 20);
        OverlayPoint[] highBand = CreateLogarithmicPoints(
            index => index == 24 ? 12 : 0,
            startFrequency: 2_000);

        OverlayPoint[] lowResult =
            OverlayMath.SmoothByOctaves(lowBand, 6);
        OverlayPoint[] highResult =
            OverlayMath.SmoothByOctaves(highBand, 6);

        Assert.Equal(
            lowResult.Select(point => point.Y),
            highResult.Select(point => point.Y));
    }

    private static OverlayPoint[] CreateLogarithmicPoints(
        Func<int, double> getValue,
        double startFrequency = 20)
    {
        return Enumerable.Range(0, 49)
            .Select(index => new OverlayPoint(
                startFrequency * Math.Pow(2, index / 12.0),
                getValue(index)))
            .ToArray();
    }
}
