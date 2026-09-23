using System.Text;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAutoDelayDialogTests
{
    [Fact]
    public void ChangingRearFillInvalidatesTheCompletedProposal() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverAutoDelayDialog
        {
            StartPosition = FormStartPosition.Manual,
            Location = new(-5000, -5000)
        };
        AutoDelayRunRequest? asked = null;
        dialog.Init(
            stereo: true,
            sceneOffsetMs: 0.25,
            rightHandDrive: false,
            nearSideCutDb: 1.0,
            request =>
            {
                asked = request;
                return Task.FromResult(new AutoDelayRunResult(
                    [], true, request, "Proposal for the current inputs.", new StringBuilder()));
            },
            hasRearFill: true,
            rearFillOffsetMs: 15.0);
        dialog.Show();
        Field<ThemedNumericUpDown>(dialog, "numericSceneOffset").Value = 0.3m;

        Field<Button>(dialog, "buttonRun").PerformClick();
        StaTest.Pump();

        Assert.NotNull(dialog.Result);
        Assert.Equal(new AutoDelayRunRequest(0.3, false, false, 1.0, 15.0), asked);
        Assert.Equal("Proposal for the current inputs.", Field<TextBox>(dialog, "textBoxReport").Text);
        Button apply = Field<Button>(dialog, "buttonApply");
        Assert.True(apply.Enabled);

        Field<ThemedNumericUpDown>(dialog, "numericRearFill").Value = 10m;

        Assert.Null(dialog.Result);
        Assert.False(apply.Enabled);
        Assert.Contains("Run again", Field<Label>(dialog, "labelStatus").Text);
    });

    [Fact]
    public void TheActionButtonsStayVisibleAtEveryHeight() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverAutoDelayDialog();
        AssertNothingCoversTheActionButtons(dialog);

        // MinimumSize is the OUTER size, so the resulting client area is observed, not repeated.
        dialog.Size = dialog.Size with { Height = 1 };
        Assert.Equal(dialog.MinimumSize.Height, dialog.Height);
        AssertNothingCoversTheActionButtons(dialog);

        dialog.ClientSize = dialog.ClientSize with { Height = 1_100 };
        AssertNothingCoversTheActionButtons(dialog);
    });

    private static void AssertNothingCoversTheActionButtons(Form dialog)
    {
        foreach (string name in new[] { "buttonApply", "buttonCancel" })
        {
            Button button = Field<Button>(dialog, name);

            Assert.True(
                button.Top >= 0 && button.Bottom <= dialog.ClientSize.Height,
                $"{name} is outside the client area: {button.Bounds} in {dialog.ClientSize}.");

            // A LOWER child index paints in front: the rear fill row once hid Apply despite correct coordinates.
            int index = dialog.Controls.GetChildIndex(button);
            foreach (Control sibling in dialog.Controls)
            {
                if (ReferenceEquals(sibling, button)
                    || dialog.Controls.GetChildIndex(sibling) > index)
                {
                    continue;
                }

                Assert.False(
                    sibling.Bounds.IntersectsWith(button.Bounds),
                    $"{sibling.Name} {sibling.Bounds} covers {name} {button.Bounds}.");
            }
        }
    }

    private static T Field<T>(Control root, string name) where T : Control =>
        (T)root.Controls.Find(name, searchAllChildren: true).Single();
}
