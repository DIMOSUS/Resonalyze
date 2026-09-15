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

            // Extraction starts `offset` past the peak; reference 0 compensates it, so phase reads peak-referenced.
            return BuildMeasuredPhase(
                spectrum,
                extractionStart: offset,
                referenceSamples: 0,
                measurement.SampleRate,
                unwrap,
                coherence);
        }

        // Gate is zero-padded to this length: one frequency grid across gates and measurements.
        public const int GatedFftLength = 32768;

        private static int MillisecondsToSamples(double milliseconds, int sampleRate) =>
            (int)Math.Round(Math.Max(0.0, milliseconds) * sampleRate / 1000.0);

        private readonly record struct GatePlacement(
            int ExtractionStart,
            int PlateauStart,
            double[] Window);

        // Single source of gate geometry, so a placement judge sees the window that gets used. See docs/tech/phase-and-group-delay.md#gate-geometry.
        private static GatePlacement ResolveGatePlacement(
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            int sampleRate)
        {
            int gateOffset = MillisecondsToSamples(gateOffsetMs, sampleRate);
            int left = MillisecondsToSamples(leftMs, sampleRate);
            int plateau = MillisecondsToSamples(plateauMs, sampleRate);
            int right = MillisecondsToSamples(rightMs, sampleRate);

            // Share the trim between plateau and fade-out (as FrequencyResponseOptions.TrimGateToFft).
            (int gate, left, right) =
                FrequencyResponseOptions.TrimGateToFft(left, plateau, right);

            double leftNorm = (double)left / gate * 2.0;
            double rightNorm = (double)right / gate * 2.0;
            return new GatePlacement(
                gateOffset - left,
                gateOffset,
                Windowing.TukeyWindow(gate, leftNorm, rightNorm));
        }

        /// <summary>Energy discarded ahead of the plateau vs kept, in dB (−∞ nothing lost, +∞ window misses the channel). See docs/tech/phase-and-group-delay.md#leading-edge-loss-guard.</summary>
        public static double GateLeadingEdgeLossDb(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs)
        {
            ArgumentNullException.ThrowIfNull(measurement);
            if (measurement.ImpulseResponse is not { Length: > 0 } impulseResponse ||
                measurement.SampleRate <= 0)
            {
                return double.NegativeInfinity;
            }

            GatePlacement placement = ResolveGatePlacement(
                gateOffsetMs, leftMs, plateauMs, rightMs, measurement.SampleRate);
            double[] window = placement.Window;
            int length = impulseResponse.Length;

            // Wrapped addressing like the extraction: a shoulder before the record reads the circular tail.
            double Energy(int position)
            {
                int index = position % length;
                double sample = impulseResponse[index < 0 ? index + length : index].Real;
                return sample * sample;
            }

            double kept = 0;
            for (int i = 0; i < window.Length; i++)
            {
                kept += window[i] * window[i] * Energy(placement.ExtractionStart + i);
            }

            double lost = 0;
            for (int position = Math.Min(0, placement.ExtractionStart);
                position < placement.PlateauStart;
                position++)
            {
                int inWindow = position - placement.ExtractionStart;
                double weight = inWindow >= 0 && inWindow < window.Length
                    ? window[inWindow]
                    : 0.0;
                lost += (1.0 - weight * weight) * Energy(position);
            }

            if (kept > 0)
            {
                return 10.0 * Math.Log10(Math.Max(double.Epsilon, lost) / kept);
            }

            // Window holds none of the channel: +∞ (worst), never −∞, or it would look like the safest placement.
            foreach (Complex sample in impulseResponse)
            {
                if (sample.Real != 0.0)
                {
                    return double.PositiveInfinity;
                }
            }

            return double.NegativeInfinity;
        }

        // Zero-padded Tukey-gated impulse; left shoulder ends at gateOffsetMs, wrap handles negative indices.
        private static Complex[] ExtractGatedWindowedImpulse(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            bool wrap,
            out int extractionStart)
        {
            GatePlacement placement = ResolveGatePlacement(
                gateOffsetMs, leftMs, plateauMs, rightMs, measurement.SampleRate);

            double[] window = new double[GatedFftLength];
            Array.Copy(placement.Window, window, placement.Window.Length);

            extractionStart = placement.ExtractionStart;
            return ExtractWindow(measurement, extractionStart, GatedFftLength, window, wrap);
        }

        // wrap: true like GetGroupDelay: one gate for both keeps phase the integral of GD. See docs/tech/phase-and-group-delay.md#gate-geometry.
        private static Complex[] BuildPhaseSpectrum(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            out int extractionStart) =>
            BuildFixedSpectra(
                measurement,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                timeWeighted: false,
                out extractionStart).Spectrum;

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

        // TimeWeighted stays null until a group-delay reader asks: it doubles FFT work and memory.
        private sealed record CachedPhaseSpectrum(
            Complex[] Spectrum,
            Complex[]? TimeWeighted,
            int ExtractionStart);

        private sealed record FdwSpectrumEntry(
            double CenterFrequencyHz,
            int EffectiveGateSamples,
            Complex[] Spectrum,
            Complex[]? TimeWeighted,
            int ExtractionStart);

        // Resolved once per (settings, rate) so the bank and the smoothing floor agree on window length.
        private readonly record struct FdwGateGeometry(
            int Left,
            int Right,
            int MinimumGate,
            int FixedGate,
            int Cycles,
            int SampleRate)
        {
            public static FdwGateGeometry Resolve(PhaseAnalysisSettings settings, int sampleRate)
            {
                int left = MillisecondsToSamples(settings.LeftMs, sampleRate);
                int plateau = MillisecondsToSamples(settings.PlateauMs, sampleRate);
                int right = MillisecondsToSamples(settings.RightMs, sampleRate);
                int fixedGate = Math.Clamp(left + plateau + right, 1, GatedFftLength);
                // The 0.8 ms floor counts after the left shoulder, so a long fade cannot zero the direct arrival.
                int minimumGate = Math.Clamp(
                    left + (int)Math.Round(FdwMinimumDurationSeconds * sampleRate),
                    1,
                    fixedGate);
                return new FdwGateGeometry(
                    left,
                    right,
                    minimumGate,
                    fixedGate,
                    settings.WindowMode == PhaseWindowMode.Fixed ? 0 : settings.ValidatedFdwCycles,
                    sampleRate);
            }

            // Cycles count analysis time after the left shoulder, like the floor.
            public int EffectiveGate(double frequencyHz) => Cycles == 0 || frequencyHz <= 0.0
                ? FixedGate
                : Math.Clamp(
                    Left + (int)Math.Round(Cycles * SampleRate / frequencyHz),
                    MinimumGate,
                    FixedGate);

            public double MinimumHalfWidthHz(double frequencyHz) =>
                GroupDelayResolutionHalfWidthFactor * SampleRate / EffectiveGate(frequencyHz);
        }

        /// <summary>Window length in samples at a frequency: the full gate (Fixed) or left shoulder + cycles/f clamped to [0.8 ms, gate] (FDW).</summary>
        internal static int FdwEffectiveGateSamples(
            double frequencyHz,
            PhaseAnalysisSettings settings,
            int sampleRate) =>
            FdwGateGeometry.Resolve(settings, sampleRate).EffectiveGate(frequencyHz);

        internal static IReadOnlyList<(double CenterFrequencyHz, int EffectiveGateSamples)>
            DescribeFdwBank(PhaseAnalysisSettings settings, int sampleRate)
        {
            var bank = new List<(double, int)>();
            foreach ((double center, int gate) in FdwBankPlan(
                FdwGateGeometry.Resolve(settings, sampleRate), sampleRate))
            {
                bank.Add((center, gate));
            }

            return bank;
        }

        // Equal-length neighbours merge into the LAST valid centre so interpolation never shortens the LF window early. See docs/tech/phase-and-group-delay.md#fdw-bank.
        private static IEnumerable<(double CenterFrequencyHz, int EffectiveGateSamples)>
            FdwBankPlan(FdwGateGeometry geometry, int sampleRate)
        {
            double binWidth = sampleRate / (double)GatedFftLength;
            double nyquist = sampleRate / 2.0;
            if (geometry.Cycles == 0)
            {
                yield return (nyquist, geometry.FixedGate);
                yield break;
            }

            double pendingCenter = double.NaN;
            int pendingGate = -1;
            for (double center = binWidth; center <= nyquist;
                 center *= Math.Pow(2.0, 1.0 / FdwCentersPerOctave))
            {
                int effectiveGate = geometry.EffectiveGate(center);
                if (effectiveGate == pendingGate)
                {
                    pendingCenter = center;
                    continue;
                }

                if (pendingGate >= 0)
                {
                    yield return (pendingCenter, pendingGate);
                }

                pendingCenter = center;
                pendingGate = effectiveGate;
            }

            if (pendingGate >= 0)
            {
                yield return (pendingCenter, pendingGate);
            }

            if (pendingGate < 0 || pendingCenter < nyquist)
            {
                if (pendingGate != geometry.MinimumGate)
                {
                    yield return (nyquist, geometry.MinimumGate);
                }
            }
        }

        private static Complex[] BuildAnalysisSpectrum(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            out int extractionStart) =>
            BuildAnalysisSpectra(measurement, settings, timeWeighted: false, out extractionStart)
                .Spectrum;

        // A group-delay reader replaces a phase-only cache entry with the pair (spectrum bit-identical).
        private static (Complex[] Spectrum, Complex[]? TimeWeighted) BuildAnalysisSpectra(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            bool timeWeighted,
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
                if (cache.Entries.TryGetValue(key, out CachedPhaseSpectrum? cached) &&
                    (!timeWeighted || cached.TimeWeighted != null))
                {
                    extractionStart = cached.ExtractionStart;
                    return (cached.Spectrum, cached.TimeWeighted);
                }
            }

            Complex[] spectrum;
            Complex[]? weighted;
            if (settings.WindowMode == PhaseWindowMode.Fixed)
            {
                (spectrum, weighted) = BuildFixedSpectra(
                    measurement,
                    settings.GateOffsetMs,
                    settings.LeftMs,
                    settings.PlateauMs,
                    settings.RightMs,
                    timeWeighted,
                    out extractionStart);
            }
            else
            {
                (spectrum, weighted) = BuildFdwSpectra(
                    measurement, settings, timeWeighted, out extractionStart);
            }
            lock (cache.Entries)
            {
                cache.Entries[key] = new CachedPhaseSpectrum(spectrum, weighted, extractionStart);
            }
            return (spectrum, weighted);
        }

        // Operands of τ = Re[T·conj(H)] / |H|². See docs/tech/phase-and-group-delay.md#group-delay-identity.
        private static (Complex[] Spectrum, Complex[]? TimeWeighted) BuildFixedSpectra(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            bool timeWeighted,
            out int extractionStart)
        {
            Complex[] windowedImpulse = ExtractGatedWindowedImpulse(
                measurement,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                wrap: true,
                out extractionStart);
            Complex[]? weighted = timeWeighted
                ? TimeWeightSpectrum(windowedImpulse, measurement.SampleRate)
                : null;
            Fourier.Forward(windowedImpulse, FourierOptions.Matlab);
            return (windowedImpulse, weighted);
        }

        private static Complex[] TimeWeightSpectrum(Complex[] windowedImpulse, int sampleRate)
        {
            int n = windowedImpulse.Length;
            double invSampleRate = 1.0 / sampleRate;
            var weighted = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                weighted[i] = windowedImpulse[i] * (i * invSampleRate);
            }

            Fourier.Forward(weighted, FourierOptions.Matlab);
            return weighted;
        }

        // Same complex-linear log-f blend for H and T, so the GD identity holds on the stitched pair. See docs/tech/phase-and-group-delay.md#fdw-bank.
        private static (Complex[] Spectrum, Complex[]? TimeWeighted) BuildFdwSpectra(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            bool timeWeighted,
            out int extractionStart)
        {
            int sampleRate = measurement.SampleRate;
            FdwGateGeometry geometry = FdwGateGeometry.Resolve(settings, sampleRate);
            double binWidth = sampleRate / (double)GatedFftLength;
            var entries = new List<FdwSpectrumEntry>();

            foreach ((double center, int effectiveGate) in FdwBankPlan(geometry, sampleRate))
            {
                Complex[] spectrum = ExtractFdwWindowedImpulse(
                    measurement,
                    settings.GateOffsetMs,
                    geometry.Left,
                    geometry.Right,
                    effectiveGate,
                    out int start);
                Complex[]? weighted = timeWeighted
                    ? TimeWeightSpectrum(spectrum, sampleRate)
                    : null;
                Fourier.Forward(spectrum, FourierOptions.Matlab);
                entries.Add(new FdwSpectrumEntry(center, effectiveGate, spectrum, weighted, start));
            }

            extractionStart = entries[0].ExtractionStart;
            foreach (FdwSpectrumEntry entry in entries)
            {
                ReReferenceSpectra(
                    entry.Spectrum,
                    entry.TimeWeighted,
                    entry.ExtractionStart,
                    extractionStart,
                    sampleRate);
            }

            var combined = new Complex[GatedFftLength];
            Complex[]? combinedWeighted = timeWeighted ? new Complex[GatedFftLength] : null;
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
                    if (combinedWeighted != null)
                    {
                        combinedWeighted[bin] = entries[0].TimeWeighted![bin];
                    }
                    continue;
                }

                FdwSpectrumEntry lower = entries[upperIndex - 1];
                FdwSpectrumEntry upper = entries[upperIndex];
                double logFrequency = Math.Log(Math.Max(frequency, lower.CenterFrequencyHz));
                double t = Math.Clamp(
                    (logFrequency - Math.Log(lower.CenterFrequencyHz)) /
                        (Math.Log(upper.CenterFrequencyHz) - Math.Log(lower.CenterFrequencyHz)),
                    0.0,
                    1.0);
                combined[bin] = InterpolateSpectrum(
                    lower.Spectrum[bin], upper.Spectrum[bin], t);
                if (combinedWeighted != null)
                {
                    combinedWeighted[bin] = InterpolateSpectrum(
                        lower.TimeWeighted![bin], upper.TimeWeighted![bin], t);
                }
            }
            for (int bin = 1; bin < combined.Length / 2; bin++)
            {
                combined[combined.Length - bin] = Complex.Conjugate(combined[bin]);
                if (combinedWeighted != null)
                {
                    combinedWeighted[combinedWeighted.Length - bin] =
                        Complex.Conjugate(combinedWeighted[bin]);
                }
            }

            return (combined, combinedWeighted);
        }

        // The twin's time weight shifts by ((s − s_ref) / fs) · H before the rotation. A no-op in today's bank; SumGatedSpectraPairs needs the general form.
        private static void ReReferenceSpectra(
            Complex[] spectrum,
            Complex[]? timeWeighted,
            int extractionStart,
            int referenceStart,
            int sampleRate)
        {
            if (extractionStart == referenceStart)
            {
                return;
            }

            if (timeWeighted != null)
            {
                double weightShift = (extractionStart - referenceStart) / (double)sampleRate;
                for (int bin = 0; bin < timeWeighted.Length; bin++)
                {
                    timeWeighted[bin] += spectrum[bin] * weightShift;
                }

                ApplyTimeReference(timeWeighted, extractionStart, referenceStart);
            }

            ApplyTimeReference(spectrum, extractionStart, referenceStart);
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

        // Complex-linear only: the one blend preserving FDW(ΣIR) = Σ FDW(IR). See docs/tech/phase-and-group-delay.md#superposition.
        private static Complex InterpolateSpectrum(Complex lower, Complex upper, double t) =>
            lower + (upper - lower) * t;

        // Local-envelope magnitude gate, global backstop, γ² floor. See docs/tech/phase-and-group-delay.md#reliability-gates.
        private const double UnwrapMagnitudeGateDb = -30.0;
        private const double UnwrapAbsoluteFloorDb = -60.0;
        private const double UnwrapEnvelopeOctaves = 1.0;
        private const double UnwrapCoherenceFloor = 0.5;

        // A gap exceeding BOTH limits is blanked and restarts wrapped. See docs/tech/phase-and-group-delay.md#anchored-unwrap.
        private const int UnwrapMaxBridgeBins = 64;
        private const double UnwrapMaxBridgeOctaves = 1.0 / 3.0;
        private const double UnwrapSlopeBlend = 0.25;

        // Phase (rad) referenced to an absolute sample, bins 1..n/2-1, unwrap anchored on reliable bins only. See docs/tech/phase-and-group-delay.md#anchored-unwrap.
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

                double referenced = spectrum[i].Phase + Math.Tau * i * referenceShift / n;
                double wrapped = Math.Atan2(Math.Sin(referenced), Math.Cos(referenced));

                bool reliable = spectrum[i].Magnitude >= absoluteFloor &&
                    spectrum[i].Magnitude >= localEnvelope[i] * localGateRatio &&
                    (coherence == null ||
                     CoherenceAt(coherence, f, sampleRate) >= UnwrapCoherenceFloor);

                if (!unwrap)
                {
                    data.Add(new SignalPoint(f, reliable ? wrapped : double.NaN));
                    continue;
                }

                if (!hasAnchor)
                {
                    // Only a reliable bin seeds the first anchor, so a garbage bin cannot offset the branch by 2π.
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

        private static bool IsBridgeTooLong(
            int unreliableRun,
            double lastReliableFrequency,
            double frequency) =>
            unreliableRun >= UnwrapMaxBridgeBins &&
            lastReliableFrequency > 0.0 &&
            frequency >= lastReliableFrequency *
                Math.Pow(2.0, UnwrapMaxBridgeOctaves);

        // Interpolated from a uniform 0..Nyquist grid (TransferFunction.ComputeAveragedRelativeIr); degenerate input counts as trusted.
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

        /// <summary>Gated phase with the gate construction of <see cref="GetPhase"/>, referenced to a fractional absolute sample.</summary>
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

        /// <summary>A copy of the cached gated analysis spectrum (Fixed or FDW), the quantity FDW superposition is stated on.</summary>
        public static Complex[] GetPhaseAnalysisSpectrum(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            out int extractionStart)
        {
            Complex[] spectrum = BuildAnalysisSpectrum(
                measurement, settings, out extractionStart);
            return (Complex[])spectrum.Clone();
        }

        /// <summary>Spectrum and time-weighted twin in one time reference; copies, since the analysis is cached.</summary>
        public static GroupDelaySpectra GetGroupDelayAnalysisSpectra(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            out int extractionStart)
        {
            (Complex[] spectrum, Complex[]? weighted) = BuildAnalysisSpectra(
                measurement, settings, timeWeighted: true, out extractionStart);
            return new GroupDelaySpectra(
                (Complex[])spectrum.Clone(),
                (Complex[])weighted!.Clone());
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

        public static List<SignalPoint> GetGatedPhaseData(
            Complex[] spectrum,
            int extractionStart,
            double referenceSamples,
            int sampleRate,
            bool unwrap,
            IReadOnlyList<double>? coherence = null) =>
            BuildMeasuredPhase(
                spectrum,
                extractionStart,
                referenceSamples,
                sampleRate,
                unwrap,
                coherence);

        /// <summary>Complex sum of gated spectra re-referenced to one extraction start. This moves only the time origin: windows that kept different stretches stay incomparable (see <see cref="GateLeadingEdgeLossDb"/>).</summary>
        public static Complex[] SumGatedSpectra(
            IReadOnlyList<(Complex[] Spectrum, int ExtractionStart)> spectra,
            int targetExtractionStart)
        {
            ArgumentNullException.ThrowIfNull(spectra);
            if (spectra.Count == 0)
            {
                throw new ArgumentException(
                    "At least one spectrum is required.", nameof(spectra));
            }

            int length = spectra[0].Spectrum.Length;
            var combined = new Complex[length];
            foreach ((Complex[] spectrum, int extractionStart) in spectra)
            {
                if (spectrum.Length != length)
                {
                    throw new ArgumentException(
                        "All spectra must share one FFT length.", nameof(spectra));
                }

                double shift = targetExtractionStart - extractionStart;
                if (shift == 0.0)
                {
                    for (int bin = 0; bin < length; bin++)
                    {
                        combined[bin] += spectrum[bin];
                    }

                    continue;
                }

                for (int bin = 0; bin < length; bin++)
                {
                    combined[bin] += spectrum[bin] * Complex.FromPolarCoordinates(
                        1.0,
                        Math.Tau * bin * shift / length);
                }
            }

            return combined;
        }

        /// <summary><see cref="SumGatedSpectra"/> for group-delay pairs; each twin's time weight shifts by its start offset before the rotation.</summary>
        public static GroupDelaySpectra SumGatedSpectraPairs(
            IReadOnlyList<(GroupDelaySpectra Spectra, int ExtractionStart)> parts,
            int targetExtractionStart,
            int sampleRate)
        {
            ArgumentNullException.ThrowIfNull(parts);
            if (parts.Count == 0)
            {
                throw new ArgumentException(
                    "At least one spectrum pair is required.", nameof(parts));
            }

            int length = parts[0].Spectra.Spectrum.Length;
            var spectrum = new Complex[length];
            var weighted = new Complex[length];
            foreach ((GroupDelaySpectra spectra, int extractionStart) in parts)
            {
                if (spectra.Spectrum.Length != length ||
                    spectra.TimeWeighted.Length != length)
                {
                    throw new ArgumentException(
                        "All spectra must share one FFT length.", nameof(parts));
                }

                double shift = targetExtractionStart - extractionStart;
                double weightShift = (extractionStart - targetExtractionStart) / (double)sampleRate;
                for (int bin = 0; bin < length; bin++)
                {
                    Complex h = spectra.Spectrum[bin];
                    Complex t = spectra.TimeWeighted[bin] + h * weightShift;
                    if (shift != 0.0)
                    {
                        Complex rotation = Complex.FromPolarCoordinates(
                            1.0, Math.Tau * bin * shift / length);
                        h *= rotation;
                        t *= rotation;
                    }

                    spectrum[bin] += h;
                    weighted[bin] += t;
                }
            }

            return new GroupDelaySpectra(spectrum, weighted);
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

        /// <summary>One Auto reference for every channel and sum; accepting only the reference measurement makes per-channel flattening impossible by accident.</summary>
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


        /// <summary>Minimum phase from the windowed magnitude: no delay or reflection component.</summary>
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

            // Magnitude-derived (Bode), so the τ detrend does not apply.
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

        /// <summary>Measured minus minimum phase: the all-pass part a minimum-phase EQ cannot correct.</summary>
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

            // Both start at bin 1: measured index j is bin j + 1.
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

        /// <summary>τ (ms, absolute from IR sample 0) that flattens the excess phase: energy-weighted slope and dominant-peak estimates.</summary>
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

        // Always-on: bridges single-bin interference nulls without changing the visible curve.
        private const double GroupDelayStabilizationOctaves = 1.0 / 48.0;

        // Smoothing never narrows below the window's resolution (1/T). See docs/tech/phase-and-group-delay.md#group-delay-smoothing.
        private const double GroupDelayResolutionHalfWidthFactor = 0.5;

        public static AnalysisCurve GetGroupDelay(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            double smoothingInverseOctaves,
            double magnitudeGateDb = -30.0) =>
            GetGroupDelayCurves(
                measurement,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                smoothingInverseOctaves,
                magnitudeGateDb,
                includeMinimumPhase: false).Measured;

        /// <summary>Group-delay curves (measured, minimum, excess) over one Fixed gate; shared smoothing and validity gate keep the subtraction bin-exact.</summary>
        public static GroupDelayCurveSet GetGroupDelayCurves(
            IImpulseMeasurement measurement,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs,
            double smoothingInverseOctaves,
            double magnitudeGateDb = -30.0,
            bool includeMinimumPhase = false) =>
            GetGroupDelayCurves(
                measurement,
                new PhaseAnalysisSettings(
                    PhaseWindowMode.Fixed,
                    PhaseAnalysisSettings.DefaultFdwCycles,
                    PhaseDetrendMode.Off,
                    ManualDetrendMilliseconds: 0.0,
                    gateOffsetMs,
                    leftMs,
                    plateauMs,
                    rightMs,
                    Unwrap: false,
                    SmoothingInverseOctaves: 0.0),
                smoothingInverseOctaves,
                magnitudeGateDb,
                includeMinimumPhase);

        /// <summary>Group-delay curves through the Fixed or FDW window. Under FDW: the identity on the stitched bank, not the derivative of FDW phase; only window fields of <paramref name="settings"/> are read. See docs/tech/phase-and-group-delay.md#fdw-group-delay.</summary>
        public static GroupDelayCurveSet GetGroupDelayCurves(
            IImpulseMeasurement measurement,
            PhaseAnalysisSettings settings,
            double smoothingInverseOctaves,
            double magnitudeGateDb = -30.0,
            bool includeMinimumPhase = false)
        {
            (Complex[] spectrum, Complex[]? weighted) = BuildAnalysisSpectra(
                measurement, settings, timeWeighted: true, out int extractionStart);
            return BuildGroupDelayCurves(
                spectrum,
                weighted!,
                extractionStart,
                measurement.SampleRate,
                settings,
                smoothingInverseOctaves,
                magnitudeGateDb,
                includeMinimumPhase,
                lowestMeasuredFrequencyHz: 0.0,
                highestMeasuredFrequencyHz: double.PositiveInfinity);
        }

        /// <summary>Curves over a prebuilt pair; <paramref name="settings"/> must carry the window geometry the pair was analysed through.</summary>
        public static GroupDelayCurveSet GetGroupDelayCurves(
            GroupDelaySpectra spectra,
            int extractionStart,
            int sampleRate,
            PhaseAnalysisSettings settings,
            double smoothingInverseOctaves,
            double magnitudeGateDb = -30.0,
            bool includeMinimumPhase = false,
            double lowestMeasuredFrequencyHz = 0.0,
            double highestMeasuredFrequencyHz = double.PositiveInfinity)
        {
            ArgumentNullException.ThrowIfNull(spectra);
            if (spectra.Spectrum.Length != spectra.TimeWeighted.Length)
            {
                throw new ArgumentException(
                    "The spectrum and its time-weighted twin must share one FFT length.",
                    nameof(spectra));
            }

            return BuildGroupDelayCurves(
                spectra.Spectrum,
                spectra.TimeWeighted,
                extractionStart,
                sampleRate,
                settings,
                smoothingInverseOctaves,
                magnitudeGateDb,
                includeMinimumPhase,
                lowestMeasuredFrequencyHz,
                highestMeasuredFrequencyHz);
        }

        /// <summary>Smoothing floor: half the resolution of the window applied at the frequency (f/16 at 8 cycles under FDW).</summary>
        internal static double GroupDelayMinimumHalfWidthHz(
            double frequencyHz,
            PhaseAnalysisSettings settings,
            int sampleRate) =>
            FdwGateGeometry.Resolve(settings, sampleRate).MinimumHalfWidthHz(frequencyHz);

        private static GroupDelayCurveSet BuildGroupDelayCurves(
            Complex[] spectrum,
            Complex[] timeWeightedSpectrum,
            int extractionStart,
            int sampleRate,
            PhaseAnalysisSettings settings,
            double smoothingInverseOctaves,
            double magnitudeGateDb,
            bool includeMinimumPhase,
            double lowestMeasuredFrequencyHz,
            double highestMeasuredFrequencyHz)
        {
            int n = spectrum.Length;
            double invSampleRate = 1.0 / sampleRate;
            int halfLength = n / 2;
            double binWidthHz = sampleRate / (double)n;

            // Smooth numerator and |H|² separately: energy-weighted, near-null spikes get weight ≈ 0.
            double[] numerator = new double[halfLength];
            double[] energy = new double[halfLength];
            for (int i = 1; i < halfLength; i++)
            {
                Complex h = spectrum[i];
                Complex t = timeWeightedSpectrum[i];
                numerator[i] = t.Real * h.Real + t.Imaginary * h.Imaginary;
                energy[i] = h.Real * h.Real + h.Imaginary * h.Imaginary;
            }

            // Minimum phase preserves |H|: one denominator and validity gate for all curves. Under FDW it follows the windowed magnitude.
            double[]? minimumNumerator = includeMinimumPhase
                ? ComputeMinimumPhaseGroupDelayNumerator(spectrum, invSampleRate)
                : null;

            double decodedOctaves =
                SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves);
            double smoothingOctaves = decodedOctaves > 0.0
                ? decodedOctaves
                : GroupDelayStabilizationOctaves;

            FdwGateGeometry geometry = FdwGateGeometry.Resolve(settings, sampleRate);
            double[] smoothedNumerator;
            double[] smoothedEnergy;
            double[]? smoothedMinimumNumerator;
            if (settings.WindowMode == PhaseWindowMode.Fixed)
            {
                double minHalfWidthHz = geometry.MinimumHalfWidthHz(frequencyHz: 0.0);
                smoothedNumerator =
                    SmoothBinsHann(numerator, smoothingOctaves, binWidthHz, minHalfWidthHz);
                smoothedEnergy =
                    SmoothBinsHann(energy, smoothingOctaves, binWidthHz, minHalfWidthHz);
                smoothedMinimumNumerator = minimumNumerator == null
                    ? null
                    : SmoothBinsHann(
                        minimumNumerator, smoothingOctaves, binWidthHz, minHalfWidthHz);
            }
            else
            {
                Func<double, double> minHalfWidthAt = geometry.MinimumHalfWidthHz;
                smoothedNumerator =
                    SmoothBinsHann(numerator, smoothingOctaves, binWidthHz, minHalfWidthAt);
                smoothedEnergy =
                    SmoothBinsHann(energy, smoothingOctaves, binWidthHz, minHalfWidthAt);
                smoothedMinimumNumerator = minimumNumerator == null
                    ? null
                    : SmoothBinsHann(
                        minimumNumerator, smoothingOctaves, binWidthHz, minHalfWidthAt);
            }

            double maxEnergy = 0.0;
            for (int i = 1; i < halfLength; i++)
            {
                maxEnergy = Math.Max(maxEnergy, smoothedEnergy[i]);
            }

            if (maxEnergy <= 0.0)
            {
                return BuildGroupDelayCurveSet(
                    new List<SignalPoint>(),
                    includeMinimumPhase ? new List<SignalPoint>() : null,
                    includeMinimumPhase ? new List<SignalPoint>() : null);
            }

            // Local-envelope gate with the −60 dB global backstop, as in the unwrap; energies are |H|², so dB/10.
            double[] localEnvelope = SmoothBinsHann(
                smoothedEnergy, 1.0, binWidthHz, minHalfWidthHz: 0.0);
            double globalGate = maxEnergy * Math.Pow(10.0, -60.0 / 10.0);
            double absoluteGate = 1e-16;
            double localGateRatio = Math.Pow(10.0, magnitudeGateDb / 10.0);

            List<SignalPoint> data = new(halfLength);
            List<SignalPoint>? minimumData =
                smoothedMinimumNumerator == null ? null : new(halfLength);
            List<SignalPoint>? excessData =
                smoothedMinimumNumerator == null ? null : new(halfLength);

            // Absolute group delay, referenced to the IR start.
            double absoluteStartTime = extractionStart * invSampleRate;

            for (int i = 1; i < halfLength; i++)
            {
                double f = i * binWidthHz;

                double minEnergy = Math.Max(
                    Math.Max(localEnvelope[i] * localGateRatio, globalGate),
                    absoluteGate);
                if (smoothedEnergy[i] < minEnergy ||
                    f < lowestMeasuredFrequencyHz ||
                    f > highestMeasuredFrequencyHz)
                {
                    data.Add(new SignalPoint(f, double.NaN));
                    minimumData?.Add(new SignalPoint(f, double.NaN));
                    excessData?.Add(new SignalPoint(f, double.NaN));
                    continue;
                }

                double delaySeconds = smoothedNumerator[i] / smoothedEnergy[i];
                double measuredMilliseconds =
                    (delaySeconds + absoluteStartTime) * 1000.0;
                data.Add(new SignalPoint(f, measuredMilliseconds));

                if (smoothedMinimumNumerator != null)
                {
                    double minimumMilliseconds =
                        smoothedMinimumNumerator[i] / smoothedEnergy[i] * 1000.0;
                    minimumData!.Add(new SignalPoint(f, minimumMilliseconds));
                    excessData!.Add(new SignalPoint(
                        f, measuredMilliseconds - minimumMilliseconds));
                }
            }

            return BuildGroupDelayCurveSet(data, minimumData, excessData);
        }

        private static GroupDelayCurveSet BuildGroupDelayCurveSet(
            List<SignalPoint> measured,
            List<SignalPoint>? minimum,
            List<SignalPoint>? excess) =>
            new(
                new AnalysisCurve("Group Delay", measured),
                minimum == null
                    ? null
                    : new AnalysisCurve(
                        "Minimum Phase GD",
                        minimum,
                        AnalysisCurveKind.MinimumPhaseGroupDelay),
                excess == null
                    ? null
                    : new AnalysisCurve(
                        "Excess GD",
                        excess,
                        AnalysisCurveKind.ExcessGroupDelay));

        // Built like the measured numerator so both share |H|². h_min is real; the FFT's imaginary residue is dropped.
        private static double[] ComputeMinimumPhaseGroupDelayNumerator(
            Complex[] spectrum,
            double invSampleRate)
        {
            int n = spectrum.Length;
            double[] magnitude = new double[n];
            for (int i = 0; i < n; i++)
            {
                magnitude[i] = spectrum[i].Magnitude;
            }

            Complex[] minimumSpectrum = MinimumPhase.Reconstruct(magnitude);

            Complex[] buffer = new Complex[n];
            Array.Copy(minimumSpectrum, buffer, n);
            Fourier.Inverse(buffer, FourierOptions.Matlab);
            for (int i = 0; i < n; i++)
            {
                buffer[i] = new Complex(buffer[i].Real * (i * invSampleRate), 0.0);
            }
            Fourier.Forward(buffer, FourierOptions.Matlab);

            int halfLength = n / 2;
            double[] numerator = new double[halfLength];
            for (int i = 1; i < halfLength; i++)
            {
                Complex h = minimumSpectrum[i];
                Complex t = buffer[i];
                numerator[i] = t.Real * h.Real + t.Imaginary * h.Imaginary;
            }

            return numerator;
        }

        // Seed grid only; SmoothingRelativeTolerance is the guarantee.
        private const double SmoothingAnchorsPerKernel = 16.0;

        // Allowed chord error at a span midpoint, relative (~0.04 dB).
        private const double SmoothingRelativeTolerance = 0.005;

        // Tolerance floor as a fraction of the peak: callers already drop bins below −60 dB.
        private const double SmoothingScaleFloor = 1e-6;

        private const int SmoothingMinimumSpan = 2;

        // Psychoacoustic mode falls back to its base width: cubic averaging is meaningless for signed phase.
        private static List<SignalPoint> SmoothPhaseCurve(
            List<SignalPoint> data, double smoothingInverseOctaves)
        {
            double octaves = SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves);
            return octaves > 0 ? SmoothLinear(data, octaves) : data;
        }

        // Non-negative Hann kernel on log anchors with midpoint-checked chords. See docs/tech/phase-and-group-delay.md#anchored-hann-smoothing.
        internal static double[] SmoothBinsHann(
            double[] source,
            double smoothingOctaves,
            double binWidthHz,
            double minHalfWidthHz)
        {
            double halfWidthFloor = Math.Max(minHalfWidthHz, binWidthHz * 2.0);
            return SmoothBinsHann(
                source, smoothingOctaves, binWidthHz, _ => halfWidthFloor, floored: true);
        }

        // Floor varying along the band (FDW resolution follows frequency).
        private static double[] SmoothBinsHann(
            double[] source,
            double smoothingOctaves,
            double binWidthHz,
            Func<double, double> minHalfWidthHzAt) =>
            SmoothBinsHann(source, smoothingOctaves, binWidthHz, minHalfWidthHzAt, floored: false);

        private static double[] SmoothBinsHann(
            double[] source,
            double smoothingOctaves,
            double binWidthHz,
            Func<double, double> minHalfWidthHzAt,
            bool floored)
        {
            int count = source.Length;
            double[] result = new double[count];
            if (count < 2)
            {
                return result;
            }

            double frequencyRatio = Math.Pow(2.0, smoothingOctaves * 0.5);
            double twoBins = binWidthHz * 2.0;
            Func<double, double> halfWidthFloor = floored
                ? minHalfWidthHzAt
                : frequency => Math.Max(minHalfWidthHzAt(frequency), twoBins);

            double peak = 0.0;
            for (int i = 1; i < count; i++)
            {
                peak = Math.Max(peak, Math.Abs(source[i]));
            }

            double toleranceFloor = peak * SmoothingScaleFloor;

            // At least one bin per step; the low end degenerates to exact per-bin evaluation.
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

        // A chord is accepted only when it matches the exact midpoint value; otherwise split.
        private static void FillSpan(
            double[] source,
            double[] result,
            int low,
            int high,
            double lowValue,
            double highValue,
            double binWidthHz,
            double frequencyRatio,
            Func<double, double> halfWidthFloor,
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

        private static double HannAverageAt(
            double[] source,
            int index,
            double binWidthHz,
            double frequencyRatio,
            Func<double, double> halfWidthFloor)
        {
            double frequency = index * binWidthHz;
            double halfDelta = Math.Max(
                frequency * (frequencyRatio - 1.0),
                halfWidthFloor(frequency));
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
