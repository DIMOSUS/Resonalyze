using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

internal readonly record struct AuditionProgress(string Status, double Fraction);

/// <summary>Panel state captured when the audition button was pressed.</summary>
internal sealed record VirtualCrossoverAuditionContext(
    Complex[] LeftSum,
    Complex[] RightSum,
    int SampleRate,
    int LeftChannelCount,
    int RightChannelCount,
    string? BorrowedSide,
    Func<string?, CalibrationFile?>? CalibrationResolver,
    IReadOnlyList<MicrophoneCalibrationEntry> CalibrationEntries,
    string? InitialCalibrationId,
    VirtualCrossoverAuditionOwnCalibration OwnCalibration,
    VirtualCrossoverAuditionSpatialAverage? SpatialAverage,
    string? SpatialAverageReason);

/// <summary>What "Own (as measured)" resolves to; <c>Conflict</c> is set when channels used different calibrations (no single filter can be baked into a summed side).</summary>
internal sealed record VirtualCrossoverAuditionOwnCalibration(
    CalibrationFile? Curve,
    string? Name,
    string? Conflict);

/// <summary>The same tune's side sums with magnitudes from spatial averages, prepared by the panel.</summary>
internal sealed record VirtualCrossoverAuditionSpatialAverage(
    Complex[] LeftSum,
    Complex[] RightSum,
    IReadOnlyList<string> ReportLines);
