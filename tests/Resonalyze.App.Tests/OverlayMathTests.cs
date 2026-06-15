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
