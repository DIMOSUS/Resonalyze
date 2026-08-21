using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    public static partial class DataHelper
    {
        // The impulse traces over the WHOLE record, on its own timeline: the onset, the
        // peak and the decay tail are one curve, and two records can be read against one
        // clock. The traces are built whole rather than to a length setting so that
        // navigating — zooming out to the tail, in to a sample — is a gesture and not a
        // trip to the settings panel; how much of it the view OPENS on is the caller's
        // framing, as is where zero sits and what the levels are normalized against.
        public static ImpulseCurveSet GetImpulseCurves(
            IImpulseMeasurement measurement,
            ImpulseResponseOptions opt,
            ImpulseRenderFrame frame)
        {
            ArgumentNullException.ThrowIfNull(measurement);
            ArgumentNullException.ThrowIfNull(opt);

            int length = Math.Max(1, measurement.ImpulseResponse?.Length ?? 0);
            Complex[] extracted = ExtractWindow(measurement, 0, length);

            // The real part carries the response; the imaginary residue an IFFT leaves
            // behind is numerical noise. Reading it in one place keeps the linear and
            // the dB rendering of the SAME trace consistent — the previous renderer
            // drew Re() linearly but |z| in dB, so a record with a residue showed two
            // different curves depending on the scale.
            double sign = opt.Invert ? -1.0 : 1.0;
            var samples = new double[length];
            for (int i = 0; i < length; i++)
            {
                samples[i] = extracted[i].Real * sign;
            }

            // A band filter replaces the signal every trace is derived from, so the peak,
            // the reference level and the SNR below all come to describe THE BAND. That is
            // the question the filter is asked — when does this band arrive, and how loud
            // is it — and it is why the broadband arrival marker stays on the plot beside
            // it: the band's peak is only worth reading against something.
            if (TryCreateBandWindow(opt, length, measurement.SampleRate) is { } band)
            {
                samples = ApplyBandWithoutCircularWrap(samples, band);
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

            // Levels are normalized against ONE peak for every curve on the plot, not
            // against each curve's own: how far a trace sits below the reference is a
            // figure of the comparison, while the peak belongs to the record. Two
            // records 4 dB apart, each normalized to itself, read as identical.
            double reference = frame.ReferencePeak is { } shared && shared > 0.0
                ? shared
                : ownPeak > 0.0
                    ? ownPeak
                    : 1.0;

            // The envelope costs an FFT over the whole displayed window, so it is
            // computed only when it is actually drawn — and the peak's confidence
            // figure, which reads that same envelope, rides along with it rather
            // than paying for a second transform of its own.
            AnalysisCurve? envelopeCurve = null;
            double? snrDb = null;
            if (opt.ShowEnvelope)
            {
                double[] envelope = EnvelopeWithoutCircularWrap(samples);
                snrDb = ownPeak > 0.0
                    ? SignalEnvelope.EstimatePeakConfidenceDecibels(envelope, ownPeak)
                    : null;
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

        // The X coordinate of a sample: the record's own index moved to wherever the
        // view put its zero, in the unit the view asked for. The origin is a double so
        // a sub-sample arrival estimate lands where it actually is.
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
                    ScaleAmplitude(samples[i], opt.AmplitudeScale, reference)));
            }

            return data;
        }

        // The band's fade skirt is half its pass width: proportional, so a third-octave
        // band is not handed an octave-wide transition, and 0.5 octaves at the full-octave
        // setting — the same shape the Time Alignment probe filters with.
        private const double BandFadeFraction = 0.5;

        // The zero-phase band mask for the requested band, sized for the padded buffer
        // the filter runs in; null when no band filter is selected or the record has no
        // usable sample rate.
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
                PaddedLength(length),
                sampleRate,
                opt.BandCenterHz,
                opt.BandFilterOctaves,
                opt.BandFilterOctaves * BandFadeFraction);
        }

        // Both the band mask and the Hilbert transform are circular over the analysis
        // window, and both have kernels that reach far outside a single sample — the
        // narrower the band, the longer its kernel. Everything that transforms here does
        // it on a buffer twice the drawn length, so what wraps lands in the padding.
        private static int PaddedLength(int length) =>
            DspMath.NextPowerOfTwo(length * 2);

        private static double[] ApplyBandWithoutCircularWrap(
            double[] samples,
            double[] window)
        {
            var padded = new double[window.Length];
            Array.Copy(samples, padded, samples.Length);

            double[] filtered = BandpassWindow.Apply(padded, window);
            var result = new double[samples.Length];
            Array.Copy(filtered, result, samples.Length);
            return result;
        }

        // The discrete Hilbert transform is CIRCULAR, and the analysis window is a bare
        // cut of the record: over it, the 1/t skirt of the onset wraps around the end
        // and lifts the far side of the envelope by tens of dB — a decay tail that is
        // pure arithmetic (a 175 ms window read −45 dB at its right edge against a
        // −82 dB noise floor). Padding to twice the length parks the wrap beyond
        // everything that is drawn, and the power-of-two length is the cheap FFT too.
        private static double[] EnvelopeWithoutCircularWrap(double[] samples)
        {
            var padded = new double[PaddedLength(samples.Length)];
            Array.Copy(samples, padded, samples.Length);

            double[] full = SignalEnvelope.Envelope(padded);
            var envelope = new double[samples.Length];
            Array.Copy(full, envelope, samples.Length);
            return envelope;
        }

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
                    ScaleAmplitude(magnitude[i], opt.AmplitudeScale, reference)));
            }

            return data;
        }

        // The step is the running integral of the impulse: what the system would do if
        // the input jumped to a level and stayed there. It is ALWAYS emitted normalized
        // (1.0 = the divisor below) for an axis of its own, in every scale. Expressing
        // it in the impulse's units to share one axis reads well on paper and fails on
        // real records: any DC or low-frequency content integrates into a step many
        // times the impulse peak (a synthetic cabin IR reached 1000 %), which flattens
        // the impulse into a line at the bottom of its own plot. dB cannot hold a
        // signed quantity that crosses zero either way.
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

        private static double ScaleAmplitude(
            double value,
            ImpulseAmplitudeScale scale,
            double reference) =>
            scale switch
            {
                // Raw sample values: the recording level and the deconvolution gain are
                // part of them, which is exactly what makes two records comparable.
                ImpulseAmplitudeScale.Linear => value,
                ImpulseAmplitudeScale.PercentOfPeak => 100.0 * value / reference,
                _ => AmplitudeToDecibels(Math.Abs(value) / reference)
            };

        // A centred (zero-phase) moving average over the requested duration. Centred
        // because this is a timing instrument: a trailing average would slide every
        // reflection later by half its own window and quietly falsify the arrival the
        // rest of the app measures.
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
            // Prefix sums make the average cost independent of the window width — the
            // widths that are useful on a long low-frequency tail are the expensive ones.
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
    }
}
