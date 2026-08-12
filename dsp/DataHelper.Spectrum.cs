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
        /// The primary (linear) response spectrum: windowed around the peak (fixed
        /// Tukey or FDW), oversampled, log-resampled with optional calibration and
        /// smoothing. Used
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
        /// <see cref="GetPrimarySpectrum"/>: Tukey-windowed around the peak (or
        /// FDW-windowed, per <see cref="FrequencyResponseOptions.MagnitudeWindowMode"/>)
        /// and oversampled, BEFORE the logarithmic resample, calibration and smoothing.
        /// Overlays store this so they reproduce the mode's smoothing EXACTLY (the same
        /// <see cref="LogarithmicResample"/>) at any width, and Off = the raw curve.
        /// </summary>
        public static List<SignalPoint> GetOversampledPrimarySpectrum(
            IImpulseMeasurement measurement,
            FrequencyResponseOptions frequencyResponseOptions)
        {
            if (frequencyResponseOptions.MagnitudeWindowMode ==
                PhaseWindowMode.FrequencyDependent)
            {
                return GetFdwPrimarySpectrum(measurement, frequencyResponseOptions);
            }

            double leftTukeyWindow = (double)frequencyResponseOptions.LeftTukeyWindow / frequencyResponseOptions.Window * 2.0;
            double rightTukeyWindow = (double)frequencyResponseOptions.RightTukeyWindow / frequencyResponseOptions.Window * 2.0;
            double[] window = Windowing.TukeyWindow(frequencyResponseOptions.Window, leftTukeyWindow, rightTukeyWindow);
            int h1Start = measurement.PeakIndex - frequencyResponseOptions.LeftTukeyWindow;
            return GetOversampledSpectrumData(measurement, h1Start, window);
        }

        // REW-style frequency-dependent window for the magnitude curve, built on
        // the SAME bank the FDW phase analysis uses (BuildAnalysisSpectrum), so
        // the two views read one analysis and share its per-impulse cache. The
        // fixed window's geometry maps directly onto the gate: its fade-in ends
        // at the peak, so the gate offset is the peak time, and the configured
        // window is the outer gate that FDW never exceeds — below the transition
        // frequency (where MagnitudeFdwCycles periods outgrow the window) the
        // curve is the fixed window's, above it the effective window shrinks as
        // cycles/frequency. Detrend/unwrap/smoothing fields of the settings
        // record are phase-only and never reach the bank.
        private static List<SignalPoint> GetFdwPrimarySpectrum(
            IImpulseMeasurement measurement,
            FrequencyResponseOptions options)
        {
            double toMilliseconds = 1000.0 / measurement.SampleRate;
            int plateau = Math.Max(
                0, options.Window - options.LeftTukeyWindow - options.RightTukeyWindow);
            var settings = new PhaseAnalysisSettings(
                PhaseWindowMode.FrequencyDependent,
                options.MagnitudeFdwCycles,
                PhaseDetrendMode.Off,
                ManualDetrendMilliseconds: 0.0,
                GateOffsetMs: measurement.PeakIndex * toMilliseconds,
                LeftMs: options.LeftTukeyWindow * toMilliseconds,
                PlateauMs: plateau * toMilliseconds,
                RightMs: options.RightTukeyWindow * toMilliseconds,
                Unwrap: false,
                SmoothingInverseOctaves: 0.0);
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out _);
            return GatedMagnitudePoints(spectrum, measurement.SampleRate);
        }

        /// <summary>
        /// The primary magnitude curve computed through the SAME gate
        /// construction as the phase analyses (<see cref="PhaseAnalysisSettings"/>:
        /// absolute ms offset and shoulders, Fixed or FDW) — for callers whose
        /// magnitude must read exactly the time window their phase view shows
        /// (the Virtual DSP tool). Shares the gated-spectrum bank and its
        /// per-impulse cache; the settings' detrend/unwrap/smoothing fields are
        /// phase-only and never affect the magnitude. Smoothing and calibration
        /// apply on the logarithmic grid exactly as in
        /// <see cref="GetPrimarySpectrum"/>.
        /// </summary>
        public static AnalysisCurve GetGatedPrimarySpectrum(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            CalibrationFile? calibration,
            double smoothingInverseOctaves)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out _);
            return ResampleGatedMagnitude(
                GatedMagnitudePoints(spectrum, measurement.SampleRate),
                calibration,
                smoothingInverseOctaves);
        }

        /// <summary>
        /// <see cref="GetGatedPrimarySpectrum"/> at two smoothing widths from ONE
        /// gate and one FFT: the display curve and the unsmoothed curve. A caller
        /// that both draws a curve and divides it into another one (the Virtual DSP
        /// summation loss) needs both, and the gated FFT — not the resample — is
        /// what costs; see <see cref="VirtualCrossoverAnalysis.SumLossCurve"/> for
        /// why the division must read the unsmoothed pair.
        /// </summary>
        public static (AnalysisCurve Display, AnalysisCurve Unsmoothed)
            GetGatedPrimarySpectrumPair(
                IImpulseMeasurement measurement,
                PhaseAnalysisSettings settings,
                CalibrationFile? calibration,
                double smoothingInverseOctaves)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out _);
            List<SignalPoint> bins = GatedMagnitudePoints(spectrum, measurement.SampleRate);
            AnalysisCurve unsmoothed = ResampleGatedMagnitude(bins, calibration, 0);
            return (
                smoothingInverseOctaves == 0
                    ? unsmoothed
                    : ResampleGatedMagnitude(bins, calibration, smoothingInverseOctaves),
                unsmoothed);
        }

        private static AnalysisCurve ResampleGatedMagnitude(
            List<SignalPoint> bins,
            CalibrationFile? calibration,
            double smoothingInverseOctaves) =>
            new(
                "Frequency Response",
                LogarithmicResample(
                    bins,
                    20,
                    20000,
                    1024,
                    calibration,
                    SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves),
                    psychoacoustic: SpectrumSmoothing.IsPsychoacoustic(
                        smoothingInverseOctaves)));

        // The magnitude bins of a gated analysis spectrum as ascending (Hz, dB)
        // points on its linear grid — the resample-ready form every gated
        // magnitude path shares.
        private static List<SignalPoint> GatedMagnitudePoints(
            Complex[] spectrum,
            int sampleRate)
        {
            var data = new List<SignalPoint>(spectrum.Length / 2);
            for (int i = 1; i < spectrum.Length / 2; i++)
            {
                double frequency = i * (sampleRate / (double)spectrum.Length);
                data.Add(new SignalPoint(
                    frequency,
                    AmplitudeToDecibels(spectrum[i].Magnitude)));
            }

            return data;
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
