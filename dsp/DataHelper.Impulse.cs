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
        // The whole record's unsmoothed envelope and its SNR depend on the record and the band only (an envelope is blind
        // to sign): a time unit, origin, scale or framing edit rebuilds the view, not them (72-185 ms on 262 k samples).
        private static readonly ConditionalWeakTable<Complex[], ImpulseEnvelopeReading> ImpulseEnvelopeReadings = new();

        private readonly record struct ImpulseEnvelopeBand(int Length, int SampleRate, double? CenterHz, double? Octaves);

        private sealed record ImpulseEnvelopeReading(ImpulseEnvelopeBand Band, double[] Envelope, double? SnrDb);

        // Traces span the whole record on its own timeline; opening length, zero and reference are the caller's framing.
        public static ImpulseCurveSet GetImpulseCurves(
            IImpulseMeasurement measurement,
            ImpulseResponseOptions opt,
            ImpulseRenderFrame frame)
        {
            ArgumentNullException.ThrowIfNull(measurement);
            ArgumentNullException.ThrowIfNull(opt);

            int length = Math.Max(1, measurement.ImpulseResponse?.Length ?? 0);
            Complex[] extracted = ExtractWindow(measurement, 0, length);

            // Real part only (the IFFT imaginary residue is noise), read once so linear and dB show the same trace.
            double sign = opt.Invert ? -1.0 : 1.0;
            var samples = new double[length];
            for (int i = 0; i < length; i++)
            {
                samples[i] = extracted[i].Real * sign;
            }

            // A band filter replaces the source signal: peak, reference and SNR then describe the band.
            if (TryCreateBandWindow(opt, length, measurement.SampleRate) is { } band)
            {
                samples = BandpassWindow.Apply(samples, band);
            }

            double ownPeak = 0.0;
            int ownPeakIndex = 0;
            for (int i = 0; i < length; i++)
            {
                double magnitude = Math.Abs(samples[i]);
                if (magnitude > ownPeak)
                {
                    ownPeak = magnitude;
                    ownPeakIndex = i;
                }
            }

            // One reference peak for every curve: two records 4 dB apart must not both read 0 dB.
            double reference = frame.ReferencePeak is { } shared && shared > 0.0
                ? shared
                : ownPeak > 0.0
                    ? ownPeak
                    : 1.0;

            // Envelope costs an FFT: computed only when drawn, and the SNR figure rides on it.
            AnalysisCurve? envelopeCurve = null;
            double? snrDb = null;
            if (opt.ShowEnvelope)
            {
                ImpulseEnvelopeReading reading = WholeRecordEnvelope(measurement, opt, samples);
                snrDb = reading.SnrDb;
                double[] envelope = (double[])reading.Envelope.Clone();
                SmoothEnvelopeInPlace(envelope, opt.EnvelopeSmoothingMs, measurement.SampleRate);
                envelopeCurve = new AnalysisCurve(
                    "Envelope (ETC)",
                    RenderMagnitudeTrace(
                        envelope, opt, frame, measurement.SampleRate, reference),
                    AnalysisCurveKind.ImpulseEnvelope);
            }

            return new ImpulseCurveSet(
                opt.ShowImpulse
                    ? new AnalysisCurve(
                        "Impulse Response",
                        RenderSignedTrace(samples, opt, frame, measurement.SampleRate, reference))
                    : null,
                envelopeCurve,
                opt.ShowStep
                    ? new AnalysisCurve(
                        "Step Response",
                        RenderStepTrace(samples, opt, frame, measurement.SampleRate, reference),
                        AnalysisCurveKind.ImpulseStep)
                    : null,
                reference,
                ownPeakIndex,
                snrDb);
        }

        private static ImpulseEnvelopeReading WholeRecordEnvelope(
            IImpulseMeasurement measurement,
            ImpulseResponseOptions opt,
            double[] samples)
        {
            bool banded = opt.HasBandFilter(measurement.SampleRate);
            var band = new ImpulseEnvelopeBand(
                samples.Length,
                measurement.SampleRate,
                banded ? opt.BandCenterHz : null,
                banded ? opt.BandFilterOctaves : null);
            Complex[]? record = measurement.ImpulseResponse;
            if (record != null &&
                ImpulseEnvelopeReadings.TryGetValue(record, out ImpulseEnvelopeReading? cached) &&
                cached.Band == band)
            {
                return cached;
            }

            double[] envelope = SignalEnvelope.Envelope(samples);
            // Against the envelope peak, as Time Alignment grades it, so the SNR figures match.
            double envelopePeak = 0.0;
            for (int i = 0; i < envelope.Length; i++)
            {
                envelopePeak = Math.Max(envelopePeak, envelope[i]);
            }

            var reading = new ImpulseEnvelopeReading(
                band,
                envelope,
                envelopePeak > 0.0
                    ? SignalEnvelope.EstimatePeakConfidenceDecibels(envelope, envelopePeak)
                    : null);
            if (record != null)
            {
                ImpulseEnvelopeReadings.AddOrUpdate(record, reading);
            }

            return reading;
        }

        private static double ImpulseTime(
            int index,
            ImpulseResponseOptions opt,
            ImpulseRenderFrame frame,
            int sampleRate)
        {
            double offset = index - frame.OriginSamples;
            return opt.TimeUnit == ImpulseTimeUnit.Milliseconds && sampleRate > 0
                ? offset * 1000.0 / sampleRate
                : offset;
        }

        private static List<SignalPoint> RenderSignedTrace(
            double[] samples,
            ImpulseResponseOptions opt,
            ImpulseRenderFrame frame,
            int sampleRate,
            double reference)
        {
            var data = new List<SignalPoint>(samples.Length);
            for (int i = 0; i < samples.Length; i++)
            {
                data.Add(new SignalPoint(
                    ImpulseTime(i, opt, frame, sampleRate),
                    ScaleImpulseAmplitude(samples[i], opt.AmplitudeScale, reference)));
            }

            return data;
        }

        // Fade skirt proportional to pass width, like the Time Alignment probe filter.
        private const double BandFadeFraction = 0.5;

        private static double[]? TryCreateBandWindow(
            ImpulseResponseOptions opt,
            int length,
            int sampleRate)
        {
            if (!opt.HasBandFilter(sampleRate))
            {
                return null;
            }

            return BandpassWindow.Create(
                length,
                sampleRate,
                opt.BandCenterHz,
                opt.BandFilterOctaves,
                opt.BandFilterOctaves * BandFadeFraction);
        }

        // Circular mask and Hilbert are correct: the record is one deconvolution period. Padding adds an edge that lifts the silent region. See docs/tech/phase-and-group-delay.md#impulse-view-traces.

        private static List<SignalPoint> RenderMagnitudeTrace(
            double[] magnitude,
            ImpulseResponseOptions opt,
            ImpulseRenderFrame frame,
            int sampleRate,
            double reference)
        {
            var data = new List<SignalPoint>(magnitude.Length);
            for (int i = 0; i < magnitude.Length; i++)
            {
                data.Add(new SignalPoint(
                    ImpulseTime(i, opt, frame, sampleRate),
                    ScaleImpulseAmplitude(magnitude[i], opt.AmplitudeScale, reference)));
            }

            return data;
        }

        // Step is always normalized for its own axis: DC integrates far past the impulse peak. See docs/tech/phase-and-group-delay.md#impulse-view-traces.
        private static List<SignalPoint> RenderStepTrace(
            double[] samples,
            ImpulseResponseOptions opt,
            ImpulseRenderFrame frame,
            int sampleRate,
            double reference)
        {
            var step = new double[samples.Length];
            double running = 0.0;
            double stepPeak = 0.0;
            for (int i = 0; i < samples.Length; i++)
            {
                running += samples[i];
                step[i] = running;
                stepPeak = Math.Max(stepPeak, Math.Abs(running));
            }

            double divisor = opt.NormalizeStepToImpulsePeak
                ? reference
                : stepPeak > 0.0
                    ? stepPeak
                    : 1.0;

            var data = new List<SignalPoint>(step.Length);
            for (int i = 0; i < step.Length; i++)
            {
                data.Add(new SignalPoint(
                    ImpulseTime(i, opt, frame, sampleRate),
                    step[i] / divisor));
            }

            return data;
        }

        /// <summary>Public so overlays re-scale stored raw traces exactly as the live curve.</summary>
        public static double ScaleImpulseAmplitude(
            double value,
            ImpulseAmplitudeScale scale,
            double reference) =>
            scale switch
            {
                ImpulseAmplitudeScale.Linear => value,
                ImpulseAmplitudeScale.PercentOfPeak => 100.0 * value / reference,
                _ => AmplitudeToDecibels(Math.Abs(value) / reference)
            };

        // Centred (zero-phase): a trailing average would delay every reflection by half its window.
        private static void SmoothEnvelopeInPlace(
            double[] envelope,
            double durationMs,
            int sampleRate)
        {
            if (durationMs <= 0.0 || sampleRate <= 0 || envelope.Length < 3)
            {
                return;
            }

            int span = (int)Math.Round(durationMs * sampleRate / 1000.0);
            if (span < 2)
            {
                return;
            }

            int half = span / 2;
            var prefix = new double[envelope.Length + 1];
            for (int i = 0; i < envelope.Length; i++)
            {
                prefix[i + 1] = prefix[i] + envelope[i];
            }

            for (int i = 0; i < envelope.Length; i++)
            {
                int first = Math.Max(0, i - half);
                int last = Math.Min(envelope.Length - 1, i + half);
                envelope[i] = (prefix[last + 1] - prefix[first]) / (last - first + 1);
            }
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

            // Wiener-Khinchin on a mean-removed signal zero-padded to 2x, so lags cannot wrap.
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

                // Correlation is linear in the shifted signal, so interpolating it equals interpolate-then-correlate.
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
    }
}
