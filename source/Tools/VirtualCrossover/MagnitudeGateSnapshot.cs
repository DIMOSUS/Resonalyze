using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The window every magnitude curve reads through: the same gate as the phase and impulse views, FIXED at the
/// steady-state lengths. Swapped atomically on the UI thread by the redraw, read by PLINQ workers.
/// See docs/tech/virtual-dsp-panel.md#gate-snapshot.</summary>
internal sealed record MagnitudeGateSnapshot(
    PhaseAnalysisSettings Template,
    double? PinnedOffsetMs,
    double? OppositePinnedOffsetMs,
    int SmoothingInverseOctaves)
{
    public static MagnitudeGateSnapshot Initial { get; } = new(
        new PhaseAnalysisSettings(
            PhaseWindowMode.Fixed,
            PhaseAnalysisSettings.DefaultFdwCycles,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: 0.0,
            LeftMs: FrequencyResponseOptions.SteadyStateLeftMs,
            PlateauMs: FrequencyResponseOptions.SteadyStatePlateauMs,
            RightMs: FrequencyResponseOptions.SteadyStateRightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0),
        PinnedOffsetMs: null,
        OppositePinnedOffsetMs: null,
        SmoothingInverseOctaves: 12);

    // Each side has its own pin: the active side's pin must never window the opposite side's sum.
    internal double ResolveGateOffsetMs(
        bool oppositeSide,
        int anchorPeakIndex,
        int sampleRate) =>
        (oppositeSide ? OppositePinnedOffsetMs : PinnedOffsetMs)
            ?? anchorPeakIndex * 1_000.0 / sampleRate;

    public GatedMagnitude Channel(
        Complex[] impulseResponse,
        int anchorIndex,
        int sampleRate,
        double gateOffsetMs,
        MeasuredBand band,
        CalibrationFile? calibration)
    {
        (AnalysisCurve display, AnalysisCurve unsmoothed) =
            DataHelper.GetGatedPrimarySpectrumPair(
                new ImpulseMeasurementView(impulseResponse, anchorIndex, sampleRate)
                {
                    LowestMeasuredFrequencyHz = band.LowEdgeHz,
                    HighestMeasuredFrequencyHz = band.HighEdgeHz
                },
                Template with { GateOffsetMs = gateOffsetMs },
                calibration,
                SmoothingInverseOctaves);
        return new GatedMagnitude(display, unsmoothed);
    }

    /// <summary>Gated magnitude of the channels' SUM, each contributing only where it measured.
    /// See docs/tech/virtual-dsp-panel.md#measured-sum.</summary>
    /// <remarks>Each channel's own correction goes INSIDE the sum (Σ HᵢCᵢ): one outside cannot undo two microphones.</remarks>
    public GatedMagnitude MeasuredSum(
        IReadOnlyList<ProcessedChannel> channels,
        int anchorIndex,
        double gateOffsetMs,
        Func<ProcessedChannel, CalibrationFile?> calibrationFor)
    {
        var views = new List<IImpulseMeasurement>(channels.Count);
        var calibrations = new List<CalibrationFile?>(channels.Count);
        foreach (ProcessedChannel channel in channels)
        {
            views.Add(new ImpulseMeasurementView(
                channel.ImpulseResponse, anchorIndex, channel.SampleRate)
            {
                LowestMeasuredFrequencyHz = channel.MeasuredBand.LowEdgeHz,
                HighestMeasuredFrequencyHz = channel.MeasuredBand.HighEdgeHz
            });
            calibrations.Add(calibrationFor(channel));
        }

        (AnalysisCurve display, AnalysisCurve unsmoothed) =
            DataHelper.GetGatedMeasuredMagnitudeSumPair(
                views,
                Template with { GateOffsetMs = gateOffsetMs },
                calibrations,
                SmoothingInverseOctaves);
        return new GatedMagnitude(display, unsmoothed).MeasuredBySomeChannel(channels);
    }

    /// <summary>The opposite side's window: its own pin, else its own anchor; never the shown side's.</summary>
    public double OppositeOffsetMs(VirtualCrossoverSideSum side) =>
        ResolveGateOffsetMs(oppositeSide: true, side.AnchorIndex, side.SampleRate);

    /// <summary>The opposite side's Sum through its own window, drawn beside the shown side's.</summary>
    public GatedMagnitude OppositeSum(
        VirtualCrossoverSideSum side,
        Func<ProcessedChannel, CalibrationFile?> calibrationFor) =>
        MeasuredSum(side.Channels, side.AnchorIndex, OppositeOffsetMs(side), calibrationFor);

    // Raw curves anchor on their own START; see docs/tech/virtual-dsp-panel.md#raw-curve-anchor.
    public AnalysisCurve Raw(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        MeasuredBand band,
        CalibrationFile? calibration)
    {
        int anchorIndex = ProcessedChannels.StartAnchorIndex(
            impulseResponse, peakIndex, sampleRate);
        return Channel(
            impulseResponse,
            anchorIndex,
            sampleRate,
            anchorIndex * 1_000.0 / sampleRate,
            band,
            calibration).Display;
    }

    /// <summary>Bypass response on canonical terms (own onset, fixed window, no smoothing, calibration only when given), matching
    /// how the hybrid spread threshold was calibrated.</summary>
    public AnalysisCurve CanonicalRaw(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        MeasuredBand band,
        CalibrationFile? calibration = null)
    {
        int anchorIndex = ProcessedChannels.StartAnchorIndex(
            impulseResponse, peakIndex, sampleRate);
        return DataHelper.GetGatedPrimarySpectrumPair(
            new ImpulseMeasurementView(impulseResponse, anchorIndex, sampleRate)
            {
                LowestMeasuredFrequencyHz = band.LowEdgeHz,
                HighestMeasuredFrequencyHz = band.HighEdgeHz
            },
            Template with { GateOffsetMs = anchorIndex * 1_000.0 / sampleRate },
            calibration,
            smoothingInverseOctaves: 0).Unsmoothed;
    }
}
