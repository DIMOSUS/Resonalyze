using Resonalyze.Dsp;

namespace Resonalyze.Options;

internal sealed record CalibrationBaseOption(string? Id, string Label)
{
    public override string ToString() => Label;
}

internal sealed record ProtectionGridOption(MicrophoneProtectionGrid Grid, string Label)
{
    public override string ToString() => Label;
}

internal sealed record AngleReferenceOption(MicrophoneAngleReference Reference, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One angle estimate's fields as the estimate dialog shows them, written into the definition only on OK.
/// See docs/tech/sweep-measurement.md#calibration-dialogs-code-map.</summary>
internal sealed class AngleCalibrationSession
{
    public static readonly NumericFieldRange AngleRange = new(0m, 90m, 1);
    public static readonly NumericFieldRange DiameterRange = new(
        (decimal)MicrophoneCalibrationDefinition.MinFrontDiameterMm,
        (decimal)MicrophoneCalibrationDefinition.MaxFrontDiameterMm,
        2);

    public static readonly IReadOnlyList<ProtectionGridOption> Grids =
    [
        new(MicrophoneProtectionGrid.Unknown, "unknown (widest uncertainty)"),
        new(MicrophoneProtectionGrid.Fitted, "fitted"),
        new(MicrophoneProtectionGrid.Removed, "removed")
    ];

    public static readonly IReadOnlyList<AngleReferenceOption> References =
    [
        new(MicrophoneAngleReference.GrasGeometry, "GRAS geometry (size and grid)"),
        new(MicrophoneAngleReference.SonarworksXref20, "Sonarworks XREF 20 (measured)")
    ];

    public AngleCalibrationSession(
        MicrophoneCalibrationDefinition definition,
        IReadOnlyList<MicrophoneCalibrationDefinition> baseCandidates)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(baseCandidates);
        Name = definition.Name;
        AngleDegrees = AngleRange.Clamp(definition.AngleDegrees);
        DiameterMm = DiameterRange.Clamp(definition.FrontDiameterMm);
        Bases =
        [
            new CalibrationBaseOption(null, "The microphone's 0° calibration"),
            .. baseCandidates.Select(candidate => new CalibrationBaseOption(candidate.Id, candidate.Name))
        ];
        // A base that is gone, or not offered, reads as the 0° calibration.
        BaseIndex = 0;
        for (int index = 1; index < Bases.Count; index++)
        {
            if (string.Equals(Bases[index].Id, definition.BaseId, StringComparison.OrdinalIgnoreCase))
            {
                BaseIndex = index;
                break;
            }
        }

        GridIndex = definition.Grid switch
        {
            MicrophoneProtectionGrid.Fitted => 1,
            MicrophoneProtectionGrid.Removed => 2,
            _ => 0
        };
        ReferenceIndex = definition.Reference == MicrophoneAngleReference.SonarworksXref20 ? 1 : 0;
    }

    public string Name { get; set; }

    public IReadOnlyList<CalibrationBaseOption> Bases { get; }

    public int BaseIndex { get; set; }

    public decimal AngleDegrees { get; private set; }

    public decimal DiameterMm { get; private set; }

    public int GridIndex { get; set; }

    public int ReferenceIndex { get; set; }

    /// <summary>A named microphone carries measured behaviour, so size and grid are not inputs.</summary>
    public bool GeometryApplies => Request.Reference == MicrophoneAngleReference.GrasGeometry;

    public MicrophoneAngleRequest Request => new(
        (double)AngleDegrees,
        (double)DiameterMm,
        GridIndex >= 0 && GridIndex < Grids.Count ? Grids[GridIndex].Grid : MicrophoneProtectionGrid.Unknown,
        ReferenceIndex >= 0 && ReferenceIndex < References.Count
            ? References[ReferenceIndex].Reference
            : MicrophoneAngleReference.GrasGeometry);

    public void SetAngle(decimal degrees) => AngleDegrees = AngleRange.Assign(degrees);

    public void SetDiameter(decimal millimetres) => DiameterMm = DiameterRange.Assign(millimetres);

    public void CommitTo(MicrophoneCalibrationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        MicrophoneAngleRequest request = Request;
        definition.Name = Name;
        definition.Kind = MicrophoneCalibrationKind.Angle;
        definition.BaseId = BaseIndex >= 0 && BaseIndex < Bases.Count ? Bases[BaseIndex].Id : null;
        definition.AngleDegrees = request.AngleDegrees;
        definition.FrontDiameterMm = request.FrontDiameterMm;
        definition.Grid = request.Grid;
        definition.Reference = request.Reference;
        definition.Normalize();
    }
}
