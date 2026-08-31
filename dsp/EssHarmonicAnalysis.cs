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
    /// <summary>The window's inclusive length in samples.</summary>
    /// <remarks>Reserve API: no caller in the solution today (see AGENTS.md).</remarks>
    public int NominalLength => EndSample - StartSample + 1;
}

/// <summary>
/// A harmonic packet's complex spectrum. A packet is a CONTAINED impulse response
/// (the linear or an harmonic IR), isolated by a unity-plateau window that covers
/// it: over the plateau the window is 1, so the FFT magnitude IS the packet's
/// transfer magnitude directly. That is why <see cref="AmplitudeAt"/> reads the
/// raw magnitude — no coherent-gain division (which is the TONE normalization and
/// would make |Hn|/|H1| depend on the two windows' lengths). Reading the plateau
/// magnitude makes the ratio window-length independent, so HDn is an honest ratio.
/// <see cref="WindowCoherentGain"/> is retained for diagnostics only.
/// </summary>
public sealed record WindowedSpectrum(
    Complex[] Bins,
    int FftLength,
    int SourceWindowLength,
    double WindowCoherentGain,
    double SampleRateHz)
{
    /// <summary>
    /// The packet's transfer magnitude at <paramref name="bin"/>. For an IR sitting
    /// under the window plateau the windowed FFT equals the IR's DFT, so the raw
    /// magnitude is the right quantity and it is independent of the window length,
    /// window shape and zero-pad factor — the invariant that makes |Hn|/|H1| exact.
    /// </summary>
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

/// <summary>
/// One extracted harmonic packet: its window geometry and its normalized
/// spectrum. Order 1 is the linear response; orders >= 2 are distortion products.
/// </summary>
public sealed record HarmonicPacket(
    int Order,
    HarmonicWindowDefinition Window,
    WindowedSpectrum Spectrum);

/// <summary>
/// How cleanly one packet is isolated in time. The window edges (toward the
/// neighbouring harmonics) are compared with the packet peak: a well-separated
/// packet has decayed far below its peak by the edge; a slowly-decaying one (bass,
/// short sweeps, car cabins) still carries energy there and leaks into — or is
/// polluted by — the neighbour. <see cref="LeadingEdgeEnergyDb"/> is the edge
/// toward the higher harmonic (earlier in time), <see cref="TrailingEdgeEnergyDb"/>
/// toward the lower harmonic (the packet's own decay, later in time).
/// <see cref="IsBelowNoiseFloor"/> marks the opposite of a leak: the window holds
/// no packet at all, only the record's own noise floor — the harmonic is too small
/// to resolve, which is a property of a CLEAN capture, not a fault. Such an order
/// is still not drawable (its "curve" would be the noise floor), so it stays
/// unreliable, but it carries no warning.
/// </summary>
public sealed record HarmonicPacketValidity(
    int Order,
    double LeadingEdgeEnergyDb,
    double TrailingEdgeEnergyDb,
    bool IsReliable,
    string? Warning,
    bool IsBelowNoiseFloor = false);

/// <summary>
/// Per-order isolation quality for a decomposition, plus the human-readable
/// warnings for the orders whose packets overlap a neighbour.
/// </summary>
public sealed record HarmonicValidity(
    bool IsValid,
    IReadOnlyList<HarmonicPacketValidity> Packets,
    IReadOnlyList<string> Warnings);

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
    EssSweepMetadata Sweep,
    HarmonicValidity Validity);

/// <summary>
/// One channel's harmonic-content reading from
/// <see cref="EssHarmonicAnalysis.MeasureHarmonicEnergy"/>.
/// <see cref="DetectedDb"/> is the energy that stood above the local floors,
/// relative to the linear packet (null when nothing did).
/// <see cref="CeilingDb"/> is the upper bound on the harmonic content of the
/// orders that were JUDGED: the summed energy of their whole isolation windows
/// (the geometric-mean-bounded regions the ESS model confines each order's
/// energy to), relative to the linear packet. The full window, because a
/// harmonic impulse response has time extent and its energy can sit anywhere
/// inside it — a probe-sized ceiling missed a harmonic one sample past the
/// probe. And no floor subtracted, because the flanks bound the background
/// beside a packet, never inside it. For a perfectly quiet record the ceiling
/// is negative infinity.
/// <para>
/// <see cref="CompleteCoverage"/> says whether that was every requested order.
/// When false — the probes of some order ran off the record's front — the
/// ceiling speaks only for the orders it covers, and a caller must not certify
/// cleanliness from it: an unread order can hide anything, including a packet
/// that sits inside the record while its outer flank does not. A detection
/// needs no such qualifier — what was found was found. "Nothing detected" is
/// therefore never "clean" by itself: certifying takes a below-threshold
/// ceiling AND complete coverage.
/// </para>
/// </summary>
public readonly record struct EssHarmonicEnergy(
    double? DetectedDb,
    double CeilingDb,
    bool CompleteCoverage);

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
/// window spanning HD2..HD5: a shared window forces the THD sum to combine the
/// packets complex-wise, making it depend on their relative phase.
/// </summary>
public static class EssHarmonicAnalysis
{
    // Overlap classification, per Farina-style packet isolation. Edge energy is
    // read relative to the packet peak: below the reliable margin the packet is
    // well isolated; between the two margins it is drawn with a marginal-isolation
    // warning (the common case on real captures, where a practical sweep does not
    // fully isolate the high harmonics); only above the invalid margin — where a
    // neighbour genuinely swamps the packet — is the order dropped and left out of
    // THD. The drop margin is deliberately lenient so real HD3/HD4 curves survive
    // (caveated) rather than vanishing.
    private const double ReliableEdgeDb = -40.0;
    private const double InvalidEdgeDb = -12.0;

    // Fraction of the window, at each boundary, treated as the "edge" region whose
    // residual energy signals overlap with the adjacent packet.
    private const double EdgeRegionFraction = 0.15;

    // The edge test above is RELATIVE to the packet's own peak, so on its own it
    // cannot tell a leaking packet from no packet at all: a harmonic that fell
    // below the noise floor leaves a windowful of flat noise, whose plateau
    // maximum (sigma·sqrt(2·ln M) over M plateau samples, ~3–4 sigma for the
    // lengths in play) reads ~10–12 dB above the edge RMS — squarely inside the
    // "overlaps its neighbour" verdict. A window whose maximum — over the WHOLE
    // window, so a packet hiding between the plateau and an edge counts too —
    // stays within this margin of the record's own tail-noise RMS is therefore
    // read as noise, not as a packet: 16 dB covers the noise crest factor up to
    // M ≈ 10^7 samples, while a genuine leak — a visible curve — sits far above
    // the floor and is untouched.
    private const double BelowNoiseWindowPeakDb = 16.0;

    // The edge regions get a second, tighter bound of their own: an edge RMS is
    // averaged over hundreds of samples and sits tight on the true noise RMS, so
    // a modest margin suffices — and it catches low-crest coherent contamination
    // (a neighbour's tail, the linear packet's skirt) whose maximum stays under
    // the window-peak ceiling above. Either failure keeps the warned overlap
    // verdict instead of blessing the capture as clean.
    private const double BelowNoiseEdgeMarginDb = 6.0;

    // Tail-noise chunking: the quiet region is split into equal chunks whose RMS
    // values are combined by a median, so one stray thump in the tail cannot
    // inflate the noise estimate. Below the minimum chunk length the tail is too
    // short to trust and the below-noise test is skipped entirely.
    private const int TailNoiseChunkCount = 8;
    private const int MinTailNoiseChunkLength = 128;

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

    // Probe radius around a packet centre. A millisecond holds a packet's core
    // at any supported rate; the spacing cap below keeps a probe from reaching
    // into its own neighbours, which close in as the order rises.
    private const double PacketProbeSeconds = 0.001;

    // Share of the tightest packet-to-boundary spacing a probe may occupy, so a
    // packet probe and the two floor probes flanking it never overlap.
    private const int ProbeSpacingDivisor = 3;

    // How far a harmonic packet must stand above the between-packet floor to
    // be read as a packet at all, rather than as the record's own noise. The
    // threshold serves DETECTION only — it bounds nothing about what an
    // undetected packet may contain, because the floor is read on the flanks
    // and the packet's interior background can sit anywhere below it.
    private const double PacketAboveFloorDb = 6.0;

    /// <summary>
    /// The harmonic content of one deconvolved sweep record: the energy of its
    /// harmonic packets, summed, relative to the linear packet, in dB. A wired
    /// electrical path reads far below -40 dB; a loudspeaker measured through
    /// the air reads tens of dB below the linear packet; an input stage being
    /// driven past its limit reads within ~15 dB of it.
    /// <para>
    /// Equal-width probes at the packet centres, each judged against the floor
    /// read on BOTH sides of it (at the half orders, the packet boundaries,
    /// where no harmonic can live), so a record that is simply noisy reports
    /// nothing instead of reporting its noise as distortion. The floor is local
    /// to each order and taken as the louder of the two flanks: the residue
    /// between packets is not stationary — leakage and packet tails vary along
    /// the record — so one quiet stretch must not license every other order.
    /// </para>
    /// <para>
    /// Null means the record could not be judged at all: a geometry where even
    /// the second order's probes do not fit inside the record (the
    /// deconvolution is a LINEAR convolution, so there is nothing before index
    /// 0 to read and a packet placed there is absent, not wrapped), or a
    /// non-finite or empty linear packet. Null is never a synonym for "clean"
    /// — only for "no verdict". Anything else carries what was found, the
    /// ceiling over the orders that were judged, and whether that was all of
    /// them; see <see cref="EssHarmonicEnergy"/> for why a caller must not
    /// read "nothing detected" — or an incompletely covered ceiling — as
    /// "clean".
    /// </para>
    /// </summary>
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

        // One radius for every probe — the ratio only means something when the
        // packet and the floor it is measured against are read over the same
        // width. It is sized by the TIGHTEST spacing in play, which is between
        // the highest order and its upper boundary; the orders below have more
        // room than they need.
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
            // The upper boundary is the farthest of the three from the peak, so
            // once it runs off the front of the record no higher order fits
            // either — and an order without both flanks has no floor to stand
            // against, which is a missing measurement, not a quiet one. The
            // coverage flag records the cut: the orders past it were NOT read
            // (one of them may even have its packet inside the record), and
            // the result must say so or a partial read would pass for a
            // certificate of the whole.
            if (Probe(impulseResponse, sweep, peakIndex, order, radius) is not { } packet ||
                Probe(impulseResponse, sweep, peakIndex, order - 0.5, radius) is not { } lower ||
                Probe(impulseResponse, sweep, peakIndex, order + 0.5, radius) is not { } upper)
            {
                completeCoverage = false;
                break;
            }

            judged = true;
            double floor = Math.Max(lower, upper);
            // The ceiling term is the energy of the order's WHOLE isolation
            // window — the same geometric-mean-bounded region AnalyzeEssHarmonics
            // isolates packets with — not the probe's. A harmonic impulse
            // response has time extent (ringing, band-limiting, an off-centre
            // peak), and a probe-sized ceiling certified clean a -20 dB
            // harmonic sitting ONE SAMPLE past the probe's edge while still
            // deep inside its order's window. Nothing is subtracted from the
            // window energy either: the flanks bound the background beside a
            // packet, never inside it.
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
        // The ceiling is what a caller may certify against — together with the
        // coverage flag: the total energy measured at the packet positions
        // bounds the harmonic content of the JUDGED orders from above wherever
        // the packets' interior background actually sits. For a perfectly
        // quiet, fully covered record it honestly reads as negative infinity.
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

    // Energy in a probe centred on a packet, or null when the probe does not fit
    // inside the record.
    private static double? PacketEnergy(
        IReadOnlyList<double> impulseResponse,
        int centre,
        int radius) =>
        RangeEnergy(impulseResponse, centre - radius, centre + radius);

    // Energy over an inclusive sample range, or null when the range does not
    // fit inside the record or the content is non-finite. Deliberately NOT
    // circular: the deconvolution returns a linear convolution, so an index
    // outside it addresses unrelated samples rather than the other end of a
    // ring.
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

            // The linear packet's later "edge" is the room decay, not a harmonic
            // neighbour, so only the harmonic packets are checked for overlap.
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

    // A robust time-domain noise amplitude read from the quiet tail after the
    // linear packet and its reverb guard — the same region EssNoise draws its
    // spectral estimate from. Returns 0 when the record has no usable tail;
    // callers then skip the below-noise classification and fall back to the
    // plain overlap verdict.
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

    // Compares the residual energy at each window edge with the packet peak. A
    // contained (fast-decaying) packet reads far below its peak at both edges; a
    // packet that has not decayed by the edge is leaking into its neighbour — but
    // a packet whose plateau never rises above the record's tail noise holds no
    // harmonic at all and is classified below-noise instead of overlapping.
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

        // Nothing anywhere in the window stands above the record's own noise —
        // the maximum is taken over the WHOLE window, not just the plateau the
        // overlap heuristic peaks at, so a packet hiding in a shoulder between
        // the plateau and an edge disqualifies the verdict too — and the edge
        // RMS values are themselves at the noise floor, which additionally
        // catches low-crest coherent contamination the maximum could miss.
        // Then there is nothing to leak and nothing to draw: dropped like an
        // overlap, but without a warning — an unresolvably small harmonic is
        // the mark of a clean capture, not a measurement fault. Anything hot
        // over a noise-level plateau falls through to the overlap verdict below
        // instead of being blessed as clean.
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
