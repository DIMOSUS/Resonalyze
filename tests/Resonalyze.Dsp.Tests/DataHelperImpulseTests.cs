using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class DataHelperImpulseTests
{
    private const int SampleRate = 48_000;

    private static SyntheticMeasurement WithPeakAt(
        int peakIndex, int length, double amplitude = 1.0)
    {
        var ir = new Complex[length];
        ir[peakIndex] = new Complex(amplitude, 0.0);
        if (peakIndex + 40 < length)
        {
            ir[peakIndex + 40] = new Complex(0.25 * amplitude, 0.0);
        }

        return new SyntheticMeasurement(ir, SampleRate, peakIndex);
    }

    // A lone tap: with a neighbour a wide average legitimately slides the maximum.
    private static SyntheticMeasurement WithSingleTapAt(int peakIndex, int length)
    {
        var ir = new Complex[length];
        ir[peakIndex] = Complex.One;
        return new SyntheticMeasurement(ir, SampleRate, peakIndex);
    }

    // Sample-unit axis with the origin at record start: X is the signed sample itself.
    private static SignalPoint At(IReadOnlyList<SignalPoint> points, double sample) =>
        points.Single(point => point.X == sample);

    private static ImpulseResponseOptions Options(
        Action<ImpulseResponseOptions>? configure = null)
    {
        var opt = new ImpulseResponseOptions
        {
            Length = 4_096,
            TimeUnit = ImpulseTimeUnit.Samples,
            AmplitudeScale = ImpulseAmplitudeScale.Linear
        };
        configure?.Invoke(opt);
        return opt;
    }

    [Fact]
    public void Impulse_DrawsOnePeriodWithTheSecondHalfBeforeZero()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 1_000, length: 2_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement, Options(), new ImpulseRenderFrame());

        AnalysisCurve curve = Assert.IsType<AnalysisCurve>(set.Impulse);
        Assert.Equal(2_000, curve.Points.Count);
        Assert.Equal(-999.0, curve.Points[0].X, precision: 12);
        Assert.Equal(1_000.0, curve.Points[^1].X, precision: 12);
        Assert.Equal(1.0, At(curve.Points, 1_000).Y, precision: 12);
        Assert.Equal(1_000, set.PeakSample);
    }

    [Fact]
    public void PreRinging_WrappedToTheRecordEnd_IsDrawnBeforeZero()
    {
        var ir = new Complex[2_000];
        ir[100] = Complex.One;
        ir[1_950] = new Complex(-0.3, 0.0);
        var measurement = new SyntheticMeasurement(ir, SampleRate, 100);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement, Options(o => o.ShowEnvelope = true), new ImpulseRenderFrame());

        Assert.Equal(-0.3, At(set.Impulse!.Points, -50).Y, precision: 12);
        Assert.Equal(-50.0, set.Envelope!.Points.Where(p => p.X < 0).MaxBy(p => p.Y).X, precision: 9);
        Assert.Equal(100, set.PeakSample);
    }

    [Fact]
    public void PeakSample_InTheRecordsSecondHalfIsNegative()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 1_900, length: 2_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement, Options(), new ImpulseRenderFrame());

        Assert.Equal(-100, set.PeakSample);
        Assert.Equal(1.0, At(set.Impulse!.Points, -100).Y, precision: 12);
    }

    [Fact]
    public void TheEnvelopeAndSnr_ReadTheSameUnderAnotherFramingOrSign_AsAFreshRecordDoes()
    {
        var random = new Random(5);
        var ir = new Complex[8_192];
        for (int i = 0; i < ir.Length; i++)
        {
            ir[i] = new Complex((random.NextDouble() - 0.5) * 1e-3, 0.0);
        }

        ir[1_000] = Complex.One;
        var measurement = new SyntheticMeasurement(ir, SampleRate, 1_000);
        Action<ImpulseResponseOptions> reframed = o =>
        {
            o.ShowEnvelope = true;
            o.Invert = true;
            o.TimeUnit = ImpulseTimeUnit.Milliseconds;
        };

        ImpulseCurveSet plain = DataHelper.GetImpulseCurves(
            measurement, Options(o => o.ShowEnvelope = true), new ImpulseRenderFrame());
        ImpulseCurveSet kept = DataHelper.GetImpulseCurves(
            measurement, Options(reframed), new ImpulseRenderFrame());
        ImpulseCurveSet fresh = DataHelper.GetImpulseCurves(
            new SyntheticMeasurement((Complex[])ir.Clone(), SampleRate, 1_000), Options(reframed), new ImpulseRenderFrame());

        Assert.NotNull(plain.SnrDb);
        Assert.Equal(plain.SnrDb, kept.SnrDb);
        Assert.Equal(fresh.SnrDb, kept.SnrDb);
        Assert.Equal(fresh.Envelope!.Points, kept.Envelope!.Points);
    }

    [Fact]
    public void Impulse_EmptyResponseYieldsASingleSample()
    {
        var measurement = new SyntheticMeasurement(Array.Empty<Complex>(), SampleRate, 0);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement, Options(), new ImpulseRenderFrame());

        Assert.Single(set.Impulse!.Points);
    }

    [Fact]
    public void Impulse_MillisecondsAxisConvertsBySampleRate()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 480, length: 4_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o => o.TimeUnit = ImpulseTimeUnit.Milliseconds),
            new ImpulseRenderFrame());

        SignalPoint peak = set.Impulse!.Points.MaxBy(p => Math.Abs(p.Y));
        Assert.Equal(10.0, peak.X, precision: 9); // 480 samples at 48 kHz
    }

    [Theory]
    [InlineData(0.0, 480.0)]   // record start: the peak keeps its absolute index
    [InlineData(480.0, 0.0)]   // an origin on the peak puts it at zero
    [InlineData(400.0, 80.0)]  // an arrival estimate ahead of the peak
    public void Impulse_OriginOnlyMovesTheAxis(double origin, double expectedPeakX)
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 480, length: 4_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement, Options(), new ImpulseRenderFrame(origin));

        SignalPoint peak = set.Impulse!.Points.MaxBy(p => Math.Abs(p.Y));
        Assert.Equal(expectedPeakX, peak.X, precision: 9);
        Assert.Equal(1.0, peak.Y, precision: 12);
        Assert.Equal(480, set.PeakSample);
    }

    [Fact]
    public void Impulse_FractionalOriginLandsBetweenSamples()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 480, length: 4_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement, Options(), new ImpulseRenderFrame(479.25));

        SignalPoint peak = set.Impulse!.Points.MaxBy(p => Math.Abs(p.Y));
        Assert.Equal(0.75, peak.X, precision: 9);
    }

    [Theory]
    [InlineData(ImpulseAmplitudeScale.Linear, 0.5)]
    [InlineData(ImpulseAmplitudeScale.PercentOfPeak, 100.0)]
    [InlineData(ImpulseAmplitudeScale.Decibels, 0.0)]
    public void Impulse_ScalesAgainstTheReferencePeak(
        ImpulseAmplitudeScale scale, double expectedPeakY)
    {
        SyntheticMeasurement measurement =
            WithPeakAt(peakIndex: 100, length: 2_000, amplitude: 0.5);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o => o.AmplitudeScale = scale),
            new ImpulseRenderFrame());

        // In dB the largest magnitude is the silence floor, not the arrival.
        Assert.Equal(expectedPeakY, At(set.Impulse!.Points, 100).Y, precision: 9);
        Assert.Equal(0.5, set.PeakReference, precision: 12);
    }

    [Fact]
    public void Impulse_SharedReferenceKeepsTheLevelDifferenceBetweenRecords()
    {
        // Per-record normalization would erase the compared difference: half amplitude reads -6 dB.
        SyntheticMeasurement main = WithPeakAt(100, 2_000, amplitude: 1.0);
        SyntheticMeasurement quiet = WithPeakAt(100, 2_000, amplitude: 0.5);
        ImpulseResponseOptions opt =
            Options(o => o.AmplitudeScale = ImpulseAmplitudeScale.Decibels);

        ImpulseCurveSet mainSet =
            DataHelper.GetImpulseCurves(main, opt, new ImpulseRenderFrame());
        ImpulseCurveSet compareSet = DataHelper.GetImpulseCurves(
            quiet, opt, new ImpulseRenderFrame(0.0, mainSet.PeakReference));

        Assert.Equal(0.0, mainSet.Impulse!.Points.Max(p => p.Y), precision: 9);
        Assert.Equal(-6.0206, compareSet.Impulse!.Points.Max(p => p.Y), precision: 3);
    }

    [Fact]
    public void Impulse_OwnReferenceIsUsedWhenNoneIsShared()
    {
        SyntheticMeasurement quiet = WithPeakAt(100, 2_000, amplitude: 0.5);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            quiet,
            Options(o => o.AmplitudeScale = ImpulseAmplitudeScale.Decibels),
            new ImpulseRenderFrame());

        Assert.Equal(0.0, set.Impulse!.Points.Max(p => p.Y), precision: 9);
    }

    [Fact]
    public void Invert_FlipsTheImpulseAndStepButNotTheEnvelope()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 100, length: 2_000);
        ImpulseResponseOptions plain = Options(o =>
        {
            o.ShowEnvelope = true;
            o.ShowStep = true;
        });
        ImpulseResponseOptions inverted = Options(o =>
        {
            o.ShowEnvelope = true;
            o.ShowStep = true;
            o.Invert = true;
        });

        ImpulseCurveSet a =
            DataHelper.GetImpulseCurves(measurement, plain, new ImpulseRenderFrame());
        ImpulseCurveSet b =
            DataHelper.GetImpulseCurves(measurement, inverted, new ImpulseRenderFrame());

        Assert.Equal(1.0, At(a.Impulse!.Points, 100).Y, precision: 12);
        Assert.Equal(-1.0, At(b.Impulse!.Points, 100).Y, precision: 12);
        Assert.Equal(-At(a.Step!.Points, 200).Y, At(b.Step!.Points, 200).Y, precision: 12);
        Assert.Equal(At(a.Envelope!.Points, 100).Y, At(b.Envelope!.Points, 100).Y, precision: 12);
        Assert.Equal(a.PeakReference, b.PeakReference, precision: 12);
    }

    [Fact]
    public void Envelope_IsTheOneTheEngineReadsOffTheSameRecord()
    {
        // Same envelope as the rest of the app: padding before the transform lifted the silent region and inflated SNR by up to 32 dB.
        var ir = new Complex[8_192];
        for (int i = 0; i < 400; i++)
        {
            ir[200 + i] = new Complex(Math.Exp(-i / 60.0) * Math.Cos(i * 0.35), 0.0);
        }

        var measurement = new SyntheticMeasurement(ir, SampleRate, 200);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o => o.ShowEnvelope = true),
            new ImpulseRenderFrame());

        double[] expected = SignalEnvelope.Envelope(
            ir.Select(sample => sample.Real).ToArray());
        double peak = expected.Max();
        Assert.Equal(
            SignalEnvelope.EstimatePeakConfidenceDecibels(expected, peak),
            set.SnrDb!.Value,
            precision: 9);
        for (int i = 0; i < expected.Length; i += 97)
        {
            Assert.Equal(
                expected[i],
                At(set.Envelope!.Points, DspMath.ToSignedLag(i, expected.Length)).Y,
                precision: 12);
        }
    }

    [Fact]
    public void Envelope_PeaksAtTheImpulseAndCarriesTheConfidenceFigure()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 300, length: 4_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o => o.ShowEnvelope = true),
            new ImpulseRenderFrame());

        SignalPoint peak = set.Envelope!.Points.MaxBy(p => p.Y);
        Assert.Equal(300.0, peak.X, precision: 9);
        Assert.NotNull(set.SnrDb);
        Assert.True(set.SnrDb > 0.0);
    }

    [Fact]
    public void Envelope_IsOnlyComputedWhenItIsDrawn()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 300, length: 4_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement, Options(), new ImpulseRenderFrame());

        Assert.Null(set.Envelope);
        Assert.Null(set.SnrDb);
    }

    [Fact]
    public void EnvelopeSmoothing_IsCentredSoNothingMovesInTime()
    {
        // A trailing average would drag arrivals half a window late. Compared against the unsmoothed envelope:
        // an even-length delta's analytic signal is not exactly symmetric (Nyquist bin survives).
        SyntheticMeasurement measurement = WithSingleTapAt(peakIndex: 300, length: 4_000);
        const double smoothingMs = 1.0; // 48 samples at 48 kHz

        ImpulseCurveSet raw = DataHelper.GetImpulseCurves(
            measurement, Options(o => o.ShowEnvelope = true), new ImpulseRenderFrame());
        ImpulseCurveSet smoothed = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.ShowEnvelope = true;
                o.EnvelopeSmoothingMs = smoothingMs;
            }),
            new ImpulseRenderFrame());

        double before = raw.Envelope!.Points.MaxBy(p => p.Y).X;
        double after = smoothed.Envelope!.Points.MaxBy(p => p.Y).X;
        Assert.Equal(300.0, before, precision: 9);
        Assert.True(
            Math.Abs(after - before) <= 2.0,
            $"smoothing moved the envelope peak from {before} to {after}");
    }

    [Fact]
    public void EnvelopeSmoothing_LowersThePeakOfAnIsolatedArrival()
    {
        SyntheticMeasurement measurement = WithSingleTapAt(peakIndex: 300, length: 4_000);

        ImpulseCurveSet raw = DataHelper.GetImpulseCurves(
            measurement, Options(o => o.ShowEnvelope = true), new ImpulseRenderFrame());
        ImpulseCurveSet smoothed = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.ShowEnvelope = true;
                o.EnvelopeSmoothingMs = 1.0;
            }),
            new ImpulseRenderFrame());

        Assert.True(
            smoothed.Envelope!.Points.Max(p => p.Y) < raw.Envelope!.Points.Max(p => p.Y));
    }

    [Fact]
    public void Step_IsTheRunningIntegralOfTheImpulse()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 100, length: 2_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.ShowStep = true;
                o.NormalizeStepToImpulsePeak = true;
            }),
            new ImpulseRenderFrame());

        IReadOnlyList<SignalPoint> step = set.Step!.Points;
        Assert.Equal(0.0, At(step, 99).Y, precision: 12);
        Assert.Equal(1.0, At(step, 100).Y, precision: 12);
        Assert.Equal(1.0, At(step, 139).Y, precision: 12);
        Assert.Equal(1.25, At(step, 140).Y, precision: 12);
        Assert.Equal(1.25, step[^1].Y, precision: 12);
    }

    [Fact]
    public void Step_IsZeroJustBeforeTimeZero_AndIntegratesThePreRingingBackwards()
    {
        var ir = new Complex[2_000];
        ir[100] = Complex.One;
        ir[1_950] = new Complex(-0.3, 0.0);
        var measurement = new SyntheticMeasurement(ir, SampleRate, 100);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement, Options(o => o.ShowStep = true), new ImpulseRenderFrame());

        IReadOnlyList<SignalPoint> step = set.Step!.Points;
        Assert.Equal(0.0, At(step, -1).Y, precision: 12);
        Assert.Equal(0.0, At(step, -50).Y, precision: 12);
        Assert.Equal(0.3, At(step, -51).Y, precision: 12);
        Assert.Equal(0.3, step[0].Y, precision: 12);
        Assert.Equal(1.0, At(step, 100).Y, precision: 12);
    }

    [Fact]
    public void Step_AgainstItsOwnPeakFillsTheAxisInstead()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 100, length: 2_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.ShowStep = true;
                o.NormalizeStepToImpulsePeak = false;
            }),
            new ImpulseRenderFrame());

        Assert.Equal(1.0, set.Step!.Points[^1].Y, precision: 12);
        Assert.Equal(0.8, At(set.Step.Points, 100).Y, precision: 12);
    }

    [Theory]
    [InlineData(ImpulseAmplitudeScale.Linear)]
    [InlineData(ImpulseAmplitudeScale.PercentOfPeak)]
    [InlineData(ImpulseAmplitudeScale.Decibels)]
    public void Step_IsNormalizedInEveryScaleForAnAxisOfItsOwn(
        ImpulseAmplitudeScale scale)
    {
        // The step never takes the level axis units: dB cannot hold a signed quantity, and DC integrates huge in linear.
        SyntheticMeasurement measurement =
            WithPeakAt(peakIndex: 100, length: 2_000, amplitude: 0.5);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.ShowStep = true;
                o.AmplitudeScale = scale;
            }),
            new ImpulseRenderFrame());

        Assert.Equal(1.0, At(set.Step!.Points, 100).Y, precision: 12);
    }

    private static SyntheticMeasurement WithTwoBandsAt(
        int lowIndex, double lowHz, int highIndex, double highHz, int length)
    {
        var ir = new Complex[length];
        AddBurst(ir, lowIndex, lowHz);
        AddBurst(ir, highIndex, highHz);
        return new SyntheticMeasurement(ir, SampleRate, highIndex);
    }

    private static void AddBurst(Complex[] ir, int center, double frequencyHz)
    {
        int span = (int)(SampleRate / frequencyHz * 2);
        for (int k = -span; k <= span; k++)
        {
            int index = center + k;
            if ((uint)index >= (uint)ir.Length)
            {
                continue;
            }

            double t = k / (double)SampleRate;
            double envelope = Math.Exp(-Math.Pow(t * frequencyHz * 1.6, 2.0));
            ir[index] += new Complex(
                envelope * Math.Cos(2 * Math.PI * frequencyHz * t), 0.0);
        }
    }

    [Fact]
    public void BandFilter_PeaksOnTheArrivalThatBelongsToTheBand()
    {
        SyntheticMeasurement measurement = WithTwoBandsAt(
            lowIndex: 400, lowHz: 125, highIndex: 1_200, highHz: 4_000, length: 8_192);

        ImpulseCurveSet low = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.Length = 7_000;
                o.BandFilterOctaves = 1.0;
                o.BandCenterHz = 125;
            }),
            new ImpulseRenderFrame());
        ImpulseCurveSet high = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.Length = 7_000;
                o.BandFilterOctaves = 1.0;
                o.BandCenterHz = 4_000;
            }),
            new ImpulseRenderFrame());

        Assert.InRange(low.PeakSample, 350, 450);
        Assert.InRange(high.PeakSample, 1_150, 1_250);
    }

    [Fact]
    public void BandFilter_RejectsWhatIsOutsideTheBand()
    {
        SyntheticMeasurement measurement = WithTwoBandsAt(
            lowIndex: 400, lowHz: 125, highIndex: 1_200, highHz: 4_000, length: 8_192);
        ImpulseResponseOptions opt = Options(o =>
        {
            o.Length = 7_000;
            o.BandFilterOctaves = 1.0;
            o.BandCenterHz = 125;
        });

        ImpulseCurveSet set =
            DataHelper.GetImpulseCurves(measurement, opt, new ImpulseRenderFrame());

        double atHighBurst = set.Impulse!.Points
            .Where(p => p.X >= 1_150 && p.X < 1_250).Max(p => Math.Abs(p.Y));
        Assert.True(
            atHighBurst < 0.05 * set.PeakReference,
            $"out-of-band burst survived at {atHighBurst} against a peak of {set.PeakReference}");
    }

    [Fact]
    public void BandFilter_IsZeroPhaseSoTheArrivalDoesNotMove()
    {
        // A phase-bearing filter would report its own group delay as the band's arrival.
        var ir = new Complex[8_192];
        AddBurst(ir, 400, 1_000);
        var measurement = new SyntheticMeasurement(ir, SampleRate, 400);

        ImpulseCurveSet plain = DataHelper.GetImpulseCurves(
            measurement, Options(o => o.Length = 7_000), new ImpulseRenderFrame());
        ImpulseCurveSet filtered = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.Length = 7_000;
                o.BandFilterOctaves = 1.0;
                o.BandCenterHz = 1_000;
            }),
            new ImpulseRenderFrame());

        Assert.Equal(400, plain.PeakSample);
        Assert.InRange(filtered.PeakSample, 398, 402);
    }

    [Fact]
    public void BandFilter_OffLeavesTheRecordAlone()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 100, length: 2_000);

        ImpulseCurveSet filtered = DataHelper.GetImpulseCurves(
            measurement,
            Options(o => o.BandCenterHz = 125),
            new ImpulseRenderFrame());
        ImpulseCurveSet plain = DataHelper.GetImpulseCurves(
            measurement, Options(), new ImpulseRenderFrame());

        Assert.Equal(plain.PeakReference, filtered.PeakReference, precision: 12);
        Assert.Equal(At(plain.Impulse!.Points, 100).Y, At(filtered.Impulse!.Points, 100).Y, precision: 12);
    }

    [Theory]
    [InlineData(30_000.0, 1.0)]
    // Octave-symmetric band: a full octave at 20 kHz needs 28.3 kHz, which 48 kHz cannot carry.
    [InlineData(20_000.0, 1.0)]
    [InlineData(23_000.0, 1.0 / 3.0)]
    public void BandFilter_ThatCannotBeRealizedIsRefusedRatherThanTruncated(
        double centerHz, double octaves)
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 100, length: 2_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.BandFilterOctaves = octaves;
                o.BandCenterHz = centerHz;
            }),
            new ImpulseRenderFrame());

        Assert.Equal(1.0, At(set.Impulse!.Points, 100).Y, precision: 12);
    }

    [Theory]
    // The passband must fit; a clipped fade skirt only costs roll-off steepness.
    [InlineData(16_000.0, 1.0, 48_000, true)]
    [InlineData(16_000.0, 1.0, 44_100, false)]   // 22.6 kHz passband against 22.05 Nyquist
    [InlineData(20_000.0, 1.0 / 3.0, 44_100, false)]
    [InlineData(20_000.0, 1.0 / 3.0, 48_000, true)]
    public void HasBandFilter_AsksWhetherThePassbandFitsUnderNyquist(
        double centerHz, double octaves, int sampleRate, bool expected)
    {
        var opt = new ImpulseResponseOptions
        {
            BandFilterOctaves = octaves,
            BandCenterHz = centerHz
        };

        Assert.Equal(expected, opt.HasBandFilter(sampleRate));
    }

    [Fact]
    public void Curves_AreOnlyBuiltWhenRequested()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 100, length: 2_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.ShowImpulse = false;
                o.ShowEnvelope = false;
                o.ShowStep = true;
            }),
            new ImpulseRenderFrame());

        Assert.Null(set.Impulse);
        Assert.Null(set.Envelope);
        Assert.NotNull(set.Step);
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
