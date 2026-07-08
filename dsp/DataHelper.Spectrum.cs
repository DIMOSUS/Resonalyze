using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    public static partial class DataHelper
    {
        // Harmonic isolation geometry, expressed in harmonic-number space because
        // HarmonicIROffset(h) maps a (fractional) harmonic number to the IR index
        // where that distortion packet sits — the offset grows with h, so a larger
        // number is an earlier sample. An HDn packet is windowed from a hair above
        // its own harmonic (n + guard, the earliest edge) down to halfway toward
        // the next-lower harmonic (n - half), which brackets the packet while
        // excluding its neighbours.
        private const double HarmonicWindowUpperGuard = 0.03;
        private const double HarmonicWindowLowerReach = 0.5;

        // THD+N integrates everything from just below the fundamental (1.5, halfway
        // between the linear response and HD2) up past the fifth harmonic (5.5),
        // capturing HD2..HD5 plus the noise between them in one window.
        private const double ThdWindowLowerHarmonic = 1.5;
        private const double ThdWindowUpperHarmonic = 5.5;

        // Harmonic and THD curves are far noisier than the primary response, so
        // they are smoothed over twice the primary's fractional-octave width.
        private const double HarmonicSmoothingWidthFactor = 2.0;

        public static List<SignalPoint> GetSpectrumData(
            IImpulseMeasurement measurement,
            int start,
            int length,
            double[]? window = null)
        {
            Complex[] spectrum = ExtractWindow(measurement, start, length, window);
            Fourier.Forward(spectrum, FourierOptions.Matlab);

            var data = new List<SignalPoint>();
            for (int i = 1; i < length / 2; i++)
            {
                double frequency = i * (measurement.SampleRate / (double)length);
                data.Add(new SignalPoint(frequency, AmplitudeToDecibels(spectrum[i].Magnitude)));
            }

            return data;
        }

        /// <summary>
        /// The primary (linear) response spectrum: Tukey-windowed around the peak,
        /// oversampled, log-resampled with optional calibration and smoothing. Used
        /// by GetSpectrum for its primary curve and directly for derived responses
        /// (e.g. the complex sum of two transfer impulse responses), where the
        /// per-curve visibility gating of GetSpectrum must not apply.
        /// </summary>
        public static AnalysisCurve GetPrimarySpectrum(
            IImpulseMeasurement measurement,
            FrequencyResponseOptions frequencyResponseOptions,
            CalibrationFile? calibration)
        {
            double leftTukeyWindow = (double)frequencyResponseOptions.LeftTukeyWindow / frequencyResponseOptions.Window * 2.0;
            double rightTukeyWindow = (double)frequencyResponseOptions.RightTukeyWindow / frequencyResponseOptions.Window * 2.0;

            double[] window = Windowing.TukeyWindow(frequencyResponseOptions.Window, leftTukeyWindow, rightTukeyWindow);

            int h1Start = measurement.PeakIndex - frequencyResponseOptions.LeftTukeyWindow;

            var data = GetOversampledSpectrumData(measurement, h1Start, window);
            data = LogarithmicResample(
                data,
                20,
                20000,
                1024,
                frequencyResponseOptions.UseCalibration ? calibration : null,
                frequencyResponseOptions.SmoothingInverseOctaves > 0
                    ? 1.0 / frequencyResponseOptions.SmoothingInverseOctaves
                    : 0.0);
            return new AnalysisCurve("Frequency Response", data);
        }

        public static IReadOnlyList<AnalysisCurve> GetSpectrum(
            IImpulseMeasurement measurement,
            FrequencyResponseOptions frequencyResponseOptions,
            CalibrationFile? calibration,
            bool includePrimary = true,
            bool includeHarmonics = true)
        {
            var curves = new List<AnalysisCurve>();
            int peakIndex = measurement.PeakIndex;

            // Each curve is gated by the caller's computational scope
            // (includePrimary / includeHarmonics) AND the user's per-curve
            // visibility flag.
            bool wantHd2 = includeHarmonics && frequencyResponseOptions.ShowHd2;
            bool wantHd3 = includeHarmonics && frequencyResponseOptions.ShowHd3;
            bool wantHd4 = includeHarmonics && frequencyResponseOptions.ShowHd4;
            bool wantThd = includeHarmonics && frequencyResponseOptions.ShowThdPlusNoise;

            if (includePrimary && frequencyResponseOptions.ShowPrimary)
            {
                curves.Add(GetPrimarySpectrum(
                    measurement,
                    frequencyResponseOptions,
                    calibration));
            }

            if (!wantHd2 && !wantHd3 && !wantHd4 && !wantThd)
            {
                return curves;
            }

            for (int h = 2; h < 5; h++)
            {
                bool wanted = h switch
                {
                    2 => wantHd2,
                    3 => wantHd3,
                    _ => wantHd4
                };
                if (!wanted)
                {
                    continue;
                }

                int peak = peakIndex - (int)measurement.HarmonicIROffset(h);

                int hStart = peakIndex - (int)measurement.HarmonicIROffset(h + HarmonicWindowUpperGuard);
                int hEnd = peakIndex - (int)measurement.HarmonicIROffset(h - HarmonicWindowLowerReach);
                int hLength = hEnd - hStart;

                int leftOffset = peak - hStart;

                double leftTukeyWindow = (double)leftOffset / hLength * 2.0;
                double rightTukeyWindow = 0.5;
                double[] window = Windowing.TukeyWindow(hLength, leftTukeyWindow, rightTukeyWindow);

                var data = GetOversampledSpectrumData(measurement, hStart, window);
                data = LogarithmicResample(
                    data,
                    20,
                    20000,
                    1024,
                    frequencyResponseOptions.UseCalibration ? calibration : null,
                    frequencyResponseOptions.SmoothingInverseOctaves > 0
                        ? HarmonicSmoothingWidthFactor / frequencyResponseOptions.SmoothingInverseOctaves
                        : 0.0);
                curves.Add(new AnalysisCurve(
                    $"HD{h}",
                    data,
                    h switch
                    {
                        2 => AnalysisCurveKind.SecondHarmonic,
                        3 => AnalysisCurveKind.ThirdHarmonic,
                        _ => AnalysisCurveKind.FourthHarmonic
                    }));
            }

            if (wantThd)
            {
                int hStart = peakIndex - (int)measurement.HarmonicIROffset(ThdWindowUpperHarmonic);
                int hEnd = peakIndex - (int)measurement.HarmonicIROffset(ThdWindowLowerHarmonic);
                int hLength = hEnd - hStart;

                double leftTukeyWindow = 0.05;
                double rightTukeyWindow = 0.05;
                double[] window = Windowing.TukeyWindow(hLength, leftTukeyWindow, rightTukeyWindow);

                var data = GetOversampledSpectrumData(measurement, hStart, window);
                data = LogarithmicResample(
                    data,
                    20,
                    20000,
                    1024,
                    frequencyResponseOptions.UseCalibration ? calibration : null,
                    frequencyResponseOptions.SmoothingInverseOctaves > 0
                        ? HarmonicSmoothingWidthFactor / frequencyResponseOptions.SmoothingInverseOctaves
                        : 0.0);
                curves.Add(new AnalysisCurve(
                    "THD+N",
                    data,
                    AnalysisCurveKind.ThdPlusNoise));
            }

            return curves;
        }

        // Oversampling length shared by the spectrum, phase and minimum-phase
        // analyses. The finer linear grid keeps the logarithmic resample well-fed at
        // low frequencies and improves the cepstral minimum-phase reconstruction (see
        // GetMinimumPhase). Rounded up to a power of two for the fast radix-2 FFT.
        private static int GetOversampledLength(int length)
        {
            int target = Math.Clamp(length * 4, 4096, 32768);
            return Math.Max(length, DspMath.NextPowerOfTwo(target));
        }

        // Computes a magnitude spectrum from a windowed segment, zero-padded to the
        // shared oversampled length. The extraction start stays at the caller's
        // window; only a zero tail is appended, so the extra samples it spans add
        // nothing while the finer frequency grid sharpens the logarithmic resample.
        public static List<SignalPoint> GetOversampledSpectrumData(
            IImpulseMeasurement measurement,
            int start,
            double[] tukeyWindow)
        {
            int length = tukeyWindow.Length;
            int analysisLength = GetOversampledLength(length);
            double[] window = new double[analysisLength];
            Array.Copy(tukeyWindow, window, length);
            return GetSpectrumData(measurement, start, analysisLength, window);
        }
    }
}
