using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>
/// Live Spectrum through its own window on a fake input: Record runs and holds the analyzer, the plot and the form's
/// surfaces follow the session, and a loaded capture stays until a run replaces it. A view or a surface left reading
/// something else fails here.
/// </summary>
public sealed class LiveSpectrumWiringTests : IDisposable
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string CaptureProgressTag = "live-spectrum:capture-progress";
    private const string SplViewOnlyTag = "live-spectrum:spl-view-only";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"resonalyze-live-{Guid.NewGuid():N}");

    public LiveSpectrumWiringTests()
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
    public void RecordDrawsTheRunAndAStopHoldsIt()
    {
        StaTest.Run(() =>
        {
            using var window = new LiveWindow();
            window.Options(options => options.AnalysisMode = LiveAnalysisMode.Rta);

            window.Record();
            Assert.Equal("Stop", window.Button("buttonRecord").Text);
            window.PumpUntil(() => window.Plot.Series.Any(IsRta), "the run's RTA");

            window.Record();
            Assert.False(window.Session.InProgress);
            Assert.Equal("Start", window.Button("buttonRecord").Text);
            Assert.NotNull(window.Session.HeldSnapshot);
            Assert.Contains(window.Plot.Series, IsRta);

            // Leaving and coming back draws the held reading again.
            window.Select(ModeTab.Frequency);
            Assert.DoesNotContain(window.Plot.Series, IsRta);
            window.Select(ModeTab.LiveSpectrum);
            Assert.Contains(window.Plot.Series, IsRta);
        });
    }

    // Save belongs to the MMM capture while the mode is on screen; it follows the session, not a call from the form.
    [Fact]
    public void SaveFollowsTheCaptureTheSessionHolds()
    {
        StaTest.Run(() =>
        {
            using var window = new LiveWindow();
            window.Options(options => options.AnalysisMode = LiveAnalysisMode.Mmm);
            Assert.False(window.Button("buttonSave").Enabled);

            window.Record();
            window.PumpUntil(() => window.Session.AveragedFrameCount > 0, "a frame");
            window.Record();
            Assert.True(window.Button("buttonSave").Enabled);

            // An acquisition change while stopped discards the capture.
            window.Options(options => options.SequenceLength = 4096);
            Assert.Null(window.Session.HeldSnapshot);
            Assert.False(window.Button("buttonSave").Enabled);
        });
    }

    // A capture shown is state: every rebuild draws it, with one read-out per plot model, until a run replaces it.
    [Fact]
    public void ALoadedCaptureStaysThroughRebuildsUntilARunReplacesIt()
    {
        StaTest.Run(() =>
        {
            using var window = new LiveWindow();
            window.Options(options => options.AnalysisMode = LiveAnalysisMode.Mmm);
            window.Record();
            window.PumpUntil(() => window.Session.AveragedFrameCount > 0, "a frame");
            window.Record();
            string path = Path.Combine(directory, "walk.json");
            LiveCaptureDocument document = window.Session.BuildCaptureDocument()!;
            document.Title = "walk";
            document.Save(path);

            window.Select(ModeTab.Frequency);
            window.Open(path);
            Assert.Equal(ModeTab.LiveSpectrum, window.ActiveTab);
            Assert.NotNull(LoadedSeries(window.Plot));
            Assert.StartsWith("Loaded", Single(window.Plot, CaptureProgressTag).Text);
            Assert.False(window.Button("buttonSave").Enabled);

            PlotModel before = window.Plot;
            window.Options(options => options.SmoothingInverseOctaves = 3);
            Assert.NotSame(before, window.Plot);
            Assert.NotNull(LoadedSeries(window.Plot));
            Assert.StartsWith("Loaded", Single(window.Plot, CaptureProgressTag).Text);

            window.Select(ModeTab.Frequency);
            window.Select(ModeTab.LiveSpectrum);
            Assert.NotNull(LoadedSeries(window.Plot));
            Assert.StartsWith("Loaded", Single(window.Plot, CaptureProgressTag).Text);

            window.Record();
            Assert.Null(window.Session.LoadedCapture);
            Assert.Null(LoadedSeries(window.Plot));
            window.Record();
        });
    }

    // The accumulation outlives a stop, so a new session must discard it or the next visit reads the old run again.
    [Fact]
    public void ANewSessionDoesNotBringBackTheLastRun()
    {
        StaTest.Run(() =>
        {
            using var window = new LiveWindow();
            window.Options(options => options.AnalysisMode = LiveAnalysisMode.Rta);
            window.Record();
            window.PumpUntil(() => window.Plot.Series.Any(IsRta), "the run's RTA");
            window.Record();

            window.NewSession();
            window.Select(ModeTab.LiveSpectrum);

            Assert.Null(window.Session.HeldSnapshot);
            Assert.False(window.Session.HasDisplayableCurve);
            Assert.False(window.Session.HasCaptureToSave);
            Assert.Empty(window.Plot.Series);
        });
    }

    // A loaded capture is state; a new session must drop it with the rest.
    [Fact]
    public void ANewSessionDoesNotBringBackALoadedCapture()
    {
        StaTest.Run(() =>
        {
            using var window = new LiveWindow();
            window.Options(options => options.AnalysisMode = LiveAnalysisMode.Mmm);
            window.Record();
            window.PumpUntil(() => window.Session.AveragedFrameCount > 0, "a frame");
            window.Record();
            string path = Path.Combine(directory, "walk.json");
            LiveCaptureDocument document = window.Session.BuildCaptureDocument()!;
            document.Title = "walk";
            document.Save(path);
            window.Open(path);
            Assert.NotNull(LoadedSeries(window.Plot));

            window.NewSession();
            window.Select(ModeTab.LiveSpectrum);

            Assert.Null(window.Session.LoadedCapture);
            Assert.Null(window.Session.DisplayedCalibrationName);
            Assert.Null(LoadedSeries(window.Plot));
        });
    }

    // dB SPL without an anchor suppresses the live curves; one notice per model says why, and only while it is true.
    [Fact]
    public void ViewOnlySplExplainsTheSuppressedCurveOncePerModel()
    {
        StaTest.Run(() =>
        {
            using var window = new LiveWindow();
            window.Options(options => options.AnalysisMode = LiveAnalysisMode.Rta);
            window.Record();
            window.PumpUntil(() => window.Plot.Series.Any(IsRta), "the run's RTA");
            window.Record();

            window.Options(options => options.MagnitudeScale = MagnitudeScale.SoundPressureLevel);
            Assert.DoesNotContain(window.Plot.Series, IsRta);
            Assert.Contains("overlays only", Single(window.Plot, SplViewOnlyTag).Text);

            window.Options(options => options.SmoothingInverseOctaves = 12);
            Assert.Contains("overlays only", Single(window.Plot, SplViewOnlyTag).Text);

            window.Options(options => options.MagnitudeScale = MagnitudeScale.Relative);
            Assert.Contains(window.Plot.Series, IsRta);
            Assert.DoesNotContain(
                window.Plot.Annotations,
                annotation => Equals(annotation.Tag, SplViewOnlyTag));
        });
    }

    // Opened first, so only the session's announcements can move the panel's warnings.
    [Fact]
    public void TheSettingsPanelFollowsTheSession()
    {
        StaTest.Run(() =>
        {
            using var window = new LiveWindow();
            window.Options(options => options.AnalysisMode = LiveAnalysisMode.Rta);
            LiveSpectrumOpt panel = window.OpenPanel();
            Assert.NotEqual(UiPalette.Warning, Control<Label>(panel, "labelSpl").ForeColor);

            // A curve now exists that an unanchored dB SPL choice would hide.
            window.Record();
            window.PumpUntil(() => window.Session.AveragedFrameCount > 0, "a frame");
            window.Record();
            Assert.Equal(UiPalette.Warning, Control<Label>(panel, "labelSpl").ForeColor);
        });
    }

    [Fact]
    public void TheSettingsPanelHearsTheLoopbackGo()
    {
        StaTest.Run(() =>
        {
            using var window = new LiveWindow();
            window.Options(options => options.AnalysisMode = LiveAnalysisMode.TransferFunction);
            LiveSpectrumOpt panel = window.OpenPanel();
            Assert.NotEqual(UiPalette.Warning, Control<RadioButton>(panel, "radioModeTransfer").ForeColor);

            window.Route(loopback: null);

            Assert.Equal(UiPalette.Warning, Control<RadioButton>(panel, "radioModeTransfer").ForeColor);
        });
    }

    private static T Control<T>(LiveSpectrumOpt panel, string name) where T : Control =>
        (T)panel.Controls.Find(name, searchAllChildren: true).Single();

    private static bool IsRta(OxyPlot.Series.Series series) =>
        Equals(series.Tag, LiveSpectrumController.LiveSpectrumInputMagnitudeTag);

    private static OxyPlot.Series.LineSeries? LoadedSeries(PlotModel model) =>
        model.Series.OfType<OxyPlot.Series.LineSeries>().SingleOrDefault(series => series.Title == "walk");

    private static TextualAnnotation Single(PlotModel model, string tag) =>
        Assert.Single(model.Annotations.OfType<TextualAnnotation>(), annotation => Equals(annotation.Tag, tag));

    private sealed class LiveWindow : IDisposable
    {
        public LiveWindow()
        {
            // The read-outs format with the thread's culture.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Form = new Form1(new FakeAudioSessionFactory(
                streamingFactory: _ => new RecordingStreamingSession(
                    framesToRaise: 400,
                    failAfterFrames: false,
                    microphonePeak: 0.5f)));
            _ = Form.Handle;
            Pump();
            Route(loopback: 1);
            Select(ModeTab.LiveSpectrum);
        }

        public Form1 Form { get; }

        public PlotModel Plot => Field<PlotView>("plotView1").Model!;

        public LiveSpectrumSession Session => Field<LiveSpectrumSession>("liveSpectrumSession");

        public ModeTab ActiveTab => Field<ModeController>("modeController").ActiveTab;

        public T Field<T>(string name) => (T)typeof(Form1).GetField(name, Hidden)!.GetValue(Form)!;

        public Control Button(string name) => Field<Control>(name);

        public void Select(ModeTab tab) => Await("SelectModeAsync", tab);

        /// <summary>A routing edit, as Record Settings applies one.</summary>
        public void Route(int? loopback)
        {
            MeasurementSettingsFile.SweepMeasurementSettings settings =
                Field<MeasurementSettingsFile>("measurementSettings").Measurement;
            settings.AudioBackend = Resonalyze.Audio.AudioBackend.Wave;
            settings.WaveInputChannelOffset = 0;
            settings.WaveLoopbackInputChannelOffset = loopback;
            Await("ApplyMeasurementConfigurationToControllersAsync");
        }

        public LiveSpectrumOpt OpenPanel()
        {
            typeof(Form1).GetMethod("ToggleLiveSpectrumOptions", Hidden)!.Invoke(Form, []);
            Pump();
            object host = Field<object>("dockedModeSettingsHost");
            return (LiveSpectrumOpt)host.GetType().GetField("activeDialog", Hidden)!.GetValue(host)!;
        }

        public void Open(string path) => Await("OpenMeasurementFileAsync", path);

        public void NewSession() => Await("StartNewSessionAsync");

        /// <summary>Clicks Record and waits until the analyzer has started or stopped.</summary>
        public void Record()
        {
            bool running = Session.InProgress;
            // PerformClick needs a shown window; the handler is what a click runs.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            typeof(Form1).GetMethod("buttonRecord_Click", Hidden)!.Invoke(Form, [Button("buttonRecord"), EventArgs.Empty]);
            PumpUntil(() => Session.InProgress != running, running ? "the stop" : "the start");
            Pump();
        }

        /// <summary>What the settings panel does on an edit: a panel holding the edited options applies them.</summary>
        public void Options(Action<LiveSpectrumOptions> edit)
        {
            var edited = new LiveSpectrumOptions();
            MeasurementSettingsFile.LiveSpectrumSettings.Capture(Session.Options).ApplyTo(edited);
            edit(edited);
            using var panel = new LiveSpectrumOpt();
            panel.Init(
                edited,
                Field<MicrophoneCalibrationService>("microphoneCalibration").GetEntries(),
                true,
                false,
                true,
                48_000);
            Await("ApplyLiveSpectrumOptionsAsync", panel);
        }

        public void Await(string method, params object[] arguments)
        {
            // Once the window is built the thread holds a plain context, which would resume the form's awaits on the pool.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var task = (Task)typeof(Form1).GetMethod(method, Hidden)!.Invoke(Form, arguments)!;
            StaTest.Settle(task);
            Pump();
            task.GetAwaiter().GetResult();
        }

        public void PumpUntil(Func<bool> condition, string what)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {what}.");
                StaTest.Pump();
                Thread.Sleep(5);
            }
        }

        public void Pump()
        {
            for (int i = 0; i < 20; i++)
            {
                StaTest.Pump();
                Thread.Sleep(5);
            }
        }

        // The window stops the analyzer before it lets go of it (Form1.Lifecycle); a test that failed mid-run must too,
        // or the analyzer's Dispose waits on a run whose continuations need this thread.
        public void Dispose()
        {
            if (Session.InProgress)
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                StaTest.Settle(Field<LiveSpectrumController>("liveSpectrumController").AbortAsync());
            }

            Form.Dispose();
        }
    }
}
