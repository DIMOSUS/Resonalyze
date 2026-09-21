using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The overlay controls driven as the user drives them, read by what the session holds and what the plot shows. The
/// session's rules have their own tests; these pin the wiring between the slot controls and the session.
/// </summary>
public sealed class OverlayPanelWiringTests
{
    [Fact]
    public void CapturingThroughTheMenu_FillsTheSlotsControls() => StaTest.Run(() =>
    {
        using var live = new LivePanel();

        live.CaptureThroughMenu(1, "HD2");

        Assert.Equal("HD2", live.Name(1).Text);
        Assert.True(live.Check(1).Checked);
        Assert.True(live.Check(1).Enabled);
        Assert.True(live.Offset(1).Enabled);
        Assert.Equal("1", live.Button(1).Text);
        Assert.Equal(-30.0, live.OverlayCurve(1).Points[0].Y);
    });

    [Fact]
    public void TheOffsetField_MovesTheCurve_AndSavesAfterAPause() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.CaptureThroughMenu(1, "Frequency Response");

        live.Offset(1).Value = 7m;

        Assert.Equal(7m, live.Slot(1).State.Offset);
        Assert.Equal(7.0, live.OverlayCurve(1).Points[0].Y);
        Assert.Equal(0.0, live.SavedOffset(1));
        live.PumpFor(900);
        Assert.Equal(7.0, live.SavedOffset(1));
    });

    [Fact]
    public void TheCheckbox_HidesAndShowsTheCurve() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.CaptureThroughMenu(1, "Frequency Response");

        live.Check(1).Checked = false;
        Assert.False(live.Slot(1).Checked);
        Assert.Null(live.OverlayCurveOrNull(1));

        live.Check(1).Checked = true;
        Assert.True(live.Slot(1).Checked);
        Assert.NotNull(live.OverlayCurveOrNull(1));
    });

    [Fact]
    public void WhatTheSessionChanges_TheControlsShow() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.CaptureThroughMenu(1, "Frequency Response");
        OverlaySession session = live.Panel.Session;
        var appearance = new OverlayAppearance(Color.FromArgb(255, 250, 250, 250), 2, OverlayLineStyle.Dot, 100);

        session.ApplyOperation(
            live.Slot(2),
            "Copy of one",
            OverlayOperationSettings.Default with { Operation = OverlayOperation.CurveA, SourceSlotA = 1 },
            appearance,
            0);

        Assert.Equal("2ƒ", live.Button(2).Text);
        Assert.Equal("Copy of one", live.Name(2).Text);
        Assert.Equal(appearance.Color, live.Button(2).Parent!.BackColor);
        Assert.Equal(Color.Black, live.Name(2).ForeColor);
        Assert.True(live.Check(2).Checked);
        Assert.True(live.Offset(2).Enabled);

        live.ClickMenuItem(1, "✕  Clear slot");

        Assert.Equal("", live.Name(1).Text);
        Assert.False(live.Check(1).Enabled);
        Assert.False(live.Check(2).Enabled);
        Assert.False(live.Check(2).Checked);
    });

    [Fact]
    public void ASlotLoadedFromItsFile_ShowsInItsControls() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.CaptureThroughMenu(3, "HD2");
        live.Offset(3).Value = -12m;
        live.Panel.Session.FlushPendingSaves();

        live.Panel.Session.Prepare(Mode.PhaseResponse);
        Assert.Equal("", live.Name(3).Text);
        Assert.Equal(0m, live.Offset(3).Value);
        live.Panel.Session.Prepare(Mode.FrequencyResponse);

        Assert.Equal("HD2", live.Name(3).Text);
        Assert.Equal(-12m, live.Offset(3).Value);
        Assert.True(live.Check(3).Enabled);
        Assert.False(live.Check(3).Checked);
    });

    private sealed class LivePanel : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly string root = Directory.CreateTempSubdirectory("resonalyze-overlay-wiring-").FullName;
        private readonly Form form;
        private readonly Panel container;
        private readonly PlotModel model = new();

        public LivePanel()
        {
            form = new Form { ClientSize = new Size(600, 500) };
            var plotView = new PlotView { Dock = DockStyle.Left, Width = 300, Model = model };
            container = new Panel { Dock = DockStyle.Right, Width = 260, Height = 400 };
            var template = new RoundedPanel { Location = new Point(3, 3), Size = new Size(206, 25) };
            template.Controls.Add(new ReleaseClickCheckBox { Location = new Point(5, 7), Size = new Size(12, 11) });
            template.Controls.Add(new ReleaseClickButton { Name = "buttonSaveOverlay", Location = new Point(23, 3), Size = new Size(38, 19) });
            template.Controls.Add(new Label { Location = new Point(64, 5), Size = new Size(78, 15) });
            template.Controls.Add(new ThemedNumericUpDown
            {
                Location = new Point(145, 3),
                Size = new Size(58, 19),
                Minimum = -180m,
                Maximum = 180m,
                DecimalPlaces = 0
            });
            container.Controls.Add(template);
            form.Controls.Add(plotView);
            form.Controls.Add(container);

            AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0);
            AddLiveCurve(AnalysisCurveKind.SecondHarmonic, "HD2", -30.0);

            var sources = new OverlayPlotSources(() => model, () => Mode.FrequencyResponse);
            Panel = new OverlayPanel(form, container, plotView, new WrappingToolTip(), sources, () => { }, root);
            form.Show();
            Panel.Session.Prepare(Mode.FrequencyResponse);
            StaTest.Pump();
        }

        public OverlayPanel Panel { get; }

        public OverlaySlot Slot(int index) => Panel.Session.Slots[index - 1];

        public Panel SlotPanel(int index) =>
            container.Controls.OfType<Panel>().OrderBy(panel => panel.Top).ElementAt(index - 1);

        public CheckBox Check(int index) => SlotPanel(index).Controls.OfType<CheckBox>().Single();

        public Button Button(int index) => SlotPanel(index).Controls.OfType<Button>().Single();

        public Label Name(int index) => SlotPanel(index).Controls.OfType<Label>().Single();

        public ThemedNumericUpDown Offset(int index) => SlotPanel(index).Controls.OfType<ThemedNumericUpDown>().Single();

        public double SavedOffset(int index) => OverlayFile.Load(Mode.FrequencyResponse, index, root)!.Offset;

        public LineSeries OverlayCurve(int index) =>
            OverlayCurveOrNull(index) ?? throw new InvalidOperationException($"Slot {index} draws nothing.");

        public LineSeries? OverlayCurveOrNull(int index) =>
            model.Series.OfType<LineSeries>().SingleOrDefault(series =>
                series.Tag is string tag && tag == $"overlay:FrequencyResponse:{index}:curve");

        public void CaptureThroughMenu(int index, string curveTitle)
        {
            ContextMenuStrip menu = OpenMenu(index);
            var capture = (ToolStripMenuItem)menu.Items[0];
            ToolStripItem pick = capture.DropDownItems.Cast<ToolStripItem>().Single(item => item.Text == curveTitle);
            menu.Close();
            pick.PerformClick();
        }

        public void ClickMenuItem(int index, string text)
        {
            ContextMenuStrip menu = OpenMenu(index);
            ToolStripItem item = menu.Items.Cast<ToolStripItem>().Single(candidate => candidate.Text == text);
            menu.Close();
            item.PerformClick();
        }

        public void PumpFor(int milliseconds)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < until)
            {
                StaTest.Pump();
                Thread.Sleep(10);
            }
        }

        public void Dispose()
        {
            form.Dispose();
            Directory.Delete(root, recursive: true);
        }

        private ContextMenuStrip OpenMenu(int index)
        {
            Button(index).PerformClick();
            OverlaySlotView view = Panel.Views[index - 1];
            var menu = (ContextMenuStrip)typeof(OverlaySlotView).GetField("captureMenu", Hidden)!.GetValue(view)!;
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!menu.Visible && DateTime.UtcNow < deadline)
            {
                StaTest.Pump();
                Thread.Sleep(5);
            }

            Assert.True(menu.Visible, "The slot menu did not open.");
            return menu;
        }

        private void AddLiveCurve(AnalysisCurveKind kind, string title, double level)
        {
            var series = new LineSeries { Title = title, Tag = new CurveTag(Mode.FrequencyResponse, kind) };
            for (int i = 0; i < 16; i++)
            {
                series.Points.Add(new DataPoint(20 * Math.Pow(2, i * 0.625), level));
            }

            model.Series.Add(series);
        }
    }
}
