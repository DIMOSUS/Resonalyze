using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class LiveRtaRawCaptureTests
{
    private const int FftLength = 2048;
    private const int SampleRate = 48_000;

    [Fact]
    public void RelativeRawThenRender_ReproducesTheDrawnTrace()
    {
        double[] spectrum = CreateSpectrum();
        string calibrationPath = WriteCalibrationFile();

        try
        {
            var calibration = new CalibrationFile(calibrationPath);
            Assert.True(calibration.HasData);

            foreach (int smoothing in new[] { 0, 6, 24 })
            {
                // What the plot draws (PlotModelFactory.ResampleLiveSpectrumMagnitude):
                // bins to dB, then one resample that also applies the correction.
                List<SignalPoint> drawn = DataHelper.LogarithmicResample(
                    BuildBinCurve(spectrum),
                    RawCurveRenderer.StartFrequency,
                    RawCurveRenderer.StopFrequency,
                    RawCurveRenderer.PointCount,
                    calibration,
                    SpectrumSmoothing.SmoothingOctaves(smoothing),
                    psychoacoustic: SpectrumSmoothing.IsPsychoacoustic(smoothing));

                // What a captured overlay stores and re-renders: uncalibrated bins plus
                // the correction frozen on the output grid, applied after smoothing.
                List<SignalPoint> raw = LiveRtaRawCapture.BuildRelativeRaw(
                    spectrum, FftLength, SampleRate);
                List<SignalPoint> rendered = RawCurveRenderer.Render(
                    raw,
                    RawCurveRenderer.CaptureCalibrationCorrection(calibration),
                    smoothing);

                Assert.Equal(drawn.Count, rendered.Count);
                for (int i = 0; i < drawn.Count; i++)
                {
                    Assert.Equal(drawn[i].X, rendered[i].X);
                    Assert.Equal(drawn[i].Y, rendered[i].Y, precision: 9);
                }
            }
        }
        finally
        {
            File.Delete(calibrationPath);
        }
    }

    [Fact]
    public void RelativeRaw_IsTheUncalibratedBinSpectrumWithoutDc()
    {
        double[] spectrum = CreateSpectrum();

        List<SignalPoint> raw = LiveRtaRawCapture.BuildRelativeRaw(
            spectrum, FftLength, SampleRate);

        // Bin 0 is DC, which has no place on a logarithmic frequency axis.
        Assert.Equal((FftLength / 2) - 1, raw.Count);
        Assert.Equal((double)SampleRate / FftLength, raw[0].X, precision: 9);
        Assert.Equal(DataHelper.AmplitudeToDecibels(spectrum[1]), raw[0].Y, precision: 9);
        Assert.All(raw, point => Assert.True(point.X > 0));
    }

    [Fact]
    public void RelativeRaw_ReturnsNothingUsableWithoutAMeasurement()
    {
        Assert.Empty(LiveRtaRawCapture.BuildRelativeRaw([], 0, 0));
        Assert.Empty(LiveRtaRawCapture.BuildRelativeRaw(CreateSpectrum(), FftLength, 0));
    }

    // The band-power integrator the SPL RTA is drawn with places its grid over the range
    // where a whole band fits inside the resolved spectrum — NOT over the 20 Hz .. 20 kHz
    // display range. This is why an SPL capture stores no raw form: re-gridding its bands
    // onto the display range would hold the lowest one down to 20 Hz and invent a bass
    // tail that was never measured.
    [Fact]
    public void PowerBandGrid_DoesNotStartAtTheDisplayRange()
    {
        List<SignalPoint> bands = DataHelper.LogarithmicPowerBandResample(
            CreateSpectrum(),
            FftLength,
            SampleRate,
            windowEnbwBins: 1.5,
            windowMainLobeBins: 4.0,
            RawCurveRenderer.StartFrequency,
            RawCurveRenderer.StopFrequency,
            RawCurveRenderer.PointCount,
            SpectrumSmoothing.SmoothingOctaves(0));

        Assert.NotEmpty(bands);
        Assert.True(
            bands[0].X > RawCurveRenderer.StartFrequency * 2,
            $"expected the band grid to start well above the display range, got {bands[0].X} Hz");
    }

    // Mirrors ResampleLiveSpectrumMagnitude's point construction.
    private static List<SignalPoint> BuildBinCurve(double[] spectrum)
    {
        var points = new List<SignalPoint>();
        int binCount = Math.Min(FftLength / 2, spectrum.Length);
        for (int i = 1; i < binCount; i++)
        {
            points.Add(new SignalPoint(
                i * ((double)SampleRate / FftLength),
                DataHelper.AmplitudeToDecibels(spectrum[i])));
        }

        return points;
    }

    // A tilted, rippled spectrum, so smoothing actually changes the curve and a
    // mis-ordered reconstruction shows up instead of cancelling.
    private static double[] CreateSpectrum()
    {
        var spectrum = new double[(FftLength / 2) + 1];
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] = 0.01 + (0.001 * (i % 17));
        }

        return spectrum;
    }

    // A frequency-dependent correction: a constant one would hide a grid mismatch
    // between the stored samples and the stored correction.
    private static string WriteCalibrationFile()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"Resonalyze.Tests.calibration.{Guid.NewGuid():N}.txt");
        File.WriteAllLines(
            path,
            [
                "20 -4.0",
                "100 -1.5",
                "1000 0.0",
                "5000 1.25",
                "20000 3.5"
            ]);
        return path;
    }
}
