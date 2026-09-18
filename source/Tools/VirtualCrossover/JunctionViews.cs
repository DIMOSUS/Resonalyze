using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The lower plot's junction analyses of one adjacent pair: correlation and score against an extra delay, and
/// the arrival-coherence ladder. Pure; the AI package reads the same views. See docs/tech/virtual-dsp-panel.md#junction-views.</summary>
internal static class JunctionViews
{
    // Both channels PROCESSED, so lag 0 is the current alignment; the score is the surface Auto delay searches.
    // See docs/tech/virtual-dsp-panel.md#junction-views.
    public static JunctionCorrelationView BuildCorrelationView(
        AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildCorrelationView");
        int sampleRate = pair.Lower.SampleRate;
        (Complex[] lower, Complex[] upper,
            ValidSampleRange lowerRange, ValidSampleRange upperRange) =
            CropJunctionPair(pair, scope, sampleRate);
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
        AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildCoherenceView");
        int sampleRate = pair.Lower.SampleRate;
        (Complex[] lower, Complex[] upper,
            ValidSampleRange lowerRange, ValidSampleRange upperRange) =
            CropJunctionPair(pair, scope, sampleRate);
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
    private static (Complex[] Lower, Complex[] Upper,
        ValidSampleRange LowerRange, ValidSampleRange UpperRange)
        CropJunctionPair(
            AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope, int sampleRate)
    {
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
        return (lower, upper,
            Shifted(pair.Lower, lower), Shifted(pair.Upper, upper));
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
