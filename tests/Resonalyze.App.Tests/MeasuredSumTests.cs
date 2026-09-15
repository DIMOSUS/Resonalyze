using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Gating the summed IR as a whole smears a channel's in-band energy into another's unmeasured (zero) bins,
/// reading above the only measured channel near the edge; each channel's unmeasured bins must be cleared.
/// </summary>
public sealed class MeasuredSumTests
{
    private const int SampleRate = 48_000;
    private const int Length = 16_384;
    private const int Arrival = 200;

    private static Complex[] BandLimited(double lowHz, double highHz)
    {
        var spectrum = new Complex[Length];
        double binHz = (double)SampleRate / Length;
        for (int bin = 1; bin < Length / 2; bin++)
        {
            double hz = bin * binHz;
            if (hz < lowHz || hz > highHz)
            {
                continue;
            }

            double phase = -2.0 * Math.PI * bin * Arrival / Length;
            var value = new Complex(Math.Cos(phase), Math.Sin(phase));
            spectrum[bin] = value;
            spectrum[Length - bin] = Complex.Conjugate(value);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return spectrum;
    }

    private static PhaseAnalysisSettings Gate() =>
        new(
            PhaseWindowMode.Fixed,
            PhaseAnalysisSettings.DefaultFdwCycles,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: Arrival * 1000.0 / SampleRate,
            FrequencyResponseOptions.SteadyStateLeftMs,
            FrequencyResponseOptions.SteadyStatePlateauMs,
            FrequencyResponseOptions.SteadyStateRightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

    private static ImpulseMeasurementView View(Complex[] ir, double lowHz, double highHz) =>
        new(ir, Arrival, SampleRate)
        {
            LowestMeasuredFrequencyHz = lowHz,
            HighestMeasuredFrequencyHz = highHz
        };

    private static double At(IReadOnlyList<SignalPoint> curve, double hz)
    {
        SignalPoint best = curve[0];
        foreach (SignalPoint point in curve)
        {
            if (Math.Abs(Math.Log(point.X / hz)) < Math.Abs(Math.Log(best.X / hz)))
            {
                best = point;
            }
        }

        return best.Y;
    }

    private static CalibrationFile Flat(double correctionDb) =>
        CalibrationFile.FromPoints(
            [
                new CalibrationPoint(20.0, correctionDb),
                new CalibrationPoint(20_000.0, correctionDb)
            ],
            $"flat {correctionDb:0.##}");

    [Fact]
    public void EachChannelsOwnCorrectionGoesINTOTheSum()
    {
        // Corrected mic 6.02 dB hot: total is 1.5x, not 2x. One correction outside the sum cannot undo two mics
        // (the 2.5 dB showed as summation gain above the loss curve's 0 dB ceiling).
        Complex[] response = BandLimited(20, 20_000);
        double halved = 20.0 * Math.Log10(2.0);

        (_, AnalysisCurve sum) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            [View(response, 20, 20_000), View(response, 20, 20_000)],
            Gate(),
            calibrations: [null, Flat(halved)],
            smoothingInverseOctaves: 0.0);
        (_, AnalysisCurve alone) = DataHelper.GetGatedPrimarySpectrumPair(
            View(response, 20, 20_000), Gate(), calibration: null, 0.0);

        foreach (double hz in new[] { 1_000.0, 4_000.0, 8_000.0 })
        {
            Assert.Equal(
                At(alone.Points, hz) + 20.0 * Math.Log10(1.5),
                At(sum.Points, hz),
                3);
        }
    }

    [Fact]
    public void OneMicrophoneForEveryChannelIsStillCorrectedOnce()
    {
        // One curve per channel: the correction commutes with the sum and the loss cancels it exactly.
        Complex[] response = BandLimited(20, 20_000);
        CalibrationFile shared = Flat(3.0);

        (_, AnalysisCurve corrected) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            [View(response, 20, 20_000), View(response, 20, 20_000)],
            Gate(),
            calibrations: [shared, shared],
            smoothingInverseOctaves: 0.0);
        (_, AnalysisCurve raw) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            [View(response, 20, 20_000), View(response, 20, 20_000)],
            Gate(),
            calibrations: [null, null],
            smoothingInverseOctaves: 0.0);

        foreach (double hz in new[] { 200.0, 1_000.0, 8_000.0 })
        {
            Assert.Equal(At(raw.Points, hz) - 3.0, At(corrected.Points, hz), 6);
        }
    }

    [Fact]
    public void WhereOnlyOneChannelMeasured_TheSumIsThatChannel()
    {
        Complex[] low = BandLimited(20, 500);
        Complex[] high = BandLimited(1_000, 20_000);

        (AnalysisCurve sum, _) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            [View(low, 20, 500), View(high, 1_000, 20_000)],
            Gate(),
            calibrations: [null, null],
            smoothingInverseOctaves: 0.0);
        (AnalysisCurve lowAlone, _) = DataHelper.GetGatedPrimarySpectrumPair(
            View(low, 20, 500), Gate(), calibration: null, smoothingInverseOctaves: 0.0);
        (AnalysisCurve highAlone, _) = DataHelper.GetGatedPrimarySpectrumPair(
            View(high, 1_000, 20_000), Gate(), calibration: null, smoothingInverseOctaves: 0.0);

        foreach (double hz in new[] { 100.0, 300.0, 450.0, 490.0 })
        {
            Assert.Equal(At(lowAlone.Points, hz), At(sum.Points, hz), 6);
        }

        foreach (double hz in new[] { 1_200.0, 5_000.0, 15_000.0 })
        {
            Assert.Equal(At(highAlone.Points, hz), At(sum.Points, hz), 6);
        }
    }

    [Fact]
    public void WhereBothMeasured_TheSumIsStillTheirSum()
    {
        Complex[] first = BandLimited(200, 5_000);
        Complex[] second = BandLimited(200, 5_000);

        (AnalysisCurve sum, _) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            [View(first, 200, 5_000), View(second, 200, 5_000)],
            Gate(),
            calibrations: [null, null],
            smoothingInverseOctaves: 0.0);
        (AnalysisCurve alone, _) = DataHelper.GetGatedPrimarySpectrumPair(
            View(first, 200, 5_000), Gate(), calibration: null, smoothingInverseOctaves: 0.0);

        foreach (double hz in new[] { 400.0, 1_000.0, 3_000.0 })
        {
            Assert.Equal(
                At(alone.Points, hz) + 20.0 * Math.Log10(2.0),
                At(sum.Points, hz),
                6);
        }
    }

    [Fact]
    public void TheOldTotalCarriedTheNeighboursLeakage()
    {
        // Why: gating the summed response reads above the measuring channel in overlapping bands.
        Complex[] mid = BandLimited(100, 3_000);
        Complex[] high = BandLimited(1_000, 20_000);
        var summed = new Complex[Length];
        for (int i = 0; i < Length; i++)
        {
            summed[i] = mid[i] + high[i];
        }

        (AnalysisCurve total, _) = DataHelper.GetGatedPrimarySpectrumPair(
            new ImpulseMeasurementView(summed, Arrival, SampleRate),
            Gate(),
            calibration: null,
            smoothingInverseOctaves: 0.0);
        (AnalysisCurve midAlone, _) = DataHelper.GetGatedPrimarySpectrumPair(
            View(mid, 100, 3_000), Gate(), calibration: null, smoothingInverseOctaves: 0.0);
        (AnalysisCurve masked, _) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            [View(mid, 100, 3_000), View(high, 1_000, 20_000)],
            Gate(),
            calibrations: [null, null],
            smoothingInverseOctaves: 0.0);

        // Measured: 1.4 dB at 900 Hz and 2.5 dB at 990 Hz.
        Assert.True(
            At(total.Points, 990.0) - At(midAlone.Points, 990.0) > 1.0,
            "the phantom this exists to remove was more than a decibel");
        Assert.Equal(At(midAlone.Points, 990.0), At(masked.Points, 990.0), 6);
        Assert.Equal(At(midAlone.Points, 500.0), At(masked.Points, 500.0), 6);
    }
}
