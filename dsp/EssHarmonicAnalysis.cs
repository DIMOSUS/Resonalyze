using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>ESS geometry used to place harmonic packets. See docs/tech/dsp-ess-harmonics.md.</summary>
/// <param name="FullAmplitudeEndFrequencyHz">Where the sweep's fade-out begins: above it the deconvolution's band gate
/// tapers to <paramref name="EndFrequencyHz"/>, so a harmonic product up there reads low. Null or non-positive when
/// unknown, read as flat to the end.</param>
public sealed record EssSweepMetadata(
    double StartFrequencyHz,
    double EndFrequencyHz,
    double DurationSeconds,
    double SampleRateHz,
    int SweepSampleCount,
    int DeconvolutionPeakIndex,
    double? FullAmplitudeEndFrequencyHz = null)
{
    public double NyquistHz => SampleRateHz / 2.0;

    /// <summary>Top of the band the deconvolution passes at full gain.</summary>
    public double FlatEndFrequencyHz =>
        FullAmplitudeEndFrequencyHz is > 0 and var flat ? Math.Min(flat, EndFrequencyHz) : EndFrequencyHz;

    public double FrequencyRatio => EndFrequencyHz / StartFrequencyHz;

    /// <summary>The app's sweep ends at Nyquist and spans <paramref name="octaves"/> downward.</summary>
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

    /// <summary>Above min(sweep end, Nyquist/order) harmonic n·f leaves the sweep band or passes Nyquist; a harmonic
    /// also stops where its product n·f enters the fade-out, whose taper the deconvolution applies to it.</summary>
    public double MaxExcitationHz(int order) =>
        order <= 1
            ? Math.Min(EndFrequencyHz, NyquistHz)
            : Math.Min(EndFrequencyHz, Math.Min(FlatEndFrequencyHz, NyquistHz) / order);
}

/// <summary>Absolute sample indices; higher harmonics sit earlier, so Start &lt;= Peak &lt;= End.</summary>
public sealed record HarmonicWindowDefinition(
    int Order,
    int PeakSample,
    int StartSample,
    int EndSample,
    int FadeInSamples,
    int FadeOutSamples)
{
    /// <remarks>Reserve API: no caller in the solution today (see AGENTS.md).</remarks>
    public int NominalLength => EndSample - StartSample + 1;
}

/// <summary>Packet spectrum read as raw plateau magnitude (no coherent-gain division), so |Hn|/|H1| is window-length independent.
/// See docs/tech/dsp-ess-harmonics.md#spectrum-normalization.</summary>
public sealed record WindowedSpectrum(
    Complex[] Bins,
    int FftLength,
    int SourceWindowLength,
    double WindowCoherentGain,
    double SampleRateHz)
{
    public double AmplitudeAt(int bin)
    {
        if ((uint)bin >= (uint)Bins.Length)
        {
            return 0.0;
        }

        return Bins[bin].Magnitude;
    }

    public double BinFrequencyHz(int bin) => bin * SampleRateHz / FftLength;

    public int UsableBinCount => FftLength / 2;
}

public sealed record HarmonicPacket(
    int Order,
    HarmonicWindowDefinition Window,
    WindowedSpectrum Spectrum);

/// <summary>Edge energy vs packet peak. <see cref="IsBelowNoiseFloor"/>: window holds only noise (clean capture, not drawable, no warning).</summary>
public sealed record HarmonicPacketValidity(
    int Order,
    double LeadingEdgeEnergyDb,
    double TrailingEdgeEnergyDb,
    bool IsReliable,
    string? Warning,
    bool IsBelowNoiseFloor = false);

public sealed record HarmonicValidity(
    bool IsValid,
    IReadOnlyList<HarmonicPacketValidity> Packets,
    IReadOnlyList<string> Warnings);

public sealed record EssHarmonicDecomposition(
    HarmonicPacket Linear,
    IReadOnlyList<HarmonicPacket> Harmonics,
    EssSweepMetadata Sweep,
    HarmonicValidity Validity);

/// <summary><see cref="CeilingDb"/> bounds only the judged orders; certifying clean needs a below-threshold ceiling AND <see cref="CompleteCoverage"/>.
/// See docs/tech/dsp-ess-harmonics.md#harmonic-energy-probe.</summary>
public readonly record struct EssHarmonicEnergy(
    double? DetectedDb,
    double CeilingDb,
    bool CompleteCoverage);

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

/// <summary>Each order gets its own window; a shared window would make THD phase-dependent.</summary>
public static class EssHarmonicAnalysis
{
    // Overlap margins; see docs/tech/dsp-ess-harmonics.md#overlap-classification.
    private const double ReliableEdgeDb = -40.0;
    private const double InvalidEdgeDb = -12.0;

    private const double EdgeRegionFraction = 0.15;

    // Noise crest factor up to M≈10^7 samples; see docs/tech/dsp-ess-harmonics.md#below-noise-classification.
    private const double BelowNoiseWindowPeakDb = 16.0;

    private const double BelowNoiseEdgeMarginDb = 6.0;

    // Median of chunk RMS so one thump cannot inflate the noise estimate.
    private const int TailNoiseChunkCount = 8;
    private const int MinTailNoiseChunkLength = 128;

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

    /// <summary>Fractional orders address packet boundaries, so centres and edges share one geometry.</summary>
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

    private const double PacketProbeSeconds = 0.001;

    private const int ProbeSpacingDivisor = 3;

    // Detection only: the floor is read on the flanks and bounds nothing inside a packet.
    private const double PacketAboveFloorDb = 6.0;

    /// <summary>Harmonic packet energy relative to the linear packet, in dB. Null means no verdict, never clean.
    /// See docs/tech/dsp-ess-harmonics.md#harmonic-energy-probe.</summary>
    public static EssHarmonicEnergy? MeasureHarmonicEnergy(
        IReadOnlyList<double> impulseResponse,
        EssSweepMetadata sweep,
        int maxHarmonic = 5)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        ArgumentNullException.ThrowIfNull(sweep);
        if (maxHarmonic < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHarmonic));
        }

        if (impulseResponse.Count < 64 ||
            sweep.SampleRateHz <= 0 ||
            sweep.SweepSampleCount <= 0 ||
            !(sweep.FrequencyRatio > 1.0))
        {
            return null;
        }
        int peakIndex = sweep.DeconvolutionPeakIndex;
        if ((uint)peakIndex >= (uint)impulseResponse.Count)
        {
            return null;
        }

        // One radius for all probes, sized by the tightest spacing (highest order to its upper boundary).
        int spacing = HarmonicOffsetSamples(sweep, maxHarmonic + 0.5) -
            HarmonicOffsetSamples(sweep, maxHarmonic);
        int radius = Math.Min(
            (int)Math.Round(PacketProbeSeconds * sweep.SampleRateHz),
            spacing / ProbeSpacingDivisor);
        if (radius < 1)
        {
            return null;
        }

        if (PacketEnergy(impulseResponse, peakIndex, radius) is not { } linear || linear <= 0)
        {
            return null;
        }

        double aboveFloor = Math.Pow(10.0, PacketAboveFloorDb / 10.0);
        double detectedEnergy = 0;
        double ceilingEnergy = 0;
        bool judged = false;
        bool completeCoverage = true;
        for (int order = 2; order <= maxHarmonic; order++)
        {
            // Past this cut the orders were not read; the coverage flag must say so.
            if (Probe(impulseResponse, sweep, peakIndex, order, radius) is not { } packet ||
                Probe(impulseResponse, sweep, peakIndex, order - 0.5, radius) is not { } lower ||
                Probe(impulseResponse, sweep, peakIndex, order + 0.5, radius) is not { } upper)
            {
                completeCoverage = false;
                break;
            }

            judged = true;
            double floor = Math.Max(lower, upper);
            // Whole isolation window, no floor subtracted: a probe-sized ceiling missed harmonics just past the probe.
            HarmonicWindowDefinition window = BuildWindow(sweep, order, fadeFraction: 0.0);
            if (RangeEnergy(impulseResponse, window.StartSample, window.EndSample)
                is not { } windowEnergy)
            {
                completeCoverage = false;
                break;
            }
            ceilingEnergy += windowEnergy;
            if (packet > floor * aboveFloor)
            {
                detectedEnergy += packet - floor;
            }
        }

        if (!judged)
        {
            return null;
        }
        return new EssHarmonicEnergy(
            detectedEnergy > 0 ? 10.0 * Math.Log10(detectedEnergy / linear) : null,
            10.0 * Math.Log10(ceilingEnergy / linear),
            completeCoverage);
    }

    private static double? Probe(
        IReadOnlyList<double> impulseResponse,
        EssSweepMetadata sweep,
        int peakIndex,
        double order,
        int radius) =>
        PacketEnergy(
            impulseResponse,
            peakIndex - HarmonicOffsetSamples(sweep, order),
            radius);

    private static double? PacketEnergy(
        IReadOnlyList<double> impulseResponse,
        int centre,
        int radius) =>
        RangeEnergy(impulseResponse, centre - radius, centre + radius);

    // Not circular: the deconvolution is a linear convolution.
    private static double? RangeEnergy(
        IReadOnlyList<double> impulseResponse,
        int start,
        int end)
    {
        if (start < 0 || end >= impulseResponse.Count)
        {
            return null;
        }

        double energy = 0;
        for (int i = start; i <= end; i++)
        {
            energy += impulseResponse[i] * impulseResponse[i];
        }
        return double.IsFinite(energy) ? energy : null;
    }

    /// <summary>Order 1's later edge mirrors its earlier one (boundary at 1/√2).</summary>
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
            // Truncate around the peak (does not happen for the app's sweeps).
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

        double tailNoiseAmplitude =
            EstimateTailNoiseAmplitude(deconvolvedImpulse, windows[0]);

        var packets = new HarmonicPacket[options.MaxHarmonic];
        var validities = new HarmonicPacketValidity[options.MaxHarmonic - 1];
        var warnings = new List<string>();
        for (int order = 1; order <= options.MaxHarmonic; order++)
        {
            HarmonicWindowDefinition definition = windows[order - 1];
            WindowedSpectrum spectrum = ComputeWindowedSpectrum(
                deconvolvedImpulse,
                definition,
                fftLength,
                sweep.SampleRateHz);
            packets[order - 1] = new HarmonicPacket(order, definition, spectrum);

            // The linear packet's later edge is room decay, not a neighbour.
            if (order >= 2)
            {
                HarmonicPacketValidity validity =
                    EvaluatePacketOverlap(deconvolvedImpulse, definition, tailNoiseAmplitude);
                validities[order - 2] = validity;
                if (validity.Warning != null)
                {
                    warnings.Add(validity.Warning);
                }
            }
        }

        return new EssHarmonicDecomposition(
            packets[0],
            packets.Skip(1).ToArray(),
            sweep,
            new HarmonicValidity(
                warnings.Count == 0,
                validities,
                warnings));
    }

    // Returns 0 when there is no usable tail; the below-noise test is then skipped.
    private static double EstimateTailNoiseAmplitude(
        ReadOnlySpan<double> impulse,
        HarmonicWindowDefinition linearWindow)
    {
        int linearStart = Math.Max(0, linearWindow.StartSample);
        int linearEnd = Math.Min(impulse.Length - 1, linearWindow.EndSample);
        int linearLength = Math.Max(1, linearEnd - linearStart + 1);

        int guard = Math.Max(linearLength, linearLength / 2 + 1);
        int regionStart = Math.Min(
            Math.Max(0, linearWindow.EndSample) + guard,
            impulse.Length);
        int regionLength = impulse.Length - regionStart;

        int chunkLength = regionLength / TailNoiseChunkCount;
        if (chunkLength < MinTailNoiseChunkLength)
        {
            return 0.0;
        }

        var chunkRms = new double[TailNoiseChunkCount];
        for (int chunk = 0; chunk < TailNoiseChunkCount; chunk++)
        {
            int start = regionStart + chunk * chunkLength;
            double sumSquares = 0.0;
            for (int i = start; i < start + chunkLength; i++)
            {
                sumSquares += impulse[i] * impulse[i];
            }
            chunkRms[chunk] = Math.Sqrt(sumSquares / chunkLength);
        }

        Array.Sort(chunkRms);
        double median = 0.5 * (
            chunkRms[TailNoiseChunkCount / 2 - 1] + chunkRms[TailNoiseChunkCount / 2]);
        return double.IsFinite(median) ? median : 0.0;
    }

    private static HarmonicPacketValidity EvaluatePacketOverlap(
        ReadOnlySpan<double> impulse,
        HarmonicWindowDefinition window,
        double tailNoiseAmplitude)
    {
        int start = Math.Max(0, window.StartSample);
        int end = Math.Min(impulse.Length - 1, window.EndSample);
        int length = end - start + 1;
        if (length < 4)
        {
            return new HarmonicPacketValidity(
                window.Order, double.NegativeInfinity, double.NegativeInfinity, true, null);
        }

        int peak = Math.Clamp(window.PeakSample, start, end);
        double peakEnergy = Math.Abs(impulse[peak]);
        int plateauFrom = Math.Max(start, peak - length / 8);
        int plateauTo = Math.Min(end, peak + length / 8);
        for (int i = plateauFrom; i <= plateauTo; i++)
        {
            peakEnergy = Math.Max(peakEnergy, Math.Abs(impulse[i]));
        }

        if (!(peakEnergy > 0.0))
        {
            return new HarmonicPacketValidity(
                window.Order, double.NegativeInfinity, double.NegativeInfinity, true, null);
        }

        int edgeLength = Math.Max(1, (int)Math.Round(EdgeRegionFraction * length));
        double leadingDb = EdgeEnergyDb(impulse, start, edgeLength, peakEnergy);
        double trailingDb = EdgeEnergyDb(impulse, end - edgeLength + 1, edgeLength, peakEnergy);

        // Whole-window max plus edge RMS both at the noise floor: nothing to draw, dropped without a warning.
        if (tailNoiseAmplitude > 0.0)
        {
            double windowPeakEnergy = 0.0;
            for (int i = start; i <= end; i++)
            {
                windowPeakEnergy = Math.Max(windowPeakEnergy, Math.Abs(impulse[i]));
            }

            double edgeCeiling =
                tailNoiseAmplitude * Math.Pow(10.0, BelowNoiseEdgeMarginDb / 20.0);
            bool belowNoise =
                windowPeakEnergy <=
                    tailNoiseAmplitude * Math.Pow(10.0, BelowNoiseWindowPeakDb / 20.0) &&
                peakEnergy * Math.Pow(10.0, leadingDb / 20.0) <= edgeCeiling &&
                peakEnergy * Math.Pow(10.0, trailingDb / 20.0) <= edgeCeiling;
            if (belowNoise)
            {
                return new HarmonicPacketValidity(
                    window.Order, leadingDb, trailingDb,
                    IsReliable: false, Warning: null, IsBelowNoiseFloor: true);
            }
        }

        double worst = Math.Max(leadingDb, trailingDb);
        bool reliable = worst <= InvalidEdgeDb;
        string? warning = null;
        if (worst > InvalidEdgeDb)
        {
            warning = $"HD{window.Order} packet overlaps its neighbour " +
                $"({worst:0} dB at the window edge); increase the sweep duration " +
                "or narrow the analysed range.";
        }
        else if (worst > ReliableEdgeDb)
        {
            warning = $"HD{window.Order} isolation is marginal " +
                $"({worst:0} dB at the window edge); a longer sweep would help.";
        }

        return new HarmonicPacketValidity(window.Order, leadingDb, trailingDb, reliable, warning);
    }

    private static double EdgeEnergyDb(
        ReadOnlySpan<double> impulse,
        int from,
        int length,
        double peakEnergy)
    {
        int start = Math.Max(0, from);
        int end = Math.Min(impulse.Length, from + length);
        if (end <= start)
        {
            return double.NegativeInfinity;
        }

        double sumSquares = 0.0;
        for (int i = start; i < end; i++)
        {
            sumSquares += impulse[i] * impulse[i];
        }

        double rms = Math.Sqrt(sumSquares / (end - start));
        return rms > 0.0 ? 20.0 * Math.Log10(rms / peakEnergy) : double.NegativeInfinity;
    }
}
