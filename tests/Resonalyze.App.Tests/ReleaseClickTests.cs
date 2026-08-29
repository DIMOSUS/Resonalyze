using System.Drawing;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// Every button, checkbox and radio button in the app is one of these. WinForms raises
/// Click from a release only while <c>WindowFromPoint</c> at that point still answers
/// with the control's own handle, so anything topmost on that one pixel — a tooltip
/// above all — takes the click silently while the control still paints its press.
/// These raise it themselves, and ask about their own bounds instead.
/// </summary>
public sealed class ReleaseClickTests
{
    [Fact]
    public void AReleaseTheFrameworkIgnoredStillCounts()
    {
        using var button = new ProbeButton();
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        // What a covering window does: MouseDown and MouseUp arrive, and the release
        // raises no Click of its own.
        button.Press(new Point(10, 10));
        button.Release(new Point(10, 10));

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void SlidingOffBeforeReleasingTakesThePressBack()
    {
        using var button = new ProbeButton();
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        // The control keeps the mouse captured, so the release still arrives — with a
        // location outside it. Pressing a button and sliding off means "no", and that
        // is the question WindowFromPoint was standing in for.
        button.Press(new Point(10, 10));
        button.Release(new Point(400, 400));

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void AReleaseWithNoPressBehindItIsNotAClick()
    {
        using var button = new ProbeButton();
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        button.Release(new Point(10, 10));

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void TheRightButtonIsNotAClick()
    {
        using var button = new ProbeButton();
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        button.Press(new Point(10, 10), MouseButtons.Right);
        button.Release(new Point(10, 10), MouseButtons.Right);

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void TwoPressesAreTwoClicks()
    {
        using var button = new ProbeButton();
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        for (int i = 0; i < 2; i++)
        {
            button.Press(new Point(10, 10));
            button.Release(new Point(10, 10));
        }

        Assert.Equal(2, clicks);
    }

    [Fact]
    public void ACheckBoxStillTogglesWhenItsClickWasStolen()
    {
        using var box = new ProbeCheckBox();

        // The toggle lives in OnClick, so a stolen click leaves the box unchanged with
        // nothing to show for the press.
        box.Press(new Point(10, 10));
        box.Release(new Point(10, 10));

        Assert.True(box.Checked);
    }

    [Fact]
    public void ARadioButtonStillTakesTheSelectionWhenItsClickWasStolen()
    {
        using var radio = new ProbeRadioButton();

        radio.Press(new Point(10, 10));
        radio.Release(new Point(10, 10));

        Assert.True(radio.Checked);
    }

    // The ordering the control cannot stage on its own: the framework raises Click from
    // INSIDE the release, between the two halves of the override.
    [Fact]
    public void AClickTheFrameworkAlreadyRaisedIsNotOwedASecondOne()
    {
        using var button = new ProbeButton();
        var release = new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0);
        var tracker = new ReleaseClickTracker(releasePointIsCovered: (_, _) => true);

        tracker.Press(release);
        tracker.BeginRelease();
        tracker.NoteClick();

        Assert.False(tracker.ClickIsOwed(button, release));
    }

    [Fact]
    public void AReleaseThatRaisedNothingIsOwedOne()
    {
        using var button = new ProbeButton();
        var release = new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0);
        var tracker = new ReleaseClickTracker(releasePointIsCovered: (_, _) => true);

        tracker.Press(release);
        tracker.BeginRelease();

        Assert.True(tracker.ClickIsOwed(button, release));
    }

    [Fact]
    public void OneOwedClickIsOwedOnlyOnce()
    {
        using var button = new ProbeButton();
        var release = new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0);
        var tracker = new ReleaseClickTracker(releasePointIsCovered: (_, _) => true);

        tracker.Press(release);
        tracker.BeginRelease();
        tracker.ClickIsOwed(button, release);

        // A second release with no press behind it — a stray one, or the same one
        // asked twice — must not manufacture another click.
        Assert.False(tracker.ClickIsOwed(button, release));
    }

    // The recovery repairs ONE failure. A missing Click is not proof that it was that
    // one: ButtonBase also withholds a click when validation was cancelled, and on its
    // own press/capture state, and those are decisions to respect rather than override.
    [Fact]
    public void AClickWithheldWhileTheControlWasStillOnTopIsLeftWithheld()
    {
        using var button = new ProbeButton();
        var release = new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0);
        var tracker = new ReleaseClickTracker(
            releasePointIsCovered: (_, _) => false);

        tracker.Press(release);
        tracker.BeginRelease();

        // The hit test answers with the control itself, so the framework had this
        // release and declined it for a reason of its own.
        Assert.False(tracker.ClickIsOwed(button, release));
    }

    [Fact]
    public void AClickWithheldWithAnotherWindowOnThePointIsOwed()
    {
        using var button = new ProbeButton();
        var release = new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0);
        var tracker = new ReleaseClickTracker(
            releasePointIsCovered: (_, _) => true);

        tracker.Press(release);
        tracker.BeginRelease();

        Assert.True(tracker.ClickIsOwed(button, release));
    }

    private sealed class ProbeButton : ReleaseClickButton
    {
        public ProbeButton() => Size = new Size(120, 24);

        public void Press(Point at, MouseButtons which = MouseButtons.Left) =>
            OnMouseDown(new MouseEventArgs(which, 1, at.X, at.Y, 0));

        public void Release(Point at, MouseButtons which = MouseButtons.Left) =>
            OnMouseUp(new MouseEventArgs(which, 1, at.X, at.Y, 0));
    }

    private sealed class ProbeCheckBox : ReleaseClickCheckBox
    {
        public ProbeCheckBox() => Size = new Size(120, 24);

        public void Press(Point at) =>
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));

        public void Release(Point at) =>
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
    }

    private sealed class ProbeRadioButton : ReleaseClickRadioButton
    {
        public ProbeRadioButton() => Size = new Size(120, 24);

        public void Press(Point at) =>
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));

        public void Release(Point at) =>
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
    }
}
