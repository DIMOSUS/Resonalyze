using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Describes the exponential sine sweep that produced a deconvolved impulse
/// response, in enough detail to place the harmonic distortion packets and map
/// their spectra back onto the excitation-frequency axis. Everything the
/// analyzer needs is derived from these fields, so the harmonic geometry is
/// never recomputed from scattered magic constants elsewhere.
/// </summary>
public sealed record EssSweepMetadata(
    double StartFrequencyHz,
    double EndFrequencyHz,
    double DurationSeconds,
    double SampleRateHz,
    int SweepSampleCount,
    int DeconvolutionPeakIndex)
{
    public double NyquistHz => SampleRateHz / 2.0;

    public double FrequencyRatio => EndFrequencyHz / StartFrequencyHz;

    /// <summary>
    /// Builds metadata for the application's exponential sweep, which always ends
    /// at Nyquist and spans <paramref name="octaves"/> octaves downward (its phase
    /// resolves to 0.5 cycles/sample at the final sample), so the start frequency
    /// is Nyquist / 2^octaves. Callers pass only the parameters they already store.
    /// </summary>
    public static EssSweepMetadata FromExponentialSweep(
        int sampleRate,
        int octaves,
        int sweepSampleCount,
        int deconvolutionPeakIndex)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (octaves <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(octaves));
        }
        if (sweepSampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sweepSampleCount));
        }
        if (deconvolutionPeakIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deconvolutionPeakIndex));
        }

        double nyquist = sampleRate / 2.0;
        double start = nyquist / Math.Pow(2.0, octaves);
        return new EssSweepMetadata(
            start,
            nyquist,
            sweepSampleCount / (double)sampleRate,
            sampleRate,
            sweepSampleCount,
            deconvolutionPeakIndex);
    }

    /// <summary>
    /// The highest excitation frequency at which harmonic <paramref name="order"/>
    /// is observable: above min(sweep end, Nyquist/order) the product n·f leaves
    /// the sweep band or passes Nyquist, so the curve must not extend there.
    /// </summary>
    public double MaxExcitationHz(int order) =>
        Math.Min(EndFrequencyHz, NyquistHz / order);
}

/// <summary>
/// One harmonic packet's time window in the deconvolved impulse response, in
/// absolute sample indices. Smaller indices are earlier in time; higher
/// harmonics sit at earlier indices, so <see cref="StartSample"/> (the earliest
/// edge) is always &lt;= <see cref="PeakSample"/> &lt;= <see cref="EndSample"/>.
/// </summary>
public sealed record HarmonicWindowDefinition(
    int Order,
    int PeakSample,
    int StartSample,
    int EndSample,
    int FadeInSamples,
    int FadeOutSamples)
{
    public int NominalLength => EndSample - StartSample + 1;
}

/// <summary>
/// A windowed segment's complex spectrum together with everything needed to read
/// a physical tone amplitude from it independent of window length, window shape
/// and zero-padding. The single normalization contract every harmonic packet
/// passes through, so H1 and every Hn are directly comparable.
/// </summary>
public sealed record WindowedSpectrum(
    Complex[] Bins,
    int FftLength,
    int SourceWindowLength,
    double WindowCoherentGain,
    double SampleRateHz)
{
    /// <summary>
    /// The physical amplitude of a tone landing on <paramref name="bin"/>.
    /// Under the Matlab FFT convention a windowed tone of amplitude A produces a
    /// bin magnitude A·(Σw)/2, so 2·|bin|/Σw recovers A regardless of the window
    /// length or the zero-pad factor (both fold into Σw / the interpolated grid).
    /// </summary>
    public double AmplitudeAt(int bin)
    {
        if (WindowCoherentGain <= 0.0 || (uint)bin >= (uint)Bins.Length)
        {
            return 0.0;
        }

        return 2.0 * Bins[bin].Magnitude / WindowCoherentGain;
    }

    public double BinFrequencyHz(int bin) => bin * SampleRateHz / FftLength;

    public int UsableBinCount => FftLength / 2;
}

/// <summary>
/// One extracted harmonic packet: its window geometry and its normalized
/// spectrum. Order 1 is the linear response; orders >= 2 are distortion products.
/// </summary>
public sealed record HarmonicPacket(
    int Order,
    HarmonicWindowDefinition Window,
    WindowedSpectrum Spectrum);

/// <summary>
/// The result of separating a deconvolved ESS impulse response into its linear
/// packet and its harmonic distortion packets, each with a consistently
/// normalized spectrum. Pure DSP: no calibration, no smoothing, no display
/// decisions, and no loopback-transfer denominator — those belong to the layers
/// that consume this.
/// </summary>
public sealed record EssHarmonicDecomposition(
    HarmonicPacket Linear,
    IReadOnlyList<HarmonicPacket> Harmonics,
    EssSweepMetadata Sweep);

/// <summary>
/// Tuning for <see cref="EssHarmonicAnalysis.AnalyzeEssHarmonics"/>.
/// </summary>
public sealed record HarmonicAnalysisOptions(
    int MaxHarmonic = 5,
    double FadeFraction = 0.5,
    int MaxFftLength = 32768)
{
    public void Validate()
    {
        if (MaxHarmonic < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHarmonic));
        }
        if (!double.IsFinite(FadeFraction) || FadeFraction < 0.0 || FadeFraction > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(FadeFraction));
        }
        if (MaxFftLength < 256)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFftLength));
        }
    }
}

/// <summary>
/// Separates a deconvolved exponential-sweep impulse response into its linear and
/// harmonic packets. Each harmonic order gets its OWN time window (bracketed by
/// the geometric-mean boundaries to its neighbours), rather than one shared
/// window spanning HD2..HD5 — that shared window is what made the old THD sum the
/// packets complex-wise and depend on their relative phase.
/// </summary>
public static class EssHarmonicAnalysis
{
    /// <summary>
    /// The time advance of harmonic <paramref name="harmonicOrder"/> relative to
    /// the linear packet for a logarithmic sweep: Δt = L · ln(n) / ln(f2/f1).
    /// Depends only on the sweep geometry, never on the signal level.
    /// </summary>
    public static double HarmonicTimeOffsetSeconds(
        EssSweepMetadata sweep,
        double harmonicOrder)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        if (harmonicOrder <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(harmonicOrder));
        }

        return sweep.DurationSeconds * Math.Log(harmonicOrder) /
            Math.Log(sweep.FrequencyRatio);
    }

    /// <summary>
    /// The sample offset (before the linear peak) of harmonic
    /// <paramref name="harmonicOrder"/>. Fractional orders address the boundaries
    /// between packets, so the same routine places both the packet centres and the
    /// window edges — one geometry, used everywhere.
    /// </summary>
    public static int HarmonicOffsetSamples(EssSweepMetadata sweep, double harmonicOrder)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        if (harmonicOrder <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(harmonicOrder));
        }

        double offsetSamples =
            sweep.SweepSampleCount * Math.Log(harmonicOrder) /
            Math.Log(sweep.FrequencyRatio);
        return (int)Math.Round(offsetSamples);
    }

    /// <summary>
    /// Builds the isolation window for one harmonic order. The window spans from
    /// the geometric-mean boundary toward order+1 (the earlier edge) to the
    /// geometric-mean boundary toward order-1 (the later edge). Order 1 has no
    /// lower neighbour, so its later edge is the symmetric reflection (boundary at
    /// 1/√2), giving the linear packet a window centred on the peak.
    /// </summary>
    public static HarmonicWindowDefinition BuildWindow(
        EssSweepMetadata sweep,
        int order,
        double fadeFraction)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        if (order < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(order));
        }

        int peak = sweep.DeconvolutionPeakIndex - HarmonicOffsetSamples(sweep, order);

        double higherNeighbourBoundary = Math.Sqrt((double)order * (order + 1));
        double lowerNeighbourBoundary = order >= 2
            ? Math.Sqrt((double)(order - 1) * order)
            : 1.0 / Math.Sqrt(2.0);

        int start = sweep.DeconvolutionPeakIndex -
            HarmonicOffsetSamples(sweep, higherNeighbourBoundary);
        int end = sweep.DeconvolutionPeakIndex -
            HarmonicOffsetSamples(sweep, lowerNeighbourBoundary);

        int fadeIn = (int)Math.Round(fadeFraction * Math.Max(0, peak - start));
        int fadeOut = (int)Math.Round(fadeFraction * Math.Max(0, end - peak));
        return new HarmonicWindowDefinition(order, peak, start, end, fadeIn, fadeOut);
    }

    /// <summary>
    /// Computes the normalized spectrum of a windowed segment of the impulse
    /// response. The window is clamped to the available samples, faded per the
    /// definition, and zero-padded to <paramref name="fftLength"/>. The result
    /// carries the window's coherent gain so callers read amplitudes that are
    /// independent of the window length and the padding factor.
    /// </summary>
    public static WindowedSpectrum ComputeWindowedSpectrum(
        ReadOnlySpan<double> impulse,
        HarmonicWindowDefinition window,
        int fftLength,
        double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (fftLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fftLength));
        }

        int start = Math.Max(0, window.StartSample);
        int end = Math.Min(impulse.Length - 1, window.EndSample);
        int length = end - start + 1;
        if (length < 1 || impulse.Length == 0)
        {
            return new WindowedSpectrum(
                new Complex[Math.Max(1, fftLength)], Math.Max(1, fftLength), 0, 0.0, sampleRateHz);
        }

        int fft = Math.Max(DspMath.NextPowerOfTwo(Math.Min(length, fftLength)), fftLength);
        if (length > fft)
        {
            // A window longer than the common FFT is truncated to the central part
            // around the peak so the packet stays represented (does not happen for
            // the app's sweeps, where every packet fits the oversampled length).
            int overshoot = length - fft;
            int trimStart = Math.Clamp(window.PeakSample - fft / 2, start, end - fft + 1);
            start = Math.Max(start, trimStart);
            length = fft;
            end = start + length - 1;
            _ = overshoot;
        }

        double leftFraction = length > 1
            ? 2.0 * window.FadeInSamples / (length - 1)
            : 0.0;
        double rightFraction = length > 1
            ? 2.0 * window.FadeOutSamples / (length - 1)
            : 0.0;
        double[] taper = Windowing.TukeyWindow(length, leftFraction, rightFraction);

        var buffer = new Complex[fft];
        double coherentGain = 0.0;
        for (int i = 0; i < length; i++)
        {
            double weight = taper[i];
            buffer[i] = new Complex(impulse[start + i] * weight, 0.0);
            coherentGain += weight;
        }

        Fourier.Forward(buffer, FourierOptions.Matlab);
        return new WindowedSpectrum(buffer, fft, length, coherentGain, sampleRateHz);
    }

    /// <summary>
    /// Separates the deconvolved impulse response into the linear packet (order 1)
    /// and harmonic packets (orders 2..MaxHarmonic), each with a normalized
    /// spectrum on a shared FFT grid.
    /// </summary>
    public static EssHarmonicDecomposition AnalyzeEssHarmonics(
        ReadOnlySpan<double> deconvolvedImpulse,
        EssSweepMetadata sweep,
        HarmonicAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (deconvolvedImpulse.Length == 0)
        {
            throw new ArgumentException(
                "Deconvolved impulse response must not be empty.",
                nameof(deconvolvedImpulse));
        }

        var windows = new HarmonicWindowDefinition[options.MaxHarmonic];
        int maxLength = 1;
        for (int order = 1; order <= options.MaxHarmonic; order++)
        {
            HarmonicWindowDefinition definition = BuildWindow(sweep, order, options.FadeFraction);
            windows[order - 1] = definition;

            int clampedStart = Math.Max(0, definition.StartSample);
            int clampedEnd = Math.Min(deconvolvedImpulse.Length - 1, definition.EndSample);
            maxLength = Math.Max(maxLength, clampedEnd - clampedStart + 1);
        }

        int fftLength = Math.Clamp(
            DspMath.NextPowerOfTwo(maxLength),
            256,
            options.MaxFftLength);

        var packets = new HarmonicPacket[options.MaxHarmonic];
        for (int order = 1; order <= options.MaxHarmonic; order++)
        {
            HarmonicWindowDefinition definition = windows[order - 1];
            WindowedSpectrum spectrum = ComputeWindowedSpectrum(
                deconvolvedImpulse,
                definition,
                fftLength,
                sweep.SampleRateHz);
            packets[order - 1] = new HarmonicPacket(order, definition, spectrum);
        }

        return new EssHarmonicDecomposition(
            packets[0],
            packets.Skip(1).ToArray(),
            sweep);
    }
}
