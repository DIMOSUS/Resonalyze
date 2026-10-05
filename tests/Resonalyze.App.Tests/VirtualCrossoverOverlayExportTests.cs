using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using static Resonalyze.App.Tests.VirtualCrossoverLivePanel;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverOverlayExportTests
{
    [Fact]
    public void TheDefaultSlot_IsAFreeOne_ElseTheCurvesEarlierCapture_ElseNone()
    {
        OverlaySlotOccupant[] taken =
            [.. Enumerable.Range(1, OverlayFile.MaximumSlotCount).Select(slot => Taken(slot, $"curve {slot}"))];

        Assert.Equal(5, VirtualCrossoverOverlayExport.DefaultSlot(
            [.. taken.Select(slot => slot.Slot is 5 or 9 ? new OverlaySlotOccupant(slot.Slot, null, null) : slot)],
            "curve 3"));
        Assert.Equal(3, VirtualCrossoverOverlayExport.DefaultSlot(taken, "curve 3"));
        Assert.Null(VirtualCrossoverOverlayExport.DefaultSlot(taken, "another"));
        Assert.Null(VirtualCrossoverOverlayExport.DefaultSlot(
            [.. taken.Select(slot => slot.Slot == 4 ? new OverlaySlotOccupant(4, null, null, Unreadable: true) : slot)],
            "another"));
        foreach (OverlayKind kind in new[] { OverlayKind.Target, OverlayKind.Operation })
        {
            Assert.Null(VirtualCrossoverOverlayExport.DefaultSlot(
                [.. taken.Select(slot => slot.Slot == 3 ? new OverlaySlotOccupant(3, "curve 3", kind) : slot)],
                "curve 3"));
        }
    }

    [Fact]
    public void ACapture_IsAResponseTheEqWizardTakes()
    {
        OverlayFile file = OverlayCapture.VirtualDspFile(
            3, "vDSP Sum L (A+B)", [new OverlayPoint(100, -3), new OverlayPoint(1_000, -4)], smoothingCode: 12);

        Assert.True(EqWizardSourceResolver.IsEligible(file));
        Assert.Equal(0, file.SmoothingInverseOctaves);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void EveryCurveOffered_IsSavedAsThePlotDrawsIt() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel(rightAmplitude: 0.5);
        var store = new SlotStore();
        live.Panel.OverlaySlots = store;
        live.Find<CheckBox>("checkBoxShowSum").Checked = true;
        live.Find<ThemedComboBox>("comboBoxStereoSum").SelectedItem = StereoSumMode.Vector;
        live.Settle();

        List<string> offered = [];
        Capture(live, store, VirtualCrossoverOverlayExport.ShownSumKey, curves => offered = curves);
        Assert.Equal(
            ["block:A", "block:B", "block:C", "sum", "sum-opposite", "stereo:Vector", "stereo:Energy", "stereo:Blend"],
            offered);
        Assert.Equal(Drawn(live, "Sum"), store.Saved[^1].Points);
        Assert.Equal(1, store.Saved[^1].Slot);
        Assert.Equal(live.Session.MagnitudeGate.SmoothingInverseOctaves, store.Saved[^1].SmoothingCode);

        foreach ((string key, string drawn) in new[]
                 { ("sum-opposite", "Sum R"), ("block:B", "B"), ("stereo:Vector", "L+R vector") })
        {
            Capture(live, store, key);
            Assert.Equal(Drawn(live, drawn), store.Saved[^1].Points);
        }

        live.Session.Channels[2].Pair.Zone = VirtualCrossoverZone.Rear;
        live.Find<ThemedComboBox>("comboBoxGroupView").SelectedItem = VirtualCrossoverGroupView.GroupsCompared;
        live.Settle();
        Capture(live, store, "group:" + VirtualCrossoverZone.Rear, curves => offered = curves);
        Assert.Equal(["group:Front", "group:Rear"], offered);
        Assert.Equal(Drawn(live, VirtualCrossoverZones.DisplayName(VirtualCrossoverZone.Rear)), store.Saved[^1].Points);
    });

    [Fact]
    [Trait("Category", "Slow")]
    public void WithEverySlotTaken_TheCurvesEarlierCaptureIsRenewed() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        var store = new SlotStore();
        live.Panel.OverlaySlots = store;
        Capture(live, store, VirtualCrossoverOverlayExport.ShownSumKey);
        string title = store.Saved[^1].Title;
        for (int slot = 1; slot <= OverlayFile.MaximumSlotCount; slot++)
        {
            store.Slots[slot - 1] = Taken(slot, slot == 7 ? title : $"measurement {slot}");
        }

        Capture(live, store, VirtualCrossoverOverlayExport.ShownSumKey);

        Assert.Equal(7, store.Saved[^1].Slot);
        Assert.Equal(title, store.Saved[^1].Title);
    });

    [Fact]
    [Trait("Category", "Slow")]
    public void UnderHybrid_TheCurvesAreTheHybridOnesThePlotDraws() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel(rightAmplitude: 0.5);
        var store = new SlotStore();
        live.Panel.OverlaySlots = store;
        var captureSession = Guid.NewGuid();
        foreach (VirtualCrossoverChannel channel in live.Session.Channels)
        {
            channel.PhysicalSideState(rightSide: false).SpatialAverage = Average(captureSession);
            channel.PhysicalSideState(rightSide: true).SpatialAverage = Average(captureSession);
        }

        live.Session.Project.SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MovingMic;
        live.Find<RadioButton>("radioSideRight").Checked = true;
        live.Find<RadioButton>("radioSideLeft").Checked = true;
        live.Find<ThemedComboBox>("comboBoxStereoSum").SelectedItem = StereoSumMode.Vector;
        live.Find<CheckBox>("checkBoxHybrid").Checked = true;
        live.Settle();

        foreach ((string key, string drawn) in new[]
                 { ("block:A", "A"), ("sum", "Sum"), ("sum-opposite", "Sum R"), ("stereo:Vector", "L+R vector") })
        {
            Capture(live, store, key);
            Assert.Equal(Drawn(live, drawn), store.Saved[^1].Points);
            Assert.EndsWith(" hybrid", store.Saved[^1].Title, StringComparison.Ordinal);
        }
    });

    private static OverlaySlotOccupant Taken(int slot, string title) => new(slot, title, OverlayKind.Captured);

    private static LiveCaptureDocument Average(Guid captureSession) => new()
    {
        SavedAtUtc = DateTimeOffset.UnixEpoch,
        Title = "average",
        Method = SpatialAverageMethod.MovingMic,
        CaptureSessionId = captureSession,
        CurveDb = Enumerable.Repeat(-20.0, 1_024).ToArray(),
        GridStartHz = 20,
        GridStopHz = 20_000,
        Recipe = new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            SampleRateHz = SampleRate,
            SequenceLength = 32_768,
            WindowType = WindowType.Rectangular,
            NoiseColor = NoiseColor.PinkPeriodic,
            SlopeCompensation = true,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        }
    };

    // Picks the curve by key and saves it into the slot the dialog offers.
    private static void Capture(
        VirtualCrossoverLivePanel live, SlotStore store, string key, Action<List<string>>? offered = null)
    {
        int saved = store.Saved.Count;
        bool answered = false;
        live.Click("buttonTools");
        live.Answer<VirtualCrossoverOverlayExportDialog>(
            () =>
            {
                live.ClickMenu("Capture to overlay");
                // The save runs as the dialog closes, inside the same pump.
                live.Wait(() => store.Saved.Count > saved || answered, "capture to overlay");
            },
            dialog =>
            {
                answered = true;
                ThemedComboBox curves = In<ThemedComboBox>(dialog, "comboBoxCurve");
                List<VirtualCrossoverOverlayCurve> items = [.. curves.Items.Cast<VirtualCrossoverOverlayCurve>()];
                offered?.Invoke([.. items.Select(curve => curve.Key)]);
                curves.SelectedItem = items.Single(curve => curve.Key == key);
                Button save = In<Button>(dialog, "buttonSave");
                Assert.True(save.Enabled, "The dialog offers no slot.");
                save.PerformClick();
                return true;
            });
        Assert.Equal(saved + 1, store.Saved.Count);
    }

    private static List<(double, double)> Drawn(VirtualCrossoverLivePanel live, string title) =>
        [.. live.Find<PlotView>("mainPlotView").Model!.Series.OfType<LineSeries>()
            .Single(series => series.Title == title).Points.Select(point => (point.X, point.Y))];

    private sealed class SlotStore : IFrequencyResponseOverlaySlots
    {
        public OverlaySlotOccupant[] Slots { get; } =
            [.. Enumerable.Range(1, OverlayFile.MaximumSlotCount).Select(slot => new OverlaySlotOccupant(slot, null, null))];

        public List<(int Slot, string Title, List<(double, double)> Points, int SmoothingCode)> Saved { get; } = [];

        public List<OverlaySlotOccupant> ReadFrequencyResponseOverlaySlots() => [.. Slots];

        public void SaveFrequencyResponseOverlay(int slot, string title, OverlayPoint[] points, int smoothingCode)
        {
            Saved.Add((slot, title, [.. points.Select(point => (point.X, point.Y))], smoothingCode));
            Slots[slot - 1] = Taken(slot, title);
        }
    }
}
