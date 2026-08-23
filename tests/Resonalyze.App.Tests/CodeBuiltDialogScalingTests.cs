using System.Drawing;
using System.Windows.Forms;
using Resonalyze.Ui;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze.App.Tests;

/// <summary>
/// The dialogs that build themselves in code instead of in a designer. They lay
/// out in 96-DPI pixels, so the one thing that must be true of them on a scaled
/// display is that something scales that layout — and that only ONE something
/// does. Both halves have been wrong here: before
/// <see cref="UiStyle.ApplyDarkDialog"/> declared the DPI it scales from, the
/// factor was 1 and the 96-DPI boxes held 125% text; once it did, a hand-written
/// pass that walked the same tree by the same DeviceDpi/96 squared the factor.
/// </summary>
public sealed class CodeBuiltDialogScalingTests
{
    [Fact]
    public void ApplyDarkDialog_DeclaresTheDpiItScalesFrom()
    {
        using var form = new Form();
        UiStyle.ApplyDarkDialog(form, new Size(300, 200));

        // Without the declared dimensions the first auto-scale adopts the
        // current ones, and a form that scales from "whatever it already is"
        // never scales at all.
        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        Assert.Equal(new SizeF(96F, 96F), form.AutoScaleDimensions);
    }

    // The size each dialog asks ApplyDarkDialog for, mirrored from its own call.
    // A deliberate resize breaks this and the number gets updated; a second
    // scaling pass breaks it by the DPI factor, which is the point.
    [Theory]
    [InlineData(452, 448)]
    public void ColorPickerDialog_ScalesItsDesignedSizeExactlyOnce(int width, int height)
    {
        using var dialog = new ColorPickerDialog(Color.Red);
        AssertScaledOnce(dialog, new Size(width, height));
    }

    [Theory]
    [InlineData(true, 500, 235)]
    [InlineData(false, 500, 215)]
    public void ApplicationUpdateDialog_ScalesItsDesignedSizeExactlyOnce(
        bool supportsAutomaticUpdate,
        int width,
        int height)
    {
        using var dialog = new ApplicationUpdateDialog(
            "0.0.0", "0.1.0", supportsAutomaticUpdate);
        AssertScaledOnce(dialog, new Size(width, height));
    }

    // On a 100% display this says the dialog is its designed size; on a scaled
    // one it says the layout grew by the display's factor and no more. The
    // second pass this guards against squared it — 1.56x at 125%, which walks
    // the buttons off the bottom of the screen.
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
            // A pixel of slack for the rounding WinForms does per dimension —
            // far below the gap a doubled pass opens.
            Assert.InRange(dialog.ClientSize.Width, expected.Width - 1, expected.Width + 1);
            Assert.InRange(dialog.ClientSize.Height, expected.Height - 1, expected.Height + 1);
        }
        finally
        {
            dialog.Hide();
        }
    }
}
