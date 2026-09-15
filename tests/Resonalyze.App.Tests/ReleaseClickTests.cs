using System.Drawing;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>WinForms' WindowFromPoint check before Click lets a topmost tooltip swallow it; only the check's coordinate is repaired, never the verdict.</summary>
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

        MouseEventArgs repaired = ReleaseClick.RepairHitTest(
            button, release, _ => Stranger);

        Assert.Same(release, repaired);
    });

    [Fact]
    public void AReleaseOutsideTheControlIsLeftAlone() => StaTest.Run(() =>
    {
        using var button = Realized();
        MouseEventArgs release = Release(new Point(400, 400));

        // Sliding off the control means "no"; moving the point would defeat that.
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
        // Touching Handle realizes the control without showing it.
        _ = button.Handle;
        return button;
    }

    private static MouseEventArgs Release(Point at) =>
        new(MouseButtons.Left, 1, at.X, at.Y, 0);
}
