using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// What a SUM may contain where one of its channels measured nothing.
/// </summary>
/// <remarks>
/// Summing the impulse responses and gating the total once is arithmetically the
/// same as gating each and adding the spectra — one shared window makes the
/// transform linear — and that is exactly the problem. A channel the sweep never
/// excited below its corner carries an exactly zero spectrum there, and the window
/// smears its in-band energy across the gap. The total then reads above the only
/// channel that measured, by a decibel and more within half an octave of the edge,
/// and nothing on the plot can show it: the channel whose leakage it is has its own
/// curve broken exactly there, so the summation loss divides a total carrying the
/// phantom by operands that do not.
/// <para>
/// It is the same defect the whole measured-band idea exists for, one level up:
/// smooth, plausible, and drawn where a crossover is read most carefully.
/// </para>
/// </remarks>
public sealed class MeasuredSumTests
{
    private const int SampleRate = 48_000;
    private const int Length = 16_384;
    private const int Arrival = 200;

    // A response whose spectrum is exactly zero outside its band — what the
    // excitation gate leaves behind, and what the protective high-pass compensation
    // refuses to invert.
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
        // Two identical arrivals, one of them measured through a microphone that
        // reads 6.02 dB hot. Corrected, that channel contributes HALF the amplitude,
        // so the total is 1.5x one raw channel rather than 2x.
        //
        // With one correction applied OUTSIDE the sum there is nothing a single
        // correction could be — one subtraction cannot undo two microphones — so the
        // total was left raw at 2x while both channel curves were drawn corrected.
        // The 2.5 dB between them is drawn as summation gain the loudspeakers never
        // produced, above the loss curve's own 0 dB ceiling, and it feeds the average
        // and minimum loss read-outs the tuner acts on.
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
        // The ordinary case has to stay exactly what it was: with one curve for every
        // channel the correction commutes with the sum, so it is applied where the
        // channel curves apply theirs — after the resample — and the summation loss
        // that divides one by the other cancels it exactly.
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

        // Inside the low channel's band the high channel measured nothing, so the sum
        // is the low channel and only the low channel — including a quarter-octave
        // from the edge, which is where its neighbour's leakage was largest.
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
        // The guard above must not have been bought by refusing to add anything.
        Complex[] first = BandLimited(200, 5_000);
        Complex[] second = BandLimited(200, 5_000);

        (AnalysisCurve sum, _) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            [View(first, 200, 5_000), View(second, 200, 5_000)],
            Gate(),
            calibrations: [null, null],
            smoothingInverseOctaves: 0.0);
        (AnalysisCurve alone, _) = DataHelper.GetGatedPrimarySpectrumPair(
            View(first, 200, 5_000), Gate(), calibration: null, smoothingInverseOctaves: 0.0);

        // Two identical arrivals in phase: 6 dB, the whole point of a summed view.
        foreach (double hz in new[] { 400.0, 1_000.0, 3_000.0 })
        {
            // 20·log10(2), not "about six": doubling an amplitude is 6.0206 dB, and
            // the sum is exact enough that the difference shows.
            Assert.Equal(
                At(alone.Points, hz) + 20.0 * Math.Log10(2.0),
                At(sum.Points, hz),
                6);
        }
    }

    [Fact]
    public void TheOldTotalCarriedTheNeighboursLeakage()
    {
        // The measurement behind the two tests above, kept so the reason survives the
        // fix: gating the SUMMED response instead reads above the only channel that
        // measured, and the closer to its neighbour's edge the worse.
        // Bands that OVERLAP, which is the ordinary crossover and where the phantom
        // lives: just under the tweeter's corner the midrange is still measuring, so
        // nothing blanks the frequency and the leakage is drawn as a summation gain.
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

        // Measured: 1.4 dB at 900 Hz and 2.5 dB at 990, falling to nothing an octave
        // down. The builder that clears each channel's own unmeasured bins leaves it
        // at zero.
        Assert.True(
            At(total.Points, 990.0) - At(midAlone.Points, 990.0) > 1.0,
            "the phantom this exists to remove was more than a decibel");
        Assert.Equal(At(midAlone.Points, 990.0), At(masked.Points, 990.0), 6);
        Assert.Equal(At(midAlone.Points, 500.0), At(masked.Points, 500.0), 6);
    }
}
