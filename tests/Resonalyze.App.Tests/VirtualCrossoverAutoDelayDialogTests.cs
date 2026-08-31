using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Resonalyze;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAutoDelayDialogTests
{
    [Fact]
    public void ChangingRearFillInvalidatesTheCompletedProposal() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverAutoDelayDialog();
        dialog.Init(
            stereo: true,
            sceneOffsetMs: 0.25,
            rightHandDrive: false,
            nearSideCutDb: 1.0,
            request => Task.FromResult(new AutoDelayRunResult(
                [], true, request, "Proposal for the current inputs.", new StringBuilder())),
            hasRearFill: true,
            rearFillOffsetMs: 15.0);

        var run = (Task)typeof(VirtualCrossoverAutoDelayDialog)
            .GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog, null)!;
        run.GetAwaiter().GetResult();

        Assert.NotNull(dialog.Result);
        Button apply = Field<Button>(dialog, "buttonApply");
        Assert.True(apply.Enabled);

        Field<DarkNumericUpDown>(dialog, "numericRearFill").Value = 10m;

        Assert.Null(dialog.Result);
        Assert.False(apply.Enabled);
        Assert.Contains("Run again", Field<Label>(dialog, "labelStatus").Text);
    });

    [Fact]
    public void TheActionButtonsStayVisibleAtEveryHeight() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverAutoDelayDialog();
        AssertNothingCoversTheActionButtons(dialog);

        // The smallest window the dialog can be. Asked for one pixel it clamps
        // to MinimumSize, which is the form's OUTER size - so the client area
        // that leaves is a number the test must not repeat, only observe.
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

            // Controls.Add appends, so a LOWER index paints in front: any
            // earlier sibling overlapping the button hides it, however
            // correct the button's own coordinates are. That is what the
            // rear fill row did to Apply - the report box moved down with
            // the row, the buttons did not, and a dialog that reported a
            // proposal had no way to accept it.
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

    private static T Field<T>(object target, string name) where T : class =>
        (T)target.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(target)!;
}
