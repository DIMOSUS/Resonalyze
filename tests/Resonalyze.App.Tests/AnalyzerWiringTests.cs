using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.WindowsForms;
using Resonalyze.Audio;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// The analyzer through its own window: every input lands in one document, and every view shows that one.
/// Drives the real main window, so a view left reading something else fails here.
/// </summary>
[Collection(MainWindowData.Name)]
[Trait("Category", "Slow")]
public sealed class AnalyzerWiringTests : IDisposable
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int SampleRate = 48_000;

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"resonalyze-analyzer-{Guid.NewGuid():N}");

    public AnalyzerWiringTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void AnOpenedFileIsWhatEveryViewShows()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();

            analyzer.Open(path);

            Assert.Equal("Frequency Response - cabin left.json", analyzer.Plot.Title);
            Assert.True(analyzer.Button("buttonSave").Enabled);
            Assert.True(analyzer.Button("buttonRewExport").Enabled);
            Assert.Contains(analyzer.History.Entries, entry => entry.SourceFilePath == path);
            analyzer.Select(ModeTab.Phase);
            Assert.Equal("Transfer IR Peak: 5.000 ms (240 samples)", analyzer.PeakInfo);
            analyzer.Select(ModeTab.TimeAlignment);
            Assert.Contains("cabin left.json", analyzer.TimeAlignment.SourceSummaryLabel.Text);
        });
    }

    // New session used to hide the curves only: Save, Send to REW and Time Alignment still had the measurement.
    [Fact]
    public void ANewSessionClosesTheMeasurementEverywhere()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);

            analyzer.Await("StartNewSessionAsync");

            Assert.Equal("Frequency Response", analyzer.Plot.Title);
            Assert.False(analyzer.Button("buttonSave").Enabled);
            Assert.False(analyzer.Button("buttonRewExport").Enabled);
            analyzer.Select(ModeTab.Phase);
            Assert.Equal("Transfer IR Peak: --", analyzer.PeakInfo);
            analyzer.Select(ModeTab.TimeAlignment);
            Assert.StartsWith("Source: waiting", analyzer.TimeAlignment.SourceSummaryLabel.Text);
        });
    }

    [Fact]
    public void AHistoryEntryReopensItsOwnMeasurement()
    {
        string first = WriteMeasurement("first.json", peak: 240);
        string second = WriteMeasurement("second.json", peak: 480);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(first);
            analyzer.Open(second);
            analyzer.Select(ModeTab.Phase);
            Assert.Equal("Transfer IR Peak: 10.000 ms (480 samples)", analyzer.PeakInfo);

            MeasurementHistoryEntry entry = Assert.Single(
                analyzer.History.Entries,
                candidate => candidate.SourceFilePath == first);
            analyzer.Await("ActivateHistoryEntryAsync", entry.Id, analyzer.Document.TryBegin()!);

            // The entry brings back its session too: it was opened in Frequency Response.
            Assert.Equal("Frequency Response - first.json", analyzer.Plot.Title);
            analyzer.Select(ModeTab.Phase);
            Assert.Equal("Transfer IR Peak: 5.000 ms (240 samples)", analyzer.PeakInfo);
        });
    }

    [Fact]
    public void AnEntryLeftForAnotherFileKeepsTheModeItWasLeftIn()
    {
        string first = WriteMeasurement("first.json", peak: 240);
        string second = WriteMeasurement("second.json", peak: 480);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(first);
            analyzer.Select(ModeTab.Phase);

            analyzer.Open(second);

            MeasurementHistoryEntry entry = Assert.Single(
                analyzer.History.Entries,
                candidate => candidate.SourceFilePath == first);
            Assert.Equal(ModeTab.Phase, entry.Session!.ActiveMode);
        });
    }

    // A newer request supersedes an older one still reading, whichever finishes first.
    [Fact]
    public void AStaleActivationDoesNotLandOverANewerOne()
    {
        string first = WriteMeasurement("first.json", peak: 240);
        string second = WriteMeasurement("second.json", peak: 480);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            AnalyzerDocument document = analyzer.Document;
            analyzer.Open(first);
            analyzer.Open(second);
            MeasurementHistoryEntry entry = Assert.Single(
                analyzer.History.Entries,
                candidate => candidate.SourceFilePath == first);
            AnalyzerDocument.Request stale = document.TryBegin()!;
            _ = document.TryBegin();

            analyzer.Await("ActivateHistoryEntryAsync", entry.Id, stale);

            Assert.Equal("Frequency Response - second.json", analyzer.Plot.Title);
            Assert.Equal(second, document.SourceName);
        });
    }

    // Nothing tells Time Alignment a measurement landed: it reads the document while it is on screen.
    [Fact]
    public void TimeAlignmentReadsAFileOpenedWhileItIsShown()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Select(ModeTab.TimeAlignment);
            Assert.StartsWith("Source: waiting", analyzer.TimeAlignment.SourceSummaryLabel.Text);

            analyzer.Open(path);

            Assert.Contains("cabin left.json", analyzer.TimeAlignment.SourceSummaryLabel.Text);
        });
    }

    [Fact]
    public void TheCompareSelectionRedrawsThePlot()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);
            var compare = analyzer.Field<CompareSelection>("compareSelection");
            Assert.DoesNotContain(analyzer.Plot.Series, IsCompareCurve);

            compare.Set("reference", null, Measurement(peak: 480));
            analyzer.Pump();
            Assert.Contains(analyzer.Plot.Series, IsCompareCurve);

            compare.Clear();
            analyzer.Pump();
            Assert.DoesNotContain(analyzer.Plot.Series, IsCompareCurve);
        });
    }

    [Fact]
    public void ASwitchShowsTheModesFrameAtOnce_AndItsCurvesOnceBuilt()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);

            Task select = analyzer.Start("SelectModeAsync", ModeTab.Phase);

            Assert.True(select.IsCompleted);
            Assert.Equal("Phase Response - cabin left.json", analyzer.Plot.Title);
            Assert.Empty(analyzer.Plot.Series);
            analyzer.Pump();
            Assert.Contains(analyzer.Plot.Series, series => IsCurveOf(series, Mode.PhaseResponse));
        });
    }

    [Fact]
    public void ASwitchBeforeTheLastOneLands_ShowsOnlyTheNewMode()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);

            Assert.True(analyzer.Start("SelectModeAsync", ModeTab.Phase).IsCompleted);
            Task phase = analyzer.Plotter.Drawing;
            Assert.True(analyzer.Start("SelectModeAsync", ModeTab.GroupDelay).IsCompleted);
            StaTest.Settle(phase);
            analyzer.Pump();

            Assert.True(phase.IsCompletedSuccessfully);
            Assert.Equal("Group Delay - cabin left.json", analyzer.Plot.Title);
            Assert.DoesNotContain(analyzer.Plot.Series, series => IsCurveOf(series, Mode.PhaseResponse));
            Assert.Contains(analyzer.Plot.Series, series => IsCurveOf(series, Mode.GroupDelay));
        });
    }

    // The build read what was open when it started; a run taking the document meanwhile cannot tear it.
    [Fact]
    public void ARunStartedWhileTheCurvesBuild_LeavesThemOnScreen()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);

            // The harmonics read the document after the slow primary spectrum, so a live read would find it emptied.
            Assert.True(analyzer.Start("SelectModeAsync", ModeTab.Frequency).IsCompleted);
            analyzer.StartRun();
            analyzer.Pump();

            Assert.Equal("Frequency Response - cabin left.json", analyzer.Plot.Title);
            Assert.Contains(analyzer.Plot.Series, series => series.Tag is CurveTag { Kind: AnalysisCurveKind.SecondHarmonic });
        });
    }

    [Fact]
    public void ARunThatLandsNothing_TakesTheReadoutOffMeasuring_EvenWhenNothingElseMoved()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Select(ModeTab.Phase);
            string idle = analyzer.PeakInfo;

            analyzer.StartRun();
            typeof(Form1).GetMethod("EnterMeasurementRunningState", Hidden)!.Invoke(analyzer.Form, []);
            analyzer.Pump();
            Assert.NotEqual(idle, analyzer.PeakInfo);

            analyzer.CompleteRun(null);
            Assert.Equal(idle, analyzer.PeakInfo);
        });
    }

    // A save renames the open measurement; the title follows the document, and nothing else is rebuilt.
    [Fact]
    public void ARenameRetitlesThePlot()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);
            PlotModel drawn = analyzer.Plot;

            analyzer.Document.Rename(Path.Combine(directory, "saved.json"));
            analyzer.Pump();

            Assert.Same(drawn, analyzer.Plot);
            Assert.Equal("Frequency Response - saved.json", analyzer.Plot.Title);
        });
    }

    [Fact]
    public void AnImportThatLandsNothing_LeavesThePlotAlone()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);
            PlotModel drawn = analyzer.Plot;

            AnalyzerDocument.Request import = analyzer.Document.TryAcquire()!;
            analyzer.Pump();
            import.Dispose();
            analyzer.Pump();

            Assert.Same(drawn, analyzer.Plot);
        });
    }

    [Fact]
    public void ACompareChosenWhereTheModeDoesNotDrawIt_LeavesThePlotAlone()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);
            analyzer.Select(ModeTab.Autocorrelation);
            PlotModel drawn = analyzer.Plot;

            analyzer.Field<CompareSelection>("compareSelection").Set("reference", null, Measurement(peak: 480));
            analyzer.Pump();

            Assert.Same(drawn, analyzer.Plot);
        });
    }

    // A run empties the document from its first sample, but the plot keeps what it drew until the result lands.
    [Fact]
    public void ARunKeepsThePlotUntilItsResultLands()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);

            analyzer.StartRun();
            analyzer.Pump();
            Assert.Equal("Frequency Response - cabin left.json", analyzer.Plot.Title);

            analyzer.CompleteRun(Measurement(peak: 480));
            Assert.Equal("Frequency Response", analyzer.Plot.Title);
        });
    }

    // The plot kept the old curves while the run held the document; a run that lands nothing must not leave them there.
    [Fact]
    public void ARunThatEndsWithoutAResultClearsThePlot()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);
            analyzer.StartRun();
            analyzer.Pump();
            Assert.Equal("Frequency Response - cabin left.json", analyzer.Plot.Title);

            analyzer.CompleteRun(null);

            Assert.Equal("Frequency Response", analyzer.Plot.Title);
            Assert.False(analyzer.Document.IsBusy);
        });
    }

    // A compare chosen while an import holds the document waits for the hold, and shows even if the import fails.
    [Fact]
    public void ACompareChosenDuringAnImportShowsWhenItEnds()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Open(path);
            AnalyzerDocument.Request import = analyzer.Document.TryAcquire()!;

            analyzer.Field<CompareSelection>("compareSelection").Set("reference", null, Measurement(peak: 480));
            analyzer.Pump();
            Assert.DoesNotContain(analyzer.Plot.Series, IsCompareCurve);

            import.Dispose();
            analyzer.Pump();
            Assert.Contains(analyzer.Plot.Series, IsCompareCurve);
        });
    }

    // Time Alignment keeps its read while a run holds the document, and follows the document when the run ends.
    [Fact]
    public void TimeAlignmentKeepsItsReadWhileARunHoldsTheDocument()
    {
        string path = WriteMeasurement("cabin left.json", peak: 240);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            analyzer.Select(ModeTab.TimeAlignment);
            analyzer.Open(path);

            analyzer.StartRun();
            analyzer.Pump();
            Assert.Contains("cabin left.json", analyzer.TimeAlignment.SourceSummaryLabel.Text);

            analyzer.CompleteRun(null);
            Assert.StartsWith("Source: waiting", analyzer.TimeAlignment.SourceSummaryLabel.Text);
        });
    }

    // New session aborts a run, but one finishing meanwhile still completes; its result belongs to the old session.
    [Fact]
    public void ARunFinishingAfterANewSessionDoesNotLand()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            AnalyzerDocument document = analyzer.Document;
            analyzer.StartRun();
            analyzer.CompleteRun(Measurement(peak: 240));
            Assert.True(document.HasResult);
            int entries = analyzer.History.Entries.Count;

            analyzer.StartRun();
            analyzer.Await("StartNewSessionAsync");
            analyzer.CompleteRun(Measurement(peak: 480));

            Assert.False(document.HasResult);
            Assert.False(document.IsBusy);
            Assert.Equal(entries, analyzer.History.Entries.Count);
            Assert.Equal("Aborted", analyzer.Button("buttonRecord").Text);
            analyzer.Select(ModeTab.Phase);
            Assert.Equal("Transfer IR Peak: --", analyzer.PeakInfo);
        });
    }

    // The import holds the document through a decode that can take seconds; New session meanwhile must win.
    [Fact]
    public void ARecordedSweepDecodedAcrossANewSessionDoesNotLand()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            AnalyzerDocument document = analyzer.Document;
            string recording = WriteRecordedSweep(analyzer, "sweep.wav");
            analyzer.Await("ImportRecordedSweepAsync", recording);
            Assert.Equal(recording, document.SourceName);
            int entries = analyzer.History.Entries.Count;

            Task import = analyzer.Start("ImportRecordedSweepAsync", recording);
            Assert.True(document.IsBusy);
            analyzer.Await("StartNewSessionAsync");
            analyzer.Settle(import);

            Assert.False(document.HasResult);
            Assert.False(document.IsBusy);
            Assert.Equal(entries, analyzer.History.Entries.Count);
        });
    }

    // An import took no request before: a load finishing under it either overwrote it or failed as "a measurement is running".
    [Fact]
    public void ALoadStartedBeforeAnImportDoesNotLandOverIt()
    {
        string first = WriteMeasurement("first.json", peak: 240);
        string second = WriteMeasurement("second.json", peak: 480);
        StaTest.Run(() =>
        {
            using var analyzer = new LiveAnalyzer();
            AnalyzerDocument document = analyzer.Document;
            analyzer.Open(first);
            analyzer.Open(second);
            string recording = WriteRecordedSweep(analyzer, "sweep.wav");
            MeasurementHistoryEntry entry = Assert.Single(
                analyzer.History.Entries,
                candidate => candidate.SourceFilePath == first);
            AnalyzerDocument.Request load = document.TryBegin()!;

            Task import = analyzer.Start("ImportRecordedSweepAsync", recording);
            analyzer.Await("ActivateHistoryEntryAsync", entry.Id, load);
            // Whichever finished first, the load must not have landed.
            Assert.NotEqual(first, document.SourceName);
            analyzer.Settle(import);

            Assert.Equal(recording, document.SourceName);
            Assert.Equal("Frequency Response - sweep.wav", analyzer.Plot.Title);
        });
    }

    private static bool IsCompareCurve(OxyPlot.Series.Series series) =>
        series.Tag is CurveTag { Source: CurveSource.Compare };

    private static bool IsCurveOf(OxyPlot.Series.Series series, Mode mode) =>
        series.Tag is CurveTag tag && tag.Mode == mode;

    private static MeasurementResult Measurement(int peak)
    {
        var impulse = new Complex[8_192];
        impulse[peak] = Complex.One;
        impulse[peak + 96] = new Complex(0.3, 0.0);
        return TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: SampleRate,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: impulse,
            sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: impulse,
            transferPeakIndex: peak);
    }

    // The sweep the window is configured for, recorded clean and mono so the import asks nothing.
    private string WriteRecordedSweep(LiveAnalyzer analyzer, string name)
    {
        SweepSignalConfiguration signal = analyzer.Field<MeasurementSettingsFile>("measurementSettings")
            .Measurement.BuildConfiguration().Signal;
        using var sweep = new ExponentialSineSweep();
        sweep.FillData(
            signal.LowFrequencyHz,
            signal.HighFrequencyHz,
            signal.RequestedDurationSeconds,
            signal.Bits,
            signal.SampleRate);
        int lead = signal.SampleRate / 20;
        var recording = new float[lead + sweep.SweepData.Length + signal.SampleRate / 10];
        for (int i = 0; i < sweep.SweepData.Length; i++)
        {
            recording[lead + i] = sweep.SweepData[i] * 0.3f;
        }

        string path = Path.Combine(directory, name);
        AudioFileCodec.WriteWav(path, new AudioFileContent([recording], signal.SampleRate));
        return path;
    }

    private string WriteMeasurement(string name, int peak)
    {
        string path = Path.Combine(directory, name);
        ImpulseResponseFile.From(Measurement(peak)).SaveAsync(path).GetAwaiter().GetResult();
        return path;
    }

    [Fact]
    public void TheSideKeys_ReachTheVirtualDsp_OnlyWhileItIsShown()
    {
        StaTest.Run(() =>
        {
            using var live = new LiveAnalyzer();
            VirtualCrossoverSession session = live.Field<VirtualCrossoverPanel>("virtualCrossoverPanel").Session;
            Assert.False(session.Project.ActiveSideRight);

            Assert.False(live.PressKey(Keys.R));
            Assert.False(session.Project.ActiveSideRight);

            live.Select(ModeTab.ToolsVirtualCrossover);
            Assert.True(live.PressKey(Keys.R));
            Assert.True(session.Project.ActiveSideRight);
            Assert.True(live.PressKey(Keys.Oemtilde));
            Assert.False(session.Project.ActiveSideRight);
        });
    }

    private sealed class LiveAnalyzer : IDisposable
    {
        public LiveAnalyzer()
        {
            // The read-outs format with the thread's culture.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            MainWindowData.Reset();
            Form = new Form1();
            _ = Form.Handle;
            Pump();
        }

        public Form1 Form { get; }

        public PlotModel Plot => Field<PlotView>("plotView1").Model!;

        public AnalyzerPlot Plotter => Field<AnalyzerPlot>("analyzerPlot");

        public string PeakInfo => Plot.Annotations
            .OfType<TextualAnnotation>()
            .Single(annotation => Equals(annotation.Tag, "PeakInfoAnnotation"))
            .Text ?? string.Empty;

        public MeasurementHistoryService History => Field<MeasurementHistoryService>("measurementHistoryService");

        public TimeAlignmentPanel TimeAlignment => Field<TimeAlignmentPanel>("timeAlignmentPanel");

        public AnalyzerDocument Document => Field<AnalyzerDocument>("analyzerDocument");

        public T Field<T>(string name) => (T)typeof(Form1).GetField(name, Hidden)!.GetValue(Form)!;

        public Control Button(string name) => Field<Control>(name);

        public void Open(string path) => Await("OpenMeasurementFileAsync", path);

        public void Select(ModeTab tab) => Await("SelectModeAsync", tab);

        public bool PressKey(Keys key)
        {
            object[] arguments = [new Message { Msg = 0x0100, WParam = (IntPtr)key }, key];
            return (bool)typeof(Form1).GetMethod("ProcessCmdKey", Hidden)!.Invoke(Form, arguments)!;
        }

        public void Await(string method, params object[] arguments) => Settle(Start(method, arguments));

        /// <summary>Runs <paramref name="method"/> to its first await; the rest runs as the test pumps.</summary>
        public Task Start(string method, params object[] arguments)
        {
            // Once the window is built the thread holds a plain context, which would resume the form's awaits on the pool.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            return (Task)typeof(Form1).GetMethod(method, Hidden)!.Invoke(Form, arguments)!;
        }

        public void Settle(Task task)
        {
            StaTest.Settle(task);
            Pump();
            // A load or import that threw would otherwise pass as one that landed nothing.
            task.GetAwaiter().GetResult();
        }

        /// <summary>What Record does once the device is ready: the run empties the document and holds it.</summary>
        public void StartRun()
        {
            Document.Clear();
            typeof(Form1).GetField("runRequest", Hidden)!.SetValue(Form, Document.TryAcquire());
        }

        /// <summary>The engine's completion, as it arrives from the audio thread; null for a run that failed or was aborted.</summary>
        public void CompleteRun(MeasurementResult? result)
        {
            typeof(Form1).GetMethod("HandleMeasurementCompleted", Hidden)!.Invoke(Form, [result]);
            Pump();
        }

        public void Dispose()
        {
            Form.Dispose();
            MainWindowData.Reset();
        }

        /// <summary>Lets queued UI work run: a view redraws after the input that changed it, and its curves land.</summary>
        public void Pump()
        {
            for (int i = 0; i < 20; i++)
            {
                StaTest.Pump();
                Thread.Sleep(5);
            }

            Task drawing;
            do
            {
                drawing = Plotter.Drawing;
                StaTest.Settle(drawing);
            }
            while (drawing != Plotter.Drawing);
        }
    }
}
