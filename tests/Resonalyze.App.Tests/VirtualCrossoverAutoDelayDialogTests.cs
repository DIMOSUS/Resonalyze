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

    private static T Field<T>(object target, string name) where T : class =>
        (T)target.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(target)!;
}
