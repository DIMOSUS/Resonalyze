namespace Resonalyze.Dsp.Tests;

public sealed class DataHelperResampleTests
{
    [Fact]
    public void LogarithmicResample_PreservesConstantLevel()
    {
        List<SignalPoint> input = BuildLinearGrid(
            startHz: 10,
            stepHz: 10,
            count: 2400,
            decibels: 5.0);

        List<SignalPoint> output = DataHelper.LogarithmicResample(input, 20, 20_000, 256);

        Assert.All(output, point => Assert.Equal(5.0, point.Y, precision: 3));
    }

    [Fact]
    public void LogarithmicResample_HoldsLastValueBeyondInputRange()
    {
        // The input spectrum ends at 10 kHz (e.g. a low sample rate), while the
        // output grid runs to 20 kHz. The kernel window above 10 kHz contains no
        // input samples, so the weight sum degenerates; the point must hold the
        // nearest sample instead of collapsing to the -160 dB floor.
        List<SignalPoint> input = BuildLinearGrid(
            startHz: 10,
            stepHz: 10,
            count: 1000,
            decibels: 5.0);

        List<SignalPoint> output = DataHelper.LogarithmicResample(input, 20, 20_000, 256);

        Assert.Equal(5.0, output[^1].Y, precision: 3);
    }

    [Fact]
    public void LogarithmicResample_PlacesAnIsolatedFeatureAtTheCorrectOutputFrequency()
    {
        // A single distinct bin at 1 kHz against a flat 0 dB floor. With a very small
        // smoothing width the Lanczos window collapses to +/-2 input bins (10 Hz
        // apart), and the kernel is exactly zero at those integer offsets, so the
        // 1 kHz output point reads ONLY the 1 kHz bin. This pins LogPositionToFrequency,
        // the BinarySearchX centre, and the kernel weighting together: a frequency-axis
        // inversion or an off-by-one centre would read a 0 dB neighbour instead of -6.
        List<SignalPoint> input = BuildLinearGrid(startHz: 10, stepHz: 10, count: 2000, decibels: 0.0);
        input[99] = new SignalPoint(1_000.0, -6.0); // bin index 99 -> 1000 Hz

        // steps = 3 over [100, 10000] puts output[1] at the geometric mean = 1000 Hz.
        List<SignalPoint> output = DataHelper.LogarithmicResample(
            input, start: 100, stop: 10_000, steps: 3, smoothingOctaves: 0.01);

        Assert.Equal(1_000.0, output[1].X, precision: 6);
        Assert.Equal(-6.0, output[1].Y, precision: 6);
        // The flat-floor endpoints stay at 0 dB, confirming the feature did not leak.
        Assert.Equal(0.0, output[0].Y, precision: 6);
        Assert.Equal(0.0, output[2].Y, precision: 6);
    }

    [Fact]
    public void SmoothLinear_PreservesConstantLevel()
    {
        List<SignalPoint> input = BuildLinearGrid(
            startHz: 10,
            stepHz: 10,
            count: 2000,
            decibels: -3.0);

        List<SignalPoint> output = DataHelper.SmoothLinear(input);

        Assert.Equal(input.Count, output.Count);
        Assert.All(output, point => Assert.Equal(-3.0, point.Y, precision: 6));
    }

    [Fact]
    public void SmoothLinear_PreservesNaNSegmentBreakWithoutBlendingAcrossIt()
    {
        var input = new List<SignalPoint>();
        for (int i = 1; i <= 80; i++)
        {
            double value = i is >= 35 and <= 45
                ? double.NaN
                : i < 35 ? 10.0 : 100.0;
            input.Add(new SignalPoint(i * 10.0, value));
        }

        List<SignalPoint> output = DataHelper.SmoothLinear(input, 1.0 / 3.0);

        Assert.All(output.Skip(34).Take(11), point => Assert.True(double.IsNaN(point.Y)));
        Assert.Equal(10.0, output[33].Y, tolerance: 1e-9);
        Assert.Equal(100.0, output[45].Y, tolerance: 1e-9);
    }

    [Fact]
    public void LogarithmicResample_PsychoacousticReducesANarrowDipWithoutClippingIt()
    {
        // A -30 dB notch 30 Hz wide at 1 kHz. Cubic averaging should reduce
        // its perceptual weight, but must not hard-clip it out of the curve.
        List<SignalPoint> input = BuildLinearGrid(
            startHz: 10, stepHz: 10, count: 2400, decibels: 0.0);
        for (int i = 0; i < input.Count; i++)
        {
            if (Math.Abs(input[i].X - 1_000.0) <= 15.0)
            {
                input[i] = new SignalPoint(input[i].X, -30.0);
            }
        }

        List<SignalPoint> plain = DataHelper.LogarithmicResample(
            input, 20, 20_000, 512, smoothingOctaves: 1.0 / 6.0);
        List<SignalPoint> psycho = DataHelper.LogarithmicResample(
            input, 20, 20_000, 512, smoothingOctaves: 1.0 / 6.0,
            psychoacoustic: true);

        double plainDip = plain.Min(point => point.Y);
        double psychoDip = psycho.Min(point => point.Y);
        Assert.True(plainDip < -1.5, $"plain smoothing lost the dip ({plainDip:0.00} dB)");
        Assert.True(
            psychoDip > plainDip,
            $"psychoacoustic did not reduce the dip ({plainDip:0.00} vs {psychoDip:0.00} dB)");
        Assert.True(
            psychoDip < -0.1,
            $"psychoacoustic hard-clipped the dip ({psychoDip:0.00} dB)");
    }

    [Fact]
    public void LogarithmicResample_PsychoacousticRetainsANarrowPeak()
    {
        // A +10 dB peak of the same narrow width must remain clearly visible
        // after the frequency-dependent Gaussian cubic average.
        List<SignalPoint> input = BuildLinearGrid(
            startHz: 10, stepHz: 10, count: 2400, decibels: 0.0);
        for (int i = 0; i < input.Count; i++)
        {
            if (Math.Abs(input[i].X - 1_000.0) <= 15.0)
            {
                input[i] = new SignalPoint(input[i].X, 10.0);
            }
        }

        List<SignalPoint> plain = DataHelper.LogarithmicResample(
            input, 20, 20_000, 512, smoothingOctaves: 1.0 / 6.0);
        List<SignalPoint> psycho = DataHelper.LogarithmicResample(
            input, 20, 20_000, 512, smoothingOctaves: 1.0 / 6.0,
            psychoacoustic: true);

        double plainPeak = plain.Max(point => point.Y);
        double psychoPeak = psycho.Max(point => point.Y);
        Assert.True(plainPeak > 0.5, $"the peak vanished entirely ({plainPeak:0.00} dB)");
        Assert.True(
            psychoPeak > 1.0,
            $"psychoacoustic removed the peak ({psychoPeak:0.00} dB)");
        Assert.True(psychoPeak <= plainPeak);
    }

    [Fact]
    public void LogarithmicResample_PsychoacousticKeepsAValleyWiderThanTheWindow()
    {
        // A -10 dB valley spanning a full octave (700-1400 Hz) represents broad
        // tonal structure and must remain visible.
        List<SignalPoint> input = BuildLinearGrid(
            startHz: 10, stepHz: 10, count: 2400, decibels: 0.0);
        for (int i = 0; i < input.Count; i++)
        {
            if (input[i].X >= 700.0 && input[i].X <= 1_400.0)
            {
                input[i] = new SignalPoint(input[i].X, -10.0);
            }
        }

        List<SignalPoint> psycho = DataHelper.LogarithmicResample(
            input, 20, 20_000, 512, smoothingOctaves: 1.0 / 6.0,
            psychoacoustic: true);

        SignalPoint center = psycho.MinBy(
            point => Math.Abs(point.X - 1_000.0));
        Assert.True(center.Y < -9.0, $"the broad valley was lifted ({center.Y:0.00} dB)");
    }

    [Fact]
    public void LogarithmicResample_PsychoacousticKeepsSmoothCurveContinuous()
    {
        var input = new List<SignalPoint>();
        for (double frequency = 20.0; frequency <= 20_000.0; frequency += 20.0)
        {
            double octave = Math.Log2(frequency / 1_000.0);
            double decibels = 6.0 - 3.0 * octave * octave;
            input.Add(new SignalPoint(frequency, decibels));
        }

        List<SignalPoint> plain = DataHelper.LogarithmicResample(
            input,
            20.0,
            20_000.0,
            512,
            smoothingOctaves: 1.0 / 6.0);
        List<SignalPoint> output = DataHelper.LogarithmicResample(
            input,
            20.0,
            20_000.0,
            512,
            smoothingOctaves: 1.0 / 6.0,
            psychoacoustic: true);

        double maximumFloorSlopeChange = Enumerable.Range(1, output.Count - 2)
            .Where(index => output[index].X is >= 500.0 and <= 2_000.0)
            .Max(index => Math.Abs(
                (output[index + 1].Y - plain[index + 1].Y) -
                2.0 * (output[index].Y - plain[index].Y) +
                (output[index - 1].Y - plain[index - 1].Y)));

        Assert.True(
            maximumFloorSlopeChange < 0.04,
            $"psychoacoustic smoothing introduced a {maximumFloorSlopeChange:0.000} dB step");
    }

    [Fact]
    public void LogarithmicPowerBandResample_PsychoacousticDoesNotClipANarrowDip()
    {
        // The RTA path pre-integrates a fixed 1/12-octave reference band, so a
        // notch smears by that band before the display smoothing ever sees it.
        // Psychoacoustic smoothing must retain a finite valley rather than
        // replacing it with a hard lower envelope.
        // Pink amplitude (1/sqrt f): flat band power per fractional octave, so
        // the smoothing window is untilted. The dip is measured against the
        // same curve computed without the notch.
        const int fftLength = 8_192;
        const int sampleRate = 48_000;
        double binWidth = (double)sampleRate / fftLength;
        double halfNotchHz = 1_000.0 * (Math.Pow(2, 1.0 / 32) - 1);
        var amplitude = new double[fftLength / 2 + 1];
        var flat = new double[fftLength / 2 + 1];
        for (int i = 0; i < amplitude.Length; i++)
        {
            double hz = i * binWidth;
            double pink = hz > 0 ? 1.0 / Math.Sqrt(hz) : 0.0;
            flat[i] = pink;
            amplitude[i] =
                Math.Abs(hz - 1_000.0) <= halfNotchHz ? pink * 1e-3 : pink;
        }

        List<SignalPoint> Curve(double[] spectrum, bool psychoacoustic) =>
            DataHelper.LogarithmicPowerBandResample(
                spectrum, fftLength, sampleRate, 1.0, 1.0, 20, 20_000, 512,
                smoothingOctaves: 1.0 / 3.0, psychoacoustic: psychoacoustic);
        double DipVsBaseline(bool psychoacoustic)
        {
            List<SignalPoint> notched = Curve(amplitude, psychoacoustic);
            List<SignalPoint> baseline = Curve(flat, psychoacoustic);
            return Enumerable.Range(0, notched.Count)
                .Where(i => notched[i].X > 800 && notched[i].X < 1_250)
                .Min(i => notched[i].Y - baseline[i].Y);
        }

        double plainDip = DipVsBaseline(psychoacoustic: false);
        double psychoDip = DipVsBaseline(psychoacoustic: true);
        Assert.True(plainDip < -0.5, $"plain smoothing lost the dip ({plainDip:0.00} dB)");
        Assert.True(
            psychoDip < -0.1,
            $"psychoacoustic hard-clipped the dip ({psychoDip:0.00} dB)");
    }

    private static List<SignalPoint> BuildLinearGrid(
        double startHz,
        double stepHz,
        int count,
        double decibels)
    {
        List<SignalPoint> points = new(count);
        for (int i = 0; i < count; i++)
        {
            points.Add(new SignalPoint(startHz + i * stepHz, decibels));
        }

        return points;
    }
}
