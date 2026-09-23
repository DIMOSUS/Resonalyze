using System.Windows.Forms;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;
using static Resonalyze.App.Tests.CalibrationDialogFixtures;

namespace Resonalyze.App.Tests;

/// <summary>A shown estimate dialog driven through its controls, beside a session changed the same way: what it shows and
/// what OK writes must be what the session and the preview make of it.</summary>
public sealed class AngleCalibrationDialogWiringTests
{
    private static readonly MicrophoneCalibrationDefinition[] Bases =
    [
        new() { Id = "left", Name = "Left mic", Kind = MicrophoneCalibrationKind.File, Path = "left.cal" },
        new() { Id = "right", Name = "Right mic", Kind = MicrophoneCalibrationKind.File, Path = "right.cal" }
    ];

    private static MicrophoneCalibrationDefinition Estimate() => new()
    {
        Id = "a1",
        Name = "Rear",
        Kind = MicrophoneCalibrationKind.Angle,
        AngleDegrees = 60,
        FrontDiameterMm = 9,
        Grid = MicrophoneProtectionGrid.Fitted,
        BaseId = "right"
    };

    public static TheoryData<string> Changes =>
        ["name", "angle", "diameter", "grid", "reference", "base", "everything"];

    [Theory]
    [MemberData(nameof(Changes))]
    public void EachFieldReachesThePreviewAndOk(string change) => Run(() =>
    {
        MicrophoneCalibrationDefinition written = Estimate();
        MicrophoneCalibrationDefinition expected = Estimate();
        using AngleCalibrationDialog dialog = Shown(new AngleCalibrationDialog(written, Bases));
        var session = new AngleCalibrationSession(expected, Bases);
        AssertShows(dialog, session);

        if (change is "name" or "everything")
        {
            In<TextBox>(dialog, "textBoxName").Text = "Passenger";
            session.Name = "Passenger";
        }

        if (change is "angle" or "everything")
        {
            In<ThemedNumericUpDown>(dialog, "numericAngle").Value = 22.5m;
            session.SetAngle(22.5m);
        }

        if (change is "diameter" or "everything")
        {
            In<ThemedNumericUpDown>(dialog, "numericDiameter").Value = 23.8m;
            session.SetDiameter(23.8m);
        }

        if (change is "grid" or "everything")
        {
            In<ThemedComboBox>(dialog, "comboBoxGrid").SelectedIndex = 2;
            session.GridIndex = 2;
        }

        if (change is "reference" or "everything")
        {
            In<ThemedComboBox>(dialog, "comboBoxReference").SelectedIndex = 1;
            session.ReferenceIndex = 1;
        }

        if (change is "base" or "everything")
        {
            In<ThemedComboBox>(dialog, "comboBoxBase").SelectedIndex = 0;
            session.BaseIndex = 0;
        }

        AssertShows(dialog, session);
        Click(dialog, "buttonOk");
        session.CommitTo(expected);

        Assert.Equal(DialogResult.OK, dialog.DialogResult);
        Assert.Equivalent(expected, written, strict: true);
    });

    [Fact]
    public void CancellingLeavesTheDefinitionUntouched() => Run(() =>
    {
        MicrophoneCalibrationDefinition definition = Estimate();
        using AngleCalibrationDialog dialog = Shown(new AngleCalibrationDialog(definition, Bases));
        In<ThemedNumericUpDown>(dialog, "numericAngle").Value = 15m;
        In<TextBox>(dialog, "textBoxName").Text = "changed";

        Click(dialog, "buttonCancel");

        Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
        Assert.Equivalent(Estimate(), definition, strict: true);
    });

    [Fact]
    public void ANamedMicrophoneLocksTheGeometryItDoesNotUse() => Run(() =>
    {
        MicrophoneCalibrationDefinition definition = Estimate();
        definition.Reference = MicrophoneAngleReference.SonarworksXref20;
        using AngleCalibrationDialog dialog = Shown(new AngleCalibrationDialog(definition, Bases));

        Assert.False(In<ThemedNumericUpDown>(dialog, "numericDiameter").Enabled);
        Assert.False(In<ThemedComboBox>(dialog, "comboBoxGrid").Enabled);
        In<ThemedComboBox>(dialog, "comboBoxReference").SelectedIndex = 0;
        Assert.True(In<ThemedNumericUpDown>(dialog, "numericDiameter").Enabled);
        Assert.True(In<ThemedComboBox>(dialog, "comboBoxGrid").Enabled);
    });

    private static void AssertShows(AngleCalibrationDialog dialog, AngleCalibrationSession session)
    {
        Assert.Equal(session.Name, In<TextBox>(dialog, "textBoxName").Text);
        Assert.Equal(session.AngleDegrees, In<ThemedNumericUpDown>(dialog, "numericAngle").Value);
        Assert.Equal(session.DiameterMm, In<ThemedNumericUpDown>(dialog, "numericDiameter").Value);
        AssertCombo(In<ThemedComboBox>(dialog, "comboBoxBase"), session.Bases.Select(b => b.Label), session.BaseIndex);
        AssertCombo(
            In<ThemedComboBox>(dialog, "comboBoxGrid"),
            AngleCalibrationSession.Grids.Select(g => g.Label),
            session.GridIndex);
        AssertCombo(
            In<ThemedComboBox>(dialog, "comboBoxReference"),
            AngleCalibrationSession.References.Select(r => r.Label),
            session.ReferenceIndex);
        Assert.Equal(session.GeometryApplies, In<ThemedNumericUpDown>(dialog, "numericDiameter").Enabled);
        Assert.Equal(session.GeometryApplies, In<ThemedComboBox>(dialog, "comboBoxGrid").Enabled);
        AngleCalibrationPreview preview = AngleCalibrationPreview.Build(session.Request);
        Assert.Equal(preview.Summary, In<Label>(dialog, "labelSummary").Text);
        var model = In<PlotView>(dialog, "plotViewPreview").Model!;
        var band = Assert.IsType<AreaSeries>(model.Series[0]);
        var center = Assert.IsType<LineSeries>(model.Series[1]);
        Assert.Equal(preview.Points.Select(p => (p.FrequencyHz, p.LowerDb)), band.Points.Select(p => (p.X, p.Y)));
        Assert.Equal(preview.Points.Select(p => (p.FrequencyHz, p.UpperDb)), band.Points2.Select(p => (p.X, p.Y)));
        Assert.Equal(preview.Points.Select(p => (p.FrequencyHz, p.CenterDb)), center.Points.Select(p => (p.X, p.Y)));
    }

    private static void AssertCombo(ThemedComboBox combo, IEnumerable<string> labels, int selected)
    {
        Assert.Equal(labels, combo.Items.Cast<object>().Select(item => combo.GetItemText(item)));
        Assert.Equal(selected, combo.SelectedIndex);
    }
}
