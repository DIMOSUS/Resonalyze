using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>
/// The dialog hands its result back by writing into the definition it was given.
/// That makes the OK button's wiring the whole contract: a handler that never
/// ran would discard the edit silently, with the manager list showing the values
/// the entry had before.
/// </summary>
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
        DarkNumericUpDown angle = Control<DarkNumericUpDown>(dialog, "numericAngle");
        DarkNumericUpDown diameter = Control<DarkNumericUpDown>(dialog, "numericDiameter");
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

        Control<DarkNumericUpDown>(dialog, "numericAngle").Value = 15m;
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

        Assert.False(Control<DarkNumericUpDown>(dialog, "numericDiameter").Enabled);
        Assert.False(Control<DarkComboBox>(dialog, "comboBoxGrid").Enabled);
    }

    // Button.PerformClick refuses on a form that was never shown (the button
    // cannot be selected), which would make these tests pass on a dialog whose
    // OK does nothing. This raises the click the way the framework does.
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
