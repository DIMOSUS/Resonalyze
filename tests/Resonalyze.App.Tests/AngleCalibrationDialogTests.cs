using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class AngleCalibrationDialogTests
{
    [Fact]
    public void AcceptingWritesTheControlsBackIntoTheDefinition()
    {
        var definition = new MicrophoneCalibrationDefinition
        {
            Id = "cal1",
            Name = "90°",
            Kind = MicrophoneCalibrationKind.Angle,
            AngleDegrees = 90,
            FrontDiameterMm = 12.7
        };
        using var dialog = new AngleCalibrationDialog(definition, []);

        TextBox name = Control<TextBox>(dialog, "textBoxName");
        ThemedNumericUpDown angle = Control<ThemedNumericUpDown>(dialog, "numericAngle");
        ThemedNumericUpDown diameter = Control<ThemedNumericUpDown>(dialog, "numericDiameter");
        name.Text = "Passenger seat";
        angle.Value = 30m;
        diameter.Value = 9m;
        Click(dialog, "buttonOk");

        Assert.Equal("Passenger seat", definition.Name);
        Assert.Equal(30.0, definition.AngleDegrees);
        Assert.Equal(9.0, definition.FrontDiameterMm);
        Assert.Equal(MicrophoneCalibrationKind.Angle, definition.Kind);
    }

    [Fact]
    public void CancellingLeavesTheDefinitionUntouched()
    {
        var definition = new MicrophoneCalibrationDefinition
        {
            Id = "cal1",
            Name = "90°",
            Kind = MicrophoneCalibrationKind.Angle,
            AngleDegrees = 90,
            FrontDiameterMm = 12.7
        };
        using var dialog = new AngleCalibrationDialog(definition, []);

        Control<ThemedNumericUpDown>(dialog, "numericAngle").Value = 15m;
        Click(dialog, "buttonCancel");

        Assert.Equal("90°", definition.Name);
        Assert.Equal(90.0, definition.AngleDegrees);
    }

    [Fact]
    public void ANamedMicrophoneModelLocksTheGeometryItDoesNotUse()
    {
        var definition = new MicrophoneCalibrationDefinition
        {
            Id = "cal1",
            Kind = MicrophoneCalibrationKind.Angle,
            AngleDegrees = 90,
            Reference = MicrophoneAngleReference.SonarworksXref20
        };
        using var dialog = new AngleCalibrationDialog(definition, []);

        Assert.False(Control<ThemedNumericUpDown>(dialog, "numericDiameter").Enabled);
        Assert.False(Control<ThemedComboBox>(dialog, "comboBoxGrid").Enabled);
    }

    // PerformClick refuses on a never-shown form, so the click is raised as the framework does.
    private static void Click(Form dialog, string name) =>
        typeof(Control)
            .GetMethod("InvokeOnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [Control<Button>(dialog, name), EventArgs.Empty]);

    private static TControl Control<TControl>(Form dialog, string name)
        where TControl : Control =>
        (TControl)typeof(AngleCalibrationDialog)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;
}
