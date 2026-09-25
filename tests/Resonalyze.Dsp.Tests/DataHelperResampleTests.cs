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
    public void LogarithmicResample_DoesNotRepeatTheLastBinPastTheTopOfTheGrid()
    {
        // 44.1 kHz, 32768-point grid: flat to 20.5 kHz, then an anti-alias roll-off to -60 dB.
        const double Step = 44_100.0 / 32_768;
        var input = new List<SignalPoint>();
        for (int bin = 1; bin <= 16_384; bin++)
        {
            double hz = bin * Step;
            input.Add(new SignalPoint(hz, hz <= 20_500 ? 0.0 : -60.0));
        }

        List<SignalPoint> output = DataHelper.LogarithmicResample(input, 20, 20_000, 512, smoothingOctaves: 1.0);

        // Over the real bins only the kernel reads about -3.4 dB here; repeating the -60 dB last bin reads -9.2 dB.
        Assert.InRange(output[^1].Y, -4.5, -2.5);
    }

    [Fact]
    public void LogarithmicResample_HoldsLastValueBeyondInputRange()
    {
        // No input samples above 10 kHz: the weight sum degenerates, so hold the nearest sample, not the -160 dB floor.
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
        // Tiny smoothing collapses Lanczos to ±2 bins where the kernel is zero: pins axis inversion and centre off-by-one.
        List<SignalPoint> input = BuildLinearGrid(startHz: 10, stepHz: 10, count: 2000, decibels: 0.0);
        input[99] = new SignalPoint(1_000.0, -6.0); // bin index 99 -> 1000 Hz

        // steps = 3 over [100, 10000] puts output[1] at 1000 Hz.
        List<SignalPoint> output = DataHelper.LogarithmicResample(
            input, start: 100, stop: 10_000, steps: 3, smoothingOctaves: 0.01);

        Assert.Equal(1_000.0, output[1].X, precision: 6);
        Assert.Equal(-6.0, output[1].Y, precision: 6);
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

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(2_048)]
    public void LogarithmicResample_PsychoacousticHonoursTwoBinResolutionFloor(
        int fftLength)
    {
        const int sampleRate = 48_000;
        double binWidth = (double)sampleRate / fftLength;
        int centerBin = 4;
        double centerFrequency = centerBin * binWidth;
        var input = new List<SignalPoint>(fftLength / 2 + 1);
        for (int bin = 0; bin <= fftLength / 2; bin++)
        {
            input.Add(new SignalPoint(
                bin * binWidth,
                bin == centerBin ? 20.0 : 0.0));
        }

        List<SignalPoint> output = DataHelper.LogarithmicResample(
            input,
            centerFrequency,
            centerFrequency * 2.0,
            64,
            smoothingOctaves: 1.0 / 6.0,
            psychoacoustic: true);

        // Without the two-bin resolution floor the Gaussian is a single-bin lookup here.
        Assert.InRange(output[0].Y, 1.0, 19.0);
    }

    [Theory]
    [InlineData(23.4375)] // 192 kHz / 8192: two bins ≈ 47 Hz, mid-display
    [InlineData(46.875)] // 192 kHz / 4096: two bins ≈ 94 Hz
    public void LogarithmicResample_PsychoacousticStaysLocalNearTwoBinsOnACoarseGrid(
        double stepHz)
    {
        // The Gaussian floor's lower radius diverged near two bins from DC and drew the midrange as a spike (field: +33 dB at 47 Hz).
        var input = new List<SignalPoint>();
        for (double f = stepHz; f <= 24_000; f += stepHz)
        {
            input.Add(new SignalPoint(f, f < 500 ? -50.0 : -15.0));
        }

        List<SignalPoint> psycho = DataHelper.LogarithmicResample(
            input, 20, 20_000, 1024,
            smoothingOctaves: 1.0 / 6.0,
            psychoacoustic: true);

        Assert.All(
            psycho.Where(point => point.X > 2.0 * stepHz && point.X < 3.5 * stepHz),
            point => Assert.True(point.Y < -45.0,
                $"{point.Y:0.##} dB at {point.X:0.##} Hz on a -50 dB floor."));
    }

    [Fact]
    public void LogarithmicPowerBandResample_PsychoacousticDoesNotClipANarrowDip()
    {
        // The RTA path pre-integrates 1/12 octave; psychoacoustic smoothing must keep a finite valley. Pink amplitude keeps the window untilted.
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
