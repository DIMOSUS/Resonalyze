using System.Drawing;

namespace Resonalyze.App.Tests;

/// <summary>
/// The autosave belongs to the stored session the first show loads. A panel built and disposed without being shown
/// holds only the constructor's placeholder, which must never reach the file: until #203 every run of the App tests
/// replaced the owner's session with it. Driven through what the host calls; the test host is portable, so the file
/// here is the one beside the tests.
/// </summary>
public sealed class VirtualCrossoverPanelAutosaveTests
{
    private const double StoredLevelDb = -37;

    [Fact]
    public void APanelNeverShown_LeavesTheStoredSessionAlone()
    {
        WithStoredSession(path =>
        {
            byte[] stored = File.ReadAllBytes(path);

            StaTest.Run(() =>
            {
                using var panel = new VirtualCrossoverPanel();
                // What the host does at startup, before anyone opens the tool.
                panel.SetTargetCurve(Target());
                StaTest.Pump();
            });

            Assert.Equal(stored, File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void AShownPanel_SavesItsEditsIntoTheStoredSession()
    {
        WithStoredSession(_ =>
        {
            StaTest.Run(() =>
            {
                using var panel = new VirtualCrossoverPanel();
                Settle(panel.OnPanelShown());
                Assert.Equal(StoredLevelDb, panel.Session.Project.TargetLevelDb);

                panel.SetTargetCurve(Target());
                StaTest.Pump();
            });

            VirtualCrossoverProjectFile saved = VirtualCrossoverProjectFile.LoadOrDefault();
            Assert.Equal(StoredLevelDb, saved.TargetLevelDb);
            Assert.Equal(TargetPreset.Car, saved.Target?.Preset);
        });
    }

    private static void WithStoredSession(Action<string> test)
    {
        string path = VirtualCrossoverProjectFile.GetPath();
        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            new VirtualCrossoverProjectFile { TargetLevelDb = StoredLevelDb }.Save();
            test(path);
        }
        finally
        {
            if (original != null)
            {
                File.WriteAllBytes(path, original);
            }
            else
            {
                File.Delete(path);
            }
        }
    }

    // The load resumes on this thread, so it is pumped rather than waited on.
    private static void Settle(Task load)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!load.IsCompleted && DateTime.UtcNow < deadline)
        {
            StaTest.Pump();
            Thread.Sleep(5);
        }

        Assert.True(load.IsCompleted, "The stored session did not load.");
    }

    private static EqTargetCurve Target() => new(
        TargetPreset.Car,
        new TargetCurveSpec(-0.7, 9.2, 105, 0.9, -3, 10_000, 0.7, 1.5, 2_800, 1.2),
        ToleranceDb: 2.5,
        TargetDeviationMode.Deviation,
        Color.FromArgb(255, 200, 120, 40),
        StrokeThickness: 3.5,
        OverlayLineStyle.DashDot,
        SmoothingInverseOctaves: 6);
}
