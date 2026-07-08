using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    public static partial class DataHelper
    {
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
    }
}
