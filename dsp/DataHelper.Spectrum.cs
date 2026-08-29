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
        /// An impulse response's UNGATED band levels on the shared spatial-average
        /// grid: the WHOLE record, no window and no gate, integrated as the band mean
        /// of POWER — the estimator an array's own positions are read with.
        /// </summary>
        /// <remarks>
        /// This is what a steady-state measurement of the same source reads, and it
        /// exists so a response can be compared against one. Two choices, both of them
        /// spelled out in <see cref="SpatialAverage.FromTransferMagnitude"/>, and both
        /// of them load-bearing here.
        /// <list type="bullet">
        /// <item>UNGATED, because the kernel it will be compared against carries the
        /// whole decay and a steady-state capture carries it too. A window leaves out
        /// the cabin's own decay, and a difference taken against a gated curve would
        /// read the missing energy as a disagreement between the measurements.</item>
        /// <item>The band mean of POWER, not the interpolating resampler. An ungated
        /// response carries every mode at full bin resolution, so sampling a handful of
        /// bins around each grid point reports whichever modal notch that point landed
        /// in — on a response with one 5 ms reflection the two estimators part by 11 dB
        /// at 500 Hz, which a difference against a capture would then spend as
        /// correction.</item>
        /// </list>
        /// The level is RELATIVE — the caller compares shapes, or levels it has
        /// levelled itself — and the result is raw band levels: smoothing and any
        /// calibration belong to the caller, in that order, as they do for a capture.
        /// </remarks>
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

        /// <summary>
        /// The primary (linear) response spectrum: windowed at the response's own
        /// start (fixed Tukey or FDW), oversampled, log-resampled with optional
        /// calibration and smoothing. Used
        /// by GetSpectrum for its primary curve and directly for derived responses
        /// (e.g. the complex sum of two transfer impulse responses), where the
        /// per-curve visibility gating of GetSpectrum must not apply.
        /// <para>
        /// <paramref name="anchorIndex"/> overrides where the window opens. A
        /// COMPOSITE record (a sum of arrivals) must pass the earliest of its
        /// parts' own starts: run on the mixed record, the start estimator reads
        /// the front of the record's dominant band, which a later, louder
        /// arrival can own — the same reason the Virtual DSP tool anchors its
        /// shared window at the min of per-channel starts
        /// (ProcessedChannels.SharedStartAnchorIndex) rather than estimating on
        /// the sum. Single records leave it null.
        /// </para>
        /// </summary>
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

        /// <summary>
        /// Breaks a finished curve where the response carries no measurement.
        /// </summary>
        /// <remarks>
        /// AFTER the smoothing, deliberately. Masking the oversampled spectrum that
        /// feeds it would let the smoothing window straddle the boundary in both
        /// directions: measured on a 1 kHz / 48 dB per octave corner, 29 bands below
        /// the limit survived on borrowed passband energy while 9 bands above it
        /// were lost to the NaN. On the output grid the break lands exactly where
        /// the filter put it.
        /// </remarks>
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

        /// <summary>
        /// The oversampled linear-frequency spectrum that feeds
        /// <see cref="GetPrimarySpectrum"/>: Tukey-windowed at the response start (or
        /// FDW-windowed, per <see cref="FrequencyResponseOptions.MagnitudeWindowMode"/>)
        /// and oversampled, BEFORE the logarithmic resample, calibration and smoothing.
        /// Overlays store this so they reproduce the mode's smoothing EXACTLY (the same
        /// <see cref="LogarithmicResample"/>) at any width, and Off = the raw curve.
        /// </summary>
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
            return GetOversampledSpectrumData(measurement, h1Start, window);
        }

        // Where the magnitude window opens: the response's estimated START, not
        // its peak. A driver's group delay puts the peak milliseconds behind the
        // front (the archived Passat woofer peaks 5.4 ms after its onset), so a
        // window whose fade-in ends at the peak starts AFTER the response has
        // begun and discards the direct arrival — with the left fade a couple of
        // milliseconds, entire octave bands misread by 10+ dB. The estimate is
        // memoized per IR array; the peak remains the fallback when the
        // estimator refuses the record.
        private static int MagnitudeAnchorIndex(IImpulseMeasurement measurement) =>
            measurement.ImpulseResponse is { Length: > 0 } impulseResponse
                ? TransferIrStartCache.ResolveStartIndex(
                    impulseResponse, measurement.SampleRate, measurement.PeakIndex)
                : measurement.PeakIndex;

        // REW-style frequency-dependent window for the magnitude curve, built on
        // the SAME bank the FDW phase analysis uses (BuildAnalysisSpectrum), so
        // the two views read one analysis and share its per-impulse cache. The
        // fixed window's geometry maps directly onto the gate: its fade-in ends
        // at the response start (MagnitudeAnchorIndex — the same anchor as the
        // fixed window's), so the gate offset is the start time, and the
        // configured window is the outer gate that FDW never exceeds — below
        // the transition frequency (where MagnitudeFdwCycles periods outgrow
        // the window) the curve is the fixed window's, above it the effective
        // window shrinks as cycles/frequency. Detrend/unwrap/smoothing fields
        // of the settings record are phase-only and never reach the bank.
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
            return Masked(
                ResampleGatedMagnitude(
                    GatedMagnitudePoints(spectrum, measurement.SampleRate),
                    calibration,
                    smoothingInverseOctaves),
                measurement.LowestMeasuredFrequencyHz,
                measurement.HighestMeasuredFrequencyHz);
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
            double lowest = measurement.LowestMeasuredFrequencyHz;
            double highest = measurement.HighestMeasuredFrequencyHz;
            // BOTH widths, including the one the summation loss divides: a channel
            // that measured nothing must contribute nothing there, and the loss is
            // told to skip what is not a number rather than to add it.
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

        /// <summary>
        /// The gated magnitude of a SUM of measured channels, at two smoothing widths,
        /// with each channel contributing only where it measured anything.
        /// </summary>
        /// <remarks>
        /// Summing the impulse responses and gating the total once is the same thing
        /// arithmetically — one shared window makes the transform linear, so the gated
        /// sum IS the sum of the gated spectra — but it is not the same thing
        /// honestly. A channel the sweep never excited below its corner carries an
        /// exactly zero spectrum there, and the window smears its in-band energy
        /// across the gap: measured on two brick-walled bands an octave apart, the
        /// total read 1.4 dB above the only channel that measured at 900 Hz and 2.5 dB
        /// above it at 990 Hz, falling to nothing an octave away. That is a summation
        /// GAIN the loudspeakers never produced, drawn exactly where a crossover is
        /// read most carefully — and the per-channel curves cannot show it, because
        /// each of them is broken there.
        /// <para>
        /// So each channel is gated first, its own unmeasured bins are cleared, and
        /// the phasors are added. Where NO channel measured the total comes out zero
        /// rather than as a level; the caller breaks those frequencies, which it must
        /// do anyway for a hole its channels' band edges cannot express.
        /// </para>
        /// <para>
        /// <paramref name="calibrations"/> is one correction per channel, because the
        /// pressure a microphone measured is the response TIMES its calibration and
        /// the sum is taken over the pressures: Σ HᵢCᵢ, not C·ΣHᵢ. The two agree
        /// exactly when one microphone measured everything, which is the ordinary
        /// case, and that case still applies the correction once at the end — where
        /// the per-channel curves apply theirs, so the summation loss that divides one
        /// by the other cancels it exactly. They part when the channels were measured
        /// through DIFFERENT microphones, and there the correction has to go inside
        /// the sum: a single one cannot undo two microphones, and leaving it out drew
        /// a raw total beside corrected channels, whose difference reads as summation
        /// loss and is not.
        /// </para>
        /// </remarks>
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

            // One microphone measured everything: keep the correction out of the sum
            // and let the resample apply it, exactly as before and exactly as the
            // channel curves do.
            bool shared = calibrations.All(
                entry => CalibrationFile.SameCurve(entry, calibrations[0]));
            CalibrationFile? calibration = shared ? calibrations[0] : null;

            Complex[]? total = null;
            int sampleRate = 0;
            for (int channel = 0; channel < channels.Count; channel++)
            {
                IImpulseMeasurement measurement = channels[channel];
                Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out _);
                total ??= new Complex[spectrum.Length];
                sampleRate = measurement.SampleRate;
                double lowest = measurement.LowestMeasuredFrequencyHz;
                double highest = measurement.HighestMeasuredFrequencyHz;
                // Null in the shared case, where the resample applies it instead.
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

        /// <summary>
        /// The summed magnitude of a channel set whose MAGNITUDE is taken from one
        /// measurement and whose PHASE is taken from another: each channel's gated
        /// complex spectrum is rescaled, bin by bin, to the level
        /// <paramref name="channels"/> supplies, and the rescaled phasors are added.
        /// </summary>
        /// <remarks>
        /// This exists for the Virtual DSP hybrid view, where the levels come from
        /// spatial averages (which hold no phase) and the phase can only come from
        /// the impulse responses. The obvious shortcut — add the supplied magnitudes
        /// as amplitudes and lay the impulse responses' own summation loss on top —
        /// is wrong wherever the two families disagree about the RELATIVE levels of
        /// the channels, because that loss is a property of the levels it was
        /// measured at. On a real car at a 1.6 kHz junction the disagreement reached
        /// 23 dB (a gate does not commute with a steep filter, so a stopband reads
        /// far above its analytic slope), and the borrowed loss drew a 13 dB dip into
        /// a sum whose own channels could not have produced more than 1.9 dB.
        /// <para>
        /// One window for every channel, the caller's: the sum of gated spectra is
        /// the gated sum only while they share it. A channel whose supplied level is
        /// NaN contributes nothing here — deciding whether that is a hole or a
        /// silence belongs to the caller, which knows what the channel was doing.
        /// </para>
        /// </remarks>
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

                    // The channel's own phase, at the level the other measurement
                    // says: a unit phasor times the substituted amplitude.
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

        // A level from an ascending (Hz, dB) curve, interpolated on the logarithmic
        // frequency axis it is sampled on. NaN outside the curve and wherever the
        // curve itself has none — a hole must not be bridged by its neighbours.
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

            // Snapped at the ends for the same reason the capture sampler is: NaN
            // times zero is NaN, so a point landing ON a level next to a hole would
            // come back as a hole itself.
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
