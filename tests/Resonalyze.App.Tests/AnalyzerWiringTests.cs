using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.WindowsForms;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// The analyzer through its own window: every input lands in one document, and every view shows that one.
/// Drives the real main window, so a view left reading something else fails here.
/// </summary>
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
            var document = analyzer.Field<AnalyzerDocument>("analyzerDocument");
            analyzer.Await("ActivateHistoryEntryAsync", entry.Id, document.BeginActivation());

            // The entry brings back its session too: it was opened in Frequency Response.
            Assert.Equal("Frequency Response - first.json", analyzer.Plot.Title);
            analyzer.Select(ModeTab.Phase);
            Assert.Equal("Transfer IR Peak: 5.000 ms (240 samples)", analyzer.PeakInfo);
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
            var document = analyzer.Field<AnalyzerDocument>("analyzerDocument");
            analyzer.Open(first);
            analyzer.Open(second);
            MeasurementHistoryEntry entry = Assert.Single(
                analyzer.History.Entries,
                candidate => candidate.SourceFilePath == first);
            long stale = document.BeginActivation();
            document.BeginActivation();

            analyzer.Await("ActivateHistoryEntryAsync", entry.Id, stale);

            Assert.Equal("Frequency Response - second.json", analyzer.Plot.Title);
            Assert.Equal(second, document.SourceName);
        });
    }

    private string WriteMeasurement(string name, int peak)
    {
        var impulse = new Complex[8_192];
        impulse[peak] = Complex.One;
        impulse[peak + 96] = new Complex(0.3, 0.0);
        MeasurementResult result = TestMeasurementResults.Restored(
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
        string path = Path.Combine(directory, name);
        ImpulseResponseFile.From(result).SaveAsync(path).GetAwaiter().GetResult();
        return path;
    }

    private sealed class LiveAnalyzer : IDisposable
    {
        public LiveAnalyzer()
        {
            // The read-outs format with the thread's culture.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Form = new Form1();
            _ = Form.Handle;
            Pump();
        }

        public Form1 Form { get; }

        public PlotModel Plot => Field<PlotView>("plotView1").Model!;

        public string PeakInfo => Plot.Annotations
            .OfType<TextualAnnotation>()
            .Single(annotation => Equals(annotation.Tag, "PeakInfoAnnotation"))
            .Text ?? string.Empty;

        public MeasurementHistoryService History => Field<MeasurementHistoryService>("measurementHistoryService");

        public TimeAlignmentPanel TimeAlignment => Field<TimeAlignmentPanel>("timeAlignmentPanel");

        public T Field<T>(string name) => (T)typeof(Form1).GetField(name, Hidden)!.GetValue(Form)!;

        public Control Button(string name) => Field<Control>(name);

        public void Open(string path) => Await("OpenMeasurementFileAsync", path);

        public void Select(ModeTab tab) => Await("SelectModeAsync", tab);

        public void Await(string method, params object[] arguments)
        {
            // Once the window is built the thread holds a plain context, which would resume the form's awaits on the pool.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var task = (Task)typeof(Form1).GetMethod(method, Hidden)!.Invoke(Form, arguments)!;
            StaTest.Settle(task);
            Pump();
        }

        public void Dispose() => Form.Dispose();

        private static void Pump()
        {
            for (int i = 0; i < 20; i++)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }
    }
}
