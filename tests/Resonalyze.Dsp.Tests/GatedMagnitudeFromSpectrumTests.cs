using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The magnitude read from a spectrum somebody else gated must be the magnitude
/// the gated pair reads from the same gate: one resample, one calibration path,
/// one measured-band mask. The Virtual DSP direct-sound loss depends on it —
/// it reads the junction phase block's spectra and divides them by curves built
/// this way.
/// </summary>
public sealed class GatedMagnitudeFromSpectrumTests
{
    private const int SampleRate = 48_000;

    private static SyntheticMeasurement Reflected()
    {
        var impulse = new Complex[8_192];
        impulse[480] = 1.0;
        impulse[700] = 0.4;
        impulse[1_300] = -0.25;
        return new SyntheticMeasurement(impulse, SampleRate, 480)
        {
            LowestMeasuredFrequencyHz = 200,
            HighestMeasuredFrequencyHz = 8_000
        };
    }

    private static PhaseAnalysisSettings Fdw8() =>
        new(
            PhaseWindowMode.FrequencyDependent,
            8,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: 480 * 1_000.0 / SampleRate,
            LeftMs: 0.5,
            PlateauMs: 4.0,
            RightMs: 1.5,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

    [Fact]
    public void GatedMagnitude_FromAPrebuiltSpectrum_IsTheGatedPairsUnsmoothedCurve()
    {
        SyntheticMeasurement measurement = Reflected();
        PhaseAnalysisSettings settings = Fdw8();

        Complex[] spectrum = DataHelper.GetPhaseAnalysisSpectrum(measurement, settings, out _);
        AnalysisCurve fromSpectrum = DataHelper.GetGatedMagnitude(
            spectrum, SampleRate,
            measurement.LowestMeasuredFrequencyHz,
            measurement.HighestMeasuredFrequencyHz,
            calibration: null,
            smoothingInverseOctaves: 0);
        (_, AnalysisCurve unsmoothed) = DataHelper.GetGatedPrimarySpectrumPair(
            measurement, settings, calibration: null, smoothingInverseOctaves: 0);

        Assert.Equal(unsmoothed.Points.Count, fromSpectrum.Points.Count);
        int masked = 0;
        for (int i = 0; i < unsmoothed.Points.Count; i++)
        {
            Assert.Equal(unsmoothed.Points[i].X, fromSpectrum.Points[i].X);
            if (double.IsNaN(unsmoothed.Points[i].Y))
            {
                Assert.True(double.IsNaN(fromSpectrum.Points[i].Y));
                masked++;
                continue;
            }

            Assert.Equal(unsmoothed.Points[i].Y, fromSpectrum.Points[i].Y, 1e-9);
        }

        // The band mask took part: outside 200–8000 Hz the curve is broken.
        Assert.True(masked > 0);
    }

    [Fact]
    public void MeasuredSum_FromPrebuiltSpectra_IsTheSumTheMeasurementsGive()
    {
        SyntheticMeasurement first = Reflected();
        var second = new SyntheticMeasurement(first.ImpulseResponse!, SampleRate, 480)
        {
            LowestMeasuredFrequencyHz = 1_000,
            HighestMeasuredFrequencyHz = 16_000
        };
        PhaseAnalysisSettings settings = Fdw8();

        (_, AnalysisCurve fromMeasurements) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            [first, second], settings, [null, null], smoothingInverseOctaves: 0);
        (_, AnalysisCurve fromSpectra) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            [
                DataHelper.GetPhaseAnalysisSpectrum(first, settings, out _),
                DataHelper.GetPhaseAnalysisSpectrum(second, settings, out _)
            ],
            SampleRate,
            [(200, 8_000), (1_000, 16_000)],
            [null, null],
            smoothingInverseOctaves: 0);

        Assert.Equal(fromMeasurements.Points.Count, fromSpectra.Points.Count);
        for (int i = 0; i < fromMeasurements.Points.Count; i++)
        {
            Assert.Equal(fromMeasurements.Points[i].X, fromSpectra.Points[i].X);
            Assert.Equal(fromMeasurements.Points[i].Y, fromSpectra.Points[i].Y, 1e-9);
        }
    }
}
