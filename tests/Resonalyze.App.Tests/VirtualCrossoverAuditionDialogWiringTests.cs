using System.Windows.Forms;
using Resonalyze.Dsp;
using static Resonalyze.App.Tests.AuditionFixtures;

namespace Resonalyze.App.Tests;

/// <summary>A shown audition dialog driven through its controls, beside a session the test changes the same way: what it
/// shows, and what a render writes, must be what the readers make of that session.</summary>
public sealed class VirtualCrossoverAuditionDialogWiringTests
{
    public static TheoryData<string> Changes =>
    [
        "track", "track cancelled", "track is the output", "unreadable track", "output", "first output", "output is the track",
        "own", "own refused", "good", "broken", "cabin", "untick", "tick without averages"
    ];

    [Theory]
    [Trait("Category", "Slow")]
    [MemberData(nameof(Changes))]
    public void EachChoice_ReachesTheReportAndTheRenderButton(string change) => StaTest.Run(() =>
    {
        using var folder = new TemporaryDirectory();
        string song = Track(folder.Path, "song.wav", 44_100, 1, 0.1);
        string other = Track(folder.Path, "other.wav", Rate, 2, 0.1);
        VirtualCrossoverAuditionContext context = change switch
        {
            "own refused" => Context(own: Conflict()),
            "tick without averages" => Context(averages: false),
            _ => Context()
        };
        var start = new VirtualCrossoverAuditionMemory
        {
            SourcePath = song,
            TargetPath = change == "first output" ? null : folder.File("out.wav")
        };
        using var audition = new Audition(context, Copy(start));
        VirtualCrossoverAuditionMemory expectedMemory = Copy(start);
        VirtualCrossoverAuditionSession expected = VirtualCrossoverAuditionSession.Restore(context, expectedMemory);
        expected.SelectCalibration(null, "Off");
        audition.AssertShows(expected);

        switch (change)
        {
            case "track":
                audition.Files.Enqueue(other);
                audition.Click("buttonChooseSource");
                expected.SelectSource(other);
                break;
            case "track cancelled":
                audition.Files.Enqueue(null);
                audition.Click("buttonChooseSource");
                break;
            case "track is the output":
                audition.Files.Enqueue(folder.File("OUT.wav"));
                audition.Click("buttonChooseSource");
                Assert.Contains("already chosen as the OUTPUT", Assert.Single(audition.Asked));
                break;
            case "unreadable track":
                audition.Files.Enqueue(folder.File("missing.wav"));
                audition.Click("buttonChooseSource");
                expected.SelectSource(folder.File("missing.wav"));
                break;
            case "output":
            case "first output":
                audition.Files.Enqueue(folder.File("second.wav"));
                audition.Click("buttonChooseTarget");
                expected.SelectTarget(folder.File("second.wav"));
                Assert.Equal(("song_processed.wav", folder.Path), audition.LastFileDialog);
                break;
            case "output is the track":
                audition.Files.Enqueue(song);
                audition.Click("buttonChooseTarget");
                Assert.Contains("the source track itself", Assert.Single(audition.Asked));
                break;
            case "own":
            case "own refused":
                audition.Calibration(VirtualCrossoverCalibrationSelection.OwnId);
                expected.SelectCalibration(VirtualCrossoverCalibrationSelection.OwnId, "Own (as measured)");
                break;
            case "good":
                audition.Calibration(Good.Id);
                expected.SelectCalibration(Good.Id, Good.Name);
                break;
            case "broken":
                audition.Calibration(Broken.Id);
                expected.SelectCalibration(Broken.Id, Broken.Name);
                break;
            case "cabin":
                audition.Find<ThemedComboBox>("comboBoxCabin").SelectedIndex = 4;
                expected.CabinStyle = CabinBodyStyle.Wagon;
                break;
            case "untick":
                audition.Find<CheckBox>("checkBoxSpatialAverage").Checked = false;
                expected.SpatialAverageRequested = false;
                break;
            case "tick without averages":
                audition.Find<CheckBox>("checkBoxSpatialAverage").Checked = true;
                expected.SpatialAverageRequested = true;
                break;
        }

        audition.AssertShows(expected);
        audition.Dialog.Close();
        expected.Remember();
        Assert.Equivalent(expectedMemory, audition.Memory);
    });

    [Fact]
    public void TheCalibrationThePanelChose_IsTheOneTheDialogOpensWith() => StaTest.Run(() =>
    {
        using var folder = new TemporaryDirectory();
        var start = new VirtualCrossoverAuditionMemory
        {
            SourcePath = Track(folder.Path, "song.wav", Rate, 2, 0.1),
            TargetPath = folder.File("out.wav")
        };
        VirtualCrossoverAuditionContext refused =
            Context(own: Conflict()) with { InitialCalibrationId = VirtualCrossoverCalibrationSelection.OwnId };
        using (var audition = new Audition(refused, Copy(start)))
        {
            VirtualCrossoverAuditionSession expected = VirtualCrossoverAuditionSession.Restore(refused, Copy(start));
            expected.SelectCalibration(VirtualCrossoverCalibrationSelection.OwnId, "Own (as measured)");
            audition.AssertShows(expected);
            Assert.False(audition.Find<Button>("buttonRender").Enabled);
        }

        using var good = new Audition(Context() with { InitialCalibrationId = Good.Id }, Copy(start));
        good.Render();
        Assert.Contains("\r\nCalibration: good mic\r\n", good.Find<TextBox>("textBoxReport").Text);
    });

    [Fact]
    [Trait("Category", "Slow")]
    public void ARender_WritesWhatTheReadersAskFor() => StaTest.Run(() =>
    {
        using var folder = new TemporaryDirectory();
        var start = new VirtualCrossoverAuditionMemory
        {
            SourcePath = Track(folder.Path, "song.wav", 44_100, 2, 0.2),
            TargetPath = folder.File("out.wav"),
            CabinStyle = null
        };
        using var audition = new Audition(Context(), Copy(start));
        audition.Calibration(Good.Id);
        audition.Find<ThemedComboBox>("comboBoxCabin").SelectedIndex = 3;
        audition.Find<CheckBox>("checkBoxSpatialAverage").Checked = false;

        audition.Render();

        VirtualCrossoverAuditionSession expected = VirtualCrossoverAuditionSession.Restore(Context(), Copy(start));
        expected.SelectTarget(folder.File("expected.wav"));
        expected.SelectCalibration(Good.Id, Good.Name);
        expected.CabinStyle = CabinBodyStyle.Hatchback;
        expected.SpatialAverageRequested = false;
        AuditionRenderOutcome outcome = VirtualCrossoverAuditionRender.Run(
            VirtualCrossoverAuditionRender.Request(expected), new SynchronousProgress<AuditionProgress>(_ => { }), default);
        Assert.Equal(File.ReadAllBytes(folder.File("expected.wav")), File.ReadAllBytes(folder.File("out.wav")));
        expected.SelectTarget(folder.File("out.wav"));
        expected.ResultSection = VirtualCrossoverAuditionReport.Result(outcome, folder.File("out.wav"));
        audition.AssertShows(expected);
        Assert.Equal("Finished — wrote out.wav", audition.Find<Label>("labelStatus").Text);
        Assert.Equal(1000, audition.Find<ProgressBar>("progressBar").Value);
        Assert.Empty(audition.Asked);
    });

    [Fact]
    public void ARestoredOutput_IsReplacedOnlyAfterYes_AndOnlyAskedOnce() => StaTest.Run(() =>
    {
        using var folder = new TemporaryDirectory();
        string previous = Track(folder.Path, "out.wav", Rate, 2, 0.05);
        byte[] before = File.ReadAllBytes(previous);
        var start = new VirtualCrossoverAuditionMemory
        {
            SourcePath = Track(folder.Path, "song.wav", Rate, 2, 0.1),
            TargetPath = previous
        };
        using var audition = new Audition(Context(), start);

        audition.Answers.Enqueue(DialogResult.No);
        audition.Click("buttonRender");
        Assert.Equal(before, File.ReadAllBytes(previous));
        Assert.Equal("Render", audition.Find<Button>("buttonRender").Text);

        audition.Answers.Enqueue(DialogResult.Yes);
        audition.Render();
        Assert.NotEqual(before, File.ReadAllBytes(previous));
        audition.Render();

        Assert.Equal(2, audition.Asked.Count);
        Assert.All(audition.Asked, question => Assert.Contains("already exists", question));
    });

    [Fact]
    [Trait("Category", "Slow")]
    public void ACancel_StopsTheRender_AndWritesNothing() => StaTest.Run(() =>
    {
        using var folder = new TemporaryDirectory();
        var start = new VirtualCrossoverAuditionMemory
        {
            SourcePath = Track(folder.Path, "song.wav", Rate, 2, LongEnoughToCancel),
            TargetPath = folder.File("out.wav")
        };
        using var audition = new Audition(Context(), start);

        audition.Click("buttonRender");
        Assert.Equal("Cancel", audition.Find<Button>("buttonRender").Text);
        foreach (string name in new[] { "buttonChooseSource", "buttonChooseTarget", "comboBoxCalibration", "comboBoxCabin", "checkBoxSpatialAverage" })
        {
            Assert.False(audition.Find<Control>(name).Enabled, name + " stays live during a render.");
        }

        audition.Click("buttonRender");
        Assert.False(audition.Find<Button>("buttonRender").Enabled);
        Assert.Equal("Cancelling…", audition.Find<Label>("labelStatus").Text);
        audition.WaitIdle();

        Assert.StartsWith(VirtualCrossoverAuditionReport.Cancelled, audition.Find<TextBox>("textBoxReport").Text);
        Assert.Equal("Cancelled.", audition.Find<Label>("labelStatus").Text);
        Assert.Empty(Directory.GetFiles(folder.Path, "out*"));
        Assert.True(audition.Find<Control>("comboBoxCabin").Enabled);
        Assert.True(audition.Find<Button>("buttonRender").Enabled);
    });

    [Fact]
    public void ClosingDuringARender_CancelsItAndClosesAfter() => StaTest.Run(() =>
    {
        using var folder = new TemporaryDirectory();
        var memory = new VirtualCrossoverAuditionMemory
        {
            SourcePath = Track(folder.Path, "song.wav", Rate, 2, LongEnoughToCancel),
            TargetPath = folder.File("out.wav"),
            CabinStyle = CabinBodyStyle.Suv
        };
        using var audition = new Audition(Context(), memory);
        audition.Find<ThemedComboBox>("comboBoxCabin").SelectedIndex = 2;

        audition.Click("buttonRender");
        audition.Dialog.Close();
        Assert.False(audition.Dialog.IsDisposed, "The dialog closed under its render.");
        audition.Wait(() => audition.Dialog.IsDisposed, "close");

        Assert.Empty(Directory.GetFiles(folder.Path, "out*"));
        Assert.Equal(CabinBodyStyle.CompactSedan, memory.CabinStyle);
    });

    // Seconds of track: a render of it outlasts a test thread that is starved between two clicks.
    private const double LongEnoughToCancel = 30;

    [Fact]
    public void ACabinTheListDoesNotOffer_OpensAsOff() => StaTest.Run(() =>
    {
        var memory = new VirtualCrossoverAuditionMemory { CabinStyle = (CabinBodyStyle)99 };
        using var audition = new Audition(Context(), memory);

        Assert.Equal(0, audition.Find<ThemedComboBox>("comboBoxCabin").SelectedIndex);
        audition.Dialog.Close();
        Assert.Null(memory.CabinStyle);
    });

    private static VirtualCrossoverAuditionMemory Copy(VirtualCrossoverAuditionMemory memory) => new()
    {
        SourcePath = memory.SourcePath,
        TargetPath = memory.TargetPath,
        SpatialAverage = memory.SpatialAverage,
        CabinStyle = memory.CabinStyle
    };

    private sealed class Audition : IDisposable
    {
        public Audition(VirtualCrossoverAuditionContext context, VirtualCrossoverAuditionMemory memory)
        {
            Memory = memory;
            Dialog = new VirtualCrossoverAuditionDialog(context, memory);
            Dialog.ShowFileDialog = dialog =>
            {
                LastFileDialog = (dialog.FileName, dialog.InitialDirectory);
                string? answer = Files.Dequeue();
                if (answer == null)
                {
                    return DialogResult.Cancel;
                }

                dialog.FileName = answer;
                return DialogResult.OK;
            };
            Dialog.Ask = (text, buttons, _) =>
            {
                Asked.Add(text);
                return buttons == MessageBoxButtons.YesNo ? Answers.Dequeue() : DialogResult.OK;
            };
            Dialog.Show();
            StaTest.Pump();
        }

        public VirtualCrossoverAuditionDialog Dialog { get; }

        public VirtualCrossoverAuditionMemory Memory { get; }

        public Queue<string?> Files { get; } = new();

        public Queue<DialogResult> Answers { get; } = new();

        public List<string> Asked { get; } = [];

        public (string FileName, string? Directory) LastFileDialog { get; private set; }

        public T Find<T>(string name) where T : Control =>
            (T)Dialog.Controls.Find(name, searchAllChildren: true).Single();

        public void Click(string name) => Find<Button>(name).PerformClick();

        public void Calibration(string? id)
        {
            ThemedComboBox combo = Find<ThemedComboBox>("comboBoxCalibration");
            combo.SelectedIndex = Enumerable.Range(0, combo.Items.Count).Single(index =>
                ((Options.MicrophoneCalibrationOption)combo.Items[index]!).CalibrationId == id);
        }

        public void Render()
        {
            Click("buttonRender");
            Assert.Equal("Cancel", Find<Button>("buttonRender").Text);
            Assert.StartsWith("== Tune ==", Find<TextBox>("textBoxReport").Text);
            WaitIdle();
        }

        public void WaitIdle() =>
            Wait(() => Find<Button>("buttonRender").Text == "Render" && Find<Button>("buttonChooseSource").Enabled, "render");

        public void Wait(Func<bool> condition, string what)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                StaTest.Pump();
                Thread.Sleep(5);
            }

            Assert.True(condition(), $"The dialog did not {what}.");
            StaTest.Pump();
        }

        public void AssertShows(VirtualCrossoverAuditionSession expected)
        {
            Assert.Equal(VirtualCrossoverAuditionReport.Compose(expected), Find<TextBox>("textBoxReport").Text);
            Assert.Equal(VirtualCrossoverAuditionRender.Available(expected), Find<Button>("buttonRender").Enabled);
            Assert.Equal(expected.SourcePath ?? "no file chosen", Find<Label>("labelSourceFile").Text);
            Assert.Equal(expected.TargetPath ?? "no file chosen", Find<Label>("labelTargetFile").Text);
            Assert.Equal(expected.CabinLabel, Find<ThemedComboBox>("comboBoxCabin").SelectedItem!.ToString());
            Assert.Equal(expected.SpatialAverageRequested, Find<CheckBox>("checkBoxSpatialAverage").Checked);
        }

        public void Dispose()
        {
            if (!Dialog.IsDisposed)
            {
                Dialog.Close();
                Dialog.Dispose();
            }
        }
    }
}
