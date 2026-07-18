using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    public static partial class DataHelper
    {
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
            List<SignalPoint> data = LogarithmicResample(
                GetOversampledPrimarySpectrum(measurement, frequencyResponseOptions),
                20,
                20000,
                1024,
                frequencyResponseOptions.UseCalibration ? calibration : null,
                SpectrumSmoothing.SmoothingOctaves(
                    frequencyResponseOptions.SmoothingInverseOctaves),
                psychoacoustic: SpectrumSmoothing.IsPsychoacoustic(
                    frequencyResponseOptions.SmoothingInverseOctaves));
            return new AnalysisCurve("Frequency Response", data);
        }

        /// <summary>
        /// The oversampled linear-frequency spectrum that feeds
        /// <see cref="GetPrimarySpectrum"/>: Tukey-windowed around the peak and
        /// oversampled, BEFORE the logarithmic resample, calibration and smoothing.
        /// Overlays store this so they reproduce the mode's smoothing EXACTLY (the same
        /// <see cref="LogarithmicResample"/>) at any width, and Off = the raw curve.
        /// </summary>
        public static List<SignalPoint> GetOversampledPrimarySpectrum(
            IImpulseMeasurement measurement,
            FrequencyResponseOptions frequencyResponseOptions)
        {
            double leftTukeyWindow = (double)frequencyResponseOptions.LeftTukeyWindow / frequencyResponseOptions.Window * 2.0;
            double rightTukeyWindow = (double)frequencyResponseOptions.RightTukeyWindow / frequencyResponseOptions.Window * 2.0;
            double[] window = Windowing.TukeyWindow(frequencyResponseOptions.Window, leftTukeyWindow, rightTukeyWindow);
            int h1Start = measurement.PeakIndex - frequencyResponseOptions.LeftTukeyWindow;
            return GetOversampledSpectrumData(measurement, h1Start, window);
        }

        /// <summary>
        /// The primary (linear) response curve for the requested set. Only
        /// <see cref="SpectrumCurves.Primary"/> is honoured here; harmonic and THD
        /// curves are produced by <see cref="EssDistortion"/> from the sweep
        /// deconvolution, which carries the harmonic packets and normalizes every
        /// order against the same linear packet.
        /// </summary>
        public static IReadOnlyList<AnalysisCurve> GetSpectrum(
            IImpulseMeasurement measurement,
            FrequencyResponseOptions frequencyResponseOptions,
            CalibrationFile? calibration,
            SpectrumCurves curves)
        {
            var result = new List<AnalysisCurve>();
            if ((curves & SpectrumCurves.Primary) != 0)
            {
                result.Add(GetPrimarySpectrum(
                    measurement,
                    frequencyResponseOptions,
                    calibration));
            }

            return result;
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
