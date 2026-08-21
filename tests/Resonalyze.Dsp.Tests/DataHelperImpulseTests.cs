using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class DataHelperImpulseTests
{
    private const int SampleRate = 48_000;

    private static SyntheticMeasurement WithPeakAt(
        int peakIndex, int length, double amplitude = 1.0)
    {
        var ir = new Complex[length];
        // A clear dominant peak plus a smaller lobe so "the peak" is unambiguous.
        ir[peakIndex] = new Complex(amplitude, 0.0);
        if (peakIndex + 40 < length)
        {
            ir[peakIndex + 40] = new Complex(0.25 * amplitude, 0.0);
        }

        return new SyntheticMeasurement(ir, SampleRate, peakIndex);
    }

    // One tap and nothing else: the smoothing tests need an arrival whose envelope
    // has no neighbour to merge with, or a wide average legitimately slides the
    // maximum towards the second tap and the test reads that as a centring bug.
    private static SyntheticMeasurement WithSingleTapAt(int peakIndex, int length)
    {
        var ir = new Complex[length];
        ir[peakIndex] = Complex.One;
        return new SyntheticMeasurement(ir, SampleRate, peakIndex);
    }

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
    public void Impulse_UsesAbsoluteSamplesAndClampsToTheAvailableLength()
    {
        // peakIndex + Length (1000 + 4096 = 5096) exceeds the 2000-sample response, so
        // the curve must clamp to the available length and keep the X axis absolute.
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 1_000, length: 2_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement, Options(), new ImpulseRenderFrame());

        AnalysisCurve curve = Assert.IsType<AnalysisCurve>(set.Impulse);
        Assert.Equal(2_000, curve.Points.Count);
        Assert.Equal(0.0, curve.Points[0].X, precision: 12);
        SignalPoint peak = curve.Points.MaxBy(p => Math.Abs(p.Y));
        Assert.Equal(1_000.0, peak.X, precision: 12);
        Assert.Equal(1_000, set.PeakSample);
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
        // The samples themselves are untouched by the framing.
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

        // Read the peak at its known index: in dB the largest MAGNITUDE is the
        // silence floor, not the arrival.
        Assert.Equal(expectedPeakY, set.Impulse!.Points[100].Y, precision: 9);
        Assert.Equal(0.5, set.PeakReference, precision: 12);
    }

    [Fact]
    public void Impulse_SharedReferenceKeepsTheLevelDifferenceBetweenRecords()
    {
        // The lesson the Time Alignment envelopes already learned: normalizing each
        // record to its own peak erases exactly the difference being compared. Half
        // the amplitude must read as -6 dB, not as another 0 dB peak.
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

        Assert.Equal(1.0, a.Impulse!.Points[100].Y, precision: 12);
        Assert.Equal(-1.0, b.Impulse!.Points[100].Y, precision: 12);
        Assert.Equal(-a.Step!.Points[200].Y, b.Step!.Points[200].Y, precision: 12);
        // A magnitude has no polarity to flip.
        Assert.Equal(a.Envelope!.Points[100].Y, b.Envelope!.Points[100].Y, precision: 12);
        // The peak reference is a magnitude too, so the scale does not move.
        Assert.Equal(a.PeakReference, b.PeakReference, precision: 12);
    }

    [Fact]
    public void Envelope_DecaysToTheEndInsteadOfWrappingBackUp()
    {
        // The discrete Hilbert transform is circular: computed over the bare window,
        // the onset's 1/t skirt wraps around and lifts the far end of the envelope
        // tens of dB above the noise floor — a decay tail made of arithmetic. The
        // envelope's last quarter must not out-level its third quarter.
        var ir = new Complex[8_192];
        ir[200] = Complex.One;
        var measurement = new SyntheticMeasurement(ir, SampleRate, 200);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.Length = 8_000;
                o.ShowEnvelope = true;
            }),
            new ImpulseRenderFrame());

        IReadOnlyList<SignalPoint> envelope = set.Envelope!.Points;
        int third = envelope.Count * 3 / 4;
        double thirdQuarterMax = envelope.Take(third).Skip(envelope.Count / 2).Max(p => p.Y);
        double lastQuarterMax = envelope.Skip(third).Max(p => p.Y);
        Assert.True(
            lastQuarterMax <= thirdQuarterMax,
            $"envelope rose from {thirdQuarterMax} to {lastQuarterMax} at the window end");
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
        // The confidence figure reads that envelope, so it is absent for the same reason.
        Assert.Null(set.SnrDb);
    }

    [Fact]
    public void EnvelopeSmoothing_IsCentredSoNothingMovesInTime()
    {
        // A trailing average would drag every arrival half a window later and quietly
        // falsify the timing the rest of the app measures off this record. Measured
        // against the UNSMOOTHED envelope rather than against the tap's index: the
        // discrete analytic signal of a delta in an even-length buffer is not exactly
        // symmetric (the Nyquist bin survives), so a wide average can settle a single
        // sample off centre — an order of magnitude less than the 24 samples the bug
        // this pins would cost.
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
        // Two taps of +1 and +0.25: the step rises to 1 at the first and to 1.25 at
        // the second, then holds — the integral, not the samples.
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
        Assert.Equal(0.0, step[99].Y, precision: 12);
        Assert.Equal(1.0, step[100].Y, precision: 12);
        Assert.Equal(1.0, step[139].Y, precision: 12);
        Assert.Equal(1.25, step[140].Y, precision: 12);
        Assert.Equal(1.25, step[^1].Y, precision: 12);
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

        // Normalized to the step's own maximum (1.25), the tail sits at exactly 1.
        Assert.Equal(1.0, set.Step!.Points[^1].Y, precision: 12);
        Assert.Equal(0.8, set.Step.Points[100].Y, precision: 12);
    }

    [Theory]
    [InlineData(ImpulseAmplitudeScale.Linear)]
    [InlineData(ImpulseAmplitudeScale.PercentOfPeak)]
    [InlineData(ImpulseAmplitudeScale.Decibels)]
    public void Step_IsNormalizedInEveryScaleForAnAxisOfItsOwn(
        ImpulseAmplitudeScale scale)
    {
        // The step never takes the level axis's units. dB cannot hold a signed
        // quantity that crosses zero, and in the linear scales a record with any DC
        // or low-frequency content integrates into a step many times the impulse
        // peak, which flattens the impulse against the bottom of its own plot.
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

        Assert.Equal(1.0, set.Step!.Points[100].Y, precision: 12);
    }

    // A tone burst at one frequency plus a much later burst at another: a band filter
    // has to keep its own and drop the other, which is exactly the "when does this band
    // arrive" reading the filter exists for.
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

        // Each band peaks on its own burst, not on the record's strongest sample.
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

        // The 4 kHz burst is five octaves outside a one-octave band around 125 Hz.
        double atHighBurst = set.Impulse!.Points
            .Skip(1_150).Take(100).Max(p => Math.Abs(p.Y));
        Assert.True(
            atHighBurst < 0.05 * set.PeakReference,
            $"out-of-band burst survived at {atHighBurst} against a peak of {set.PeakReference}");
    }

    [Fact]
    public void BandFilter_IsZeroPhaseSoTheArrivalDoesNotMove()
    {
        // A filter with phase would delay the band it passes, and the view would report
        // the filter's own group delay as the band's arrival time — at 1 kHz through a
        // one-octave minimum-phase section that is tens of samples.
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
            // A centre without a width is not a band: nothing may be filtered.
            Options(o => o.BandCenterHz = 125),
            new ImpulseRenderFrame());
        ImpulseCurveSet plain = DataHelper.GetImpulseCurves(
            measurement, Options(), new ImpulseRenderFrame());

        Assert.Equal(plain.PeakReference, filtered.PeakReference, precision: 12);
        Assert.Equal(plain.Impulse!.Points[100].Y, filtered.Impulse!.Points[100].Y, precision: 12);
    }

    [Fact]
    public void BandFilter_AboveNyquistIsIgnoredRatherThanApplied()
    {
        SyntheticMeasurement measurement = WithPeakAt(peakIndex: 100, length: 2_000);

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            measurement,
            Options(o =>
            {
                o.BandFilterOctaves = 1.0;
                o.BandCenterHz = 30_000; // above 24 kHz Nyquist
            }),
            new ImpulseRenderFrame());

        Assert.Equal(1.0, set.Impulse!.Points[100].Y, precision: 12);
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
