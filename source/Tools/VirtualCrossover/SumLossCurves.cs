using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One channel of a sum as the loss reads it: its processed response, where it measured and what corrects it.
/// Neutral so the EQ Wizard can sum a channel it re-filters with the ones frozen at handoff.</summary>
internal sealed record SumLossMember(
    Complex[] ImpulseResponse,
    int PeakIndex,
    ValidSampleRange ValidRange,
    int SampleRate,
    MeasuredBand Band,
    CalibrationFile? Calibration)
{
    public PlacementChannel Placement => new(ImpulseResponse, PeakIndex, ValidRange);
}

/// <summary>The two Sum loss curves over a set of members. See docs/tech/virtual-dsp-analysis.md#sum-loss-window-full-vs-direct.</summary>
internal static class SumLossCurves
{
    /// <summary>Full-window loss: every member through one gate at one shared anchor. Null for fewer than two members.</summary>
    public static List<SignalPoint>? Full(
        IReadOnlyList<SumLossMember> members,
        int anchorIndex,
        PhaseAnalysisSettings gate,
        double smoothingInverseOctaves)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(gate);
        if (members.Count < 2)
        {
            return null;
        }

        var views = new List<IImpulseMeasurement>(members.Count);
        var operands = new List<IReadOnlyList<SignalPoint>>(members.Count);
        var calibrations = new List<CalibrationFile?>(members.Count);
        foreach (SumLossMember member in members)
        {
            var view = new ImpulseMeasurementView(member.ImpulseResponse, anchorIndex, member.SampleRate)
            {
                LowestMeasuredFrequencyHz = member.Band.LowEdgeHz,
                HighestMeasuredFrequencyHz = member.Band.HighEdgeHz
            };
            views.Add(view);
            calibrations.Add(member.Calibration);
            // Operands unsmoothed: smoothing before the division invents corner dips.
            operands.Add(DataHelper.GetGatedPrimarySpectrumPair(
                view, gate, member.Calibration, smoothingInverseOctaves: 0).Unsmoothed.Points);
        }

        (_, AnalysisCurve sum) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            views, gate, calibrations, smoothingInverseOctaves: 0);
        return VirtualCrossoverAnalysis.SumLossCurve(
            MeasuredBySomeMember(sum.Points, members), operands, smoothingInverseOctaves);
    }

    /// <summary>Direct-sound loss from per-member 8-cycle windows rotated into one time frame (≤ 0 dB).
    /// Null for fewer than two members or mixed rates.</summary>
    public static List<SignalPoint>? Direct(
        IReadOnlyList<SumLossMember> members,
        double? pinnedOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs,
        double smoothingInverseOctaves)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count < 2)
        {
            return null;
        }

        int sampleRate = members[0].SampleRate;
        if (sampleRate <= 0 || members.Any(member => member.SampleRate != sampleRate))
        {
            return null;
        }

        List<Complex[]> spectra = JunctionPhaseSpectra.Build(
            members.Select(member => member.Placement).ToList(),
            members.Select(member => member.SampleRate).ToList(),
            sampleRate,
            pinnedOffsetMs,
            leftMs,
            plateauMs,
            rightMs);
        return Direct(members, spectra, smoothingInverseOctaves);
    }

    /// <summary>Over spectra already built, one per member in order (the junction read-out shares its own).</summary>
    public static List<SignalPoint>? Direct(
        IReadOnlyList<SumLossMember> members,
        IReadOnlyList<Complex[]> spectra,
        double smoothingInverseOctaves)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(spectra);
        if (members.Count < 2 || spectra.Count != members.Count)
        {
            return null;
        }

        int sampleRate = members[0].SampleRate;
        if (sampleRate <= 0 || members.Any(member => member.SampleRate != sampleRate))
        {
            return null;
        }

        var operands = new List<IReadOnlyList<SignalPoint>>(members.Count);
        var bands = new List<(double LowestHz, double HighestHz)>(members.Count);
        var calibrations = new List<CalibrationFile?>(members.Count);
        for (int i = 0; i < members.Count; i++)
        {
            MeasuredBand band = members[i].Band;
            CalibrationFile? calibration = members[i].Calibration;
            operands.Add(DataHelper.GetGatedMagnitude(
                spectra[i], sampleRate, band.LowEdgeHz, band.HighEdgeHz,
                calibration, smoothingInverseOctaves: 0).Points);
            bands.Add((band.LowEdgeHz, band.HighEdgeHz));
            calibrations.Add(calibration);
        }

        (_, AnalysisCurve sum) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            spectra, sampleRate, bands, calibrations, smoothingInverseOctaves: 0);
        return VirtualCrossoverAnalysis.SumLossCurve(
            MeasuredBySomeMember(sum.Points, members), operands, smoothingInverseOctaves);
    }

    private static IReadOnlyList<SignalPoint> MeasuredBySomeMember(
        IReadOnlyList<SignalPoint> curve,
        IReadOnlyList<SumLossMember> members) =>
        MeasuredBand.MaskUnmeasured(curve, members.Select(member => member.Band).ToList());
}
