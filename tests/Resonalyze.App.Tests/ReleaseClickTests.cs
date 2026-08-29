using System.Drawing;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// Every button, checkbox and radio button in the app is a ReleaseClick one. WinForms
/// runs a <c>WindowFromPoint</c> ownership check on its way to raising Click, so
/// anything topmost on that one pixel — a tooltip above all — takes the click silently
/// while the control still paints its press.
/// </summary>
/// <remarks>
/// What is repaired is the COORDINATE that check runs at, never the verdict: the
/// framework still decides whether a click is due, so every condition it withholds one
/// under — cancelled validation, its own press and capture state — keeps applying,
/// including when the release point happens to be covered as well. These pin the move
/// itself; that a moved release then goes through the framework untouched is the shape
/// of the design rather than something a test has to assert around it.
/// </remarks>
public sealed class ReleaseClickTests
{
    private static readonly IntPtr Stranger = new(0x5678);

    [Fact]
    public void AReleaseNothingCoversIsHandedOnUntouched() => StaTest.Run(() =>
    {
        using var button = Realized();
        MouseEventArgs release = Release(new Point(10, 10));

        MouseEventArgs repaired = ReleaseClick.RepairHitTest(
            button, release, _ => button.Handle);

        Assert.Same(release, repaired);
    });

    [Fact]
    public void ACoveredReleaseMovesToAFreePointOnTheSameControl() => StaTest.Run(() =>
    {
        using var button = Realized();
        MouseEventArgs release = Release(new Point(60, 12));

        // One window over the release point, the rest of the control clear — the shape
        // a tooltip leaves.
        MouseEventArgs repaired = ReleaseClick.RepairHitTest(
            button,
            release,
            screen => button.PointToClient(screen) == new Point(60, 12)
                ? Stranger
                : button.Handle);

        Assert.NotEqual(release.Location, repaired.Location);
        Assert.True(button.ClientRectangle.Contains(repaired.Location));
        Assert.Equal(release.Button, repaired.Button);
        Assert.Equal(release.Clicks, repaired.Clicks);
    });

    [Fact]
    public void AControlCoveredEdgeToEdgeIsLeftToTheFramework() => StaTest.Run(() =>
    {
        using var button = Realized();
        MouseEventArgs release = Release(new Point(60, 12));

        // Nowhere free to move to, so nothing is moved and the framework's own verdict
        // stands — the same one a plain Button would reach.
        MouseEventArgs repaired = ReleaseClick.RepairHitTest(
            button, release, _ => Stranger);

        Assert.Same(release, repaired);
    });

    [Fact]
    public void AReleaseOutsideTheControlIsLeftAlone() => StaTest.Run(() =>
    {
        using var button = Realized();
        MouseEventArgs release = Release(new Point(400, 400));

        // Pressing a control and sliding off it means "no", and the framework's own hit
        // test is what enforces that. Moving the point would defeat it.
        MouseEventArgs repaired = ReleaseClick.RepairHitTest(
            button, release, _ => Stranger);

        Assert.Same(release, repaired);
    });

    [Fact]
    public void OnlyTheLeftButtonIsRepaired() => StaTest.Run(() =>
    {
        using var button = Realized();
        var release = new MouseEventArgs(MouseButtons.Right, 1, 60, 12, 0);

        MouseEventArgs repaired = ReleaseClick.RepairHitTest(
            button, release, _ => Stranger);

        Assert.Same(release, repaired);
    });

    [Fact]
    public void AControlWithNoWindowOfItsOwnIsLeftAlone()
    {
        // Never realized, so there is no handle for a hit test to answer with and
        // nothing to repair — the framework will make its own window and its own call.
        using var button = new ReleaseClickButton { Size = new Size(120, 24) };
        MouseEventArgs release = Release(new Point(60, 12));

        MouseEventArgs repaired = ReleaseClick.RepairHitTest(
            button, release, _ => Stranger);

        Assert.False(button.IsHandleCreated);
        Assert.Same(release, repaired);
    }

    [Fact]
    public void TheMoveLandsAsCloseToTheRealReleaseAsItCan() => StaTest.Run(() =>
    {
        using var button = Realized();
        MouseEventArgs release = Release(new Point(4, 4));

        // Only the release point is covered, so the corner beside it is where the
        // release goes: the closest free stand-in is the least distorted one.
        MouseEventArgs repaired = ReleaseClick.RepairHitTest(
            button,
            release,
            screen => button.PointToClient(screen) == new Point(4, 4)
                ? Stranger
                : button.Handle);

        Assert.True(
            repaired.X <= 8 && repaired.Y <= 8,
            $"The stand-in landed at {repaired.Location}, far from the release at 4,4.");
    });

    private static ReleaseClickButton Realized()
    {
        var button = new ReleaseClickButton { Size = new Size(120, 24) };
        // Touching the handle realizes it, which is all the hit test needs; the control
        // never goes on screen.
        _ = button.Handle;
        return button;
    }

    private static MouseEventArgs Release(Point at) =>
        new(MouseButtons.Left, 1, at.X, at.Y, 0);
}
