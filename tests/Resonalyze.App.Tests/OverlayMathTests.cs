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
        // Interpolation is logarithmic in frequency, so x = 2 — the GEOMETRIC
        // midpoint of the b span [1, 4] — reads the midpoint value 12.
        OverlayPoint[] a =
        [
            new OverlayPoint(1, 10),
            new OverlayPoint(2, 14),
            new OverlayPoint(4, 20)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1, 8),
            new OverlayPoint(4, 16)
        ];

        OverlayPoint[] result =
            OverlayMath.CalculateOperation(a, b, operation);

        Assert.Equal(expectedValues.Length, result.Length);
        for (int i = 0; i < expectedValues.Length; i++)
        {
            Assert.Equal(expectedValues[i], result[i].Y, precision: 9);
        }
    }

    [Fact]
    public void CalculateOperation_WrappedPhaseInterpolatesThroughTheBranchCut()
    {
        // B steps from +170° to −170°: physically the short way passes through
        // ±180°. A linear blend of the raw numbers would read 0° at the
        // geometric midpoint and the wrapped difference could never recover
        // the lost branch; the phasor interpolation must read ±180°.
        OverlayPoint[] a =
        [
            new OverlayPoint(1_000, 0),
            new OverlayPoint(Math.Sqrt(2) * 1_000, 0),
            new OverlayPoint(2_000, 0)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1_000, 170),
            new OverlayPoint(2_000, -170)
        ];

        OverlayPoint[] result = OverlayMath.CalculateOperation(
            a,
            b,
            OverlayOperation.AMinusB,
            wrapPhaseDifference: true);

        Assert.Equal(3, result.Length);
        // A − B at the midpoint: 0 − (±180) wraps to ±180, never to 0.
        Assert.Equal(180.0, Math.Abs(result[1].Y), precision: 6);
    }

    [Fact]
    public void CalculateOperation_AmplitudeSpaceDifferenceKeepsPositiveResults()
    {
        // A is 6 dB above B everywhere, so the amplitude difference is positive
        // and has a real dB value.
        OverlayPoint[] a =
        [
            new OverlayPoint(100, 6),
            new OverlayPoint(1_000, 6)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(100, 0),
            new OverlayPoint(1_000, 0)
        ];

        OverlayPoint[] result = OverlayMath.CalculateOperation(
            a,
            b,
            OverlayOperation.AMinusB,
            useAmplitudeSpace: true);

        Assert.All(result, point => Assert.True(double.IsFinite(point.Y)));
    }

    [Fact]
    public void CalculateOperation_AmplitudeSpaceDifferenceGapsNegativeResults()
    {
        // B is louder than A, so the amplitude difference is negative: there is
        // no dB value for it. The curve must show a gap (NaN), not a -160 dB
        // floor pretending to be data.
        OverlayPoint[] a =
        [
            new OverlayPoint(100, 0),
            new OverlayPoint(1_000, 0)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(100, 6),
            new OverlayPoint(1_000, 6)
        ];

        OverlayPoint[] result = OverlayMath.CalculateOperation(
            a,
            b,
            OverlayOperation.AMinusB,
            useAmplitudeSpace: true);

        Assert.NotEmpty(result);
        Assert.All(result, point => Assert.True(double.IsNaN(point.Y)));
    }

    [Fact]
    public void CalculateOperation_BlendCrossfadesAroundCenterFrequency()
    {
        // x = 2 is the GEOMETRIC midpoint of the b span [1, 4] (interpolation
        // is logarithmic in frequency), so b(2) = 12; it is also the blend
        // centre, so the result is the plain average (14 + 12) / 2 = 13.
        OverlayPoint[] a =
        [
            new OverlayPoint(1, 10),
            new OverlayPoint(2, 14),
            new OverlayPoint(4, 20)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1, 8),
            new OverlayPoint(4, 16)
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
    public void CalculateOperation_PreservesNaNGaps()
    {
        OverlayPoint[] a =
        [
            new OverlayPoint(1, 10),
            new OverlayPoint(2, double.NaN),
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
            OverlayOperation.AMinusB);

        Assert.Equal(3, result.Length);
        Assert.Equal(2, result[0].Y);
        Assert.True(double.IsNaN(result[1].Y));
        Assert.Equal(4, result[2].Y);
    }

    [Theory]
    [InlineData(OverlayOperation.AMinusB, -20)]
    [InlineData(OverlayOperation.BMinusA, 20)]
    [InlineData(OverlayOperation.AbsoluteDifference, 20)]
    public void CalculateOperation_WrapsPhaseDifferenceAcrossTheBranchCut(
        OverlayOperation operation,
        double expected)
    {
        // 170 and -170 degrees are only 20 degrees apart; a raw subtraction would report
        // 340. The wrapped formula must take the shortest angular distance.
        OverlayPoint[] a =
        [
            new OverlayPoint(1, 170),
            new OverlayPoint(2, 170)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1, -170),
            new OverlayPoint(2, -170)
        ];

        OverlayPoint[] result = OverlayMath.CalculateOperation(
            a,
            b,
            operation,
            wrapPhaseDifference: true);

        Assert.Equal(2, result.Length);
        Assert.All(result, point => Assert.Equal(expected, point.Y, precision: 9));
    }

    [Fact]
    public void CalculateOperation_RawDifferencePreservesUnwrappedSlope()
    {
        // Two unwrapped curves whose difference exceeds 180 degrees: without wrapping the
        // accumulated slope (and hence delay) must survive untouched.
        OverlayPoint[] a =
        [
            new OverlayPoint(1, 170),
            new OverlayPoint(2, 170)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1, -170),
            new OverlayPoint(2, -170)
        ];

        OverlayPoint[] result = OverlayMath.CalculateOperation(
            a,
            b,
            OverlayOperation.AMinusB,
            wrapPhaseDifference: false);

        Assert.All(result, point => Assert.Equal(340, point.Y, precision: 9));
    }

    [Fact]
    public void CalculateOperation_DoesNotWrapNonDifferenceOperations()
    {
        OverlayPoint[] a =
        [
            new OverlayPoint(1, 170),
            new OverlayPoint(2, 170)
        ];
        OverlayPoint[] b =
        [
            new OverlayPoint(1, 170),
            new OverlayPoint(2, 170)
        ];

        OverlayPoint[] result = OverlayMath.CalculateOperation(
            a,
            b,
            OverlayOperation.Sum,
            wrapPhaseDifference: true);

        Assert.All(result, point => Assert.Equal(340, point.Y, precision: 9));
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
    public void SmoothByOctaves_PreservesNaNGaps()
    {
        OverlayPoint[] points = CreateLogarithmicPoints(
            index => index == 24 ? double.NaN : 10);

        OverlayPoint[] result = OverlayMath.SmoothByOctaves(points, 3);

        Assert.True(double.IsNaN(result[24].Y));
        Assert.All(
            result.Where((_, index) => index != 24),
            point => Assert.Equal(10, point.Y, precision: 12));
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

    [Fact]
    public void SmoothByOctaves_PsychoacousticWeightsPeaksWithoutClippingDips()
    {
        // A fine log grid (1/96 octave) around 1 kHz, flat at 0 dB, with a
        // -24 dB dip and a +9 dB peak each 5 points (~1/19 octave) wide.
        // Psychoacoustic cubic averaging must favour the peak while retaining
        // a smooth, finite dip instead of clipping it to a median envelope.
        OverlayPoint[] points = Enumerable.Range(0, 385)
            .Select(index =>
            {
                double frequency = 250.0 * Math.Pow(2, index / 96.0);
                double value = index is >= 190 and <= 194 ? -24.0
                    : index is >= 280 and <= 284 ? 9.0
                    : 0.0;
                return new OverlayPoint(frequency, value);
            })
            .ToArray();

        OverlayPoint[] plain = OverlayMath.SmoothByOctaves(
            points, Dsp.SpectrumSmoothing.PsychoacousticBaseInverseOctaves);
        OverlayPoint[] psycho = OverlayMath.SmoothByOctaves(
            points, Dsp.SpectrumSmoothing.PsychoacousticCode);

        double plainDip = plain.Min(point => point.Y);
        double psychoDip = psycho.Min(point => point.Y);
        Assert.True(plainDip < -1.0, $"plain smoothing lost the dip ({plainDip:0.00} dB)");
        Assert.True(psychoDip > plainDip);
        Assert.True(
            psychoDip < -0.1,
            $"psychoacoustic hard-clipped the dip ({psychoDip:0.00} dB)");

        double plainPeak = plain.Max(point => point.Y);
        double psychoPeak = psycho.Max(point => point.Y);
        Assert.True(plainPeak > 0.5, $"the peak vanished entirely ({plainPeak:0.00} dB)");
        Assert.True(psychoPeak > 1.0);
    }

    [Fact]
    public void SmoothByOctaves_MagnitudeSemanticsDisabledMatchesPlainBaseWidth()
    {
        // A phase-like signed curve with a sharp negative excursion: with the
        // magnitude semantics disabled (phase / group-delay / coherence) the
        // psychoacoustic code must decode to plain 1/6-octave smoothing —
        // identical per point, no upward bias anywhere.
        OverlayPoint[] points = Enumerable.Range(0, 385)
            .Select(index => new OverlayPoint(
                250.0 * Math.Pow(2, index / 96.0),
                index is >= 190 and <= 194 ? -170.0 : 20.0 * Math.Sin(index / 25.0)))
            .ToArray();

        OverlayPoint[] plain = OverlayMath.SmoothByOctaves(
            points, Dsp.SpectrumSmoothing.PsychoacousticBaseInverseOctaves);
        OverlayPoint[] gated = OverlayMath.SmoothByOctaves(
            points,
            Dsp.SpectrumSmoothing.PsychoacousticCode,
            psychoacousticMagnitude: false);

        Assert.Equal(plain.Length, gated.Length);
        for (int i = 0; i < plain.Length; i++)
        {
            Assert.Equal(plain[i].Y, gated[i].Y, precision: 9);
        }
    }
}
