using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot.Series;

namespace Resonalyze.App.Tests;

/// <summary>
/// The Time Alignment panel driven as the user drives it, read by what the options hold and what the panel shows. The
/// read, the report and the previews have their own tests; these pin the wiring between the controls and them.
/// </summary>
public sealed class TimeAlignmentPanelWiringTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void OpeningARecord_ReadsItIntoTheReportAndBothPreviews() => StaTest.Run(() =>
    {
        using var live = new LiveTa();

        live.Open(Measurement(peak: 400), @"C:\ir\left tweeter.json");

        Assert.Equal("Source: left tweeter.json, 48000 Hz, 24 bit.", live.Panel.SourceSummaryLabel.Text);
        Assert.Equal("Compare: -", live.Panel.CompareLabel.Text);
        Assert.StartsWith("detected: ", live.Panel.AutoBandLabel.Text, StringComparison.Ordinal);
        Assert.Contains("Main Signal: ", live.Status, StringComparison.Ordinal);
        Assert.Contains(live.StatusLines, line => line.StartsWith(DelayTableText.FirstArrivalLabel, StringComparison.Ordinal));
        Assert.Single(live.Panel.BandpassPlotView.Model!.Series);
        Assert.Single(live.Panel.EnvelopePlotView.Model!.Series);
        Assert.Contains("M First", live.EnvelopeMarkers);
    });

    [Fact]
    public void TheBandRadios_WriteTheOptions_SaveThem_AndReadAgain() => StaTest.Run(() =>
    {
        using var live = new LiveTa();
        live.Open(Measurement(peak: 400), "a.json");

        live.Panel.BandModeFullRadio.Checked = true;
        live.Settle();

        Assert.Equal(TimeAlignmentBandMode.FullBand, live.Options.BandMode);
        Assert.Equal(1, live.Saves);
        Assert.Equal("-", live.Panel.AutoBandLabel.Text);
        Assert.False(live.Panel.BandpassCenterNumeric.Enabled);
        Assert.Empty(live.Panel.BandpassPlotView.Model!.Series);
        Assert.DoesNotContain("Arrival probe:", live.Status, StringComparison.Ordinal);

        live.Panel.BandModeManualRadio.Checked = true;
        live.Settle();

        Assert.Equal(TimeAlignmentBandMode.ManualBand, live.Options.BandMode);
        Assert.Equal(2, live.Saves);
        Assert.True(live.Panel.BandpassCenterNumeric.Enabled);
        Assert.True(live.Panel.BandpassFadeOctavesNumeric.Enabled);
        Assert.Contains("Arrival probe:", live.Status, StringComparison.Ordinal);
    });

    [Fact]
    public void TheManualFields_MoveTheBandTheReadUses() => StaTest.Run(() =>
    {
        using var live = new LiveTa(new TimeAlignmentOptions { BandMode = TimeAlignmentBandMode.ManualBand });
        live.Open(Measurement(peak: 400), "a.json");

        live.Panel.BandpassCenterNumeric.Value = 4_000m;
        live.Settle();
        live.Panel.BandpassPassOctavesNumeric.Value = 2m;
        live.Settle();
        live.Panel.BandpassFadeOctavesNumeric.Value = 1m;
        live.Settle();

        Assert.Equal(4_000.0, live.Options.BandpassCenterHz);
        Assert.Equal(2.0, live.Options.BandpassPassOctaves);
        Assert.Equal(1.0, live.Options.BandpassFadeOctaves);
        Assert.Equal(3, live.Saves);
        // Pass band 2 kHz..8 kHz: the probe reads its upper half.
        Assert.Contains("4000-8000 Hz upper half", live.Status, StringComparison.Ordinal);
        Assert.InRange(live.FirstPassFrequency(), 2_000.0, 2_070.0);
    });

    [Fact]
    public void OptionsWrittenBehindThePanel_ReachTheControlsWhenItRefreshes() => StaTest.Run(() =>
    {
        using var live = new LiveTa();
        live.Open(Measurement(peak: 400), "a.json");

        live.Options.BandMode = TimeAlignmentBandMode.ManualBand;
        live.Options.BandpassCenterHz = 3_000;
        live.Controller.RefreshConfiguration();
        live.Settle();

        Assert.True(live.Panel.BandModeManualRadio.Checked);
        Assert.Equal(3_000m, live.Panel.BandpassCenterNumeric.Value);
        Assert.Contains("3000-4243 Hz upper half", live.Status, StringComparison.Ordinal);
        Assert.Equal(0, live.Saves);
    });

    [Fact]
    public void ACompareRecord_JoinsTheReportAndTheEnvelope_AndLeavesWithIt() => StaTest.Run(() =>
    {
        using var live = new LiveTa();
        live.Open(Measurement(peak: 400), "a.json");

        live.Compare.Set("b.json", null, Measurement(peak: 448));
        live.Settle();

        Assert.Equal("Compare: b.json, 48000 Hz, 24 bit.", live.Panel.CompareLabel.Text);
        Assert.EndsWith("(shared with Compare)", live.Panel.AutoBandLabel.Text, StringComparison.Ordinal);
        Assert.Contains("Compare Signal: ", live.Status, StringComparison.Ordinal);
        Assert.Equal(2, live.Panel.EnvelopePlotView.Model!.Series.Count);
        Assert.Contains("C First", live.EnvelopeMarkers);

        live.Compare.Clear();
        live.Settle();

        Assert.Equal("Compare: -", live.Panel.CompareLabel.Text);
        Assert.DoesNotContain("Compare Signal: ", live.Status, StringComparison.Ordinal);
        Assert.Single(live.Panel.EnvelopePlotView.Model!.Series);
    });

    [Fact]
    public void ClickingADelayCell_CopiesItsNumber_AndNothingElseCopies() => StaTest.Run(() =>
    {
        using var live = new LiveTa();
        live.Open(Measurement(peak: 400), "a.json");
        int row = Array.FindIndex(
            live.StatusLines, line => line.StartsWith(DelayTableText.FirstArrivalLabel, StringComparison.Ordinal));
        string expected = DelayTableText.GetValue(live.StatusLines[row], DelayTableText.MillisecondsColumn);

        Point cell = live.PointAt(row, DelayTableText.MillisecondsColumn + 1);
        Point label = live.PointAt(row, 2);
        live.Click(cell, MouseButtons.Right);
        live.Click(label, MouseButtons.Left);
        Assert.Empty(live.Copied);

        live.Click(cell, MouseButtons.Left);

        Assert.Equal([expected], live.Copied);
        Assert.NotEmpty(expected);
        Assert.True(live.Panel.StatusTextBox.UseHandCursorAt!(cell));
        Assert.False(live.Panel.StatusTextBox.UseHandCursorAt!(label));
    });

    [Fact]
    public void AHiddenPanel_ReadsOnlyWhenShown() => StaTest.Run(() =>
    {
        using var live = new LiveTa();
        live.Controller.SetVisible(false);

        live.Open(Measurement(peak: 400), "a.json");
        Assert.StartsWith("No impulse response is loaded.", live.Status, StringComparison.Ordinal);

        live.Controller.SetVisible(true);
        live.Settle();

        Assert.Contains("Main Signal: ", live.Status, StringComparison.Ordinal);
    });

    [Fact]
    public void ShowingThePanelAgain_DoesNotReReadAnUnchangedRecord() => StaTest.Run(() =>
    {
        using var live = new LiveTa();
        live.Open(Measurement(peak: 400), "a.json");

        live.Controller.SetVisible(false);
        live.Controller.SetVisible(true);

        Assert.True(live.Controller.Session.Reads.IsIdle);
    });

    [Fact]
    public void ARunHoldingTheDocument_KeepsWhatWasRead_UntilItLands() => StaTest.Run(() =>
    {
        using var live = new LiveTa();
        live.Open(Measurement(peak: 400), "a.json");
        string before = live.Status;

        using (AnalyzerDocument.Request hold = live.Document.TryAcquire()!)
        {
            live.Compare.Set("c.json", null, Measurement(peak: 448));
            live.Settle();
            Assert.Equal(before, live.Status);
            Assert.Equal("Compare: -", live.Panel.CompareLabel.Text);

            hold.Install(Measurement(peak: 900), "b.json");
        }

        live.Settle();

        Assert.Equal("Source: b.json, 48000 Hz, 24 bit.", live.Panel.SourceSummaryLabel.Text);
        Assert.Equal("Compare: c.json, 48000 Hz, 24 bit.", live.Panel.CompareLabel.Text);
        Assert.Contains("Compare Signal: ", live.Status, StringComparison.Ordinal);
    });

    [Fact]
    public void ARecordWithoutLoopback_SaysSoAndClearsTheEnvelope() => StaTest.Run(() =>
    {
        using var live = new LiveTa();
        live.Open(Measurement(peak: 400), "a.json");

        live.Open(Measurement(peak: 400, loopback: false), "sweep only.json");

        Assert.StartsWith("This record was captured without loopback.", live.Status, StringComparison.Ordinal);
        Assert.Equal("detected: waiting for a record", live.Panel.AutoBandLabel.Text);
        Assert.Empty(live.Panel.EnvelopePlotView.Model!.Series);
        Assert.Empty(live.Panel.BandpassPlotView.Model!.Series);
    });

    // A click on a delta, a reflection 4 ms later and a faint noise floor, so the read has an SNR to grade.
    private static MeasurementResult Measurement(int peak, bool loopback = true)
    {
        var impulse = new Complex[16_384];
        var random = new Random(peak);
        for (int i = 0; i < impulse.Length; i++)
        {
            impulse[i] = new Complex((random.NextDouble() - 0.5) * 2e-5, 0.0);
        }

        impulse[peak] += Complex.One;
        impulse[peak + 192] += new Complex(0.3, 0.0);
        return TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: SampleRate,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: impulse,
            sweepDeconvolutionPeakIndex: peak,
            measurementMode: loopback ? SweepMeasurementMode.LoopbackTransfer : SweepMeasurementMode.SweepDeconvolution,
            transferImpulseResponse: loopback ? impulse : null,
            transferPeakIndex: loopback ? peak : null);
    }

    private sealed class LiveTa : IDisposable
    {
        private readonly Form form;

        public LiveTa(TimeAlignmentOptions? options = null)
        {
            Options = options ?? new TimeAlignmentOptions();
            form = new Form { ClientSize = new Size(1300, 800) };
            Panel = new TimeAlignmentPanel { Dock = DockStyle.Fill };
            form.Controls.Add(Panel);
            Controller = new TimeAlignmentPanelController(form, Panel, Options, Document, () => Saves++, Compare);
            Controller.CopyCell = Copied.Add;
            form.Show();
            Controller.SetVisible(true);
            Settle();
        }

        public TimeAlignmentOptions Options { get; }

        public AnalyzerDocument Document { get; } = new();

        public CompareSelection Compare { get; } = new();

        public TimeAlignmentPanel Panel { get; }

        public TimeAlignmentPanelController Controller { get; }

        public List<string> Copied { get; } = [];

        public int Saves { get; private set; }

        public string Status => Panel.StatusTextBox.Text;

        public string[] StatusLines => Panel.StatusTextBox.Lines;

        public List<string> EnvelopeMarkers =>
            Panel.EnvelopePlotView.Model!.Annotations.OfType<PlotCalloutMarkerAnnotation>().Select(marker => marker.Text).ToList();

        public void Open(MeasurementResult result, string name)
        {
            Document.TryBegin()!.Install(result, name);
            Settle();
        }

        /// <summary>Pumps until the source refresh has run and no read is running or waiting.</summary>
        public void Settle()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            int quiet = 0;
            while (quiet < 3)
            {
                Assert.True(DateTime.UtcNow < deadline, "The read did not finish in time.");
                StaTest.Pump();
                Thread.Sleep(10);
                quiet = Controller.Session.Reads.IsIdle ? quiet + 1 : 0;
            }
        }

        public double FirstPassFrequency() =>
            ((LineSeries)Panel.BandpassPlotView.Model!.Series[0]).Points.First(point => point.Y >= 0.0).X;

        public Point PointAt(int line, int column)
        {
            StatusRichTextBox box = Panel.StatusTextBox;
            Point point = box.GetPositionFromCharIndex(box.GetFirstCharIndexFromLine(line) + column);
            point.Offset(2, 3);
            return point;
        }

        public void Click(Point point, MouseButtons button) =>
            typeof(Control)
                .GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Panel.StatusTextBox, [new MouseEventArgs(button, 1, point.X, point.Y, 0)]);

        public void Dispose()
        {
            Controller.Dispose();
            form.Dispose();
        }
    }
}
