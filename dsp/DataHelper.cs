using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    public enum MicrophoneCalibrationMode
    {
        Off,
        Degrees0,
        Degrees90
    }

    public sealed class FrequencyResponseOptions
    {
        public int Window { get; set; } = 4096;
        public int LeftTukeyWindow { get; set; } = 256;
        public int RightTukeyWindow { get; set; } = 256;
        public double SmoothingInverseOctaves { get; set; } = 6;
        public int Offset { get; set; }
        public bool Unwrap { get; set; } = true;
        public MicrophoneCalibrationMode CalibrationMode { get; set; } =
            MicrophoneCalibrationMode.Degrees0;

        public bool UseCalibration
        {
            get => CalibrationMode != MicrophoneCalibrationMode.Off;
            set => CalibrationMode = value
                ? MicrophoneCalibrationMode.Degrees0
                : MicrophoneCalibrationMode.Off;
        }
        public bool ShowCoherence { get; set; } = true;

        // Phase-mode curve visibility. Ignored by the other modes that reuse this
        // options type.
        public bool ShowMeasuredPhase { get; set; } = true;
        public bool ShowMinimumPhase { get; set; } = true;
        public bool ShowExcessPhase { get; set; } = true;

        // Phase-mode windowing (milliseconds): the Tukey gate is left + plateau + right
        // with the peak at the fade-in/plateau boundary. PhaseDetrendMs is the τ used
        // to detrend the excess phase (absolute reference). Phase mode uses these
        // instead of Window/LeftTukeyWindow/RightTukeyWindow/Offset.
        // Single source of truth for the phase-mode defaults. Tune these to taste;
        // they drive the first-run values, the settings-file fallback and the "R"
        // reset buttons.
        public const double DefaultPhaseGateOffsetMs = 0.0;
        public const double DefaultPhaseLeftMs = 0.5;
        public const double DefaultPhasePlateauMs = 4.0;
        public const double DefaultPhaseRightMs = 1.5;
        public const double DefaultPhaseDetrendMs = 0.0;
        public const double DefaultPhaseSmoothingInverseOctaves = 12.0;

        public double PhaseGateOffsetMs { get; set; } = DefaultPhaseGateOffsetMs;
        public double PhaseLeftMs { get; set; } = DefaultPhaseLeftMs;
        public double PhasePlateauMs { get; set; } = DefaultPhasePlateauMs;
        public double PhaseRightMs { get; set; } = DefaultPhaseRightMs;
        public double PhaseDetrendMs { get; set; } = DefaultPhaseDetrendMs;

        // Single source of truth for the group-delay gate defaults (ms). Group delay is
        // usually viewed a bit lower than the phase crossover region, so the gate is
        // slightly wider than the phase default.
        public const double DefaultGroupDelayGateOffsetMs = 0.0;
        public const double DefaultGroupDelayLeftMs = 0.5;
        public const double DefaultGroupDelayPlateauMs = 10.0;
        public const double DefaultGroupDelayRightMs = 3.0;
        public const double DefaultGroupDelaySmoothingInverseOctaves = 12.0;

        public double GroupDelayGateOffsetMs { get; set; } = DefaultGroupDelayGateOffsetMs;
        public double GroupDelayLeftMs { get; set; } = DefaultGroupDelayLeftMs;
        public double GroupDelayPlateauMs { get; set; } = DefaultGroupDelayPlateauMs;
        public double GroupDelayRightMs { get; set; } = DefaultGroupDelayRightMs;

        // The lowest frequency the gated window can resolve (~one period inside the
        // gate). Driven purely by the gate duration, not the sample rate or FFT size.
        public static double GateMinReliableFrequencyHz(
            double leftMs,
            double plateauMs,
            double rightMs)
        {
            double gateMs = leftMs + plateauMs + rightMs;
            return gateMs > 0.0 ? 1000.0 / gateMs : 0.0;
        }

        // Frequency-response curve visibility. Ignored by the other modes that reuse
        // this options type.
        public bool ShowPrimary { get; set; } = true;
        public bool ShowHd2 { get; set; } = true;
        public bool ShowHd3 { get; set; } = true;
        public bool ShowHd4 { get; set; } = true;
        public bool ShowThdPlusNoise { get; set; } = true;

        // Group-delay-mode curve visibility. Ignored by the other modes that reuse
        // this options type.
        public bool ShowGroupDelay { get; set; } = true;
    }

    public sealed class ImpulseResponseOptions
    {
        public int Length { get; set; } = 4096;
        public bool Logarithmic { get; set; }

        // Curve visibility. Impulse Response and Autocorrelation modes share this
        // options type but read their own flag.
        public bool ShowImpulse { get; set; } = true;
        public bool ShowAutocorrelation { get; set; } = true;
    }

    /// <summary>
    /// Converts measured impulse responses into frequency-domain and time-domain plot data.
    /// </summary>
    public static class DataHelper
    {
        private const double MinimumAmplitude = 1e-8;

        public static double AmplitudeToDecibels(double amplitude)
        {
            return 20.0 * Math.Log10(Math.Max(amplitude, MinimumAmplitude));
        }

        public static double DecibelsToAmplitude(double decibels)
        {
            return Math.Pow(10.0, decibels / 20.0);
        }

        public static Complex[] ExtractWindow(
            IImpulseMeasurement measurement,
            int start,
            int length,
            double[]? window = null,
            bool wrap = false)
        {
            ArgumentNullException.ThrowIfNull(measurement);
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            Complex[] source = measurement.ImpulseResponse
                ?? throw new InvalidOperationException("Impulse response is not available.");
            Complex[] result = new Complex[length];

            for (int i = 0; i < length; i++)
            {
                int sourceIndex = start + i;
                if (wrap)
                {
                    sourceIndex %= source.Length;
                    if (sourceIndex < 0)
                    {
                        sourceIndex += source.Length;
                    }
                }

                if ((uint)sourceIndex < (uint)source.Length)
                {
                    result[i] = source[sourceIndex] *
                        (window is { Length: > 0 } && i < window.Length ? window[i] : 1.0);
                }
            }

            return result;
        }

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

                int hStart = peakIndex - (int)measurement.HarmonicIROffset(h + 0.03);
                int hEnd = peakIndex - (int)measurement.HarmonicIROffset(h - 0.5);
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
                        ? 2.0 / frequencyResponseOptions.SmoothingInverseOctaves
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
                int hStart = peakIndex - (int)measurement.HarmonicIROffset(5.5);
                int hEnd = peakIndex - (int)measurement.HarmonicIROffset(1.5);
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
                        ? 2.0 / frequencyResponseOptions.SmoothingInverseOctaves
                        : 0.0);
                curves.Add(new AnalysisCurve(
                    "THD+N",
                    data,
                    AnalysisCurveKind.ThdPlusNoise));
            }

            return curves;
        }

        public static List<SignalPoint> GetPhaseData(
            IImpulseMeasurement measurement,
            int offset,
            int length,
            double[] window,
            bool unwrap,
            IReadOnlyList<double>? coherence = null)
        {
            Complex[] spectrum = ExtractWindow(
                measurement,
                measurement.PeakIndex + offset,
                length,
                window);
            Fourier.Forward(spectrum, FourierOptions.Matlab);

            // The extraction starts `offset` samples past the peak; a reference of 0
            // makes BuildMeasuredPhase compensate exactly that offset, so the phase
            // reads as if referenced to the peak.
            return BuildMeasuredPhase(
                spectrum,
                extractionStart: offset,
                referenceSamples: 0,
                measurement.SampleRate,
                unwrap,
                coherence);
        }

        // Fixed analysis length for the gated phase / group-delay FFTs. The gate
        // (left + plateau + right) is specified in time and zero-padded to this length,
        // so the frequency grid is constant regardless of the gate and identical across
        // measurements.
        public const int GatedFftLength = 32768;

        private static int MillisecondsToSamples(double milliseconds, int sampleRate) =>
            (int)Math.Round(Math.Max(0.0, milliseconds) * sampleRate / 1000.0);

        // Builds a zero-padded, gated windowed impulse (time domain). The Tukey gate
        // spans left + plateau + right samples; the end of its left shoulder (the
        // fade-in/plateau boundary) is placed at gateOffsetMs from the IR start. Wrap
        // handles a gate that runs into negative indices. Shared by phase and GD.
        private static Complex[] ExtractGatedWindowedImpulse(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            bool wrap,
            out int extractionStart)
        {
            int sampleRate = measurement.SampleRate;
            int gateOffset = MillisecondsToSamples(gateOffsetMs, sampleRate);
            int left = MillisecondsToSamples(leftMs, sampleRate);
            int plateau = MillisecondsToSamples(plateauMs, sampleRate);
            int right = MillisecondsToSamples(rightMs, sampleRate);

            int gate = Math.Clamp(left + plateau + right, 1, GatedFftLength);
            // Keep the fades coherent if the clamp had to trim the gate.
            left = Math.Min(left, gate);
            right = Math.Min(right, gate - left);

            double leftNorm = (double)left / gate * 2.0;
            double rightNorm = (double)right / gate * 2.0;
            double[] tukey = Windowing.TukeyWindow(gate, leftNorm, rightNorm);

            double[] window = new double[GatedFftLength];
            Array.Copy(tukey, window, gate);

            // Left shoulder ends at the gate offset, so extraction starts a shoulder
            // earlier; the time correction downstream keeps readings absolute.
            extractionStart = gateOffset - left;
            return ExtractWindow(measurement, extractionStart, GatedFftLength, window, wrap);
        }

        // Gated windowed spectrum for phase analysis (the impulse FFT'd in place).
        private static Complex[] BuildPhaseSpectrum(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            out int extractionStart)
        {
            Complex[] spectrum = ExtractGatedWindowedImpulse(
                measurement,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                wrap: false,
                out extractionStart);
            Fourier.Forward(spectrum, FourierOptions.Matlab);
            return spectrum;
        }

        // Reliability gates for the unwrapped phase: bins this far below the peak
        // magnitude — or with squared coherence below the floor, when coherence is
        // available — still contribute their phase to the output, but never anchor
        // the unwrap, so one noisy or masked bin cannot shift the whole tail by 2π.
        // The magnitude gate matches the group-delay validity gate; γ² = 0.5 is the
        // point where less than half the measured energy is coherent with the
        // reference and the per-bin phase variance makes branch choices unsafe.
        private const double UnwrapMagnitudeGateDb = -30.0;
        private const double UnwrapCoherenceFloor = 0.5;
        // Blend factor for the running dφ/df estimate used to predict the next
        // bin's phase; the smoothing keeps a single jittery-but-reliable bin from
        // steering the branch choice for the bins that follow.
        private const double UnwrapSlopeBlend = 0.25;

        // Measured phase (radians) referenced to an absolute sample, for bins
        // 1..n/2-1. The reference is shared across measurements (common origin), so
        // setting it equal across two captures preserves their relative phase; setting
        // it to a measurement's own arrival flattens that curve.
        //
        // Unwrapping is anchored to reliable bins: each bin takes the 2π branch
        // closest to the phase predicted from the last reliable anchor and a running
        // slope estimate. Unreliable bins (nulls, noise-floor, low coherence) get a
        // branch too, but never become anchors, so the unwrap bridges them instead
        // of accumulating their phase noise into the tail. With every bin reliable
        // and a zero slope this reduces to the classic nearest-to-previous choice.
        private static List<SignalPoint> BuildMeasuredPhase(
            Complex[] spectrum,
            int extractionStart,
            double referenceSamples,
            int sampleRate,
            bool unwrap,
            IReadOnlyList<double>? coherence = null)
        {
            int n = spectrum.Length;
            double referenceShift = referenceSamples - extractionStart;
            var data = new List<SignalPoint>(n / 2);

            const double minFrequency = 100;

            double maxMagnitude = 0.0;
            if (unwrap)
            {
                for (int i = 1; i < n / 2; i++)
                {
                    maxMagnitude = Math.Max(maxMagnitude, spectrum[i].Magnitude);
                }
            }
            double minReliableMagnitude =
                maxMagnitude * Math.Pow(10.0, UnwrapMagnitudeGateDb / 20.0);

            bool hasAnchor = false;
            bool hasSlope = false;
            double anchorFrequency = 0.0;
            double anchorPhase = 0.0;
            double slope = 0.0; // rad per Hz

            for (int i = 1; i < n / 2; i++)
            {
                double f = i * sampleRate / (double)n;

                // Re-reference the segment phase to the absolute reference sample.
                double referenced = spectrum[i].Phase + Math.Tau * i * referenceShift / n;
                double wrapped = Math.Atan2(Math.Sin(referenced), Math.Cos(referenced));

                if (!unwrap)
                {
                    data.Add(new SignalPoint(f, wrapped));
                    continue;
                }

                bool reliable = spectrum[i].Magnitude >= minReliableMagnitude &&
                    (coherence == null ||
                     CoherenceAt(coherence, f, sampleRate) >= UnwrapCoherenceFloor);

                if (f < minFrequency || !hasAnchor)
                {
                    // Below the unwrap floor the output stays wrapped (unchanged
                    // behavior); a running bin seeds the anchor only when it
                    // passes the same reliability gate, so a garbage bin near the
                    // floor cannot offset the first unwrapped branch by 2π.
                    data.Add(new SignalPoint(f, wrapped));
                    if (reliable)
                    {
                        anchorFrequency = f;
                        anchorPhase = wrapped;
                        hasAnchor = true;
                    }
                    continue;
                }

                double predicted = anchorPhase + slope * (f - anchorFrequency);
                double branch = Math.Round((predicted - wrapped) / Math.Tau);
                double unwrappedPhase = wrapped + Math.Tau * branch;
                if (reliable && f > anchorFrequency)
                {
                    double localSlope =
                        (unwrappedPhase - anchorPhase) / (f - anchorFrequency);
                    slope = hasSlope
                        ? UnwrapSlopeBlend * localSlope + (1.0 - UnwrapSlopeBlend) * slope
                        : localSlope;
                    hasSlope = true;
                    anchorFrequency = f;
                    anchorPhase = unwrappedPhase;
                }

                data.Add(new SignalPoint(f, unwrappedPhase));
            }

            return data;
        }

        // Squared coherence at a frequency, linearly interpolated from an array
        // covering 0..Nyquist in uniform bins (length fftLength/2 + 1 — the layout
        // produced by TransferFunction.ComputeAveragedRelativeIr), so the coherence
        // grid does not need to match the phase FFT grid. Degenerate inputs count
        // as trusted, mirroring how the plots treat missing coherence coverage.
        private static double CoherenceAt(
            IReadOnlyList<double> coherence,
            double frequency,
            int sampleRate)
        {
            if (coherence.Count < 2 || sampleRate <= 0)
            {
                return 1.0;
            }

            double position = frequency * (coherence.Count - 1) * 2.0 / sampleRate;
            if (position <= 0.0)
            {
                return coherence[0];
            }
            if (position >= coherence.Count - 1)
            {
                return coherence[coherence.Count - 1];
            }

            int index = (int)Math.Floor(position);
            double fraction = position - index;
            return coherence[index] +
                (coherence[index + 1] - coherence[index]) * fraction;
        }

        /// <summary>
        /// Wrapped or unwrapped gated phase (radians) using the same gate
        /// construction as <see cref="GetPhase"/>, referenced to an absolute
        /// sample position (fractional samples allowed, so a τ reference is
        /// not limited to whole samples). For callers that render their own
        /// phase view (the Virtual DSP tool) without drifting from the Phase
        /// mode's gating.
        /// </summary>
        public static List<SignalPoint> GetGatedPhaseData(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            double referenceSamples,
            bool unwrap,
            IReadOnlyList<double>? coherence = null)
        {
            Complex[] spectrum = BuildPhaseSpectrum(
                measurement,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                out int extractionStart);
            return BuildMeasuredPhase(
                spectrum,
                extractionStart,
                referenceSamples,
                measurement.SampleRate,
                unwrap,
                coherence);
        }

        public static AnalysisCurve GetPhase(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            double detrendMilliseconds,
            double smoothingInverseOctaves,
            bool unwrap,
            IReadOnlyList<double>? coherence = null)
        {
            List<SignalPoint> phase = GetGatedPhaseData(
                measurement,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                detrendMilliseconds * measurement.SampleRate / 1000.0,
                unwrap,
                coherence);

            List<SignalPoint> data = new(phase.Count);
            foreach (SignalPoint point in phase)
            {
                data.Add(new SignalPoint(point.X, point.Y / Math.PI * 180.0));
            }

            return new AnalysisCurve(
                "Phase",
                smoothingInverseOctaves > 0
                    ? SmoothLinear(data, 1.0 / smoothingInverseOctaves)
                    : data);
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

        /// <summary>
        /// Computes the minimum-phase response derived from the windowed magnitude
        /// spectrum. Unlike <see cref="GetPhase"/> this contains no excess (delay or
        /// reflection) component, so it shows the phase that remains after a perfect
        /// minimum-phase equalization of the magnitude.
        /// </summary>
        public static AnalysisCurve GetMinimumPhase(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            double smoothingInverseOctaves)
        {
            Complex[] spectrum = BuildPhaseSpectrum(
                measurement,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                out _);

            int n = spectrum.Length;
            double[] magnitude = new double[n];
            for (int i = 0; i < n; i++)
            {
                magnitude[i] = spectrum[i].Magnitude;
            }

            // The minimum phase depends only on the magnitude (Bode relation); it is
            // the magnitude-derived reference and is not affected by the τ detrend.
            double[] minimumPhase = MinimumPhase.FromMagnitude(magnitude);

            List<SignalPoint> data = new(n / 2);
            for (int i = 1; i < n / 2; i++)
            {
                double f = i * measurement.SampleRate / (double)n;
                data.Add(new SignalPoint(f, minimumPhase[i] / Math.PI * 180.0));
            }

            return new AnalysisCurve(
                "Minimum Phase",
                smoothingInverseOctaves > 0
                    ? SmoothLinear(data, 1.0 / smoothingInverseOctaves)
                    : data,
                AnalysisCurveKind.MinimumPhase);
        }

        /// <summary>
        /// Computes the excess phase: measured phase minus minimum phase. This is the
        /// all-pass component (pure delay plus reflections) that a minimum-phase
        /// equalizer cannot correct. The measured part is always taken unwrapped so
        /// the difference is continuous.
        /// </summary>
        public static AnalysisCurve GetExcessPhase(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            double detrendMilliseconds,
            double smoothingInverseOctaves,
            IReadOnlyList<double>? coherence = null)
        {
            Complex[] spectrum = BuildPhaseSpectrum(
                measurement,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                out int extractionStart);

            double referenceSamples =
                detrendMilliseconds * measurement.SampleRate / 1000.0;

            // Measured phase (always unwrapped so the difference is continuous) and the
            // minimum phase share the same grid; the τ detrend rides on the measured
            // part, so the excess inherits it.
            List<SignalPoint> measured = BuildMeasuredPhase(
                spectrum,
                extractionStart,
                referenceSamples,
                measurement.SampleRate,
                unwrap: true,
                coherence);

            int n = spectrum.Length;
            double[] magnitude = new double[n];
            for (int i = 0; i < n; i++)
            {
                magnitude[i] = spectrum[i].Magnitude;
            }
            double[] minimumPhase = MinimumPhase.FromMagnitude(magnitude);

            // BuildMeasuredPhase and the minimum-phase array both start at bin 1, so the
            // measured point at index j corresponds to bin j + 1.
            List<SignalPoint> data = new(measured.Count);
            for (int j = 0; j < measured.Count; j++)
            {
                double excess = measured[j].Y - minimumPhase[j + 1];
                data.Add(new SignalPoint(measured[j].X, excess / Math.PI * 180.0));
            }

            return new AnalysisCurve(
                "Excess Phase",
                smoothingInverseOctaves > 0
                    ? SmoothLinear(data, 1.0 / smoothingInverseOctaves)
                    : data,
                AnalysisCurveKind.ExcessPhase);
        }

        /// <summary>
        /// Estimates the τ (in milliseconds) that flattens the excess phase, using the
        /// same window as the displayed curves. Returns both the energy-weighted
        /// average (slope) and the dominant-arrival (peak) estimates. The values are
        /// absolute (referenced to IR sample 0), so the same value can be entered on a
        /// second measurement to compare their relative phase.
        /// </summary>
        public static (double SlopeMilliseconds, double PeakMilliseconds) EstimatePhaseDetrend(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs)
        {
            Complex[] spectrum = BuildPhaseSpectrum(
                measurement,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                out int extractionStart);

            ExcessDelayResult result = ExcessDelay.Estimate(
                spectrum,
                measurement.SampleRate);

            // ExcessDelay reports the delay relative to the window start; add the
            // extraction start to get an absolute sample, then convert to ms.
            double toMilliseconds = 1000.0 / measurement.SampleRate;
            double slopeMs = (extractionStart + result.SlopeDelaySamples) * toMilliseconds;
            double peakMs = (extractionStart + result.PeakDelaySamples) * toMilliseconds;
            return (slopeMs, peakMs);
        }

        public static AnalysisCurve GetImpulse(
            IImpulseMeasurement measurement,
            ImpulseResponseOptions opt)
        {
            int offset = 512;
            int start = measurement.PeakIndex - offset;

            int length = offset + opt.Length;
            Complex[] impulse = ExtractWindow(measurement, start, length);
            return RenderImpulseCurve(impulse, length, -offset, opt.Logarithmic);
        }

        // Impulse from the very start of the response (sample 0) up to peakIndex plus
        // opt.Length, with the X coordinate in absolute samples. Used when a transfer IR
        // is available, so the onset, the peak and the decay tail are all visible on a
        // single absolute-sample timeline.
        public static AnalysisCurve GetImpulseFromStart(
            IImpulseMeasurement measurement,
            ImpulseResponseOptions opt)
        {
            int available = measurement.ImpulseResponse?.Length ?? 0;
            int length = Math.Clamp(
                measurement.PeakIndex + opt.Length,
                1,
                Math.Max(1, available));
            Complex[] impulse = ExtractWindow(measurement, 0, length);
            return RenderImpulseCurve(impulse, length, 0, opt.Logarithmic);
        }

        private static AnalysisCurve RenderImpulseCurve(
            Complex[] impulse,
            int length,
            int xOffset,
            bool logarithmic)
        {
            List<SignalPoint> data = new(length);

            if (logarithmic)
            {
                // Show the impulse in dB relative to its own peak (peak = 0 dB). The absolute
                // sample scale depends on the recording level and the deconvolution gain, so an
                // absolute dB floor can sit above the whole curve and collapse it to a flat line.
                double peakMagnitude = 0;
                for (int i = 0; i < length; i++)
                {
                    peakMagnitude = Math.Max(peakMagnitude, impulse[i].Magnitude);
                }

                double reference = peakMagnitude > 0 ? peakMagnitude : 1.0;
                for (int i = 0; i < length; i++)
                {
                    data.Add(new SignalPoint(
                        i + xOffset,
                        AmplitudeToDecibels(impulse[i].Magnitude / reference)));
                }
            }
            else
            {
                for (int i = 0; i < length; i++)
                {
                    data.Add(new SignalPoint(i + xOffset, impulse[i].Real));
                }
            }

            return new AnalysisCurve("Impulse Response", data);
        }

        public static AnalysisCurve GetAutocorrelation(
            IImpulseMeasurement measurement,
            ImpulseResponseOptions opt)
        {
            int offset = 64;
            int length = 2048;
            const double timeWindowMilliseconds = 3.0;

            int start = measurement.PeakIndex - offset;
            Complex[] impulse = ExtractWindow(measurement, start, length);

            double mean = 0;
            for (int i = 0; i < length; i++)
            {
                mean += impulse[i].Real;
            }
            mean /= length;

            // Linear (non-circular) autocorrelation by Wiener-Khinchin: zero-pad the
            // mean-removed signal to twice its length so lags cannot wrap, then
            // FFT -> power spectrum -> inverse FFT. O(n log n) instead of the direct
            // O(n^2)-per-lag sum this replaced.
            int fftLength = DspMath.NextPowerOfTwo(length * 2);
            var spectrum = new Complex[fftLength];
            for (int i = 0; i < length; i++)
            {
                spectrum[i] = new Complex(impulse[i].Real - mean, 0.0);
            }

            Fourier.Forward(spectrum, FourierOptions.Matlab);
            for (int i = 0; i < fftLength; i++)
            {
                spectrum[i] = new Complex(
                    spectrum[i].Real * spectrum[i].Real +
                    spectrum[i].Imaginary * spectrum[i].Imaginary,
                    0.0);
            }
            Fourier.Inverse(spectrum, FourierOptions.Matlab);

            // Lag 0 is the signal's energy — the normalization denominator.
            double denominator = spectrum[0].Real;
            var correlation = new double[length];
            for (int k = 0; k < length; k++)
            {
                correlation[k] = spectrum[k].Real;
            }

            List<SignalPoint> data = new();
            for (int k = 0; k < length; k++)
            {
                if (k / (double)measurement.SampleRate * 1000.0 > timeWindowMilliseconds)
                {
                    break;
                }

                // Sub-sample interpolation avoids the stair-step shape of integer-lag
                // autocorrelation. Correlation is linear in the shifted signal, so
                // interpolating the correlation equals the interpolate-then-correlate
                // it replaced, at a fraction of the cost.
                for (int step = 0; step < 10; step++)
                {
                    double position = k + step * 0.1;
                    double timeMs = position / measurement.SampleRate * 1000.0;
                    double value = denominator > 1e-30
                        ? InterpolateCorrelation(correlation, position) / denominator
                        : 0;
                    data.Add(new SignalPoint(timeMs, value));
                }
            }

            return new AnalysisCurve("Autocorrelation", data);
        }

        // Normalized 4-tap Lanczos read of the correlation at a fractional lag.
        private static double InterpolateCorrelation(double[] correlation, double position)
        {
            int center = (int)Math.Floor(position);
            double weightSum = 0;
            double weightedSum = 0;
            for (int l = -1; l <= 2; l++)
            {
                int index = center + l;
                if ((uint)index >= (uint)correlation.Length)
                {
                    continue;
                }

                double weight = DspMath.LanczosKernel(position - index, 2.0);
                weightedSum += correlation[index] * weight;
                weightSum += weight;
            }

            return weightSum > 1e-12
                ? weightedSum / weightSum
                : correlation[Math.Clamp(center, 0, correlation.Length - 1)];
        }

        public static AnalysisCurve GetGroupDelay(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            double smoothingInverseOctaves,
            double magnitudeGateDb = -30.0)
        {
            // Same gate as the phase mode: the left shoulder ends at the gate offset.
            // Wrap handles a gate that runs into negative indices and must read the
            // cyclic tail; the time correction downstream keeps the delay absolute.
            Complex[] windowedImpulse = ExtractGatedWindowedImpulse(
                measurement,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                wrap: true,
                out int extractionStart);

            int n = windowedImpulse.Length;
            Complex[] spectrum = new Complex[n];
            Complex[] timeWeightedSpectrum = new Complex[n];

            double invSampleRate = 1.0 / measurement.SampleRate;

            for (int i = 0; i < n; i++)
            {
                Complex imp = windowedImpulse[i];
                spectrum[i] = imp;
                timeWeightedSpectrum[i] = imp * (i * invSampleRate);
            }

            Fourier.Forward(spectrum, FourierOptions.Matlab);
            Fourier.Forward(timeWeightedSpectrum, FourierOptions.Matlab);

            int halfLength = n / 2;

            double maxMagnitude = 0.0;
            for (int i = 1; i < halfLength; i++)
            {
                maxMagnitude = Math.Max(maxMagnitude, spectrum[i].Magnitude);
            }

            if (maxMagnitude <= 0.0)
            {
                return new AnalysisCurve("Group Delay", new List<SignalPoint>());
            }

            double relativeGate = maxMagnitude * Math.Pow(10.0, magnitudeGateDb / 20.0);
            double absoluteGate = 1e-8;
            double minMagnitude = Math.Max(relativeGate, absoluteGate);

            List<SignalPoint> data = new();

            // The gate buffer starts at extractionStart; adding it back makes the group
            // delay absolute (referenced to the IR start), so a peak well into the IR
            // reads its true arrival time.
            double absoluteStartTime = extractionStart * invSampleRate;

            for (int i = 1; i < halfLength; i++)
            {
                double magnitude = spectrum[i].Magnitude;
                double f = i * measurement.SampleRate / (double)n;

                // Skip nulls/noise-floor bins before dividing: a near-zero spectrum
                // would otherwise yield NaN/Infinity and poison the later smoothing.
                if (magnitude < minMagnitude)
                {
                    data.Add(new SignalPoint(f, double.NaN));
                    continue;
                }

                Complex groupDelay = timeWeightedSpectrum[i] / spectrum[i];
                double delayMilliseconds = (groupDelay.Real + absoluteStartTime) * 1000.0;

                data.Add(new SignalPoint(f, delayMilliseconds));
            }

            if (smoothingInverseOctaves > 0.0 && data.Count > 1)
            {
                data = SmoothLinear(data, 1.0 / smoothingInverseOctaves);
            }

            return new AnalysisCurve("Group Delay", data);
        }

        public static double LogPositionToFrequency(double x, double start, double stop)
        {
            double val = x * Math.Log10(stop / start);
            return Math.Pow(10.0, val) * start;
        }

        public static double FrequencyToLogPosition(double frequency, double start, double stop)
        {
            return Math.Log10(frequency / start) / Math.Log10(stop / start);
        }

        public static double LanczosKernel(double x, double a = 1) =>
            DspMath.LanczosKernel(x, a);

        /// <summary>
        /// Resamples linearly spaced FFT bins onto a logarithmic frequency grid.
        /// A Lanczos kernel is used to avoid the aliasing and jagged traces produced by nearest-bin lookup.
        /// </summary>
        public static List<SignalPoint> LogarithmicResample(
            List<SignalPoint> input,
            double start,
            double stop,
            int steps,
            CalibrationFile? calibration = null,
            double smoothingOctaves = 1.0 / 6.0,
            bool dBUnpack = true)
        {
            if (input.Count < 2)
            {
                return new List<SignalPoint>();
            }
            if (steps < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(steps));
            }

            List<SignalPoint> output = new List<SignalPoint>(steps);

            double inputStep = input[1].X - input[0].X;
            double a = 2.0;
            double frequencyRatio = Math.Pow(2.0, smoothingOctaves * 0.5);

            int BinarySearchX(double searchedX)
            {
                searchedX += inputStep * 0.5;
                int left = 0;
                int right = input.Count - 1;

                if (searchedX <= input[0].X)
                    return 0;
                if (searchedX >= input[input.Count - 1].X)
                    return input.Count - 1;

                while (left <= right)
                {
                    var middle = (left + right) / 2;

                    if (searchedX >= input[middle].X && searchedX < input[middle + 1].X)
                    {
                        return middle;
                    }
                    else if (searchedX < input[middle].X)
                    {
                        right = middle - 1;
                    }
                    else
                    {
                        left = middle + 1;
                    }
                }
                return -1;
            }

            SignalPoint Sample(int index)
            {
                return input[Math.Clamp(index, 0, input.Count - 1)];
            }

            for (int i = 0; i < steps; i++)
            {
                double frequency = LogPositionToFrequency(i / (steps - 1.0), start, stop);

                double halfDeltaFrequency = Math.Max(frequency * (frequencyRatio - 1), inputStep * a);
                double invHalfDeltaFrequency = 1.0 / halfDeltaFrequency * a;

                int centerIndex = BinarySearchX(frequency);
                int windowRadius = (int)Math.Ceiling(halfDeltaFrequency / inputStep);

                double weightSum = 0;
                double weightedSum = 0;

                for (int sampleIndex = Math.Max(centerIndex - windowRadius, 0);
                    sampleIndex <= centerIndex + windowRadius;
                    sampleIndex++)
                {
                    SignalPoint samplePoint = Sample(sampleIndex);
                    double weight = LanczosKernel((frequency - samplePoint.X) * invHalfDeltaFrequency, a);

                    if (dBUnpack)
                    {
                        weightedSum += DecibelsToAmplitude(samplePoint.Y) * weight;
                    }
                    else
                    {
                        weightedSum += samplePoint.Y * weight;
                    }
                    weightSum += weight;
                }

                double filteredValue;
                if (weightSum > 1e-12)
                {
                    filteredValue = dBUnpack
                        ? AmplitudeToDecibels(weightedSum / weightSum)
                        : weightedSum / weightSum;
                }
                else
                {
                    // Lanczos weights are signed, so the sum degenerates when the
                    // kernel window falls outside the input grid (e.g. resampling to
                    // 20 kHz from a spectrum that ends below it). Hold the nearest
                    // input sample instead of pinning the point to the -160 dB floor.
                    filteredValue = Sample(centerIndex).Y;
                }

                if (calibration != null)
                {
                    output.Add(new SignalPoint(
                        frequency,
                        filteredValue - calibration.GetDecibelCorrection(frequency)));
                }
                else
                {
                    output.Add(new SignalPoint(frequency, filteredValue));
                }
            }

            return output;
        }

        public static List<SignalPoint> SmoothLinear(List<SignalPoint> input, double smoothingOctaves = 1.0 / 6.0)
        {
            if (input.Count < 2)
            {
                return new List<SignalPoint>(input);
            }

            List<SignalPoint> output = new List<SignalPoint>(input.Count);

            double a = 2.0;
            double frequencyRatio = Math.Pow(2.0, smoothingOctaves * 0.5);

            SignalPoint Sample(int index)
            {
                return input[Math.Clamp(index, 0, input.Count - 1)];
            }

            double fStep = input[1].X - input[0].X;

            for (int i = 0; i < input.Count; i++)
            {
                var centerPoint = Sample(i);
                if (!double.IsFinite(centerPoint.Y))
                {
                    output.Add(centerPoint);
                    continue;
                }

                double frequency = centerPoint.X;

                double halfDeltaFrequency = Math.Max(frequency * (frequencyRatio - 1), fStep * a);

                int win = (int)Math.Max(2, Math.Ceiling(halfDeltaFrequency / fStep));

                double weightSum = 0;
                double weightedSum = 0;

                for (int sampleIndex = Math.Max(i - win, 0); sampleIndex <= i + win; sampleIndex++)
                {
                    SignalPoint samplePoint = Sample(sampleIndex);
                    if (!double.IsFinite(samplePoint.Y))
                    {
                        continue;
                    }

                    double weight = LanczosKernel((frequency - samplePoint.X) / halfDeltaFrequency, a);

                    weightedSum += samplePoint.Y * weight;

                    weightSum += weight;
                }

                // Same degenerate-weight-sum fallback as LogarithmicResample: hold
                // the centre sample rather than collapsing the point to 0.
                double filteredValue = weightSum > 1e-12
                    ? weightedSum / weightSum
                    : centerPoint.Y;

                output.Add(new SignalPoint(frequency, filteredValue));
            }

            return output;
        }
    }
}
