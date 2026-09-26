using System.Drawing;

namespace Resonalyze.App.Tests;

public sealed class WindowPlacementTests : IDisposable
{
    private static readonly Rectangle Primary = new(0, 0, 1920, 1040);
    private static readonly Rectangle RightOfPrimary = new(1920, 0, 2560, 1400);

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "resonalyze-window-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(directory, "window.json");

    [Fact]
    public void BoundsOnAScreenThatIsStillThere_AreKeptAsTheyWere()
    {
        var saved = new Rectangle(2100, 120, 1600, 900);

        Assert.Equal(saved, WindowPlacementFit.Fit(saved, [Primary, RightOfPrimary], Primary));
    }

    [Fact]
    public void NothingSaved_KeepsTheDesignersSize()
    {
        Assert.Null(WindowPlacementFit.Fit(Rectangle.Empty, [Primary], Primary));
    }

    [Fact]
    public void BoundsOnAnUnpluggedMonitor_KeepTheirSizeCentredOnThePrimary()
    {
        var saved = new Rectangle(2100, 120, 1600, 900);

        Assert.Equal(
            new Rectangle(160, 70, 1600, 900),
            WindowPlacementFit.Fit(saved, [Primary], Primary));
    }

    [Fact]
    public void BoundsHangingOffTheEdge_MoveBackOntoTheScreenTheyMostlySitOn()
    {
        var saved = new Rectangle(-300, 600, 1500, 800);

        Assert.Equal(
            new Rectangle(0, 240, 1500, 800),
            WindowPlacementFit.Fit(saved, [Primary, RightOfPrimary], Primary));
    }

    [Fact]
    public void BoundsLargerThanTheScreenNow_ShrinkToItsWorkingArea()
    {
        var saved = new Rectangle(1920, 0, 2560, 1400);
        var smaller = new Rectangle(1920, 0, 1920, 1040);

        Assert.Equal(smaller, WindowPlacementFit.Fit(saved, [Primary, smaller], Primary));
    }

    [Fact]
    public void AMissingFile_HoldsNoBounds()
    {
        WindowPlacementFile placement = WindowPlacementFile.LoadOrDefault(Path_);

        Assert.True(placement.NormalBounds.IsEmpty);
        Assert.False(placement.Maximized);
    }

    [Fact]
    public void TheBoundsAndTheMaximizedState_SurviveARoundTrip()
    {
        Directory.CreateDirectory(directory);
        WindowPlacementFile saved = WindowPlacementFile.LoadOrDefault(Path_);
        saved.NormalBounds = new Rectangle(-1200, 40, 1500, 850);
        saved.Maximized = true;
        Assert.True(saved.TrySave());

        WindowPlacementFile loaded = WindowPlacementFile.LoadOrDefault(Path_);
        Assert.Equal(new Rectangle(-1200, 40, 1500, 850), loaded.NormalBounds);
        Assert.True(loaded.Maximized);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    public void AFileItCannotUnderstand_HoldsNoBounds(string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path_, content);

        Assert.True(WindowPlacementFile.LoadOrDefault(Path_).NormalBounds.IsEmpty);
    }

    [Fact]
    public void AnUnwritablePath_AnswersFalse()
    {
        // A directory where the file belongs: the write fails without needing a permission fixture.
        Directory.CreateDirectory(Path_);
        WindowPlacementFile placement = WindowPlacementFile.LoadOrDefault(Path_);
        placement.NormalBounds = new Rectangle(10, 10, 1500, 850);

        Assert.False(placement.TrySave());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
