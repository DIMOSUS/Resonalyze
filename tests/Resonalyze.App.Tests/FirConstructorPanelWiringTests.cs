using System.Drawing;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>The FIR Constructor through its controls, file dialogs and host callbacks beside a session driven the same way:
/// after every step the panel shows what the session's readers return, so a read-out, a field or a plot bound to
/// anything else fails here.</summary>
public sealed class FirConstructorPanelWiringTests
{
    [Fact]
    public void TheDesignFields_BuildTheKernel_AndEveryReadOutFollowsTheSession()
    {
        StaTest.Run(() =>
        {
            using var constructor = new Constructor();
            constructor.AssertShows();

            constructor.Pick("comboBoxType", "High pass", draft => draft with { Kind = CrossoverKind.HighPass });
            constructor.Pick("comboBoxType", "Band pass", draft => draft with { Kind = CrossoverKind.BandPass });
            constructor.Number("numericHighPassHz", 250, draft => draft with { HighPassEdge = draft.HighPassEdge with { FrequencyHz = 250 } });
            constructor.Number("numericLowPassHz", 3_000, draft => draft with { LowPassEdge = draft.LowPassEdge with { FrequencyHz = 3_000 } });
            constructor.Pick("comboBoxMethod", "Windowed sinc", draft => draft with { Method = FirCrossoverMethod.WindowedSinc });
            constructor.Pick("comboBoxWindow", "Hann", draft => draft with { Window = FirWindow.Hann });
            constructor.Pick("comboBoxWindow", "Kaiser", draft => draft with { Window = FirWindow.Kaiser });
            constructor.Number("numericKaiserBeta", 4.5m, draft => draft with { KaiserBeta = 4.5 });
            constructor.Pick("comboBoxMethod", "IIR magnitude", draft => draft with { Method = FirCrossoverMethod.IirMagnitude });
            constructor.Pick("comboBoxLowPassSlope", "96 dB/oct", draft => draft with { LowPassEdge = draft.LowPassEdge with { SlopeDbPerOctave = 96 } });
            // A family without the slope keeps the nearest it offers.
            constructor.Pick("comboBoxLowPassFamily", "Bessel", draft => draft with
            {
                LowPassEdge = draft.LowPassEdge with { Family = CrossoverFilterFamily.Bessel, SlopeDbPerOctave = 48 }
            });
            constructor.Pick("comboBoxHighPassFamily", "Butterworth", draft => draft with
            {
                HighPassEdge = draft.HighPassEdge with { Family = CrossoverFilterFamily.Butterworth }
            });
            constructor.Number("numericTaps", 2_000, draft => draft with { TapCount = 2_001 });
            constructor.Pick("comboBoxSampleRate", "96 kHz", draft => draft with { SampleRateHz = 96_000 });
            constructor.Pick("comboBoxType", "Low pass", draft => draft with { Kind = CrossoverKind.LowPass });
        });
    }

    [Fact]
    public void AProblem_ClearsTheKernel_AndTheNextEditBuildsAgain()
    {
        StaTest.Run(() =>
        {
            using var folder = new TemporaryDirectory();
            using var constructor = new Constructor();
            string kernel = folder.File("room.txt");
            FirFilterFiles.Save(kernel, new FirFilter([0.5, 0.25, -0.125]), 48_000, null);
            constructor.Pick("comboBoxType", "Band pass", draft => draft with { Kind = CrossoverKind.BandPass });
            constructor.Number("numericHighPassHz", 5_000, draft => draft with { HighPassEdge = draft.HighPassEdge with { FrequencyHz = 5_000 } });
            Assert.NotEmpty(constructor.Text("labelProblem"));
            Assert.Null(constructor.Panel.CurrentKernel);

            // A kernel shown in the problem's place clears it.
            constructor.Import(kernel);
            Assert.Empty(constructor.Text("labelProblem"));
            constructor.Number("numericHighPassHz", 6_000, draft => draft with { HighPassEdge = draft.HighPassEdge with { FrequencyHz = 6_000 } });
            Assert.NotEmpty(constructor.Text("labelProblem"));

            constructor.Number("numericHighPassHz", 500, draft => draft with { HighPassEdge = draft.HighPassEdge with { FrequencyHz = 500 } });
            Assert.Empty(constructor.Text("labelProblem"));
        });
    }

    [Fact]
    public void TheImpulseScale_RefitsTheView_AndTheSameKernelRedrawnKeepsTheZoom()
    {
        StaTest.Run(() =>
        {
            using var constructor = new Constructor();
            var room = new FirFilter([0.9, -0.3, 0.2, -0.1, 0.05], 48_000);
            var owner = new VirtualCrossoverChannel("B");
            owner.Pair.Left.Fir = room;
            owner.Pair.Left.FirSourceName = "room.wav";

            constructor.Click<CheckBox>("checkBoxImpulseDb");
            constructor.AssertShows();

            constructor.Handoff(FirConstructorHandoff.Build(owner, false, 1, 48_000));
            constructor.ZoomImpulse();
            constructor.Handoff(FirConstructorHandoff.Build(owner, false, 2, 48_000));
            Assert.Equal(-0.5, constructor.ImpulseTimeAxis().ActualMinimum, 9);

            constructor.Click<CheckBox>("checkBoxImpulseDb");
            constructor.AssertShows();
            Assert.NotEqual(-0.5, constructor.ImpulseTimeAxis().ActualMinimum, 9);

            constructor.ZoomImpulse();
            constructor.Number("numericTaps", 255, draft => draft with { TapCount = 255 });
            Assert.NotEqual(-0.5, constructor.ImpulseTimeAxis().ActualMinimum, 9);
        });
    }

    [Fact]
    public void ImportAndExport_GoThroughTheFileDialog_AndAFailureIsAWarning()
    {
        StaTest.Run(() =>
        {
            using var folder = new TemporaryDirectory();
            using var constructor = new Constructor();
            string bad = folder.File("bad.txt");
            File.WriteAllText(bad, "no taps here");
            string kernel = folder.File("room correction.txt");
            FirFilterFiles.Save(kernel, new FirFilter([0.5, 0.25, -0.125, 0.0625]), 44_100, null);

            constructor.Export(folder.File("design.wav"));
            constructor.Export(folder.File("design.txt"));
            constructor.Export(null);
            constructor.Export(folder.File(Path.Combine("missing", "x.txt")));
            Assert.StartsWith("FIR filter could not be exported." + Environment.NewLine + Environment.NewLine, constructor.Warnings.Single());

            constructor.Import(null);
            constructor.Import(bad);
            Assert.StartsWith("FIR filter could not be opened." + Environment.NewLine + Environment.NewLine, constructor.Warnings[^1]);
            constructor.Pick("comboBoxSampleRate", "88.2 kHz", draft => draft with { SampleRateHz = 88_200 });
            constructor.Import(kernel);
            Assert.Equal("room correction.txt", constructor.Shadow.KernelName);

            constructor.Export(folder.File("bare.txt"));
            Assert.Equal(2, constructor.Warnings.Count);
        });
    }

    [Fact]
    public void AHandoff_WritesTheDesignOrTheSeedAtTheProcessorsRate_AndItsEndPutsTheStandaloneWorkBack()
    {
        StaTest.Run(() =>
        {
            using var folder = new TemporaryDirectory();
            using var constructor = new Constructor();
            string kernel = folder.File("room.txt");
            FirFilterFiles.Save(kernel, new FirFilter([0.5, 0.25, -0.125]), 48_000, null);
            constructor.Pick("comboBoxSampleRate", "96 kHz", draft => draft with { SampleRateHz = 96_000 });
            constructor.Import(kernel);

            FirCrossoverDesign design = new(
                CrossoverKind.HighPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_000, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 120, 36),
                FirCrossoverMethod.IirMagnitude,
                FirWindow.Blackman,
                6,
                1_023,
                96_000);
            var designed = new VirtualCrossoverChannel("B");
            designed.Pair.Left.Fir = design.Build();
            designed.Pair.Left.FirDesign = design;
            constructor.Handoff(FirConstructorHandoff.Build(designed, false, 1, 48_000));
            Assert.Contains("rebuilt here at 48 kHz", constructor.Text("labelSession"));

            var seeded = new VirtualCrossoverChannel("C");
            seeded.Pair.Right.CrossoverKind = CrossoverKind.BandPass;
            seeded.Pair.Right.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Chebyshev, 400, 18);
            seeded.Pair.Right.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Bessel, 3_500, 24);
            constructor.Handoff(FirConstructorHandoff.Build(seeded, true, 1, 44_100));

            constructor.Number("numericTaps", 511, draft => draft with { TapCount = 511 });
            constructor.EndHandoff();
            Assert.Null(constructor.Panel.CurrentDesign);

            constructor.Number("numericTaps", 1_025, draft => draft with { TapCount = 1_025 });
            constructor.Handoff(FirConstructorHandoff.Build(new VirtualCrossoverChannel("D"), false, 1, 192_000));
            // A processor rate the list does not offer joins it.
            constructor.Handoff(FirConstructorHandoff.Build(new VirtualCrossoverChannel("E"), false, 1, 32_000));
            constructor.EndHandoff();
        });
    }

    [Fact]
    public void ReturnAndBack_ReachTheHost_AndReturnTakesOnlyADesign()
    {
        StaTest.Run(() =>
        {
            using var constructor = new Constructor();
            var owner = new VirtualCrossoverChannel("B");
            FirConstructorHandoffRequest request = FirConstructorHandoff.Build(owner, false, 1, 48_000);
            constructor.Handoff(request);

            constructor.Click<Button>("buttonReturnToDsp");
            (FirConstructorReturnToken token, FirFilter? kernel, FirCrossoverDesign? design) = constructor.Returned;
            Assert.Same(request.Token, token);
            Assert.Same(constructor.Panel.CurrentKernel, kernel);
            Assert.Equal(constructor.Shadow.Design, design);

            constructor.Click<Button>("buttonBackToDsp");
            Assert.Equal(1, constructor.Backs);

            var bare = new VirtualCrossoverChannel("C");
            bare.Pair.Left.Fir = new FirFilter([1.0, 0.5]);
            constructor.Returned = default;
            constructor.Handoff(FirConstructorHandoff.Build(bare, false, 1, 48_000));
            constructor.Click<Button>("buttonReturnToDsp");
            Assert.Equal(default, constructor.Returned);
        });
    }

    private static double[]? TapsOf(FirFilter? kernel) => kernel?.Taps.ToArray();

    private sealed class Constructor : IDisposable
    {
        private readonly Form host;
        private readonly Queue<string?> files = new();
        private readonly string cancelled = Path.Combine(Path.GetTempPath(), $"resonalyze-fir-cancelled-{Guid.NewGuid():N}.txt");

        public Constructor()
        {
            host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-10_000, -10_000),
                Size = new Size(1_400, 900),
                ShowInTaskbar = false
            };
            Panel = new FirConstructorPanel { Dock = DockStyle.Fill };
            host.Controls.Add(Panel);
            Panel.ShowFileDialog = dialog =>
            {
                Suggested = dialog.FileName;
                if (files.Dequeue() is not { } answer)
                {
                    // A cancelled dialog still holds a name.
                    dialog.FileName = cancelled;
                    return DialogResult.Cancel;
                }

                dialog.FileName = answer;
                return DialogResult.OK;
            };
            Panel.Warn = Warnings.Add;
            Panel.ReturnFirRequested = (token, kernel, design) => Returned = (token, kernel, design);
            Panel.BackToVirtualDspRequested = () => Backs++;
            host.Show();
            Settle();
            Edit(Draft);
        }

        public FirConstructorPanel Panel { get; }

        public FirConstructorSession Shadow { get; } = new();

        // The designer's fields.
        public FirCrossoverDesign Draft { get; private set; } = new(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24),
            FirCrossoverMethod.IirMagnitude,
            FirWindow.Kaiser,
            8,
            4_095,
            48_000);

        public List<string> Warnings { get; } = [];

        public string? Suggested { get; private set; }

        public (FirConstructorReturnToken, FirFilter?, FirCrossoverDesign?) Returned { get; set; }

        public int Backs { get; private set; }

        public void Pick(string name, string label, Func<FirCrossoverDesign, FirCrossoverDesign> edit)
        {
            ThemedComboBox combo = Find<ThemedComboBox>(name);
            int index = combo.Items.Cast<object>().Select(combo.GetItemText).ToList().IndexOf(label);
            Assert.True(index >= 0, $"no {label} in {name}");
            combo.SelectedIndex = index;
            Edit(edit(Draft));
        }

        public void Number(string name, decimal value, Func<FirCrossoverDesign, FirCrossoverDesign> edit)
        {
            Find<ThemedNumericUpDown>(name).Value = value;
            Edit(edit(Draft));
        }

        public void Click<T>(string name) where T : Control
        {
            T control = Find<T>(name);
            if (control is Button button)
            {
                button.PerformClick();
            }
            else
            {
                ((CheckBox)(Control)control).Checked ^= true;
            }

            Settle();
        }

        public void Import(string? path)
        {
            int warned = Warnings.Count;
            files.Enqueue(path);
            Find<Button>("buttonImport").PerformClick();
            Settle();
            if (path != null && Warnings.Count == warned)
            {
                Land(Shadow.ShowBare(FirFilterFiles.Load(path), Path.GetFileName(path), Draft.SampleRateHz));
            }

            AssertShows();
        }

        public void Export(string? path)
        {
            FirConstructorExportRequest request = FirConstructorExport.Request(Shadow)!;
            int warned = Warnings.Count;
            files.Enqueue(path);
            Find<Button>("buttonExport").PerformClick();
            Settle();
            Assert.Equal(request.SuggestedFileName, Suggested);
            Assert.False(File.Exists(cancelled), "a cancelled export wrote a file");
            if (path != null && Warnings.Count == warned)
            {
                string expected = path + ".expected" + Path.GetExtension(path);
                FirConstructorExport.Save(request, expected);
                Assert.Equal(File.ReadAllBytes(expected), File.ReadAllBytes(path));
            }

            AssertShows();
        }

        public void Handoff(FirConstructorHandoffRequest request)
        {
            Shadow.BeginHandoff(request, Draft);
            if (request.Design is { } arrived)
            {
                Draft = Written(arrived, request.ProcessorSampleRateHz);
            }
            else if (request.SeedCrossover is { Kind: not CrossoverKind.Off } seed)
            {
                Draft = Draft with
                {
                    Kind = seed.Kind,
                    HighPassEdge = seed.HighPassEdge is { } high ? Written(high) : Draft.HighPassEdge,
                    LowPassEdge = seed.LowPassEdge is { } low ? Written(low) : Draft.LowPassEdge,
                    SampleRateHz = request.ProcessorSampleRateHz
                };
            }
            else
            {
                Draft = Draft with { SampleRateHz = request.ProcessorSampleRateHz };
            }

            Panel.BeginVirtualDspHandoff(request);
            Settle();
            if (request.Design == null && request.Kernel is { } bare)
            {
                Land(Shadow.ShowBare(bare, request.KernelName, request.ProcessorSampleRateHz));
            }
            else
            {
                ShadowEdit();
            }

            AssertShows();
        }

        public void EndHandoff()
        {
            FirConstructorStandaloneWork? work = Shadow.EndHandoff();
            Panel.EndVirtualDspHandoff();
            Settle();
            if (work != null)
            {
                Draft = work.Controls;
                if (work.BareKernel is { } bare)
                {
                    Land(Shadow.ShowBare(bare, work.BareName, work.Controls.SampleRateHz));
                }
                else
                {
                    ShadowEdit();
                }
            }

            AssertShows();
        }

        public void ZoomImpulse()
        {
            ImpulseTimeAxis().Zoom(-0.5, 0.75);
            ImpulseModel().InvalidatePlot(false);
        }

        public Axis ImpulseTimeAxis()
        {
            PlotModel model = ImpulseModel();
            ((IPlotModel)model).Update(false);
            return model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        }

        public string Text(string name) => Find<Control>(name).Text;

        public void AssertShows()
        {
            Assert.Equal(Shadow.Design, Panel.CurrentDesign);
            Assert.Equal(TapsOf(Shadow.Kernel), TapsOf(Panel.CurrentKernel));
            Assert.Equal(Shadow.InHandoff, Panel.InVirtualDspHandoff);
            Assert.False(Panel.RebuildPending);
            Assert.Equal(FirConstructorReadout.Session(Shadow), Text("labelSession"));
            Assert.Equal(FirConstructorReadout.Latency(Shadow), Text("labelLatency"));
            Assert.Equal(FirConstructorReadout.Deviation(Shadow), Text("labelDeviation"));
            Assert.Equal(Shadow.Problem, Text("labelProblem"));
            Assert.Equal(FirConstructorAvailability.CanExport(Shadow), Find<Button>("buttonExport").Enabled);
            Assert.Equal(FirConstructorAvailability.Return(Shadow) != null, Find<Button>("buttonReturnToDsp").Enabled);
            Assert.Equal(Shadow.InHandoff, Find<Button>("buttonReturnToDsp").Visible);
            Assert.Equal(Shadow.InHandoff, Find<Button>("buttonBackToDsp").Visible);
            Assert.Equal(!Shadow.InHandoff, Find<ThemedComboBox>("comboBoxSampleRate").Enabled);

            Assert.Equal(Label(FirConstructorChoices.Kinds, Draft.Kind), Selected("comboBoxType"));
            Assert.Equal(Label(FirConstructorChoices.Methods, Draft.Method), Selected("comboBoxMethod"));
            Assert.Equal(Label(FirConstructorChoices.Windows, Draft.Window), Selected("comboBoxWindow"));
            Assert.Equal(FirConstructorChoices.RateLabel(Draft.SampleRateHz), Selected("comboBoxSampleRate"));
            AssertEdge(Draft.HighPassEdge, "numericHighPassHz", "comboBoxHighPassFamily", "comboBoxHighPassSlope");
            AssertEdge(Draft.LowPassEdge, "numericLowPassHz", "comboBoxLowPassFamily", "comboBoxLowPassSlope");
            Assert.Equal((decimal)Draft.KaiserBeta, Find<ThemedNumericUpDown>("numericKaiserBeta").Value);
            Assert.Equal(Draft.TapCount, (int)Find<ThemedNumericUpDown>("numericTaps").Value);

            FirConstructorFields fields = FirConstructorAvailability.Fields(Draft);
            AssertTakes(fields.HighPass, "labelHighPass", "numericHighPassHz");
            AssertTakes(fields.HighPassShape, "comboBoxHighPassFamily", "comboBoxHighPassSlope");
            AssertTakes(fields.LowPass, "labelLowPass", "numericLowPassHz");
            AssertTakes(fields.LowPassShape, "comboBoxLowPassFamily", "comboBoxLowPassSlope");
            AssertTakes(fields.KaiserBeta, "labelKaiserBeta", "numericKaiserBeta");

            FirConstructorRendering? rendering = Shadow.Rendering;
            PlotModel response = Find<PlotView>("plotResponse").Model!;
            Assert.Equal(rendering?.Magnitude ?? [], Series(response, "Magnitude").Points);
            Assert.Equal(rendering?.Target ?? [], Series(response, "Target").Points);
            Assert.Equal(rendering?.Phase ?? [], Series(response, "Phase").Points);
            bool decibels = Find<CheckBox>("checkBoxImpulseDb").Checked;
            var impulse = (LineSeries)ImpulseModel().Series.Single();
            Assert.Equal((decibels ? rendering?.ImpulseDb : rendering?.Impulse) ?? [], impulse.Points);
            Assert.Equal(decibels ? "dB" : "Amplitude", ImpulseModel().Axes.Single(axis => axis.Position == AxisPosition.Left).Title);
        }

        public void Dispose()
        {
            Panel.Dispose();
            host.Dispose();
        }

        private void Edit(FirCrossoverDesign draft)
        {
            Draft = draft;
            Settle();
            ShadowEdit();
            AssertShows();
        }

        private void ShadowEdit()
        {
            if (Shadow.Edit(Draft) is { } rebuild)
            {
                Land(rebuild);
            }
        }

        private void Land(FirConstructorRebuild rebuild)
        {
            using (rebuild)
            {
                Assert.True(Shadow.Land(rebuild, FirConstructorRender.Run(rebuild, CancellationToken.None)));
                Shadow.Finish(rebuild);
            }
        }

        // Background rebuild continuations are posted to this thread's WinForms synchronization context.
        private void Settle()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            do
            {
                StaTest.Pump();
                Thread.Sleep(5);
                Assert.True(DateTime.UtcNow < deadline, "The constructor's rebuild never landed.");
            }
            while (Panel.RebuildPending);

            StaTest.Pump();
        }

        private static FirCrossoverDesign Written(FirCrossoverDesign arrived, int rate) =>
            arrived with
            {
                HighPassEdge = Written(arrived.HighPassEdge),
                LowPassEdge = Written(arrived.LowPassEdge),
                SampleRateHz = rate
            };

        // What an edge reads once the fields hold it: a family the constructor offers and the slope nearest the edge's.
        private static CrossoverEdge Written(CrossoverEdge edge)
        {
            CrossoverFilterFamily family = FirConstructorChoices.OfferedFamily(edge.Family);
            return new CrossoverEdge(family, edge.FrequencyHz, FirConstructorChoices.NearestSlope(family, edge.SlopeDbPerOctave));
        }

        private void AssertEdge(CrossoverEdge edge, string frequency, string family, string slope)
        {
            Assert.Equal((decimal)edge.FrequencyHz, Find<ThemedNumericUpDown>(frequency).Value);
            Assert.Equal(Label(FirConstructorChoices.Families, edge.Family), Selected(family));
            Assert.Equal($"{edge.SlopeDbPerOctave} dB/oct", Selected(slope));
        }

        private void AssertTakes(bool takes, string first, string second)
        {
            foreach (Control control in new[] { Find<Control>(first), Find<Control>(second) })
            {
                if (control is Label label)
                {
                    Assert.Equal(takes, label.ForeColor != UiPalette.TextDisabled);
                }
                else
                {
                    Assert.Equal(takes, control.Enabled);
                }
            }
        }

        private string Selected(string name)
        {
            ThemedComboBox combo = Find<ThemedComboBox>(name);
            return combo.GetItemText(combo.SelectedItem);
        }

        private static string Label<T>(IEnumerable<(T Value, string Label)> choices, T value) =>
            choices.Single(choice => EqualityComparer<T>.Default.Equals(choice.Value, value)).Label;

        private static LineSeries Series(PlotModel model, string title) =>
            model.Series.OfType<LineSeries>().Single(series => series.Title == title);

        private PlotModel ImpulseModel() => Find<PlotView>("plotImpulse").Model!;

        private T Find<T>(string name) where T : Control =>
            (T)Panel.Controls.Find(name, searchAllChildren: true).Single();
    }
}
