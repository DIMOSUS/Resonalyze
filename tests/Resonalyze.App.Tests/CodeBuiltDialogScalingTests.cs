using System.Drawing;
using System.Windows.Forms;
using Resonalyze.Ui;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze.App.Tests;

/// <summary>Code-built dialogs lay out in 96-DPI pixels and must be scaled exactly once (<see cref="UiStyle.ApplyDarkDialog"/>).</summary>
public sealed class CodeBuiltDialogScalingTests
{
    [Fact]
    public void ApplyDarkDialog_DeclaresTheDpiItScalesFrom()
    {
        using var form = new Form();
        UiStyle.ApplyDarkDialog(form, new Size(300, 200));

        // Without declared dimensions the first auto-scale adopts the current ones and never scales.
        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        Assert.Equal(new SizeF(96F, 96F), form.AutoScaleDimensions);
    }

    // Mirrored from the dialog's own call; a second scaling pass breaks it by the DPI factor.
    [Theory]
    [InlineData(452, 448)]
    public void ColorPickerDialog_ScalesItsDesignedSizeExactlyOnce(int width, int height) =>
        StaTest.Run(() =>
        {
            using var dialog = new ColorPickerDialog(Color.Red);
            AssertScaledOnce(dialog, new Size(width, height));
        });

    [Theory]
    [InlineData(560, 348)]
    public void RewExportDialog_ScalesItsDesignedSizeExactlyOnce(int width, int height) =>
        StaTest.Run(() =>
        {
            using var dialog = new RewExportDialog(
                "probe",
                "http://localhost:4735/",
                rewVersion: null,
                splOffsetDb: null,
                TimingReference.SynchronizedLoopback);
            AssertScaledOnce(dialog, new Size(width, height));
        });

    [Theory]
    [InlineData(true, 500, 235)]
    [InlineData(false, 500, 215)]
    public void ApplicationUpdateDialog_ScalesItsDesignedSizeExactlyOnce(
        bool supportsAutomaticUpdate,
        int width,
        int height) =>
        StaTest.Run(() =>
        {
            using var dialog = new ApplicationUpdateDialog(
                "0.0.0", "0.1.0", supportsAutomaticUpdate);
            AssertScaledOnce(dialog, new Size(width, height));
        });

    // A second pass squared the factor (1.56x at 125%), walking buttons off screen.
    private static void AssertScaledOnce(Form dialog, Size designed)
    {
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(-6000, -6000);
        dialog.Show();
        try
        {
            double factor = dialog.DeviceDpi / 96.0;
            var expected = new Size(
                (int)Math.Round(designed.Width * factor),
                (int)Math.Round(designed.Height * factor));
            Assert.InRange(dialog.ClientSize.Width, expected.Width - 1, expected.Width + 1);
            Assert.InRange(dialog.ClientSize.Height, expected.Height - 1, expected.Height + 1);
        }
        finally
        {
            dialog.Hide();
        }
    }
}
