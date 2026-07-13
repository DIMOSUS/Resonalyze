using System.Windows.Forms;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverGateDialogLayoutTests
{
    [Fact]
    public void ActionButtonsRemainInsideTheClientArea()
    {
        using var dialog = new VirtualCrossoverGateDialog();
        Button save = dialog.Controls.OfType<Button>()
            .Single(button => button.Text == "Save");
        Button cancel = dialog.Controls.OfType<Button>()
            .Single(button => button.Text == "Cancel");

        Assert.True(save.Bottom <= dialog.ClientSize.Height);
        Assert.True(cancel.Bottom <= dialog.ClientSize.Height);
        Assert.True(save.Top >= 0);
        Assert.True(cancel.Top >= 0);
    }
}
