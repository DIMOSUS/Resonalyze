using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class AngleCalibrationSessionTests
{
    private static readonly MicrophoneCalibrationDefinition[] Bases =
    [
        new() { Id = "left", Name = "Left mic", Kind = MicrophoneCalibrationKind.File, Path = "left.cal" },
        new() { Id = "right", Name = "Right mic", Kind = MicrophoneCalibrationKind.File, Path = "right.cal" }
    ];

    private static MicrophoneCalibrationDefinition Estimate() => new()
    {
        Id = "a1",
        Name = "Rear 60",
        Kind = MicrophoneCalibrationKind.Angle,
        AngleDegrees = 60,
        FrontDiameterMm = 6.35,
        Grid = MicrophoneProtectionGrid.Removed,
        BaseId = "RIGHT"
    };

    [Fact]
    public void TheFieldsStartFromTheDefinition()
    {
        var session = new AngleCalibrationSession(Estimate(), Bases);

        Assert.Equal("Rear 60", session.Name);
        Assert.Equal(60m, session.AngleDegrees);
        Assert.Equal(6.35m, session.DiameterMm);
        Assert.Equal(["The microphone's 0° calibration", "Left mic", "Right mic"], session.Bases.Select(b => b.Label));
        Assert.Equal(2, session.BaseIndex);
        Assert.Equal(2, session.GridIndex);
        Assert.Equal(0, session.ReferenceIndex);
        Assert.True(session.GeometryApplies);
    }

    [Theory]
    [InlineData(MicrophoneProtectionGrid.Unknown, 0)]
    [InlineData(MicrophoneProtectionGrid.Fitted, 1)]
    [InlineData(MicrophoneProtectionGrid.Removed, 2)]
    public void TheGridPicksItsOption(MicrophoneProtectionGrid grid, int index)
    {
        MicrophoneCalibrationDefinition definition = Estimate();
        definition.Grid = grid;

        var session = new AngleCalibrationSession(definition, Bases);

        Assert.Equal(index, session.GridIndex);
        Assert.Equal(grid, AngleCalibrationSession.Grids[index].Grid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("gone")]
    public void ABaseNotOfferedReadsAsZeroDegrees(string? baseId)
    {
        MicrophoneCalibrationDefinition definition = Estimate();
        definition.BaseId = baseId;

        Assert.Equal(0, new AngleCalibrationSession(definition, Bases).BaseIndex);
    }

    [Fact]
    public void ANamedMicrophoneHasNoGeometry()
    {
        MicrophoneCalibrationDefinition definition = Estimate();
        definition.Reference = MicrophoneAngleReference.SonarworksXref20;

        var session = new AngleCalibrationSession(definition, Bases);

        Assert.Equal(1, session.ReferenceIndex);
        Assert.False(session.GeometryApplies);
        session.ReferenceIndex = 0;
        Assert.True(session.GeometryApplies);
    }

    [Theory]
    [InlineData(120.0, 5.0, 90, 5)]
    [InlineData(-3.0, 0.5, 0, 1)]
    [InlineData(33.33, 99.0, 33.3, 60)]
    [InlineData(12.25, 12.345, 12.2, 12.34)]
    public void ValuesHoldWhatTheirFieldsWouldShow(double angle, double diameter, double shownAngle, double shownDiameter)
    {
        MicrophoneCalibrationDefinition definition = Estimate();
        definition.AngleDegrees = angle;
        definition.FrontDiameterMm = diameter;

        var session = new AngleCalibrationSession(definition, Bases);

        Assert.Equal((decimal)shownAngle, session.AngleDegrees);
        Assert.Equal((decimal)shownDiameter, session.DiameterMm);
        session.SetAngle(95m);
        session.SetDiameter(0.2m);
        Assert.Equal(90m, session.AngleDegrees);
        Assert.Equal(1m, session.DiameterMm);
    }

    [Fact]
    public void TheRequestIsWhatTheFieldsShow()
    {
        var session = new AngleCalibrationSession(Estimate(), Bases);
        session.SetAngle(30m);
        session.SetDiameter(12.5m);
        session.GridIndex = 1;
        session.ReferenceIndex = 1;

        Assert.Equal(
            new MicrophoneAngleRequest(30, 12.5, MicrophoneProtectionGrid.Fitted, MicrophoneAngleReference.SonarworksXref20),
            session.Request);
    }

    [Fact]
    public void OkWritesTheFieldsIntoTheDefinition()
    {
        MicrophoneCalibrationDefinition definition = Estimate();
        var session = new AngleCalibrationSession(definition, Bases)
        {
            Name = "  Passenger  ",
            BaseIndex = 1,
            GridIndex = 1,
            ReferenceIndex = 0
        };
        session.SetAngle(45.5m);
        session.SetDiameter(9m);

        Assert.Equal("Rear 60", definition.Name);
        session.CommitTo(definition);

        Assert.Equal("Passenger", definition.Name);
        Assert.Equal("left", definition.BaseId);
        Assert.Equal(45.5, definition.AngleDegrees);
        Assert.Equal(9.0, definition.FrontDiameterMm);
        Assert.Equal(MicrophoneProtectionGrid.Fitted, definition.Grid);
        Assert.Equal(MicrophoneAngleReference.GrasGeometry, definition.Reference);
        Assert.Equal(MicrophoneCalibrationKind.Angle, definition.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankNameIsNamedAfterTheAngle(string name)
    {
        MicrophoneCalibrationDefinition definition = Estimate();
        var session = new AngleCalibrationSession(definition, Bases) { Name = name, BaseIndex = 0 };

        session.CommitTo(definition);

        Assert.Equal("60°", definition.Name);
        Assert.Null(definition.BaseId);
    }

    [Fact]
    public void ThePreviewSpansTheAudioBandOnALogGrid()
    {
        AngleCalibrationPreview preview = AngleCalibrationPreview.Build(new MicrophoneAngleRequest(90, 12.7));

        Assert.Equal(240, preview.Points.Count);
        Assert.Equal(20.0, preview.Points[0].FrequencyHz, 9);
        Assert.Equal(20_000.0, preview.Points[^1].FrequencyHz, 6);
        Assert.Equal(
            Math.Pow(1000, 1.0 / 239),
            preview.Points[1].FrequencyHz / preview.Points[0].FrequencyHz,
            9);
        Assert.All(preview.Points, point => Assert.InRange(point.CenterDb, point.LowerDb, point.UpperDb));
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(new MicrophoneAngleRequest(90, 12.7));
        Assert.Equal(estimate.Deltas(1_000).CenterDb, estimate.DeltaDb(1_000));
        Assert.Equal(estimate.Deltas(preview.Points[100].FrequencyHz), new MicrophoneAngleBounds(
            preview.Points[100].CenterDb, preview.Points[100].LowerDb, preview.Points[100].UpperDb));
    }

    [Fact]
    public void TheSummaryNamesTheTopOfTheBandTheSpreadAndTheReferences()
    {
        using var culture = new InvariantCultureScope();
        var request = new MicrophoneAngleRequest(90, 12.7);
        AngleCalibrationPreview preview = AngleCalibrationPreview.Build(request);
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(request);
        AngleCorrectionPoint widest = preview.Points.MaxBy(point => point.UpperDb - point.LowerDb);

        Assert.StartsWith(
            $"Estimated, not measured: {preview.Points[^1].CenterDb:+0.00;-0.00;0.00} dB at 20 kHz, references disagreeing " +
            $"by up to {widest.UpperDb - widest.LowerDb:0.00} dB around {FrequencyText.Format(widest.FrequencyHz)}.\r\n" +
            $"Built from: {string.Join(" · ", estimate.References)}.",
            preview.Summary);
    }

    [Fact]
    public void ReferencesThatAgreeShowNoSpread()
    {
        using var culture = new InvariantCultureScope();
        AngleCalibrationPreview preview = AngleCalibrationPreview.Build(new MicrophoneAngleRequest(0, 12.7));

        Assert.StartsWith(
            "Estimated, not measured: 0.00 dB at 20 kHz, from a single reference, so no spread is shown.",
            preview.Summary);
    }

    [Theory]
    [InlineData(90.0, 12.7, MicrophoneAngleReference.GrasGeometry)]
    [InlineData(90.0, 60.0, MicrophoneAngleReference.GrasGeometry)]
    [InlineData(45.0, 12.7, MicrophoneAngleReference.SonarworksXref20)]
    public void AModelThatStopsShortSaysWhere(double angle, double diameter, MicrophoneAngleReference reference)
    {
        using var culture = new InvariantCultureScope();
        var request = new MicrophoneAngleRequest(angle, diameter, Reference: reference);
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(request);

        string summary = AngleCalibrationPreview.Build(request).Summary;

        Assert.EndsWith(
            estimate.HighestSupportedFrequencyHz < 20_000
                ? $". Modelled to {FrequencyText.Format(estimate.HighestSupportedFrequencyHz)}; references hold above that."
                : $"Built from: {string.Join(" · ", estimate.References)}.",
            summary);
    }
}
