using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
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
        // wrap: true, matching GetGroupDelay exactly: the dialog advertises ONE
        // gate for both, and phase must stay the mathematical integral of the
        // group delay. A gate whose left shoulder runs before the IR start
        // (offset < left fade) reads the cyclic tail — the transfer IR is
        // circular by construction, so its negative-time content lives there —
        // where zero-padding used to silently feed phase and GD two different
        // signals.
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
                wrap: true,
                out extractionStart);
            Fourier.Forward(spectrum, FourierOptions.Matlab);
            return spectrum;
        }

        private const double FdwMinimumDurationSeconds = 0.0008;
        private const double FdwCentersPerOctave = 3.0;
        private static readonly ConditionalWeakTable<Complex[], PhaseSpectrumCache>
            PhaseSpectrumCaches = new();

        private sealed class PhaseSpectrumCache
        {
            public Dictionary<PhaseSpectrumCacheKey, CachedPhaseSpectrum> Entries { get; } = new();
        }

        private readonly record struct PhaseSpectrumCacheKey(
            int SampleRate,
            double GateOffsetMs,
            double LeftMs,
            double PlateauMs,
            double RightMs,
            PhaseWindowMode WindowMode,
            int FdwCycles,
            int FftLength);

        private sealed record CachedPhaseSpectrum(Complex[] Spectrum, int ExtractionStart);

        private sealed record FdwSpectrumEntry(
            double CenterFrequencyHz,
            int EffectiveGateSamples,
            Complex[] Spectrum,
            int ExtractionStart);

        private static Complex[] BuildAnalysisSpectrum(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            out int extractionStart)
        {
            Complex[] impulse = measurement.ImpulseResponse
                ?? throw new InvalidOperationException("Impulse response is not available.");
            var key = new PhaseSpectrumCacheKey(
                measurement.SampleRate,
                settings.GateOffsetMs,
                settings.LeftMs,
                settings.PlateauMs,
                settings.RightMs,
                settings.WindowMode,
                settings.ValidatedFdwCycles,
                GatedFftLength);
            PhaseSpectrumCache cache = PhaseSpectrumCaches.GetOrCreateValue(impulse);
            lock (cache.Entries)
            {
                if (cache.Entries.TryGetValue(key, out CachedPhaseSpectrum? cached))
                {
                    extractionStart = cached.ExtractionStart;
                    return cached.Spectrum;
                }
            }

            Complex[] spectrum;
            if (settings.WindowMode == PhaseWindowMode.Fixed)
            {
                spectrum = BuildPhaseSpectrum(
                    measurement,
                    settings.GateOffsetMs,
                    settings.LeftMs,
                    settings.PlateauMs,
                    settings.RightMs,
                    out extractionStart);
            }
            else
            {
                spectrum = BuildFdwSpectrum(measurement, settings, out extractionStart);
            }
            lock (cache.Entries)
            {
                cache.Entries[key] = new CachedPhaseSpectrum(spectrum, extractionStart);
            }
            return spectrum;
        }

        private static Complex[] BuildFdwSpectrum(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            out int extractionStart)
        {
            int sampleRate = measurement.SampleRate;
            int left = MillisecondsToSamples(settings.LeftMs, sampleRate);
            int plateau = MillisecondsToSamples(settings.PlateauMs, sampleRate);
            int right = MillisecondsToSamples(settings.RightMs, sampleRate);
            int fixedGate = Math.Clamp(left + plateau + right, 1, GatedFftLength);
            // The left shoulder is the immutable temporal anchor. The 0.8 ms
            // floor therefore applies to analysis time after that shoulder;
            // otherwise a long configured fade could consume the whole shortest
            // window and zero the direct arrival at its endpoint.
            int minimumGate = Math.Clamp(
                left + (int)Math.Round(FdwMinimumDurationSeconds * sampleRate),
                1,
                fixedGate);
            int cycles = settings.ValidatedFdwCycles;
            double binWidth = sampleRate / (double)GatedFftLength;
            double nyquist = sampleRate / 2.0;
            var entries = new List<FdwSpectrumEntry>();
            int previousGate = -1;

            for (double center = binWidth; center <= nyquist;
                 center *= Math.Pow(2.0, 1.0 / FdwCentersPerOctave))
            {
                // cycles/frequency is the analysis time AFTER the left-shoulder
                // anchor — the same convention as the minimum-gate floor above
                // and the duration the docs promise. Counting the shoulder
                // inside the cycles would silently shorten the post-arrival
                // window by the configured fade, making FDW more aggressive
                // than advertised whenever the fade is long.
                int effectiveGate = Math.Clamp(
                    left + (int)Math.Round(cycles * sampleRate / center),
                    minimumGate,
                    fixedGate);
                if (effectiveGate == previousGate)
                {
                    // The spectrum is unchanged, but this gate remains valid up
                    // to the current center. Keep that upper boundary so the
                    // following interpolation does not start at the first FFT
                    // bin and prematurely shorten the low-frequency window.
                    entries[^1] = entries[^1] with
                    {
                        CenterFrequencyHz = center
                    };
                    continue;
                }

                Complex[] spectrum = ExtractFdwWindowedImpulse(
                    measurement,
                    settings.GateOffsetMs,
                    left,
                    right,
                    effectiveGate,
                    out int start);
                Fourier.Forward(spectrum, FourierOptions.Matlab);
                entries.Add(new FdwSpectrumEntry(center, effectiveGate, spectrum, start));
                previousGate = effectiveGate;
            }

            if (entries.Count == 0 || entries[^1].CenterFrequencyHz < nyquist)
            {
                int effectiveGate = minimumGate;
                Complex[] spectrum = ExtractFdwWindowedImpulse(
                    measurement,
                    settings.GateOffsetMs,
                    left,
                    right,
                    effectiveGate,
                    out int start);
                Fourier.Forward(spectrum, FourierOptions.Matlab);
                if (entries.Count == 0 || entries[^1].EffectiveGateSamples != effectiveGate)
                {
                    entries.Add(new FdwSpectrumEntry(nyquist, effectiveGate, spectrum, start));
                }
            }

            extractionStart = entries[0].ExtractionStart;
            foreach (FdwSpectrumEntry entry in entries)
            {
                ApplyTimeReference(entry.Spectrum, entry.ExtractionStart, extractionStart);
            }

            var combined = new Complex[GatedFftLength];
            int upperIndex = 0;
            for (int bin = 0; bin <= combined.Length / 2; bin++)
            {
                double frequency = bin * binWidth;
                while (upperIndex < entries.Count - 1 &&
                       entries[upperIndex].CenterFrequencyHz < frequency)
                {
                    upperIndex++;
                }

                if (upperIndex == 0)
                {
                    combined[bin] = entries[0].Spectrum[bin];
                    continue;
                }

                FdwSpectrumEntry lower = entries[upperIndex - 1];
                FdwSpectrumEntry upper = entries[upperIndex];
                double logFrequency = Math.Log(Math.Max(frequency, lower.CenterFrequencyHz));
                double t = (logFrequency - Math.Log(lower.CenterFrequencyHz)) /
                    (Math.Log(upper.CenterFrequencyHz) - Math.Log(lower.CenterFrequencyHz));
                combined[bin] = InterpolateSpectrum(
                    lower.Spectrum[bin], upper.Spectrum[bin], Math.Clamp(t, 0.0, 1.0));
            }
            for (int bin = 1; bin < combined.Length / 2; bin++)
            {
                combined[combined.Length - bin] = Complex.Conjugate(combined[bin]);
            }

            return combined;
        }

        private static Complex[] ExtractFdwWindowedImpulse(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            int requestedLeft,
            int requestedRight,
            int gate,
            out int extractionStart)
        {
            int left = Math.Min(requestedLeft, gate);
            int right = Math.Min(requestedRight, gate - left);
            double[] tukey = Windowing.TukeyWindow(
                gate,
                (double)left / gate * 2.0,
                (double)right / gate * 2.0);
            double[] window = new double[GatedFftLength];
            Array.Copy(tukey, window, gate);
            extractionStart = MillisecondsToSamples(gateOffsetMs, measurement.SampleRate) - left;
            return ExtractWindow(measurement, extractionStart, GatedFftLength, window, wrap: true);
        }

        private static void ApplyTimeReference(
            Complex[] spectrum,
            int extractionStart,
            double referenceSamples)
        {
            double shift = referenceSamples - extractionStart;
            if (shift == 0.0)
            {
                return;
            }

            for (int bin = 0; bin < spectrum.Length; bin++)
            {
                spectrum[bin] *= Complex.FromPolarCoordinates(
                    1.0,
                    Math.Tau * bin * shift / spectrum.Length);
            }
        }

        // Complex LINEAR interpolation between two bank spectra. The FFT is
        // linear, so lerping the spectra IS analyzing the IR through the lerped
        // time window w = (1−t)·w_lower + t·w_upper — and, critically, it is the
        // only blend that preserves superposition, FDW(ΣIR) = Σ FDW(IR), the
        // invariant Virtual DSP's channels-vs-Sum phase view rests on. The
        // earlier log-magnitude / shortest-arc-phase blend was nonlinear: two
        // channels whose spectra rotate differently between neighboring windows
        // interpolated to a different phase than their vector sum did (tens of
        // degrees from the order of operations alone), so the drawn Sum did not
        // have to match the drawn channels. Where the two windows genuinely
        // disagree in phase the lerp can pass near zero — that is a real null
        // of the interpolated window, and the reliability gate masks it,
        // instead of an arc gliding over it with a fabricated magnitude.
        private static Complex InterpolateSpectrum(Complex lower, Complex upper, double t) =>
            lower + (upper - lower) * t;

        // Reliability gates for the phase output: bins this far below the LOCAL
        // magnitude envelope — or with squared coherence below the floor, when
        // coherence is available — are treated as carrying no trustworthy phase.
        // Unwrapped, they still contribute their phase but never anchor the
        // unwrap, so one noisy or masked bin cannot shift the whole tail by 2π.
        // Wrapped, they are blanked (NaN) outright: a wrapped display has no
        // bridging to hide behind, and drawing the phase of a null, a filter
        // stop-band or an incoherent band paints ±180° noise that reads as
        // signal. The magnitude gate reads against an octave-smoothed
        // local envelope rather than the global curve maximum: one tall resonance
        // (a subwoofer's cabin peak) must not disqualify a quieter but perfectly
        // repeatable band tens of dB below it. An absolute backstop against the
        // global maximum still rejects true silence — inside a wide dead band the
        // local envelope IS the noise floor and would otherwise pass itself.
        // γ² = 0.5 is the point where less than half the measured energy is
        // coherent with the reference and branch choices become unsafe.
        private const double UnwrapMagnitudeGateDb = -30.0;
        private const double UnwrapAbsoluteFloorDb = -60.0;
        private const double UnwrapEnvelopeOctaves = 1.0;
        private const double UnwrapCoherenceFloor = 0.5;

        // How far the unwrap may bridge an unreliable stretch before conceding the
        // branch is unknowable. Inside a gap the turn count is genuinely lost —
        // an all-pass section or a crossover transition can add whole turns that
        // no slope extrapolation can see — so past these limits the bridged points
        // are blanked (NaN) and a fresh wrapped segment starts, instead of drawing
        // one confident continuous line through guessed branches. Both limits must
        // be exceeded: a gap narrow in hertz carries few delay turns (turns =
        // τ·Δf) and bridges safely however many octaves it spans near DC, and a
        // gap narrow in octaves is a local feature the running slope handles.
        private const int UnwrapMaxBridgeBins = 64;
        private const double UnwrapMaxBridgeOctaves = 1.0 / 3.0;
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
        // of accumulating their phase noise into the tail. A gap that runs past the
        // bridge limits is conceded instead of guessed: its points are blanked and
        // a fresh wrapped segment starts at the next reliable bin. With every bin
        // reliable and a zero slope this reduces to the classic nearest-to-previous
        // choice.
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

            // The reliability gate serves both output modes (see the constants
            // above), so its envelope is always computed.
            double maxMagnitude = 0.0;
            var magnitude = new double[n / 2];
            for (int i = 1; i < n / 2; i++)
            {
                magnitude[i] = spectrum[i].Magnitude;
                maxMagnitude = Math.Max(maxMagnitude, magnitude[i]);
            }

            double[] localEnvelope = SmoothBinsHann(
                magnitude,
                UnwrapEnvelopeOctaves,
                sampleRate / (double)n,
                minHalfWidthHz: 0.0);
            double absoluteFloor =
                maxMagnitude * Math.Pow(10.0, UnwrapAbsoluteFloorDb / 20.0);
            double localGateRatio = Math.Pow(10.0, UnwrapMagnitudeGateDb / 20.0);

            bool hasAnchor = false;
            bool hasSlope = false;
            double anchorFrequency = 0.0;
            double anchorPhase = 0.0;
            double slope = 0.0; // rad per Hz
            int unreliableRun = 0;
            double lastReliableFrequency = 0.0;

            for (int i = 1; i < n / 2; i++)
            {
                double f = i * sampleRate / (double)n;

                // Re-reference the segment phase to the absolute reference sample.
                double referenced = spectrum[i].Phase + Math.Tau * i * referenceShift / n;
                double wrapped = Math.Atan2(Math.Sin(referenced), Math.Cos(referenced));

                bool reliable = spectrum[i].Magnitude >= absoluteFloor &&
                    spectrum[i].Magnitude >= localEnvelope[i] * localGateRatio &&
                    (coherence == null ||
                     CoherenceAt(coherence, f, sampleRate) >= UnwrapCoherenceFloor);

                if (!unwrap)
                {
                    // Wrapped output blanks unreliable bins instead of drawing
                    // their ±180° noise as if it were a curve.
                    data.Add(new SignalPoint(f, reliable ? wrapped : double.NaN));
                    continue;
                }

                if (!hasAnchor)
                {
                    // Before the first reliable bin the output stays wrapped; a
                    // bin seeds the anchor only when it passes the reliability
                    // gate, so a garbage bin near the bottom of the band cannot
                    // offset the first unwrapped branch by 2π.
                    data.Add(new SignalPoint(f, wrapped));
                    if (reliable)
                    {
                        anchorFrequency = f;
                        anchorPhase = wrapped;
                        hasAnchor = true;
                        lastReliableFrequency = f;
                    }
                    continue;
                }

                if (reliable && IsBridgeTooLong(unreliableRun, lastReliableFrequency, f))
                {
                    // The turn count inside the gap is unknowable — blank the
                    // guessed bridge and restart a fresh wrapped segment here,
                    // claiming no branch relation across the gap.
                    for (int back = 1; back <= unreliableRun; back++)
                    {
                        data[^back] = new SignalPoint(data[^back].X, double.NaN);
                    }

                    hasSlope = false;
                    slope = 0.0;
                    anchorFrequency = f;
                    anchorPhase = wrapped;
                    unreliableRun = 0;
                    lastReliableFrequency = f;
                    data.Add(new SignalPoint(f, wrapped));
                    continue;
                }

                double predicted = anchorPhase + slope * (f - anchorFrequency);
                double branch = Math.Round((predicted - wrapped) / Math.Tau);
                double unwrappedPhase = wrapped + Math.Tau * branch;
                if (reliable)
                {
                    unreliableRun = 0;
                    lastReliableFrequency = f;
                    if (f > anchorFrequency)
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
                }
                else
                {
                    unreliableRun++;
                }

                data.Add(new SignalPoint(f, unwrappedPhase));
            }

            // A gap still open at Nyquist gets the same honesty: if it already
            // exceeded the bridge limits, its guessed points are blanked too.
            if (unwrap && data.Count > 0 &&
                IsBridgeTooLong(unreliableRun, lastReliableFrequency, data[^1].X))
            {
                for (int back = 1; back <= unreliableRun; back++)
                {
                    data[^back] = new SignalPoint(data[^back].X, double.NaN);
                }
            }

            return data;
        }

        // Both limits must be exceeded before a bridge is declared unknowable —
        // see the constants above for why each alone is not enough.
        private static bool IsBridgeTooLong(
            int unreliableRun,
            double lastReliableFrequency,
            double frequency) =>
            unreliableRun >= UnwrapMaxBridgeBins &&
            lastReliableFrequency > 0.0 &&
            frequency >= lastReliableFrequency *
                Math.Pow(2.0, UnwrapMaxBridgeOctaves);

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

        /// <summary>
        /// The gated complex analysis spectrum behind the phase views — the
        /// Fixed-gate FFT or the FDW-combined bank, per
        /// <paramref name="settings"/> — with the extraction start needed to
        /// re-reference it to an absolute sample. This is the quantity the
        /// FDW linearity contract is stated on (FDW of a sum of IRs equals
        /// the sum of the FDW spectra), so callers can verify or build on
        /// superposition directly. Returns a copy: the underlying array is a
        /// shared cache entry.
        /// </summary>
        public static Complex[] GetPhaseAnalysisSpectrum(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            out int extractionStart)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(
                measurement, settings, out extractionStart);
            return (Complex[])spectrum.Clone();
        }

        public static List<SignalPoint> GetGatedPhaseData(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            IReadOnlyList<double>? coherence = null)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out int extractionStart);
            double detrendMilliseconds = ResolveDetrendMilliseconds(
                spectrum,
                extractionStart,
                measurement.SampleRate,
                settings);
            return BuildMeasuredPhase(
                spectrum,
                extractionStart,
                detrendMilliseconds * measurement.SampleRate / 1000.0,
                measurement.SampleRate,
                settings.Unwrap,
                coherence);
        }

        public static double ResolvePhaseDetrendMilliseconds(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out int extractionStart);
            return ResolveDetrendMilliseconds(
                spectrum,
                extractionStart,
                measurement.SampleRate,
                settings);
        }

        /// <summary>
        /// Resolves the one Auto reference that a multi-curve view must reuse for
        /// every channel and sum. The reference measurement is intentionally the
        /// only signal accepted, making per-channel auto-flattening a caller-visible
        /// policy error rather than an accidental loop implementation.
        /// </summary>
        public static double ResolveCommonPhaseDetrendMilliseconds(
            IImpulseMeasurement referenceMeasurement,
            PhaseAnalysisSettings settings) =>
            ResolvePhaseDetrendMilliseconds(referenceMeasurement, settings);

        private static double ResolveDetrendMilliseconds(
            Complex[] spectrum,
            int extractionStart,
            int sampleRate,
            PhaseAnalysisSettings settings) => settings.DetrendMode switch
            {
                PhaseDetrendMode.Off => 0.0,
                PhaseDetrendMode.Manual => settings.ManualDetrendMilliseconds,
                PhaseDetrendMode.Auto => EstimatePhaseDetrend(
                    spectrum, extractionStart, sampleRate).SlopeMilliseconds,
                _ => 0.0
            };

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
                SmoothPhaseCurve(data, smoothingInverseOctaves));
        }

        public static AnalysisCurve GetPhase(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            IReadOnlyList<double>? coherence = null)
        {
            List<SignalPoint> phase = GetGatedPhaseData(measurement, settings, coherence);
            List<SignalPoint> data = phase
                .Select(point => new SignalPoint(point.X, point.Y / Math.PI * 180.0))
                .ToList();
            return new AnalysisCurve(
                "Phase",
                SmoothPhaseCurve(data, settings.SmoothingInverseOctaves));
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
                SmoothPhaseCurve(data, smoothingInverseOctaves),
                AnalysisCurveKind.MinimumPhase);
        }

        public static AnalysisCurve GetMinimumPhase(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out _);
            return BuildMinimumPhaseCurve(
                spectrum, measurement.SampleRate, settings.SmoothingInverseOctaves);
        }

        private static AnalysisCurve BuildMinimumPhaseCurve(
            Complex[] spectrum,
            int sampleRate,
            double smoothingInverseOctaves)
        {
            double[] magnitude = spectrum.Select(value => value.Magnitude).ToArray();
            double[] minimumPhase = MinimumPhase.FromMagnitude(magnitude);
            var data = new List<SignalPoint>(spectrum.Length / 2);
            for (int i = 1; i < spectrum.Length / 2; i++)
            {
                data.Add(new SignalPoint(
                    i * sampleRate / (double)spectrum.Length,
                    minimumPhase[i] / Math.PI * 180.0));
            }
            return new AnalysisCurve(
                "Minimum Phase",
                SmoothPhaseCurve(data, smoothingInverseOctaves),
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
                SmoothPhaseCurve(data, smoothingInverseOctaves),
                AnalysisCurveKind.ExcessPhase);
        }

        public static AnalysisCurve GetExcessPhase(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            IReadOnlyList<double>? coherence = null)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out int extractionStart);
            double detrendMilliseconds = ResolveDetrendMilliseconds(
                spectrum, extractionStart, measurement.SampleRate, settings);
            List<SignalPoint> measured = BuildMeasuredPhase(
                spectrum,
                extractionStart,
                detrendMilliseconds * measurement.SampleRate / 1000.0,
                measurement.SampleRate,
                unwrap: true,
                coherence);
            double[] minimumPhase = MinimumPhase.FromMagnitude(
                spectrum.Select(value => value.Magnitude).ToArray());
            var data = new List<SignalPoint>(measured.Count);
            for (int j = 0; j < measured.Count; j++)
            {
                double excess = double.IsNaN(measured[j].Y)
                    ? double.NaN
                    : measured[j].Y - minimumPhase[j + 1];
                data.Add(new SignalPoint(measured[j].X, excess / Math.PI * 180.0));
            }
            return new AnalysisCurve(
                "Excess Phase",
                SmoothPhaseCurve(data, settings.SmoothingInverseOctaves),
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

            return EstimatePhaseDetrend(spectrum, extractionStart, measurement.SampleRate);
        }

        public static (double SlopeMilliseconds, double PeakMilliseconds) EstimatePhaseDetrend(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(measurement, settings, out int extractionStart);
            return EstimatePhaseDetrend(spectrum, extractionStart, measurement.SampleRate);
        }

        private static (double SlopeMilliseconds, double PeakMilliseconds) EstimatePhaseDetrend(
            Complex[] spectrum,
            int extractionStart,
            int sampleRate)
        {
            ExcessDelayResult result = ExcessDelay.Estimate(spectrum, sampleRate);
            double toMilliseconds = 1000.0 / sampleRate;
            return (
                (extractionStart + result.SlopeDelaySamples) * toMilliseconds,
                (extractionStart + result.PeakDelaySamples) * toMilliseconds);
        }

        // Minimal energy-weighted smoothing applied even when display smoothing is
        // off: wide enough to bridge single-bin interference nulls (whose group
        // delay legitimately diverges but carries almost no energy), narrow enough
        // to leave the visible curve unchanged elsewhere.
        private const double GroupDelayStabilizationOctaves = 1.0 / 48.0;

        // The smoothing window never narrows below the gate's own spectral
        // resolution (1/T for a gate of duration T): features narrower than that
        // cannot be resolved by the gate in the first place, and it is exactly
        // the scale of the interference nulls of the longest in-gate reflection,
        // whose group-delay spikes the energy weighting is meant to absorb.
        private const double GroupDelayResolutionHalfWidthFactor = 0.5;

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
            double binWidthHz = measurement.SampleRate / (double)n;

            // Per-bin numerator and denominator of τg = Re[T·conj(H)] / |H|². Smoothing
            // them separately and dividing the averages makes the result energy-weighted:
            // near-null bins (where the per-bin ratio legitimately spikes to ±tens of ms)
            // enter with weight |H|² ≈ 0, so the curve follows the delay of the dominant
            // energy instead of the singularity.
            double[] numerator = new double[halfLength];
            double[] energy = new double[halfLength];
            for (int i = 1; i < halfLength; i++)
            {
                Complex h = spectrum[i];
                Complex t = timeWeightedSpectrum[i];
                numerator[i] = t.Real * h.Real + t.Imaginary * h.Imaginary;
                energy[i] = h.Real * h.Real + h.Imaginary * h.Imaginary;
            }

            double decodedOctaves =
                SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves);
            double smoothingOctaves = decodedOctaves > 0.0
                ? decodedOctaves
                : GroupDelayStabilizationOctaves;
            int sampleRate = measurement.SampleRate;
            int gateSamples = Math.Clamp(
                MillisecondsToSamples(leftMs, sampleRate) +
                MillisecondsToSamples(plateauMs, sampleRate) +
                MillisecondsToSamples(rightMs, sampleRate),
                1,
                GatedFftLength);
            double minHalfWidthHz =
                GroupDelayResolutionHalfWidthFactor * sampleRate / gateSamples;
            double[] smoothedNumerator =
                SmoothBinsHann(numerator, smoothingOctaves, binWidthHz, minHalfWidthHz);
            double[] smoothedEnergy =
                SmoothBinsHann(energy, smoothingOctaves, binWidthHz, minHalfWidthHz);

            double maxEnergy = 0.0;
            for (int i = 1; i < halfLength; i++)
            {
                maxEnergy = Math.Max(maxEnergy, smoothedEnergy[i]);
            }

            if (maxEnergy <= 0.0)
            {
                return new AnalysisCurve("Group Delay", new List<SignalPoint>());
            }

            // The validity gate reads against a LOCAL octave-smoothed energy
            // envelope, like the unwrap's reliability gate: one tall resonance
            // must not blank a quieter but perfectly measured band 30+ dB below
            // it. The −60 dB global backstop (the same figure as the unwrap's)
            // still rejects true silence, where the local envelope IS the noise
            // floor and would otherwise pass itself. Energies compare as |H|²,
            // so the dB thresholds divide by 10.
            double[] localEnvelope = SmoothBinsHann(
                smoothedEnergy, 1.0, binWidthHz, minHalfWidthHz: 0.0);
            double globalGate = maxEnergy * Math.Pow(10.0, -60.0 / 10.0);
            double absoluteGate = 1e-16;
            double localGateRatio = Math.Pow(10.0, magnitudeGateDb / 10.0);

            List<SignalPoint> data = new(halfLength);

            // The gate buffer starts at extractionStart; adding it back makes the group
            // delay absolute (referenced to the IR start), so a peak well into the IR
            // reads its true arrival time.
            double absoluteStartTime = extractionStart * invSampleRate;

            for (int i = 1; i < halfLength; i++)
            {
                double f = i * binWidthHz;

                // Regions with no coherent energy anywhere in the smoothing window
                // (outside the sweep band, true silence, deep local notches) stay
                // gated out.
                double minEnergy = Math.Max(
                    Math.Max(localEnvelope[i] * localGateRatio, globalGate),
                    absoluteGate);
                if (smoothedEnergy[i] < minEnergy)
                {
                    data.Add(new SignalPoint(f, double.NaN));
                    continue;
                }

                double delaySeconds = smoothedNumerator[i] / smoothedEnergy[i];
                data.Add(new SignalPoint(f, (delaySeconds + absoluteStartTime) * 1000.0));
            }

            return new AnalysisCurve("Group Delay", data);
        }

        // The SEED grid: how many anchors span one kernel width before refinement. Not
        // the guarantee — SmoothingRelativeTolerance is.
        private const double SmoothingAnchorsPerKernel = 16.0;

        // A span between two anchors is interpolated when the chord's error at its
        // midpoint is within this, relative to the local value. ~0.04 dB: far under the
        // 30 dB margin the reliability gates compare against, and under anything the
        // group-delay ratio resolves.
        private const double SmoothingRelativeTolerance = 0.005;

        // …floored at this fraction of the array's peak. Below it every caller has
        // already thrown the bin away — both gates carry a -60 dB global backstop, the
        // unwrap's on amplitude and the group delay's on energy (hence the tighter figure
        // here, since energy compares as |H|²) — so chasing precision into the noise
        // floor would only buy subdivision nobody reads.
        private const double SmoothingScaleFloor = 1e-6;

        // Refinement stops here: a span this short is at the bin grid's own resolution,
        // and the exact evaluation is what further splitting would converge to anyway.
        private const int SmoothingMinimumSpan = 2;

        // Hann-weighted fractional-octave moving average over the linear FFT bin
        // grid (bin 0 excluded). A strictly non-negative kernel, unlike the Lanczos
        // used for display smoothing: the group-delay division needs the smoothed
        // energy to stay positive, and a signed kernel could cancel it near sharp
        // spectral transitions and reintroduce the very spikes being removed.
        //
        // Display smoothing for the phase-domain curves (phase, minimum/excess
        // phase): the stored code decodes through SpectrumSmoothing, so the
        // psychoacoustic magnitude mode falls back to its plain base width here
        // — the asymmetric dip floor is a magnitude concept and would bias a
        // signed phase trace upward. Off (0) passes the data through untouched.
        private static List<SignalPoint> SmoothPhaseCurve(
            List<SignalPoint> data, double smoothingInverseOctaves)
        {
            double octaves = SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves);
            return octaves > 0 ? SmoothLinear(data, octaves) : data;
        }

        // Evaluated on LOG-spaced anchors and interpolated between them, with every span
        // checked against the exact curve at its midpoint and split until it agrees.
        //
        // Evaluating at every linear bin asks for orders of magnitude more resolution
        // than a fractional-octave average carries, and the cost is quadratic: the kernel
        // widens in proportion to frequency, so the naive form ran ~1.1e8 iterations (each
        // with a cosine) over a 32k FFT — half a second per curve, on the UI thread.
        //
        // The seeded grid alone is NOT enough, though: "smoothed" does not mean "linear".
        // Across a sweep's band edge or a steep stopband the average falls exponentially,
        // and a chord drawn over that reads high — 10 dB high at the edge of the band,
        // worst exactly where the value is small and the dB error therefore largest. The
        // midpoint probe is what turns the bound from a hope into an assertion, and it
        // costs nothing on the smooth stretches that make up most of a response.
        // Internal rather than private so the tests can hold it against an exact
        // reference: its contract is an error bound, and nothing observable through the
        // public surface pins that.
        internal static double[] SmoothBinsHann(
            double[] source,
            double smoothingOctaves,
            double binWidthHz,
            double minHalfWidthHz)
        {
            int count = source.Length;
            double[] result = new double[count];
            if (count < 2)
            {
                return result;
            }

            double frequencyRatio = Math.Pow(2.0, smoothingOctaves * 0.5);
            double halfWidthFloor = Math.Max(minHalfWidthHz, binWidthHz * 2.0);

            double peak = 0.0;
            for (int i = 1; i < count; i++)
            {
                peak = Math.Max(peak, Math.Abs(source[i]));
            }

            double toleranceFloor = peak * SmoothingScaleFloor;

            // Never advance by less than one bin: at the low end a log step is
            // sub-bin, and the walk then degenerates to the exact per-bin evaluation
            // on its own — which is also where the kernel is narrowest and cheapest.
            double anchorStep = Math.Pow(2.0, smoothingOctaves / SmoothingAnchorsPerKernel);
            var anchors = new List<int>();
            for (double position = 1.0; position < count;)
            {
                int anchor = (int)position;
                anchors.Add(anchor);
                position = Math.Max(position * anchorStep, anchor + 1.0);
            }

            if (anchors[^1] != count - 1)
            {
                anchors.Add(count - 1);
            }

            double previous = HannAverageAt(
                source, anchors[0], binWidthHz, frequencyRatio, halfWidthFloor);
            for (int a = 0; a + 1 < anchors.Count; a++)
            {
                double next = HannAverageAt(
                    source, anchors[a + 1], binWidthHz, frequencyRatio, halfWidthFloor);
                FillSpan(
                    source,
                    result,
                    anchors[a],
                    anchors[a + 1],
                    previous,
                    next,
                    binWidthHz,
                    frequencyRatio,
                    halfWidthFloor,
                    toleranceFloor);
                previous = next;
            }

            result[anchors[^1]] = previous;
            return result;
        }

        // Fills [low, high) by the chord between the ends when that tracks the exact curve
        // at the midpoint, and splits on the midpoint when it does not. The probe is the
        // whole point: it is where a chord over a convex stretch is furthest from it, so
        // accepting it bounds the error across the span.
        private static void FillSpan(
            double[] source,
            double[] result,
            int low,
            int high,
            double lowValue,
            double highValue,
            double binWidthHz,
            double frequencyRatio,
            double halfWidthFloor,
            double toleranceFloor)
        {
            result[low] = lowValue;
            if (high - low <= SmoothingMinimumSpan)
            {
                for (int i = low + 1; i < high; i++)
                {
                    result[i] = HannAverageAt(
                        source, i, binWidthHz, frequencyRatio, halfWidthFloor);
                }

                return;
            }

            int middle = (low + high) / 2;
            double exact = HannAverageAt(
                source, middle, binWidthHz, frequencyRatio, halfWidthFloor);
            double interpolated =
                lowValue + ((highValue - lowValue) * (middle - low) / (double)(high - low));
            double tolerance =
                SmoothingRelativeTolerance * Math.Max(Math.Abs(exact), toleranceFloor);
            if (Math.Abs(exact - interpolated) <= tolerance)
            {
                double slope = (highValue - lowValue) / (high - low);
                for (int i = low + 1; i < high; i++)
                {
                    result[i] = lowValue + (slope * (i - low));
                }

                return;
            }

            FillSpan(
                source, result, low, middle, lowValue, exact,
                binWidthHz, frequencyRatio, halfWidthFloor, toleranceFloor);
            FillSpan(
                source, result, middle, high, exact, highValue,
                binWidthHz, frequencyRatio, halfWidthFloor, toleranceFloor);
        }

        // The exact Hann-weighted average centred on one bin — the kernel the anchors
        // sample. Kept whole so the anchored walk above and any future direct caller
        // cannot drift apart.
        private static double HannAverageAt(
            double[] source,
            int index,
            double binWidthHz,
            double frequencyRatio,
            double halfWidthFloor)
        {
            double frequency = index * binWidthHz;
            double halfDelta = Math.Max(
                frequency * (frequencyRatio - 1.0),
                halfWidthFloor);
            int win = (int)Math.Ceiling(halfDelta / binWidthHz);

            double weightedSum = 0.0;
            double weightSum = 0.0;
            for (int j = Math.Max(index - win, 1);
                j <= Math.Min(index + win, source.Length - 1);
                j++)
            {
                double x = (j - index) * binWidthHz / halfDelta;
                if (Math.Abs(x) >= 1.0)
                {
                    continue;
                }

                double weight = 0.5 * (1.0 + Math.Cos(Math.PI * x));
                weightedSum += source[j] * weight;
                weightSum += weight;
            }

            return weightSum > 0.0 ? weightedSum / weightSum : source[index];
        }
    }
}
