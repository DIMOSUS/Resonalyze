using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>
/// The calculated-overlay dialog offers every capture of the mode as an operand,
/// coherence traces included, so what only applies to decibels — the tilt and
/// amplitude-space math — has to follow the OPERANDS and not just the mode. Both were
/// left on the mode alone and had to be found by reading.
/// </summary>
public sealed class OverlayOperationSettingsDialogTests
{
    [Theory]
    [InlineData(OverlayOperation.AMinusB, false, true, true)]
    [InlineData(OverlayOperation.AMinusB, true, false, false)]
    // "A only" hands curve A through: there is no arithmetic to do in amplitude space,
    // but a slope still applies to the decibels it draws.
    [InlineData(OverlayOperation.CurveA, false, false, true)]
    [InlineData(OverlayOperation.CurveA, true, false, false)]
    public void DecibelOnlyControls_FollowTheOperands(
        OverlayOperation operation,
        bool coherenceOperands,
        bool expectedAmplitudeSpace,
        bool expectedTilt) => StaTest.Run(() =>
    {
        using OverlayOperationSettingsDialog dialog =
            CreateDialog(operation, coherenceOperands);

        Assert.Equal(expectedAmplitudeSpace, IsOffered(dialog, "amplitudeSpaceCheckBox"));
        Assert.Equal(expectedTilt, IsOffered(dialog, "tiltCheckBox"));
        // Greyed out is not enough: what the dialog REPORTS must not carry the setting
        // either, or a slot saved before the operands changed would keep applying it.
        Assert.Equal(expectedAmplitudeSpace, dialog.UseAmplitudeSpace);
        Assert.Equal(expectedTilt, dialog.TiltEnabled);
    });

    [Fact]
    public void TiltInputs_AreDisabledWithTheirCheckBox() => StaTest.Run(() =>
    {
        using OverlayOperationSettingsDialog dialog =
            CreateDialog(OverlayOperation.AMinusB, coherenceOperands: true);

        Assert.False(Control<Control>(dialog, "tiltPivotInput").Enabled);
        Assert.False(Control<Control>(dialog, "tiltSlopeInput").Enabled);
    });

    // CreateControl realises the handle, so every caller runs on an STA thread.
    private static OverlayOperationSettingsDialog CreateDialog(
        OverlayOperation operation,
        bool coherenceOperands)
    {
        OverlayCurveSemantics semantics = OverlayCurveSemantics.ForCurve(
            MagnitudeScale.Relative,
            coherenceOperands ? PlotModelFactory.CoherenceAxisKey : "decibel");
        var dialog = new OverlayOperationSettingsDialog(
            Mode.FrequencyResponse,
            "Calculated overlay 1",
            1,
            null,
            2,
            null,
            operation,
            blendFrequencyHz: 1_000,
            blendWidthOctaves: 1,
            useAmplitudeSpace: true,
            tiltEnabled: true,
            tiltDbPerOctave: 6,
            tiltPivotHz: 1_000,
            compareDelayMs: 0,
            compareInvertPolarity: false,
            Color.Aqua,
            strokeThickness: 2,
            OverlayLineStyle.Dash,
            opacityPercent: 100,
            smoothingInverseOctaves: 0,
            [new OverlaySlotOption(1, "A", semantics),
             new OverlaySlotOption(2, "B", semantics)],
            []);
        dialog.CreateControl();
        return dialog;
    }

    // A control the dialog greys out stays visible but stops taking input, so "offered"
    // is the muted text plus AutoCheck, not Enabled (see UiStyle.SetTextEnabledLook).
    private static bool IsOffered(OverlayOperationSettingsDialog dialog, string name)
    {
        CheckBox checkBox = Control<CheckBox>(dialog, name);
        return checkBox.AutoCheck && checkBox.ForeColor != UiPalette.TextDisabled;
    }

    private static T Control<T>(OverlayOperationSettingsDialog dialog, string name) =>
        (T)typeof(OverlayOperationSettingsDialog)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;
}
