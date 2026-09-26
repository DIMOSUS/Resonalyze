using System.Numerics;
using System.Runtime.InteropServices;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The lower plot's junction analyses of one adjacent pair: correlation and score against an extra delay, and
/// the arrival-coherence ladder. Pure; the AI package reads the same views. See docs/tech/virtual-dsp-panel.md#junction-views.</summary>
internal static class JunctionViews
{
    // Both channels PROCESSED, so lag 0 is the current alignment; the score is the surface Auto delay searches.
    // See docs/tech/virtual-dsp-panel.md#junction-views.
    public static JunctionCorrelationView BuildCorrelationView(
        AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope) =>
        BuildCorrelationView(pair, Crop(pair, scope));

    public static JunctionCorrelationView BuildCorrelationView(AdjacentPair pair, JunctionCrop crop)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildCorrelationView");
        (Complex[] lower, Complex[] upper,
            ValidSampleRange lowerRange, ValidSampleRange upperRange, int sampleRate) = crop;
        // No anchor: each channel windowed at its own band-limited front, as Auto delay measures junctions.

        // 1.5 crossover periods each side (floor 3 ms) keeps neighbouring comb lobes in view at 80 Hz.
        double windowMs = Math.Max(3.0, 1.5 * 1000.0 / pair.CrossoverHz);
        double passOctaves = Math.Log2(pair.BandHighHz / pair.BandLowHz);

        // The comb repeats per period: a tenth of a period avoids aliasing at high junctions; window/300 bounds the sweep.
        double stepMs = Math.Max(
            Math.Min(windowMs / 60.0, 100.0 / pair.CrossoverHz),
            Math.Max(0.005, windowMs / 300.0));

        List<SignalPoint> whitened = null!;
        List<SignalPoint> whitenedDirect = null!;
        List<SignalPoint> scoreNormal = null!;
        List<SignalPoint> scoreInverted = null!;
        double lowerArrivalMs = 0;
        double upperArrivalMs = 0;
        Parallel.Invoke(
            // UNTRIMMED: reflections are this curve's subject (honest at bass junctions).
            () => whitened = VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                lower, upper, sampleRate, pair.CrossoverHz, passOctaves,
                windowMs, centerLagMs: 0, phaseTransform: true),
            // Direct sound only: the cut the engine's direct-coherence witness reads.
            () =>
            {
                (Complex[] directLower, Complex[] directUpper) =
                    VirtualCrossoverAnalysis.CutDirectSoundPair(
                        lower, upper, sampleRate,
                        pair.BandLowHz, pair.BandHighHz, pair.CrossoverHz,
                        searchRangeMs: windowMs, lowerRange, upperRange);
                whitenedDirect = VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                    directLower, directUpper, sampleRate,
                    pair.CrossoverHz, passOctaves,
                    windowMs, centerLagMs: 0, phaseTransform: true);
            },
            // The search's own settings (null anchor, level match), or the drawn surface is not the searched one.
            () =>
            {
                (List<VirtualCrossoverAnalysis.JunctionSweepPoint> normal,
                    List<VirtualCrossoverAnalysis.JunctionSweepPoint> inverted) =
                    VirtualCrossoverAnalysis.JunctionLossSweepBothPolarities(
                        upper, lower, sampleRate,
                        pair.BandLowHz, pair.BandHighHz,
                        -windowMs, windowMs, stepMs,
                        gateAnchorSample: null,
                        levelMatch: true,
                        variableValidRange: upperRange,
                        fixedValidRange: lowerRange);
                scoreNormal = Penalized(normal);
                scoreInverted = Penalized(inverted);
            },
            // The band-limited envelope fronts: the number the agent package exports as arrivalLagMs. Not drawn.
            () =>
            {
                lowerArrivalMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                    lower, sampleRate, pair.BandLowHz, pair.BandHighHz, lowerRange);
                upperArrivalMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                    upper, sampleRate, pair.BandLowHz, pair.BandHighHz, upperRange);
            });

        return new JunctionCorrelationView(
            $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}",
            pair.Upper.Channel.Name,
            pair.CrossoverHz,
            pair.BandLowHz,
            pair.BandHighHz,
            whitened,
            whitenedDirect,
            scoreNormal,
            scoreInverted,
            lowerArrivalMs - upperArrivalMs);
    }

    private static List<SignalPoint> Penalized(
        List<VirtualCrossoverAnalysis.JunctionSweepPoint> sweep) =>
        sweep
            .Select(point => new SignalPoint(
                point.DelayMs,
                point.LossDb +
                    VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
                    (point.DipDb - point.LossDb)))
            .ToList();

    // See VirtualCrossoverAnalysis.ArrivalCoherenceLadder.
    public static JunctionCoherenceView BuildCoherenceView(
        AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope) =>
        BuildCoherenceView(pair, Crop(pair, scope));

    public static JunctionCoherenceView BuildCoherenceView(AdjacentPair pair, JunctionCrop crop)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildCoherenceView");
        (Complex[] lower, Complex[] upper,
            ValidSampleRange lowerRange, ValidSampleRange upperRange, int sampleRate) = crop;
        return new JunctionCoherenceView(
            $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}",
            pair.Upper.Channel.Name,
            pair.CrossoverHz,
            pair.BandLowHz,
            pair.BandHighHz,
            VirtualCrossoverAnalysis.ArrivalCoherenceLadder(
                lower, upper, sampleRate,
                pair.BandLowHz, pair.BandHighHz, pair.CrossoverHz,
                lowerRange, upperRange));
    }

    // Valid ranges are shifted into the crop frame so front detections match the search's (matters on glitch-headed records).
    public static JunctionCrop Crop(AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope)
    {
        int sampleRate = pair.Lower.SampleRate;
        List<ProcessedChannel> all = scope.Contains(pair.Lower)
            ? scope.ToList()
            : [pair.Lower, pair.Upper];
        // These records are already processed: a FIR's pre-ring sits ahead of its peak, so the crop keeps all of it and as
        // much after the peak as without one (the engine crops before the chain and needs none of this).
        int leadSamples = all.Max(item => item.ValidRange.LeadSamples);
        Complex[][] cropped = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            all.Select(item => item.ImpulseResponse).ToList(),
            AlignmentReprocessor.SearchCropLength(sampleRate) + leadSamples,
            AlignmentReprocessor.SearchCropPrePeakSamples(sampleRate) + leadSamples,
            out int cropStart);
        Complex[] lower = cropped[all.IndexOf(pair.Lower)];
        Complex[] upper = cropped[all.IndexOf(pair.Upper)];
        ValidSampleRange Shifted(ProcessedChannel item, Complex[] croppedIr) =>
            item.ValidRange.IsKnown
                ? item.ValidRange with
                {
                    StartSample = Math.Max(0, item.ValidRange.StartSample - cropStart),
                    EndSample = Math.Clamp(
                        item.ValidRange.EndSample - cropStart,
                        0,
                        croppedIr.Length)
                }
                : item.ValidRange;
        return new JunctionCrop(
            lower, upper, Shifted(pair.Lower, lower), Shifted(pair.Upper, upper), sampleRate);
    }

    // A failing view is reported missing rather than failing its caller, as the lower plot's redraw does.
    public static (JunctionCorrelationView? Correlation, JunctionCoherenceView? Coherence) BuildBoth(
        AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope)
    {
        JunctionCorrelationView? correlation = null;
        JunctionCoherenceView? coherence = null;
        try
        {
            correlation = BuildCorrelationView(pair, scope);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Agent package correlation view failed: {exception}");
        }
        try
        {
            coherence = BuildCoherenceView(pair, scope);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Agent package coherence view failed: {exception}");
        }

        return (correlation, coherence);
    }
}

/// <summary>The pair as the junction analyses read it: both records cropped to the side's shared window, their valid ranges
/// in the crop's frame, and the rate.</summary>
internal sealed record JunctionCrop(
    Complex[] Lower,
    Complex[] Upper,
    ValidSampleRange LowerRange,
    ValidSampleRange UpperRange,
    int SampleRate)
{
    /// <summary>Same samples bit for bit, same ranges and rate: every analysis of the pair reads the same input.</summary>
    public bool SameAs(JunctionCrop other) =>
        LowerRange == other.LowerRange &&
        UpperRange == other.UpperRange &&
        SampleRate == other.SampleRate &&
        SameSamples(Lower, other.Lower) &&
        SameSamples(Upper, other.Upper);

    private static bool SameSamples(Complex[] left, Complex[] right) =>
        MemoryMarshal.Cast<Complex, long>(left).SequenceEqual(MemoryMarshal.Cast<Complex, long>(right));
}

/// <summary>The last view of each kind and what it was built from, so a redraw that left the pair's cropped samples, band
/// and names alone rebuilds nothing; the crop is a copy and a peak scan, the views hundreds of milliseconds of FFTs.</summary>
internal sealed class JunctionViewCache
{
    private readonly object sync = new();
    private Entry<JunctionCorrelationView>? correlation;
    private Entry<JunctionCoherenceView>? coherence;

    public JunctionCorrelationView Correlation(AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope) =>
        Read(ref correlation, pair, scope, JunctionViews.BuildCorrelationView);

    public JunctionCoherenceView Coherence(AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope) =>
        Read(ref coherence, pair, scope, JunctionViews.BuildCoherenceView);

    private TView Read<TView>(
        ref Entry<TView>? slot,
        AdjacentPair pair,
        IReadOnlyList<ProcessedChannel> scope,
        Func<AdjacentPair, JunctionCrop, TView> build)
        where TView : IJunctionView
    {
        JunctionCrop crop = JunctionViews.Crop(pair, scope);
        lock (sync)
        {
            if (slot is { } cached && cached.Matches(pair, crop))
            {
                return cached.View;
            }
        }

        var entry = new Entry<TView>(pair.CrossoverHz, pair.BandLowHz, pair.BandHighHz, crop, build(pair, crop));
        lock (sync)
        {
            slot = entry;
        }

        return entry.View;
    }

    // Names are read off the view itself: a rename during the build cannot key it under the other name.
    private sealed record Entry<TView>(
        double CrossoverHz, double BandLowHz, double BandHighHz, JunctionCrop Crop, TView View)
        where TView : IJunctionView
    {
        public bool Matches(AdjacentPair pair, JunctionCrop crop) =>
            SameBits(CrossoverHz, pair.CrossoverHz) &&
            SameBits(BandLowHz, pair.BandLowHz) &&
            SameBits(BandHighHz, pair.BandHighHz) &&
            View.UpperName == pair.Upper.Channel.Name &&
            View.PairTitle == $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}" &&
            Crop.SameAs(crop);

        private static bool SameBits(double left, double right) =>
            BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);
    }
}
