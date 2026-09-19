using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// The autosave belongs to the stored session the first show loads. A panel built and disposed without being shown
/// holds only the constructor's placeholder, which must never reach the file: until #203 every run of the App tests
/// replaced the owner's session with it. The test host is portable, so the file here is the one beside the tests.
/// </summary>
public sealed class VirtualCrossoverPanelAutosaveTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
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
                PickDsp(panel, "radioDspPhase");
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
                panel.OnPanelShown();
                AwaitStoredLoad(panel);
                Assert.Equal(StoredLevelDb, panel.Session.Project.TargetLevelDb);

                PickDsp(panel, "radioDspPhase");
            });

            VirtualCrossoverProjectFile saved = VirtualCrossoverProjectFile.LoadOrDefault();
            Assert.Equal(StoredLevelDb, saved.TargetLevelDb);
            Assert.Equal(DspPlotMode.Phase, saved.EffectiveDspPlotMode);
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

    private static void PickDsp(VirtualCrossoverPanel panel, string radio)
    {
        foreach (string other in new[]
            { "radioDspMagnitude", "radioDspPhase", "radioDspGroupDelay", "radioDspCorrelation", "radioDspCoherence" })
        {
            Control<RadioButton>(panel, other).Checked = false;
        }

        Control<RadioButton>(panel, radio).Checked = true;
        StaTest.Pump();
    }

    private static void AwaitStoredLoad(VirtualCrossoverPanel panel)
    {
        var load = Control<Task>(panel, "storedProjectLoad");
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!load.IsCompleted && DateTime.UtcNow < deadline)
        {
            StaTest.Pump();
            Thread.Sleep(5);
        }

        Assert.True(load.IsCompleted, "The stored session did not load.");
    }

    private static T Control<T>(VirtualCrossoverPanel panel, string name) =>
        (T)typeof(VirtualCrossoverPanel).GetField(name, Hidden)!.GetValue(panel)!;

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
