using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// The guard every menu in the app is opened with. It exists for one WinForms
/// artifact — the borderless custom chrome emits a focus change as a dropdown
/// appears, and WinForms reads that as a reason to close it, which is the "button
/// pressed, no menu" symptom — and must not swallow anything else.
/// </summary>
public sealed class DropDownFocusGuardTests
{
    [Fact]
    public void TheFocusChangeOpeningTheMenuCausesDoesNotCloseIt()
    {
        using var dropDown = new TestDropDown();
        DropDownFocusGuard.Attach(dropDown);

        dropDown.RaiseOpened();

        Assert.True(dropDown.RaiseClosing(ToolStripDropDownCloseReason.AppFocusChange));
    }

    [Fact]
    public void LeavingTheAppStillClosesIt()
    {
        using var dropDown = new TestDropDown();
        DropDownFocusGuard.Attach(dropDown);
        dropDown.RaiseOpened();
        dropDown.RaiseClosing(ToolStripDropDownCloseReason.AppFocusChange);

        // Only the one close that rides in with the opening is an artifact. The next
        // focus change is the user going somewhere else, and the menu has to follow.
        Assert.False(dropDown.RaiseClosing(ToolStripDropDownCloseReason.AppFocusChange));
    }

    [Theory]
    [InlineData(ToolStripDropDownCloseReason.AppClicked)]
    [InlineData(ToolStripDropDownCloseReason.ItemClicked)]
    [InlineData(ToolStripDropDownCloseReason.Keyboard)]
    [InlineData(ToolStripDropDownCloseReason.CloseCalled)]
    public void EveryDeliberateDismissalStillCloses(ToolStripDropDownCloseReason reason)
    {
        using var dropDown = new TestDropDown();
        DropDownFocusGuard.Attach(dropDown);
        dropDown.RaiseOpened();

        Assert.False(dropDown.RaiseClosing(reason));
    }

    [Fact]
    public void AFocusChangeLongAfterOpeningStillCloses()
    {
        using var dropDown = new TestDropDown();
        DropDownFocusGuard.Attach(dropDown);
        dropDown.RaiseOpened();

        // The artifact lands in the same breath as the opening. A focus change a
        // third of a second later is somebody switching windows.
        Thread.Sleep(300);

        Assert.False(dropDown.RaiseClosing(ToolStripDropDownCloseReason.AppFocusChange));
    }

    [Fact]
    public void GuardingTheSameMenuTwiceGuardsItOnce()
    {
        // Menus built once and re-shown (the title bar's Tools menu) pass through the
        // attach on every open. A second guard must not buy a second cancelled close —
        // the menu would then need two focus changes to go away.
        using var dropDown = new TestDropDown();
        DropDownFocusGuard.Attach(dropDown);
        DropDownFocusGuard.Attach(dropDown);
        dropDown.RaiseOpened();

        Assert.True(dropDown.RaiseClosing(ToolStripDropDownCloseReason.AppFocusChange));
        Assert.False(dropDown.RaiseClosing(ToolStripDropDownCloseReason.AppFocusChange));
    }

    // The events the guard listens to are raised by WinForms from inside a real
    // show; this reaches them without one.
    private sealed class TestDropDown : ToolStripDropDown
    {
        public void RaiseOpened() => OnOpened(EventArgs.Empty);

        /// <returns>Whether the guard cancelled the close.</returns>
        public bool RaiseClosing(ToolStripDropDownCloseReason reason)
        {
            var args = new ToolStripDropDownClosingEventArgs(reason);
            OnClosing(args);
            return args.Cancel;
        }
    }
}
