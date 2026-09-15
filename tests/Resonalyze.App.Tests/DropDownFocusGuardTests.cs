using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>The borderless chrome emits a focus change as a dropdown appears, which WinForms treats as a close; only that is swallowed.</summary>
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

        Thread.Sleep(300);

        Assert.False(dropDown.RaiseClosing(ToolStripDropDownCloseReason.AppFocusChange));
    }

    [Fact]
    public void GuardingTheSameMenuTwiceGuardsItOnce()
    {
        // Re-shown menus attach on every open; a second guard must not cancel a second close.
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

            // The cancel is unconditional: nothing distinguishes the artifact from a switch in the same quarter second.
            Assert.True(dropDown.RaiseClosing(ToolStripDropDownCloseReason.AppFocusChange));
            Assert.Empty(closed);

            // If the app is still in the background after the churn, it was a real switch and the topmost menu closes.
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

    private sealed class TestDropDown : ToolStripDropDown
    {
        public void RaiseOpened() => OnOpened(EventArgs.Empty);

        public bool RaiseClosing(ToolStripDropDownCloseReason reason)
        {
            var args = new ToolStripDropDownClosingEventArgs(reason);
            OnClosing(args);
            return args.Cancel;
        }
    }
}
