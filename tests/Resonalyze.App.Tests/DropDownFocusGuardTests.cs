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

    [Fact]
    public void AMenuStrandedByARealSwitchClosesOnceTheChurnHasSettled() =>
        StaTest.Run(() =>
        {
            using var dropDown = new TestDropDown();
            var closed = new List<ToolStripDropDown>();
            DropDownFocusGuard.Attach(
                dropDown, applicationIsActive: () => false, closed.Add);
            dropDown.RaiseOpened();

            // The cancel is unconditional — nothing on the spot can tell the artifact
            // from an application switch landing in the same quarter second.
            Assert.True(dropDown.RaiseClosing(ToolStripDropDownCloseReason.AppFocusChange));
            Assert.Empty(closed);

            // Once the churn is over, the app is still in the background: it was a real
            // switch, and a topmost menu must not be left floating over whatever the
            // user moved to.
            PumpUntil(() => closed.Count > 0);

            Assert.Single(closed);
        });

    [Fact]
    public void AMenuTheArtifactTriedToCloseIsLeftOpen() => StaTest.Run(() =>
    {
        using var dropDown = new TestDropDown();
        var closed = new List<ToolStripDropDown>();
        DropDownFocusGuard.Attach(
            dropDown, applicationIsActive: () => true, closed.Add);
        dropDown.RaiseOpened();

        Assert.True(dropDown.RaiseClosing(ToolStripDropDownCloseReason.AppFocusChange));

        // The application never went anywhere, so the close was the artifact and the
        // menu stays — which is the whole point of the guard.
        PumpFor(TimeSpan.FromMilliseconds(900));

        Assert.Empty(closed);
    });

    private static void PumpUntil(Func<bool> done)
    {
        for (int attempt = 0; attempt < 200 && !done(); attempt++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void PumpFor(TimeSpan duration)
    {
        int until = Environment.TickCount + (int)duration.TotalMilliseconds;
        while (Environment.TickCount < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
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
