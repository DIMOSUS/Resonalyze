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
            double[]? window = null,
            bool wrapPreRoll = false)
        {
            Complex[] spectrum = ExtractWindow(measurement, start, length, window, wrapPreRoll: wrapPreRoll);
            Fourier.Forward(spectrum, FourierOptions.Matlab);

            var data = new List<SignalPoint>();
            for (int i = 1; i < length / 2; i++)
            {
                double frequency = i * (measurement.SampleRate / (double)length);
                data.Add(new SignalPoint(frequency, AmplitudeToDecibels(spectrum[i].Magnitude)));
            }

            return data;
        }

        /// <summary>Ungated band levels (whole record, band mean of power) on the spatial-average grid; relative, unsmoothed, uncalibrated. See docs/tech/phase-and-group-delay.md#ungated-band-levels.</summary>
        public static double[] GetUngatedBandLevels(IImpulseMeasurement measurement)
        {
            ArgumentNullException.ThrowIfNull(measurement);
            Complex[] response = measurement.ImpulseResponse
                ?? throw new InvalidOperationException("Impulse response is not available.");
            int length = response.Length;
            if (length < 4 || measurement.SampleRate <= 0)
            {
                return [];
            }

            var spectrum = new Complex[length];
            Array.Copy(response, spectrum, length);
            Fourier.Forward(spectrum, FourierOptions.Matlab);

            var magnitude = new double[length / 2 + 1];
            for (int bin = 0; bin < magnitude.Length; bin++)
            {
                magnitude[bin] = spectrum[bin].Magnitude;
            }

            return SpatialAverage.FromTransferMagnitude(
                magnitude, measurement.SampleRate / (double)length);
        }

        /// <summary>Primary magnitude spectrum windowed at the response start (Tukey or FDW), log-resampled, calibrated and smoothed, without GetSpectrum's visibility gating.</summary>
        /// <remarks>A composite record must pass <paramref name="anchorIndex"/> = the earliest part's start. See docs/tech/phase-and-group-delay.md#magnitude-window-anchor.</remarks>
        public static AnalysisCurve GetPrimarySpectrum(
            IImpulseMeasurement measurement,
            FrequencyResponseOptions frequencyResponseOptions,
            CalibrationFile? calibration,
            int? anchorIndex = null)
        {
            List<SignalPoint> data = LogarithmicResample(
                GetOversampledPrimarySpectrum(
                    measurement, frequencyResponseOptions, anchorIndex),
                20,
                20000,
                1024,
                frequencyResponseOptions.UseCalibration ? calibration : null,
                SpectrumSmoothing.SmoothingOctaves(
                    frequencyResponseOptions.SmoothingInverseOctaves),
                psychoacoustic: SpectrumSmoothing.IsPsychoacoustic(
                    frequencyResponseOptions.SmoothingInverseOctaves));
            return new AnalysisCurve(
                "Frequency Response",
                MaskUnmeasuredBands(
                    data,
                    measurement.LowestMeasuredFrequencyHz,
                    measurement.HighestMeasuredFrequencyHz));
        }

        /// <summary>Breaks a finished curve where nothing was measured; applied AFTER smoothing. See docs/tech/phase-and-group-delay.md#measured-band-mask.</summary>
        private static AnalysisCurve Masked(
            AnalysisCurve curve,
            double lowestMeasuredFrequencyHz,
            double highestMeasuredFrequencyHz) =>
            !(lowestMeasuredFrequencyHz > 0.0) &&
                double.IsPositiveInfinity(highestMeasuredFrequencyHz)
                ? curve
                : curve with
                {
                    Points = MaskUnmeasuredBands(
                        [.. curve.Points],
                        lowestMeasuredFrequencyHz,
                        highestMeasuredFrequencyHz)
                };

        private static List<SignalPoint> MaskUnmeasuredBands(
            List<SignalPoint> data,
            double lowestMeasuredFrequencyHz,
            double highestMeasuredFrequencyHz)
        {
            bool maskBelow = lowestMeasuredFrequencyHz > 0.0 &&
                double.IsFinite(lowestMeasuredFrequencyHz);
            bool maskAbove = highestMeasuredFrequencyHz > 0.0 &&
                double.IsFinite(highestMeasuredFrequencyHz);
            if (!maskBelow && !maskAbove)
            {
                return data;
            }

            for (int i = 0; i < data.Count; i++)
            {
                if ((maskBelow && data[i].X < lowestMeasuredFrequencyHz) ||
                    (maskAbove && data[i].X > highestMeasuredFrequencyHz))
                {
                    data[i] = new SignalPoint(data[i].X, double.NaN);
                }
            }

            return data;
        }

        /// <summary>The oversampled linear spectrum before log resample, calibration and smoothing; overlays store it to reproduce smoothing exactly.</summary>
        public static List<SignalPoint> GetOversampledPrimarySpectrum(
            IImpulseMeasurement measurement,
            FrequencyResponseOptions frequencyResponseOptions,
            int? anchorIndex = null)
        {
            int anchor = anchorIndex ?? MagnitudeAnchorIndex(measurement);
            if (frequencyResponseOptions.MagnitudeWindowMode ==
                PhaseWindowMode.FrequencyDependent)
            {
                return GetFdwPrimarySpectrum(
                    measurement, frequencyResponseOptions, anchor);
            }

            double leftTukeyWindow = (double)frequencyResponseOptions.LeftTukeyWindow / frequencyResponseOptions.Window * 2.0;
            double rightTukeyWindow = (double)frequencyResponseOptions.RightTukeyWindow / frequencyResponseOptions.Window * 2.0;
            double[] window = Windowing.TukeyWindow(frequencyResponseOptions.Window, leftTukeyWindow, rightTukeyWindow);
            int h1Start = anchor - frequencyResponseOptions.LeftTukeyWindow;
            // A transfer IR is circular: a response starting inside the left fade has its pre-roll at the record's end,
            // as the FDW, phase and group-delay gates read it. Read as zeros, it lifts the bass 0.3-0.4 dB at a 1.25 ms
            // arrival.
            return GetOversampledSpectrumData(measurement, h1Start, window, wrapPreRoll: true);
        }

        /// <summary>Where a magnitude window's fade-in ends: the response START, not the peak, which a driver's group delay
        /// delays. See docs/tech/phase-and-group-delay.md#magnitude-window-anchor.</summary>
        public static int MagnitudeAnchorIndex(IImpulseMeasurement measurement) =>
            measurement.ImpulseResponse is { Length: > 0 } impulseResponse
                ? TransferIrStartCache.ResolveStartIndex(
                    impulseResponse, measurement.SampleRate, measurement.PeakIndex)
                : measurement.PeakIndex;

        // FDW magnitude on the phase analysis bank (shared cache); the configured window is the outer gate. See docs/tech/phase-and-group-delay.md#fdw-magnitude.
        private static List<SignalPoint> GetFdwPrimarySpectrum(
            IImpulseMeasurement measurement,
            FrequencyResponseOptions options,
            int anchorIndex)
        {
            double toMilliseconds = 1000.0 / measurement.SampleRate;
            int plateau = Math.Max(
                0, options.Window - options.LeftTukeyWindow - options.RightTukeyWindow);
            var settings = new PhaseAnalysisSettings(
                PhaseWindowMode.FrequencyDependent,
                options.MagnitudeFdwCycles,
                PhaseDetrendMode.Off,
                ManualDetrendMilliseconds: 0.0,
                GateOffsetMs: anchorIndex * toMilliseconds,
                LeftMs: options.LeftTukeyWindow * toMilliseconds,
                PlateauMs: plateau * toMilliseconds,
                RightMs: options.RightTukeyWindow * toMilliseconds,
                Unwrap: false,
                SmoothingInverseOctaves: 0.0);
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out _);
            return GatedMagnitudePoints(spectrum, measurement.SampleRate);
        }

        /// <summary>Primary magnitude through the phase analyses' gate construction, so it reads the window the phase view shows; phase-only settings fields are ignored.</summary>
        public static AnalysisCurve GetGatedPrimarySpectrum(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            CalibrationFile? calibration,
            double smoothingInverseOctaves)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out _);
            return Masked(
                ResampleGatedMagnitude(
                    GatedMagnitudePoints(spectrum, measurement.SampleRate),
                    calibration,
                    smoothingInverseOctaves),
                measurement.LowestMeasuredFrequencyHz,
                measurement.HighestMeasuredFrequencyHz);
        }

        /// <summary>Display and unsmoothed curves from one gated FFT; the summation loss divides the unsmoothed pair (see <see cref="VirtualCrossoverAnalysis.SumLossCurve"/>).</summary>
        public static (AnalysisCurve Display, AnalysisCurve Unsmoothed)
            GetGatedPrimarySpectrumPair(
                IImpulseMeasurement measurement,
                PhaseAnalysisSettings settings,
                CalibrationFile? calibration,
                double smoothingInverseOctaves)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out _);
            List<SignalPoint> bins = GatedMagnitudePoints(spectrum, measurement.SampleRate);
            double lowest = measurement.LowestMeasuredFrequencyHz;
            double highest = measurement.HighestMeasuredFrequencyHz;
            // Mask both widths: an unmeasured channel must contribute NaN, which the loss skips.
            AnalysisCurve unsmoothed = Masked(
                ResampleGatedMagnitude(bins, calibration, 0), lowest, highest);
            return (
                smoothingInverseOctaves == 0
                    ? unsmoothed
                    : Masked(
                        ResampleGatedMagnitude(bins, calibration, smoothingInverseOctaves),
                        lowest,
                        highest),
                unsmoothed);
        }

        /// <summary>Magnitude of a caller-built gated spectrum (full complex FFT at <paramref name="sampleRate"/>), with the same resample, calibration, smoothing and mask.</summary>
        public static AnalysisCurve GetGatedMagnitude(
            Complex[] spectrum,
            int sampleRate,
            double lowestMeasuredFrequencyHz,
            double highestMeasuredFrequencyHz,
            CalibrationFile? calibration,
            double smoothingInverseOctaves)
        {
            ArgumentNullException.ThrowIfNull(spectrum);
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            return Masked(
                ResampleGatedMagnitude(
                    GatedMagnitudePoints(spectrum, sampleRate),
                    calibration,
                    smoothingInverseOctaves),
                lowestMeasuredFrequencyHz,
                highestMeasuredFrequencyHz);
        }

        /// <summary>Gated magnitude of a sum of channels, each contributing only where it measured; per-channel calibration when microphones differ. See docs/tech/phase-and-group-delay.md#measured-sums.</summary>
        public static (AnalysisCurve Display, AnalysisCurve Unsmoothed)
            GetGatedMeasuredMagnitudeSumPair(
                IReadOnlyList<IImpulseMeasurement> channels,
                PhaseAnalysisSettings settings,
                IReadOnlyList<CalibrationFile?> calibrations,
                double smoothingInverseOctaves)
        {
            ArgumentNullException.ThrowIfNull(channels);
            ArgumentNullException.ThrowIfNull(calibrations);
            if (channels.Count != calibrations.Count)
            {
                throw new ArgumentException(
                    "Every channel needs its own calibration entry.",
                    nameof(calibrations));
            }
            if (channels.Count == 0)
            {
                AnalysisCurve empty = new(string.Empty, []);
                return (empty, empty);
            }

            var spectra = new List<Complex[]>(channels.Count);
            var bands = new List<(double LowestHz, double HighestHz)>(channels.Count);
            int sampleRate = 0;
            foreach (IImpulseMeasurement measurement in channels)
            {
                spectra.Add(BuildAnalysisSpectrum(measurement, settings, out _));
                bands.Add((
                    measurement.LowestMeasuredFrequencyHz,
                    measurement.HighestMeasuredFrequencyHz));
                sampleRate = measurement.SampleRate;
            }

            return GetGatedMeasuredMagnitudeSumPair(
                spectra, sampleRate, bands, calibrations, smoothingInverseOctaves);
        }

        /// <summary>The measured sum from spectra already gated in one time frame, with each channel's measured band.</summary>
        public static (AnalysisCurve Display, AnalysisCurve Unsmoothed)
            GetGatedMeasuredMagnitudeSumPair(
                IReadOnlyList<Complex[]> spectra,
                int sampleRate,
                IReadOnlyList<(double LowestHz, double HighestHz)> measuredBands,
                IReadOnlyList<CalibrationFile?> calibrations,
                double smoothingInverseOctaves)
        {
            ArgumentNullException.ThrowIfNull(spectra);
            ArgumentNullException.ThrowIfNull(measuredBands);
            ArgumentNullException.ThrowIfNull(calibrations);
            if (spectra.Count != calibrations.Count || spectra.Count != measuredBands.Count)
            {
                throw new ArgumentException(
                    "Every spectrum needs its own measured band and calibration entry.",
                    nameof(spectra));
            }
            if (spectra.Count == 0)
            {
                AnalysisCurve empty = new(string.Empty, []);
                return (empty, empty);
            }

            // One microphone: calibration applied once by the resample, like the channel curves, so the loss cancels it.
            bool shared = calibrations.All(
                entry => CalibrationFile.SameCurve(entry, calibrations[0]));
            CalibrationFile? calibration = shared ? calibrations[0] : null;

            Complex[]? total = null;
            for (int channel = 0; channel < spectra.Count; channel++)
            {
                Complex[] spectrum = spectra[channel];
                total ??= new Complex[spectrum.Length];
                (double lowest, double highest) = measuredBands[channel];
                CalibrationFile? own = shared ? null : calibrations[channel];
                int usable = Math.Min(total.Length, spectrum.Length);
                for (int i = 1; i < usable / 2; i++)
                {
                    double frequency = i * (sampleRate / (double)spectrum.Length);
                    if ((lowest > 0.0 && frequency < lowest) ||
                        (double.IsFinite(highest) && highest > 0.0 && frequency > highest))
                    {
                        continue;
                    }

                    total[i] += own == null
                        ? spectrum[i]
                        : spectrum[i] * DecibelsToAmplitude(
                            -own.GetDecibelCorrection(frequency));
                }
            }

            if (total == null || sampleRate <= 0)
            {
                AnalysisCurve empty = new(string.Empty, []);
                return (empty, empty);
            }

            var bins = new List<SignalPoint>(total.Length / 2);
            for (int i = 1; i < total.Length / 2; i++)
            {
                bins.Add(new SignalPoint(
                    i * (sampleRate / (double)total.Length),
                    AmplitudeToDecibels(total[i].Magnitude)));
            }

            AnalysisCurve unsmoothed = ResampleGatedMagnitude(bins, calibration, 0);
            return (
                smoothingInverseOctaves == 0
                    ? unsmoothed
                    : ResampleGatedMagnitude(bins, calibration, smoothingInverseOctaves),
                unsmoothed);
        }

        /// <summary>Sum with MAGNITUDE from <paramref name="channels"/> and PHASE from each gated spectrum; one shared window. See docs/tech/phase-and-group-delay.md#substituted-magnitude-sum.</summary>
        public static List<SignalPoint> GetGatedSubstitutedMagnitudeSum(
            IReadOnlyList<(IImpulseMeasurement Measurement,
                IReadOnlyList<SignalPoint> MagnitudeDb)> channels,
            PhaseAnalysisSettings settings,
            double smoothingInverseOctaves)
        {
            ArgumentNullException.ThrowIfNull(channels);
            if (channels.Count == 0)
            {
                return [];
            }

            Complex[]? total = null;
            int sampleRate = 0;
            foreach ((IImpulseMeasurement measurement,
                IReadOnlyList<SignalPoint> magnitudeDb) in channels)
            {
                Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out _);
                total ??= new Complex[spectrum.Length];
                sampleRate = measurement.SampleRate;
                int usable = Math.Min(total.Length, spectrum.Length);
                for (int i = 1; i < usable / 2; i++)
                {
                    double magnitude = spectrum[i].Magnitude;
                    if (magnitude <= 0)
                    {
                        continue;
                    }

                    double frequency = i * (sampleRate / (double)spectrum.Length);
                    double levelDb = InterpolateLevelDb(magnitudeDb, frequency);
                    if (!double.IsFinite(levelDb))
                    {
                        continue;
                    }

                    total[i] += spectrum[i] / magnitude * DecibelsToAmplitude(levelDb);
                }
            }

            if (total == null || sampleRate <= 0)
            {
                return [];
            }

            var bins = new List<SignalPoint>(total.Length / 2);
            for (int i = 1; i < total.Length / 2; i++)
            {
                bins.Add(new SignalPoint(
                    i * (sampleRate / (double)total.Length),
                    AmplitudeToDecibels(total[i].Magnitude)));
            }

            return LogarithmicResample(
                bins,
                20,
                20000,
                1024,
                calibration: null,
                SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves),
                psychoacoustic: SpectrumSmoothing.IsPsychoacoustic(smoothingInverseOctaves));
        }

        // Log-frequency interpolation; NaN outside the curve and at holes (never bridged).
        private static double InterpolateLevelDb(
            IReadOnlyList<SignalPoint> curve, double frequency)
        {
            if (curve.Count == 0 || frequency <= 0 ||
                frequency < curve[0].X || frequency > curve[^1].X)
            {
                return double.NaN;
            }

            int low = 0;
            int high = curve.Count - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (curve[middle].X <= frequency)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            double span = Math.Log(curve[high].X / curve[low].X);
            if (span <= 0)
            {
                return curve[low].Y;
            }

            // Snap at the ends: NaN times zero is NaN, so a point on a level next to a hole would read as a hole.
            double fraction = Math.Log(frequency / curve[low].X) / span;
            const double SnapTolerance = 1e-9;
            if (fraction <= SnapTolerance)
            {
                return curve[low].Y;
            }

            return fraction >= 1.0 - SnapTolerance
                ? curve[high].Y
                : curve[low].Y + (curve[high].Y - curve[low].Y) * fraction;
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

        /// <summary>Only <see cref="SpectrumCurves.Primary"/> is honoured; harmonics come from <see cref="EssDistortion"/>.</summary>
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

        // Finer grid feeds the log resample at LF and the cepstral minimum phase; power of two for the radix-2 FFT.
        private static int GetOversampledLength(int length)
        {
            int target = Math.Clamp(length * 4, 4096, 32768);
            return Math.Max(length, DspMath.NextPowerOfTwo(target));
        }

        // Only a zero tail is appended: the window stays put, the grid gets finer.
        /// <param name="wrapPreRoll">Read indices before the record from its end (a transfer IR's pre-roll).</param>
        public static List<SignalPoint> GetOversampledSpectrumData(
            IImpulseMeasurement measurement,
            int start,
            double[] tukeyWindow,
            bool wrapPreRoll = false)
        {
            int length = tukeyWindow.Length;
            int analysisLength = GetOversampledLength(length);
            double[] window = new double[analysisLength];
            Array.Copy(tukeyWindow, window, length);
            return GetSpectrumData(measurement, start, analysisLength, window, wrapPreRoll);
        }
    }
}
