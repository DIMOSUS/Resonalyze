using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    public static partial class DataHelper
    {
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
    }
}
