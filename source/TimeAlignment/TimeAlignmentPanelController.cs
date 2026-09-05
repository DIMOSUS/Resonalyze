using System.Numerics;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze;

internal sealed class TimeAlignmentPanelController : IDisposable
{

    private readonly Form owner;
    private readonly TimeAlignmentOptions options;
    private readonly ExpSweepMeasurement measurement;
    private readonly Action saveSettings;
    private readonly Func<string?> getImpulseResponseFileName;
    private readonly Func<TimeAlignmentCompareMeasurement?> getCompareMeasurement;
    private readonly TimeAlignmentPanel panel;
    private readonly Label sourceSummaryLabel;
    private readonly Label compareLabel;
    private readonly RadioButton bandModeFullRadio;
    private readonly RadioButton bandModeAutoRadio;
    private readonly RadioButton bandModeManualRadio;
    private readonly Label autoBandLabel;
    private readonly DarkNumericUpDown bandpassCenterNumeric;
    private readonly DarkNumericUpDown bandpassPassOctavesNumeric;
    private readonly DarkNumericUpDown bandpassFadeOctavesNumeric;
    private readonly PlotView bandpassPlotView;
    private readonly PlotView envelopePlotView;
    // Both previews are rebuilt from scratch on every configuration change, so a
    // zoom into a junction would last exactly until the next edit without these.
    private readonly PlotViewportMemory bandpassViewports;
    private readonly PlotViewportMemory envelopeViewports;
    private readonly StatusRichTextBox statusTextBox;
    private readonly Font resultTableFont;
    // The band detected for the Auto mode on the last refresh (null when no
    // data or another mode is active); feeds the preview plot and the label.
    private DominantBand? lastAutoBand;
    // Whether that band is the overlap of Main's and Compare's own bands rather
    // than Main's alone; the label says which, because the two answer different
    // questions ("where this driver plays" against "where these two meet").
    private bool lastAutoBandIsShared;
    private bool disposed;
    // What is drawn, what is running, what waits. This panel is refreshed from
    // more places than there are changes to draw: a mode switch asks twice (the
    // tab shows the panel, then the redraw that follows asks again), and a
    // compare change, a loaded file and a restored history entry each arrive
    // through two paths of their own. Every one of those used to re-run the
    // whole band-limited analysis of a record that had not moved.
    private readonly AnalysisReadSchedule<AnalysisRequest> reads = new();
    // Per-record derivations, kept so that a band edit — which changes neither
    // the record nor its hygiene — stops paying for them again. One slot per
    // role; the entry is swapped as a whole, so a reader never sees a verdict
    // paired with another record's samples.
    private ProjectionEntry? mainProjection;
    private ProjectionEntry? compareProjection;
    private HygieneEntry? mainHygiene;
    private HygieneEntry? compareHygiene;
    // Guards those four slots: a superseded read can still be running on its own
    // thread when the next one starts, and both reach for them.
    private readonly object recordDerivations = new();

    // The fade the Auto mode puts around the detected pass band.
    private const double AutoBandFadeOctaves = 0.5;

    // Names the reference both envelope curves are drawn against, so a Compare
    // curve sitting below Main reads as the level difference it is.
    private const string EnvelopeDecibelAxisTitle = "dB re Main peak";

    // How far under its own maximum one envelope curve is drawn before the
    // floor takes over, and how tall the envelope plot opens: a Compare record
    // tens of dB under Main (a sub against a tweeter) would otherwise squeeze
    // the arrivals into a sliver. The axis still PANS to the full range —
    // only the opening view is bounded.
    private const double CurveFloorDb = 80.0;
    private const double EnvelopeOpeningSpanDb = 100.0;

    public TimeAlignmentPanelController(
        Form owner,
        TimeAlignmentPanel panel,
        TimeAlignmentOptions options,
        ExpSweepMeasurement measurement,
        Action saveSettings,
        Func<string?> getImpulseResponseFileName,
        Func<TimeAlignmentCompareMeasurement?> getCompareMeasurement)
    {
        this.owner = owner;
        this.panel = panel;
        this.options = options;
        this.measurement = measurement;
        this.saveSettings = saveSettings;
        this.getImpulseResponseFileName = getImpulseResponseFileName;
        this.getCompareMeasurement = getCompareMeasurement;
        // One step over the panel font, not four: the delay table is three
        // rows of three cells with Compare deltas (66 characters with every
        // delta), and at +4 the status box wrapped the meters cell.
        resultTableFont = new Font(
            FontFamily.GenericMonospace,
            owner.Font.Size + 1.0f,
            FontStyle.Bold);

        sourceSummaryLabel = panel.SourceSummaryLabel;
        compareLabel = panel.CompareLabel;
        bandModeFullRadio = panel.BandModeFullRadio;
        bandModeAutoRadio = panel.BandModeAutoRadio;
        bandModeManualRadio = panel.BandModeManualRadio;
        autoBandLabel = panel.AutoBandLabel;
        bandpassCenterNumeric = panel.BandpassCenterNumeric;
        bandpassPassOctavesNumeric = panel.BandpassPassOctavesNumeric;
        bandpassFadeOctavesNumeric = panel.BandpassFadeOctavesNumeric;
        bandpassPlotView = panel.BandpassPlotView;
        envelopePlotView = panel.EnvelopePlotView;
        // Same zoom, pan and limits gestures as the other analysis plots.
        PlotInteraction.Enable(bandpassPlotView);
        PlotInteraction.Enable(envelopePlotView);
        bandpassViewports = new PlotViewportMemory(bandpassPlotView);
        envelopeViewports = new PlotViewportMemory(envelopePlotView);
        statusTextBox = panel.StatusTextBox;
        statusTextBox.UseHandCursorAt = point => TryGetCopyableStatusLine(point, out _);
        statusTextBox.MouseClick += StatusTextBoxMouseClick;

        ApplyOptionsToControls();
        WireEvents();
        RefreshAnalysis();
    }

    public bool InProgress => false;

    public void SetLayoutBounds(Rectangle bounds)
    {
        panel.Bounds = bounds;
    }

    public void SetVisible(bool visible)
    {
        panel.Visible = visible;
        if (visible)
        {
            RefreshConfiguration();
        }
    }

    public void RefreshConfiguration()
    {
        // The options object is SHARED and is written behind this panel's back. Twice:
        // the persisted settings land in it after the controls were first filled from
        // the defaults (the controllers are built before ApplyPersistedSettings runs),
        // and every history entry restores its own session into it. Neither touches a
        // control, so without re-reading it here the band radios keep showing what they
        // were built with while the analysis runs on something else — the panel then
        // says "Auto" with the band caption on "-" and reads the whole spectrum, which
        // is what FullBand looks like from the outside.
        ApplyOptionsToControls();
        RefreshAnalysis();
    }

    public Task AbortAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        resultTableFont.Dispose();
    }

    private void WireEvents()
    {
        // A radio switch fires CheckedChanged on both the leaving and the
        // arriving button; reacting to the arriving one alone refreshes once.
        void OnRadio(object? sender, EventArgs _)
        {
            if (!applyingOptions && sender is RadioButton { Checked: true })
            {
                ApplyBandpassOptionChange();
            }
        }
        void OnNumeric(object? sender, EventArgs _)
        {
            if (!applyingOptions)
            {
                ApplyBandpassOptionChange();
            }
        }
        bandModeFullRadio.CheckedChanged += OnRadio;
        bandModeAutoRadio.CheckedChanged += OnRadio;
        bandModeManualRadio.CheckedChanged += OnRadio;
        bandpassCenterNumeric.ValueChanged += OnNumeric;
        bandpassPassOctavesNumeric.ValueChanged += OnNumeric;
        bandpassFadeOctavesNumeric.ValueChanged += OnNumeric;
    }

    private void ApplyBandpassOptionChange()
    {
        UpdateOptionsFromControls();
        RefreshAnalysis();
        saveSettings();
    }

    private void RefreshAnalysis()
    {
        sourceSummaryLabel.Text = CreateSourceSummary();
        compareLabel.Text = CreateCompareSummary();

        if (!TryGetMainSource(out TimeAlignmentAnalysisSource mainSource, out string noDataMessage))
        {
            reads.Clear();
            lastAutoBand = null;
            UpdateAutoBandLabel();
            UpdateBandpassPreview();
            SetStatusText(noDataMessage);
            ClearEnvelopePreview();
            return;
        }

        // The window preview and its caption follow the CONTROLS, so an edit
        // moves them at once instead of at the end of the read behind it. In the
        // Auto mode the caption still names the band of the last finished read
        // until the new one lands, which is what "detected" has always meant.
        UpdateAutoBandLabel();
        UpdateBandpassPreview();

        // The band is FROZEN into the request here. The read runs off the UI
        // thread, where the shared options object can be edited under it, and a
        // number on screen has to state the band it was actually taken in.
        var request = new AnalysisRequest(
            mainSource,
            getCompareMeasurement(),
            options.BandMode,
            options.BandpassCenterHz,
            options.BandpassPassOctaves,
            options.BandpassFadeOctaves,
            options.FirstPeakThresholdBelowMaxDb,
            options.FirstPeakMinimumSnrDb,
            options.PeakSearchWindowMilliseconds);
        if (reads.Submit(request) is { } version)
        {
            StartAnalysis(request, version);
        }
    }

    // Starts the read, off the UI thread wherever there is a message loop to
    // come back to. At a megabyte of transfer IR one read is a few hundred
    // milliseconds, and it used to be spent inside the click that asked for it:
    // that is what made a nudge of the band boxes feel like a hang, and what
    // held the shell still for a moment after every sweep.
    private void StartAnalysis(AnalysisRequest request, int version)
    {
        if (!owner.IsHandleCreated || owner.IsDisposed || owner.InvokeRequired)
        {
            // Nothing to come back to: the controllers are built and refreshed
            // before the shell has a window, and the panel tests never open one.
            CompleteAnalysis(request, RunAnalysis(request), version);
            return;
        }

        _ = RunAnalysisAsync(request, version);
    }

    private async Task RunAnalysisAsync(AnalysisRequest request, int version)
    {
        AnalysisOutcome outcome;
        try
        {
            outcome = await Task.Run(() => RunAnalysis(request));
        }
        catch (Exception exception)
        {
            outcome = AnalysisOutcome.Failed(exception.Message);
        }

        if (!disposed && !owner.IsDisposed)
        {
            CompleteAnalysis(request, outcome, version);
        }
    }

    // Draws a finished read — unless a newer one has been asked for since, in
    // which case this one is stale and drawing it would put the previous band's
    // numbers under the band the user is now looking at.
    private void CompleteAnalysis(
        AnalysisRequest request,
        AnalysisOutcome outcome,
        int version)
    {
        if (disposed)
        {
            return;
        }

        // A read the controls have left is not drawn at all — the panel would
        // otherwise stand there stating an alignment for a band nobody is
        // looking at — but its slot on the pool frees here, so what IS wanted
        // starts either way.
        if (!reads.Complete(request, version))
        {
            StartDesiredAnalysis();
            return;
        }

        lastAutoBand = outcome.AutoBand;
        lastAutoBandIsShared = outcome.AutoBandShared;
        UpdateAutoBandLabel();
        UpdateBandpassPreview();
        if (outcome.Message is { } message)
        {
            SetStatusText(message);
            ClearEnvelopePreview();
            StartDesiredAnalysis();
            return;
        }

        SetMeasurementResultStatus(
            request.BandMode,
            outcome.MainSource,
            outcome.MainResult,
            outcome.MainProbe,
            outcome.MainCrosstalk,
            outcome.Compare,
            outcome.CompareProbe,
            outcome.CompareCrosstalk,
            outcome.CompareWarning,
            outcome.BandCenterHz);
        UpdateEnvelopePreview(
            outcome.MainResult,
            outcome.MainSource.SampleRate,
            outcome.Compare?.Result);
        StartDesiredAnalysis();
    }

    // What the controls are still waiting for, now that the pool is free.
    private void StartDesiredAnalysis()
    {
        if (reads.TakeDesired(out AnalysisRequest desired) is { } version)
        {
            StartAnalysis(desired, version);
        }
    }

    // The read itself. Nothing here touches a control or the shared options
    // object: it works from the request alone, so it is safe on a worker thread
    // and it states the band it was given rather than the band the boxes have
    // reached by the time it lands.
    private AnalysisOutcome RunAnalysis(AnalysisRequest request)
    {
        try
        {
            TimeAlignmentAnalysisSource mainSource = request.MainSource;
            // Crosstalk hygiene first: detection always runs on the RAW
            // record, then the banded modes analyze the CLEANED record —
            // the same order the Auto delay engine uses. A broadband click
            // lands inside the analysis band and the upper-half probe
            // alike, so analyzing the raw record could green-light
            // ("verified") an arrival that times the click. The bypass mode
            // keeps the raw record and flags the contamination instead.
            HygieneEntry mainHygieneEntry = Hygiene(ref mainHygiene, mainSource);
            TimeAlignmentAnalysisSource mainAnalysisSource = CleanForAnalysis(
                mainSource, mainHygieneEntry, request.BandMode);

            // The Compare record is resolved and cleaned BEFORE the band is
            // chosen: in the Auto mode the band is the one both records share,
            // and a band taken from Main alone would make the delta depend on
            // which of the two was loaded as Main (a field mid pair: 32.7-7671
            // Hz one way, 75.5-4695 Hz the other, and 0.3 ms of delta with it).
            TimeAlignmentAnalysisSource? compareSource = TryGetCompareSource(
                request,
                mainSource,
                out string? compareWarning,
                out CrosstalkHeadGate? compareCrosstalk);

            // One options object per read, and the Compare measurement is
            // analyzed in the same band, so the delta column compares like with
            // like.
            TimeAlignmentAnalysisOptions analysisOptions = CreateAnalysisOptions(
                request,
                mainAnalysisSource,
                compareSource,
                out DominantBand? autoBand,
                out bool autoBandShared);

            TimeAlignmentAnalysisResult mainResult = TimeAlignmentAnalysis.Analyze(
                mainAnalysisSource.TransferImpulseResponse,
                mainAnalysisSource.SampleRate,
                analysisOptions,
                mainAnalysisSource.TransferCoherence);
            if (!mainResult.IsValid)
            {
                return AnalysisOutcome.Failed(
                    "No signal in the analysis band.\r\n" +
                    "The transfer IR carries no energy inside the current " +
                    "band-pass window — widen or move the band, or check " +
                    "that the measurement actually captured the driver.",
                    autoBand,
                    autoBandShared);
            }

            TimeAlignmentArrivalProbe? mainProbe = TimeAlignmentAnalysis.ProbeArrivalHonesty(
                mainAnalysisSource.TransferImpulseResponse,
                mainAnalysisSource.SampleRate,
                analysisOptions,
                mainResult,
                mainAnalysisSource.TransferCoherence);
            TimeAlignmentCompareAnalysis? compareAnalysis = AnalyzeCompare(
                compareSource, analysisOptions, ref compareWarning);
            TimeAlignmentArrivalProbe? compareProbe = compareAnalysis == null
                ? null
                : TimeAlignmentAnalysis.ProbeArrivalHonesty(
                    compareAnalysis.Value.Source.TransferImpulseResponse,
                    compareAnalysis.Value.Source.SampleRate,
                    analysisOptions,
                    compareAnalysis.Value.Result,
                    compareAnalysis.Value.Source.TransferCoherence);
            return new AnalysisOutcome(
                mainSource,
                autoBand,
                autoBandShared,
                mainResult,
                mainProbe,
                mainHygieneEntry.Crosstalk,
                compareAnalysis,
                compareProbe,
                compareCrosstalk,
                compareWarning,
                Message: null,
                BandCenterHz: request.BandMode == TimeAlignmentBandMode.FullBand
                    ? null
                    : analysisOptions.BandpassCenterHz);
        }
        catch (Exception exception)
        {
            return AnalysisOutcome.Failed(exception.Message);
        }
    }

    // Everything one read needs, taken on the UI thread before it starts. Two
    // requests that compare equal describe the same read of the same records,
    // which is what lets a repeated refresh recognize the answer already drawn.
    private readonly record struct AnalysisRequest(
        TimeAlignmentAnalysisSource MainSource,
        TimeAlignmentCompareMeasurement? Compare,
        TimeAlignmentBandMode BandMode,
        double BandpassCenterHz,
        double BandpassPassOctaves,
        double BandpassFadeOctaves,
        double FirstPeakThresholdBelowMaxDb,
        double FirstPeakMinimumSnrDb,
        double PeakSearchWindowMilliseconds);

    // What one read produced. A Message instead of a result is the read saying
    // why it has nothing to show — a band with no energy in it, or a record the
    // analysis threw on — and the panel prints that in place of the tables.
    private sealed record AnalysisOutcome(
        TimeAlignmentAnalysisSource MainSource,
        DominantBand? AutoBand,
        bool AutoBandShared,
        TimeAlignmentAnalysisResult MainResult,
        TimeAlignmentArrivalProbe? MainProbe,
        CrosstalkHeadGate? MainCrosstalk,
        TimeAlignmentCompareAnalysis? Compare,
        TimeAlignmentArrivalProbe? CompareProbe,
        CrosstalkHeadGate? CompareCrosstalk,
        string? CompareWarning,
        string? Message,
        // The centre of the band the read was taken in; null for a full-band
        // read. The recommendation reads it: below the engine's energy-onset
        // edge the onset row is the figure to align from.
        double? BandCenterHz = null)
    {
        public static AnalysisOutcome Failed(
            string message,
            DominantBand? autoBand = null,
            bool autoBandShared = false) =>
            new(
                default,
                autoBand,
                autoBandShared,
                default,
                null,
                null,
                null,
                null,
                null,
                null,
                message);
    }

    // A transfer IR's real projection, remembered per record. The analysis reads
    // doubles while the measurement holds Complex, and converting a megabyte of
    // samples on every refresh costs both the copy and a NEW array — and a new
    // array would make every request look like a different one.
    private sealed record ProjectionEntry(Complex[] Source, double[] Samples);

    // The crosstalk verdict and the cleaned copy that follows from it,
    // remembered for the same reason: neither depends on the band, so a spin of
    // a numeric box should not pay for a detection pass and a full copy of the
    // samples before the analysis it asked for even starts.
    private sealed record HygieneEntry(
        double[] Samples,
        CrosstalkHeadGate? Crosstalk,
        double[] Cleaned);

    private double[] RealSamples(ref ProjectionEntry? slot, Complex[] transfer)
    {
        lock (recordDerivations)
        {
            if (slot is { } entry && ReferenceEquals(entry.Source, transfer))
            {
                return entry.Samples;
            }

            var projected = new ProjectionEntry(
                transfer,
                Array.ConvertAll(transfer, sample => sample.Real));
            slot = projected;
            return projected.Samples;
        }
    }

    private HygieneEntry Hygiene(ref HygieneEntry? slot, TimeAlignmentAnalysisSource source)
    {
        double[] samples = source.TransferImpulseResponse;
        lock (recordDerivations)
        {
            if (slot is { } cached && ReferenceEquals(cached.Samples, samples))
            {
                return cached;
            }
        }

        CrosstalkHeadGate? crosstalk = TransferIrDiagnostics.DetectCrosstalkHead(
            samples, source.SampleRate);
        var entry = new HygieneEntry(
            samples,
            crosstalk,
            crosstalk is { } gate
                ? TransferIrDiagnostics.CleanCrosstalkHead(samples, source.SampleRate, gate)
                : samples);
        lock (recordDerivations)
        {
            slot = entry;
        }

        return entry;
    }

    // The banded modes analyze the record with the convicted click removed
    // (band detection included); the bypass mode shows the record as-is and
    // relies on the red flag instead.
    private static TimeAlignmentAnalysisSource CleanForAnalysis(
        TimeAlignmentAnalysisSource source,
        HygieneEntry hygiene,
        TimeAlignmentBandMode bandMode) =>
        bandMode != TimeAlignmentBandMode.FullBand && hygiene.Crosstalk != null
            ? source with { TransferImpulseResponse = hygiene.Cleaned }
            : source;

    private bool TryGetMainSource(
        out TimeAlignmentAnalysisSource source,
        out string message)
    {
        // An imported recording has no absolute time: nothing tied the recorder's
        // start to the playback, so its arrival sits wherever the record button
        // was pressed. Every delay this mode reports is a comparison against
        // another arrival, which makes those numbers meaningless here — and a
        // meaningless delay presented as a measurement is worse than no mode.
        if (measurement.TimingReference == TimingReference.RecordedSweep)
        {
            source = default;
            message =
                "This measurement was imported from a recorded sweep.\r\n" +
                "Its arrival time is set by when the recorder was started, not by " +
                "the tract, so delays cannot be compared across measurements.\r\n" +
                "Time Alignment needs a sweep measured against its own loopback.";
            return false;
        }

        if (measurement.TransferImpulseResponse is { Length: > 0 } transferImpulseResponse)
        {
            source = new TimeAlignmentAnalysisSource(
                "Main",
                getImpulseResponseFileName() ?? "Transfer IR",
                measurement.SampleRate,
                measurement.Bits,
                measurement.Sweep?.ComputedDuration ?? 0.0,
                measurement.PlaybackChannel,
                measurement.MeasurementMode,
                RealSamples(ref mainProjection, transferImpulseResponse),
                measurement.TransferCoherence,
                measurement.CurrentLevels);
            message = string.Empty;
            return true;
        }

        if (measurement.SweepDeconvolutionImpulseResponse is { Length: > 0 })
        {
            source = default;
            message =
                "This record was captured without loopback.\r\n" +
                "Time Alignment requires a transfer IR.\r\n" +
                "Run a new measurement with loopback enabled or load a file that contains transfer IR.";
            return false;
        }

        source = default;
        message =
            "No impulse response is loaded.\r\n" +
            "Run a loopback measurement or load an impulse response file with transfer IR.";
        return false;
    }

    private string CreateSourceSummary()
    {
        if (measurement.TransferImpulseResponse is { Length: > 0 })
        {
            string source = getImpulseResponseFileName() ?? "Transfer IR";
            return $"Source: {source}, {measurement.SampleRate} Hz, {measurement.Bits} bit.";
        }

        if (measurement.SweepDeconvolutionImpulseResponse is { Length: > 0 })
        {
            return
                $"Source: Sweep deconvolution IR only, {measurement.SampleRate} Hz, {measurement.Bits} bit.\r\n" +
                "Loopback was not recorded for this entry.";
        }

        return "Source: waiting for a loopback measurement or file with transfer IR.";
    }

    private string CreateCompareSummary()
    {
        TimeAlignmentCompareMeasurement? compare = getCompareMeasurement();
        if (compare == null)
        {
            return "Compare: -";
        }

        MeasurementHistorySnapshot snapshot = compare.Value.Snapshot;
        return $"Compare: {compare.Value.DisplayName}, {snapshot.SampleRate} Hz, {snapshot.Bits} bit.";
    }

    private TimeAlignmentAnalysisSource CreateCompareSource(
        TimeAlignmentCompareMeasurement compare,
        MeasurementHistorySnapshot snapshot) =>
        new(
            "Compare",
            compare.DisplayName,
            snapshot.SampleRate,
            snapshot.Bits,
            snapshot.SweepDurationSeconds,
            snapshot.PlayChannel,
            snapshot.MeasurementMode,
            RealSamples(ref compareProjection, snapshot.TransferImpulseResponse!),
            snapshot.TransferCoherence,
            snapshot.MeterSnapshot);

    private static TimeAlignmentAnalysisOptions CreateAnalysisOptions(
        AnalysisRequest request,
        TimeAlignmentAnalysisSource source,
        TimeAlignmentAnalysisSource? compareSource,
        out DominantBand? autoBand,
        out bool autoBandShared)
    {
        double centerHz = request.BandpassCenterHz;
        double passOctaves = request.BandpassPassOctaves;
        double fadeOctaves = request.BandpassFadeOctaves;
        autoBand = null;
        autoBandShared = false;
        if (request.BandMode == TimeAlignmentBandMode.AutoBand)
        {
            DominantBand band = DetectDominantBand(source);
            if (compareSource is { } compare &&
                TryDetectDominantBand(compare, out DominantBand compareBand))
            {
                (band, autoBandShared) = SharedBand(band, compareBand);
            }

            autoBand = band;
            centerHz = Math.Sqrt(band.LowHz * band.HighHz);
            passOctaves = Math.Log2(band.HighHz / band.LowHz);
            fadeOctaves = AutoBandFadeOctaves;
        }

        return new TimeAlignmentAnalysisOptions
        {
            UseBandpassWindow = request.BandMode != TimeAlignmentBandMode.FullBand,
            BandpassCenterHz = centerHz,
            BandpassPassOctaves = passOctaves,
            BandpassFadeOctaves = fadeOctaves,
            FirstPeakThresholdBelowMaxDb = request.FirstPeakThresholdBelowMaxDb,
            FirstPeakMinimumSnrDb = request.FirstPeakMinimumSnrDb,
            PeakSearchWindowMilliseconds = request.PeakSearchWindowMilliseconds,
            // Every position this panel prints is a delay against another
            // arrival, so a peak past the halfway mark is read as the negative
            // lead it is rather than as a buffer-length delay.
            WrapPeakPositions = true
        };
    }

    private static DominantBand DetectDominantBand(TimeAlignmentAnalysisSource source) =>
        TransferIrDiagnostics.DetectDominantBand(
            source.TransferImpulseResponse,
            source.SampleRate,
            coherence: source.TransferCoherence);

    // A record whose coherence never clears the threshold has no dominant band,
    // and the detector says so by throwing. For COMPARE that verdict must stay
    // Compare's: before the band was agreed between the two records, such a
    // failure was caught with the rest of the Compare handling and Main kept
    // working, and it still has to — the band simply falls back to Main's own,
    // which the label then stops calling shared. Main's own failure keeps
    // reaching the refresh handler: with no band for the Main record there is
    // no analysis to show.
    internal static bool TryDetectDominantBand(
        TimeAlignmentAnalysisSource source,
        out DominantBand band)
    {
        try
        {
            band = DetectDominantBand(source);
            return true;
        }
        catch (InvalidOperationException)
        {
            band = default;
            return false;
        }
    }

    // The band the two records are read in when both are present: the overlap of
    // their own dominant bands. Two drivers are only comparable where both
    // actually play — outside the overlap one of the curves is its own noise —
    // and an overlap is symmetric, so loading the pair the other way round
    // returns the same band and the same delta. Two records that share too
    // little to carve a band (a subwoofer against a tweeter) keep MAIN's band:
    // the reading is then Main's own, which the shared-band note says out loud.
    internal static (DominantBand Band, bool Shared) SharedBand(
        DominantBand main, DominantBand compare)
    {
        double low = Math.Max(main.LowHz, compare.LowHz);
        double high = Math.Min(main.HighHz, compare.HighHz);
        return high < low * VirtualCrossoverAnalysis.MinimumArrivalBandRatio
            ? (main, false)
            : (new DominantBand(low, high, Math.Clamp(main.PeakHz, low, high)), true);
    }

    // Writing the controls raises the same events the user's own edits do, and those
    // read the controls straight back into the options and save them. Harmless while
    // the two agree; the whole point of this call is the case where they do not.
    private bool applyingOptions;

    private void ApplyOptionsToControls()
    {
        applyingOptions = true;
        try
        {
            bandModeFullRadio.Checked =
                options.BandMode == TimeAlignmentBandMode.FullBand;
            bandModeAutoRadio.Checked =
                options.BandMode == TimeAlignmentBandMode.AutoBand;
            bandModeManualRadio.Checked =
                options.BandMode == TimeAlignmentBandMode.ManualBand;
            bandpassCenterNumeric.Value =
                bandpassCenterNumeric.ClampValue(options.BandpassCenterHz);
            bandpassPassOctavesNumeric.Value =
                bandpassPassOctavesNumeric.ClampValue(options.BandpassPassOctaves);
            bandpassFadeOctavesNumeric.Value =
                bandpassFadeOctavesNumeric.ClampValue(options.BandpassFadeOctaves);
        }
        finally
        {
            applyingOptions = false;
        }

        UpdateBandpassControlStates();
    }

    private void UpdateOptionsFromControls()
    {
        options.BandMode =
            bandModeAutoRadio.Checked ? TimeAlignmentBandMode.AutoBand
            : bandModeManualRadio.Checked ? TimeAlignmentBandMode.ManualBand
            : TimeAlignmentBandMode.FullBand;
        options.BandpassCenterHz = (double)bandpassCenterNumeric.Value;
        options.BandpassPassOctaves = (double)bandpassPassOctavesNumeric.Value;
        options.BandpassFadeOctaves = (double)bandpassFadeOctavesNumeric.Value;
        UpdateBandpassControlStates();
    }

    private void UpdateBandpassControlStates()
    {
        bool manual = bandModeManualRadio.Checked;
        bandpassCenterNumeric.Enabled = manual;
        bandpassPassOctavesNumeric.Enabled = manual;
        bandpassFadeOctavesNumeric.Enabled = manual;
    }

    private void UpdateAutoBandLabel()
    {
        autoBandLabel.Text = options.BandMode != TimeAlignmentBandMode.AutoBand
            ? "-"
            : lastAutoBand is { } band
                ? $"detected: {band.LowHz:0}-{band.HighHz:0} Hz" +
                    (lastAutoBandIsShared ? " (shared with Compare)" : string.Empty)
                : "detected: waiting for a record";
    }

    private void UpdateBandpassPreview()
    {
        bool addCurve = options.BandMode == TimeAlignmentBandMode.ManualBand ||
            (options.BandMode == TimeAlignmentBandMode.AutoBand && lastAutoBand != null);
        PlotModel model = CreateBandpassPreviewModel(addCurve);
        bandpassViewports.Show(model, Mode.TimeAlignment);
    }

    private PlotModel CreateBandpassPreviewModel(bool addCurve)
    {
        double maxFrequency = Math.Min(20_000, measurement.SampleRate > 0
            ? measurement.SampleRate * 0.5
            : 20_000);
        var model = CreatePreviewPlotModel("Bandpass Window");
        var frequencyAxis = new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 20,
            Maximum = maxFrequency,
            AbsoluteMaximum = 20_000,
            AbsoluteMinimum = 20
        };
        ApplyPreviewAxisStyle(frequencyAxis);
        model.Axes.Add(frequencyAxis);
        var dbAxis = CreateDecibelAxis();
        dbAxis.AbsoluteMinimum = -80;
        dbAxis.AbsoluteMaximum = 0;
        model.Axes.Add(dbAxis);

        if (!addCurve)
        {
            return model;
        }

        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(255, 210, 80),
            StrokeThickness = 2
        };
        (double f1, double f2, double f3, double f4) =
            options.BandMode == TimeAlignmentBandMode.AutoBand && lastAutoBand is { } band
                ? BandpassWindow.BandAround(
                    Math.Sqrt(band.LowHz * band.HighHz),
                    Math.Log2(band.HighHz / band.LowHz),
                    AutoBandFadeOctaves)
                : BandpassWindow.BandAround(
                    (double)bandpassCenterNumeric.Value,
                    (double)bandpassPassOctavesNumeric.Value,
                    (double)bandpassFadeOctavesNumeric.Value);
        const int pointCount = 240;
        double minLog = Math.Log10(20);
        double maxLog = Math.Log10(maxFrequency);
        for (int i = 0; i < pointCount; i++)
        {
            double t = i / (double)(pointCount - 1);
            double frequency = Math.Pow(10.0, minLog + (maxLog - minLog) * t);
            double weight = BandpassWindow.Weight(frequency, f1, f2, f3, f4);
            double decibels = weight > 0
                ? DataHelper.AmplitudeToDecibels(weight)
                : -80;
            series.Points.Add(new DataPoint(frequency, Math.Max(-80, decibels)));
        }

        model.Series.Add(series);
        return model;
    }

    // Resolves the Compare record into an analysis-ready source: the same
    // hygiene Main gets (its own crosstalk detection on the raw IR, cleaned
    // analysis in the banded modes), and no analysis yet — the band still has
    // to be agreed between the two records.
    private TimeAlignmentAnalysisSource? TryGetCompareSource(
        AnalysisRequest request,
        TimeAlignmentAnalysisSource mainSource,
        out string? warning,
        out CrosstalkHeadGate? crosstalk)
    {
        warning = null;
        crosstalk = null;
        TimeAlignmentCompareMeasurement? compare = request.Compare;
        if (compare == null)
        {
            return null;
        }

        TimeAlignmentCompareMeasurement compareValue = compare.Value;
        MeasurementHistorySnapshot snapshot = compareValue.Snapshot;
        if (snapshot.SampleRate != mainSource.SampleRate)
        {
            warning =
                $"Sample rate mismatch: Main is {mainSource.SampleRate} Hz, " +
                $"Compare is {snapshot.SampleRate} Hz.";
            return null;
        }

        if (snapshot.TransferImpulseResponse is not { Length: > 0 })
        {
            warning = "Compare impulse response has no transfer IR.";
            return null;
        }

        try
        {
            TimeAlignmentAnalysisSource compareSource =
                CreateCompareSource(compareValue, snapshot);
            HygieneEntry hygiene = Hygiene(ref compareHygiene, compareSource);
            crosstalk = hygiene.Crosstalk;
            return CleanForAnalysis(compareSource, hygiene, request.BandMode);
        }
        catch (Exception exception)
        {
            warning = exception.Message;
            return null;
        }
    }

    private TimeAlignmentCompareAnalysis? AnalyzeCompare(
        TimeAlignmentAnalysisSource? compareSource,
        TimeAlignmentAnalysisOptions analysisOptions,
        ref string? warning)
    {
        if (compareSource is not { } source)
        {
            return null;
        }

        try
        {
            TimeAlignmentAnalysisResult compareResult = TimeAlignmentAnalysis.Analyze(
                source.TransferImpulseResponse,
                source.SampleRate,
                analysisOptions,
                source.TransferCoherence);
            if (!compareResult.IsValid)
            {
                warning = "Compare: no signal in the analysis band.";
                return null;
            }

            return new TimeAlignmentCompareAnalysis(source, compareResult);
        }
        catch (Exception exception)
        {
            warning = exception.Message;
            return null;
        }
    }

    private void UpdateEnvelopePreview(
        TimeAlignmentAnalysisResult result,
        int sampleRate,
        TimeAlignmentAnalysisResult? compareResult = null)
    {
        double[] envelope = result.EnvelopeSamples;
        if (envelope.Length == 0 || result.StrongestEnvelopePeak <= 0 || sampleRate <= 0)
        {
            ClearEnvelopePreview();
            return;
        }

        // ONE reference for both curves: the Main record's strongest peak.
        // Normalizing each curve by its own first-arrival level (what this plot
        // used to do) makes the dB axis mean something different per curve — a
        // record whose pick sits 6 dB below its peak and one whose pick sits 25
        // dB below get drawn on references 19 dB apart, so two measurements of
        // the same level read as 19 dB apart on screen. The prominence of a pick
        // is an analysis figure, not a property of the record; the strongest
        // peak is, so it is the only reference that keeps levels comparable.
        double referenceAmplitude = result.StrongestEnvelopePeak;

        int radius = Math.Min(
            envelope.Length / 2,
            Math.Max(1, (int)Math.Round(sampleRate * 0.025)));
        double minMilliseconds = -radius * 1000.0 / sampleRate;
        double maxMilliseconds = radius * 1000.0 / sampleRate;
        double compareOffsetMilliseconds = 0.0;
        if (compareResult.HasValue)
        {
            compareOffsetMilliseconds =
                compareResult.Value.FirstArrivalDelayMilliseconds -
                result.FirstArrivalDelayMilliseconds;
            minMilliseconds = Math.Min(
                minMilliseconds,
                compareOffsetMilliseconds - radius * 1000.0 / sampleRate);
            maxMilliseconds = Math.Max(
                maxMilliseconds,
                compareOffsetMilliseconds + radius * 1000.0 / sampleRate);
        }

        int step = Math.Max(1, radius * 2 / 600);
        LineSeries mainSeries = CreateEnvelopeSeries(
            result,
            referenceAmplitude,
            sampleRate,
            radius,
            step,
            xOffsetMilliseconds: 0.0,
            OxyColor.FromRgb(255, 210, 80),
            strokeThickness: 2,
            out double maxDb,
            out double minDb);

        var model = CreatePreviewPlotModel("Envelope Around Peak");
        model.Axes.Add(CreateMillisecondsAxis(minMilliseconds, maxMilliseconds));
        var dbAxis = CreateDecibelAxis();
        dbAxis.Title = EnvelopeDecibelAxisTitle;
        ApplyEnvelopeDecibelRange(dbAxis, maxDb, minDb);
        model.Axes.Add(dbAxis);

        model.Series.Add(mainSeries);
        if (compareResult.HasValue)
        {
            LineSeries compareSeries = CreateEnvelopeSeries(
                compareResult.Value,
                referenceAmplitude,
                sampleRate,
                radius,
                step,
                compareOffsetMilliseconds,
                OxyColor.FromArgb(155, 80, 210, 255),
                strokeThickness: 1.75,
                out double compareMaxDb,
                out double compareMinDb);
            maxDb = Math.Max(maxDb, compareMaxDb);
            minDb = Math.Min(minDb, compareMinDb);
            ApplyEnvelopeDecibelRange(dbAxis, maxDb, minDb);
            model.Series.Add(compareSeries);
        }

        if (compareResult.HasValue)
        {
            AddComparePeakMarkers(
                model,
                result,
                compareResult.Value,
                referenceAmplitude,
                compareOffsetMilliseconds);
        }
        else
        {
            AddMainPeakMarkers(model, result, referenceAmplitude);
        }
        envelopeViewports.Show(model, Mode.TimeAlignment);
    }

    private static void ApplyEnvelopeDecibelRange(
        LinearAxis axis,
        double maxDb,
        double minDb)
    {
        axis.AbsoluteMaximum = maxDb + 30;
        axis.AbsoluteMinimum = minDb - 10;
        axis.Maximum = maxDb + 2;
        axis.Minimum = Math.Max(minDb - 2, maxDb - EnvelopeOpeningSpanDb);
    }

    // internal for the plot-construction tests: the reference both curves are
    // drawn against is the whole point of this builder.
    internal static LineSeries CreateEnvelopeSeries(
        TimeAlignmentAnalysisResult result,
        double referenceAmplitude,
        int sampleRate,
        int radius,
        int step,
        double xOffsetMilliseconds,
        OxyColor color,
        double strokeThickness,
        out double maxDb,
        out double minDb)
    {
        double[] envelope = result.EnvelopeSamples;
        maxDb = -10000;
        minDb = +10000;
        var series = new LineSeries
        {
            Color = color,
            StrokeThickness = strokeThickness
        };
        double localMaxDb = maxDb;
        double localMinDb = minDb;
        void AddPoint(int offset)
        {
            int index = DspMath.WrapIndex(result.EnvelopePeakIndex + offset, envelope.Length);
            double milliseconds = offset * 1000.0 / sampleRate + xOffsetMilliseconds;
            double relativeAmplitude = envelope[index] / referenceAmplitude;
            double decibels = DataHelper.AmplitudeToDecibels(relativeAmplitude);
            series.Points.Add(new DataPoint(milliseconds, decibels));
            localMaxDb = Math.Max(localMaxDb, decibels);
            localMinDb = Math.Min(localMinDb, decibels);
        }

        // Min/max pooling per decimation bucket: sampling every Nth value would
        // skip a narrow reflection peak entirely, hiding the very feature the
        // markers point at.
        for (int bucketStart = -radius; bucketStart <= radius; bucketStart += step)
        {
            int bucketEnd = Math.Min(radius, bucketStart + step - 1);
            int minOffset = bucketStart;
            int maxOffset = bucketStart;
            double minValue = double.PositiveInfinity;
            double maxValue = double.NegativeInfinity;
            for (int offset = bucketStart; offset <= bucketEnd; offset++)
            {
                double value = envelope[
                    DspMath.WrapIndex(result.EnvelopePeakIndex + offset, envelope.Length)];
                if (value < minValue)
                {
                    minValue = value;
                    minOffset = offset;
                }
                if (value > maxValue)
                {
                    maxValue = value;
                    maxOffset = offset;
                }
            }

            AddPoint(Math.Min(minOffset, maxOffset));
            if (minOffset != maxOffset)
            {
                AddPoint(Math.Max(minOffset, maxOffset));
            }
        }

        // The floor rides under THIS curve's own maximum rather than under the
        // shared reference: it is there to stop a null from dragging the axis
        // to the numeric floor, and a Compare record genuinely quieter than
        // Main must still be drawn whole instead of flattened onto an absolute
        // line 80 dB under Main's peak.
        double floorDb = localMaxDb - CurveFloorDb;
        for (int i = 0; i < series.Points.Count; i++)
        {
            DataPoint point = series.Points[i];
            if (point.Y < floorDb)
            {
                series.Points[i] = new DataPoint(point.X, floorDb);
            }
        }

        maxDb = localMaxDb;
        minDb = Math.Max(localMinDb, floorDb);
        return series;
    }

    internal static void AddMainPeakMarkers(
        PlotModel model,
        TimeAlignmentAnalysisResult mainResult,
        double referenceAmplitude)
    {
        double strongestMilliseconds =
            mainResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        AddCalloutMarker(
            model,
            "M First",
            0.0,
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.EnvelopePeakIndex),
            OxyColor.FromRgb(255, 96, 96),
            PlotCalloutDirection.LeftUp);
        if (Math.Abs(strongestMilliseconds) > 0.001)
        {
            AddCalloutMarker(
                model,
                "M Peak",
                strongestMilliseconds,
                GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.StrongestEnvelopePeakIndex),
                OxyColor.FromRgb(140, 170, 255),
                PlotCalloutDirection.RightUp);
        }

        AddCalloutMarker(
            model,
            "M Onset",
            mainResult.EnergyOnsetDelayMilliseconds - mainResult.FirstArrivalDelayMilliseconds,
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, GetEnergyOnsetIndex(mainResult)),
            OxyColor.FromRgb(96, 200, 120),
            PlotCalloutDirection.LeftDown);
    }

    // The envelope index of the energy onset: its sample rounded and wrapped
    // into the (circular) envelope, since a complete record reports positions
    // as signed delays (see TimeAlignmentAnalysisOptions.WrapPeakPositions).
    internal static int GetEnergyOnsetIndex(TimeAlignmentAnalysisResult result)
    {
        int length = result.EnvelopeSamples.Length;
        if (length == 0)
        {
            return 0;
        }

        long rounded = (long)Math.Round(result.EnergyOnsetSample);
        return (int)(((rounded % length) + length) % length);
    }

    internal static void AddComparePeakMarkers(
        PlotModel model,
        TimeAlignmentAnalysisResult mainResult,
        TimeAlignmentAnalysisResult compareResult,
        double referenceAmplitude,
        double compareFirstArrivalMilliseconds)
    {
        double mainFirstArrivalDecibels =
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.EnvelopePeakIndex);
        double compareFirstArrivalDecibels =
            GetPeakMarkerDecibels(compareResult, referenceAmplitude, compareResult.EnvelopePeakIndex);
        double mainStrongestMilliseconds =
            mainResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        double compareStrongestMilliseconds =
            compareResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        double mainStrongestDecibels =
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.StrongestEnvelopePeakIndex);
        double compareStrongestDecibels =
            GetPeakMarkerDecibels(compareResult, referenceAmplitude, compareResult.StrongestEnvelopePeakIndex);

        AddCalloutMarker(
            model,
            "M First",
            0.0,
            mainFirstArrivalDecibels,
            OxyColor.FromRgb(255, 96, 96),
            mainFirstArrivalDecibels >= compareFirstArrivalDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);
        AddCalloutMarker(
            model,
            "C First",
            compareFirstArrivalMilliseconds,
            compareFirstArrivalDecibels,
            OxyColor.FromArgb(145, 255, 96, 96),
            compareFirstArrivalDecibels > mainFirstArrivalDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);

        if (Math.Abs(mainStrongestMilliseconds) > 0.001)
        {
            AddCalloutMarker(
                model,
                "M Peak",
                mainStrongestMilliseconds,
                mainStrongestDecibels,
                OxyColor.FromRgb(140, 170, 255),
                mainStrongestDecibels >= compareStrongestDecibels
                    ? PlotCalloutDirection.RightUp
                    : PlotCalloutDirection.RightDown);
        }

        if (Math.Abs(compareStrongestMilliseconds - compareFirstArrivalMilliseconds) > 0.001)
        {
            AddCalloutMarker(
                model,
                "C Peak",
                compareStrongestMilliseconds,
                compareStrongestDecibels,
                OxyColor.FromArgb(145, 140, 170, 255),
                compareStrongestDecibels > mainStrongestDecibels
                    ? PlotCalloutDirection.RightUp
                    : PlotCalloutDirection.RightDown);
        }

        double mainOnsetDecibels =
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, GetEnergyOnsetIndex(mainResult));
        double compareOnsetDecibels =
            GetPeakMarkerDecibels(compareResult, referenceAmplitude, GetEnergyOnsetIndex(compareResult));
        AddCalloutMarker(
            model,
            "M Onset",
            mainResult.EnergyOnsetDelayMilliseconds - mainResult.FirstArrivalDelayMilliseconds,
            mainOnsetDecibels,
            OxyColor.FromRgb(96, 200, 120),
            mainOnsetDecibels >= compareOnsetDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);
        AddCalloutMarker(
            model,
            "C Onset",
            compareResult.EnergyOnsetDelayMilliseconds - mainResult.FirstArrivalDelayMilliseconds,
            compareOnsetDecibels,
            OxyColor.FromArgb(145, 96, 200, 120),
            compareOnsetDecibels > mainOnsetDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);
    }

    private static void AddCalloutMarker(
        PlotModel model,
        string label,
        double milliseconds,
        double decibels,
        OxyColor color,
        PlotCalloutDirection direction)
    {
        model.Annotations.Add(new PlotCalloutMarkerAnnotation
        {
            Text = label,
            AnchorPoint = new DataPoint(milliseconds, decibels),
            Color = color,
            Direction = direction,
            Layer = AnnotationLayer.AboveSeries
        });
    }

    internal static double GetPeakMarkerDecibels(
        TimeAlignmentAnalysisResult result,
        double referenceAmplitude,
        int peakIndex)
    {
        if ((uint)peakIndex >= (uint)result.EnvelopeSamples.Length ||
            referenceAmplitude <= 0)
        {
            return 0.0;
        }

        // Same floor the curve itself is drawn with, so a marker never parks
        // under its own line on a record much quieter than the reference.
        double peakDecibels = DataHelper.AmplitudeToDecibels(
            result.StrongestEnvelopePeak / referenceAmplitude);
        double relativeAmplitude = result.EnvelopeSamples[peakIndex] / referenceAmplitude;
        return Math.Max(
            peakDecibels - CurveFloorDb,
            DataHelper.AmplitudeToDecibels(relativeAmplitude));
    }

    private void ClearEnvelopePreview()
    {
        envelopeViewports.Show(CreateEmptyEnvelopePreviewModel(), Mode.TimeAlignment);
    }

    private PlotModel CreateEmptyEnvelopePreviewModel()
    {
        var model = CreatePreviewPlotModel("Envelope Around Peak");
        model.Axes.Add(CreateMillisecondsAxis(-50, 50));
        var dbAxis = CreateDecibelAxis();
        dbAxis.Title = EnvelopeDecibelAxisTitle;
        dbAxis.AbsoluteMaximum = 0;
        dbAxis.AbsoluteMinimum = -80;
        dbAxis.Maximum = 0;
        dbAxis.Minimum = -80;
        model.Axes.Add(dbAxis);
        return model;
    }

    private void SetStatusText(string text)
    {
        statusTextBox.BeginUpdate();
        try
        {
            statusTextBox.Clear();
            AppendStatusText(text, UiPalette.TextSecondarySoft);
        }
        finally
        {
            statusTextBox.EndUpdate();
        }
    }

    private void SetMeasurementResultStatus(
        TimeAlignmentBandMode bandMode,
        TimeAlignmentAnalysisSource mainSource,
        TimeAlignmentAnalysisResult mainResult,
        TimeAlignmentArrivalProbe? mainProbe,
        CrosstalkHeadGate? mainCrosstalk,
        TimeAlignmentCompareAnalysis? compareAnalysis,
        TimeAlignmentArrivalProbe? compareProbe,
        CrosstalkHeadGate? compareCrosstalk,
        string? compareWarning,
        double? bandCenterHz)
    {
        statusTextBox.BeginUpdate();
        try
        {
            statusTextBox.Clear();
            AppendMeasurementResult(
                bandMode, "Main", mainSource.Levels, mainResult, mainProbe, mainCrosstalk,
                bandCenterHz);
            AppendCompareResult(
                bandMode,
                mainResult,
                compareAnalysis,
                compareProbe,
                compareCrosstalk,
                compareWarning,
                bandCenterHz);
            statusTextBox.SelectionStart = 0;
            statusTextBox.SelectionLength = 0;
        }
        finally
        {
            statusTextBox.EndUpdate();
        }
    }

    private void AppendCompareResult(
        TimeAlignmentBandMode bandMode,
        TimeAlignmentAnalysisResult mainResult,
        TimeAlignmentCompareAnalysis? compareAnalysis,
        TimeAlignmentArrivalProbe? compareProbe,
        CrosstalkHeadGate? compareCrosstalk,
        string? warning,
        double? bandCenterHz)
    {
        if (compareAnalysis == null && warning == null)
        {
            return;
        }

        if (warning != null)
        {
            AppendStatusText("\r\nCompare: ", UiPalette.TextPrimarySoft, resultTableFont);
            AppendStatusText(warning + "\r\n", UiPalette.WarningAmber);
            return;
        }

        AppendStatusText("\r\n", UiPalette.TextPrimarySoft);
        // Passing the Main result makes the Compare delay table show each value's delta
        // against Source in parentheses.
        AppendMeasurementResult(
            bandMode,
            "Compare",
            compareAnalysis!.Value.Source.Levels,
            compareAnalysis.Value.Result,
            compareProbe,
            compareCrosstalk,
            bandCenterHz,
            mainResult);
    }

    private void AppendMeasurementResult(
        TimeAlignmentBandMode bandMode,
        string title,
        InputLevelMeterSnapshot levels,
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? honestyProbe,
        CrosstalkHeadGate? crosstalk,
        double? bandCenterHz,
        TimeAlignmentAnalysisResult? reference = null)
    {
        AppendSignalQuality(title, result);
        AppendAlignmentConfidence(result);
        AppendArrivalHonesty(bandMode, result, honestyProbe);
        AppendCrosstalkFlag(bandMode, crosstalk);
        AppendLevelsLine(levels);
        AppendSeparator();
        DelayRow? recommended = RecommendedRow(
            result, honestyProbe, bandMode, crosstalk != null, bandCenterHz);
        AppendDelayTable(result, reference, recommended);
        if (recommended is { } row)
        {
            AppendStatusText("Recommended for alignment: ", UiPalette.TextPrimarySoft);
            AppendStatusText(RowLabel(row) + "\r\n", UiPalette.SuccessGreen);
            AppendStrongestPeakHint(result);
        }
    }

    /// <summary>The instants the delay table reports, one row each.</summary>
    internal enum DelayRow
    {
        FirstArrival,
        StrongestPeak,
        EnergyOnset
    }

    internal static string RowLabel(DelayRow row) => row switch
    {
        DelayRow.FirstArrival => DelayTableText.FirstArrivalLabel,
        DelayRow.StrongestPeak => DelayTableText.StrongestPeakLabel,
        _ => DelayTableText.EnergyOnsetLabel
    };

    // Which row of the delay table the analysis trusts most — the one the
    // table marks and the hint below it names. None on a near-noise record,
    // a modal latch or a contaminated full-band read (see
    // IsArrivalRecommendable). In a band-limited read whose band is centred
    // below the engine's energy-onset edge, with the SNR the onset needs, the
    // energy onset outranks the first peak for the reason the engine's
    // cross-side links read it: on a slow low-frequency envelope the first
    // peak is a coin. Everywhere else the first arrival is the figure, as the
    // strongest peak never is — a later, stronger peak is a mode or a
    // reflection, not the driver's timing.
    internal static DelayRow? RecommendedRow(
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? honestyProbe,
        TimeAlignmentBandMode bandMode,
        bool crosstalkDetected,
        double? bandCenterHz)
    {
        if (result.SignalToNoiseDecibels < AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            return null;
        }

        if (bandMode != TimeAlignmentBandMode.FullBand &&
            bandCenterHz is { } centerHz &&
            centerHz < AutoAlignmentEngine.EnergyOnsetBandCenterHz &&
            result.SignalToNoiseDecibels >= AutoAlignmentEngine.EnergyOnsetMinimumSnrDb)
        {
            return DelayRow.EnergyOnset;
        }

        return IsArrivalRecommendable(result, honestyProbe, bandMode, crosstalkDetected)
            ? DelayRow.FirstArrival
            : null;
    }

    // Whether the First Arrival may be RECOMMENDED as the alignment figure.
    // The strongest-peak hint ends with "Use First Arrival for alignment",
    // and that advice must never print next to a verdict that just
    // disqualified the arrival: a modal latch, a near-noise record, or a
    // full-band read over a record with detected crosstalk (the bypass mode
    // analyzes it raw). The states are independent, so without this gate the
    // status box could give two opposite instructions at once.
    internal static bool IsArrivalRecommendable(
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? honestyProbe,
        TimeAlignmentBandMode bandMode,
        bool crosstalkDetected) =>
        result.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb &&
        honestyProbe?.Certificate != AutoAlignmentEngine.ArrivalCertificate.Latched &&
        !(bandMode == TimeAlignmentBandMode.FullBand && crosstalkDetected);

    // Field-proven failure (v3): an electrical copy of the playback lands at
    // a fixed early sample in every record of a session; on band-limited
    // drivers it sits within the first-peak threshold and the FULL-BAND
    // First Arrival confidently times it instead of the sound.
    private void AppendCrosstalkFlag(
        TimeAlignmentBandMode bandMode,
        CrosstalkHeadGate? crosstalk)
    {
        if (crosstalk is not { } gate)
        {
            return;
        }

        // The mode the READ was taken in, never the one the controls have
        // reached since: "removed from this analysis" is a statement about
        // which record these very figures came off, and a bypass read whose
        // panel has moved to Auto would otherwise claim a cleaning it never
        // had.
        if (bandMode == TimeAlignmentBandMode.FullBand)
        {
            // Bypass shows the record as-is, so the figures above may time
            // the click; the banded modes analyze it removed.
            AppendStatusText(
                $"⚠ Playback crosstalk at {gate.BurstTimeMs:0.00} ms " +
                $"({gate.BurstPeakDbReMax:0.0} dB re max) — an electrical copy of\r\n" +
                "the playback, not the driver's sound; the full-band First Arrival\r\n" +
                "may be timing it. Switch to Auto band (analyzed with it removed).\r\n",
                UiPalette.ErrorSoft);
            return;
        }

        AppendStatusText(
            $"⚠ Playback crosstalk at {gate.BurstTimeMs:0.00} ms " +
            $"({gate.BurstPeakDbReMax:0.0} dB re max) removed from this analysis\r\n",
            UiPalette.WarningAmber);
    }

    // The auto-alignment engine's arrival honesty probe, surfaced on the
    // manual table: with a bandpass window active, the full-band first
    // arrival is re-checked against the band's upper half. A full-band read
    // far LATER than its own upper half is the modal latch — the read times
    // the band's late build-up (a room mode), not the direct sound — which
    // produces a confident wrong number exactly where this tool is used most
    // (subwoofer and midbass bands).
    private void AppendArrivalHonesty(
        TimeAlignmentBandMode bandMode,
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? probe)
    {
        if (bandMode == TimeAlignmentBandMode.FullBand)
        {
            return;
        }

        AppendStatusText("Arrival probe: ", UiPalette.TextPrimarySoft);
        if (probe == null)
        {
            AppendStatusText(
                "pass band too narrow for the upper-half check\r\n",
                UiPalette.TextSecondarySoft);
            return;
        }

        TimeAlignmentArrivalProbe probeValue = probe.Value;
        switch (probeValue.Certificate)
        {
            case AutoAlignmentEngine.ArrivalCertificate.Verified:
                AppendStatusText(
                    $"verified — the {probeValue.ProbeLowHz:0}-{probeValue.ProbeHighHz:0} Hz " +
                    "upper half agrees " +
                    $"({probeValue.ProbeResult.FirstArrivalDelayMilliseconds:0.000} ms)\r\n",
                    UiPalette.SuccessGreenSoft);
                break;
            case AutoAlignmentEngine.ArrivalCertificate.Latched:
                // The upper-half figure is DIAGNOSTIC only: it proves the
                // full-band read is not the direct front, but it is no
                // alignment target itself (the engine's field case: an
                // upper-half read walked a woofer 6 ms off).
                AppendStatusText(
                    $"MODAL LATCH — full band {result.FirstArrivalDelayMilliseconds:0.000} ms " +
                    $"vs upper half {probeValue.ProbeResult.FirstArrivalDelayMilliseconds:0.000} ms\r\n",
                    UiPalette.ErrorSoft);
                AppendStatusText(
                    "⚠ Not the direct front (modal build-up) — do not align " +
                    "from this arrival;\r\nchange the analysis band or check " +
                    "the measurement.\r\n",
                    UiPalette.ErrorSoft);
                break;
            default:
                AppendStatusText(
                    "not certified — the upper half is unmeasurable or does not " +
                    "show the front\r\n",
                    UiPalette.TextSecondarySoft);
                break;
        }
    }

    // A subwoofer or any narrowband/modal measurement can leave the strongest peak
    // a room mode or reflection well after the direct sound, so the two columns
    // disagree. Point the reader at the first arrival instead of the misleading
    // strongest peak.
    private void AppendStrongestPeakHint(TimeAlignmentAnalysisResult result)
    {
        if (!result.StrongestPeakIsSeparateArrival)
        {
            return;
        }

        AppendStatusText(
            $"⚠ Strongest peak is ~{result.StrongestPeakSeparationMilliseconds:0.0} ms " +
            "after first arrival — likely a room mode or reflection.\r\n",
            UiPalette.WarningAmber);
    }

    // Two figures, deliberately not conflated into one "quality" number: the
    // recording's SNR (strongest envelope peak vs the rest of the record) grades
    // the measurement, while the first-arrival prominence (its level relative to
    // the strongest peak) grades how sharply defined the pick is. A woofer's
    // broad leading edge gives a low prominence on an excellent recording —
    // physics, not a bad measurement — and must not drag the signal grade down.
    private void AppendSignalQuality(string title, TimeAlignmentAnalysisResult result)
    {
        AppendStatusText($"{title} Signal: ", UiPalette.TextPrimarySoft);

        // Below the same SNR floor the auto-alignment engine refuses to
        // measure at, the "arrival" is a bump in the noise (independent noise
        // records read ~8 dB): the manual table still shows its figures, but
        // graded as not-evidence rather than as a poor measurement.
        if (result.SignalToNoiseDecibels < AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            AppendStatusText(
                $"Unmeasurable ({result.SignalToNoiseDecibels:0.0} dB SNR, below " +
                $"the {AutoAlignmentEngine.MinimumArrivalSnrDb:0} dB floor)\r\n",
                UiPalette.ErrorSoft);
            AppendStatusText(
                "⚠ The arrival is not distinguishable from the record's noise\r\n" +
                "floor — the delay figures below are noise, not measurements.\r\n",
                UiPalette.ErrorSoft);
            return;
        }

        string signalGrade = FormatConfidence(result.SignalToNoiseDecibels);
        AppendStatusText(
            $"{signalGrade} ({result.SignalToNoiseDecibels:0.0} dB SNR)\r\n",
            GetConfidenceColor(signalGrade));

        double prominence = result.FirstArrivalProminenceDecibels;
        AppendStatusText("First arrival: ", UiPalette.TextPrimarySoft);
        if (prominence >= -1.0)
        {
            AppendStatusText(
                "coincides with the strongest peak\r\n",
                UiPalette.SuccessGreen);
            return;
        }

        string hint = prominence <= BroadRiseProminenceDb
            ? " — broad rise, normal for low-frequency drivers"
            : string.Empty;
        Color color = prominence >= BroadRiseProminenceDb
            ? UiPalette.SuccessGreenSoft
            : UiPalette.TextSecondarySoft;
        AppendStatusText(
            $"{prominence:0.0} dB re strongest peak{hint}\r\n",
            color);
    }

    // Below this the first arrival sits far down a slow leading edge; typical
    // for band-limited low-frequency drivers, where the envelope rises over
    // milliseconds before the in-room energy peaks.
    private const double BroadRiseProminenceDb = -12.0;

    // The GCC-PHAT trust for the first arrival: how sharply the whitened correlation
    // located the sub-sample delay. RefinedByPhat=false means the whitened peak was
    // too weak and the envelope parabola set the position, so the figure is the
    // honest "coarse alignment" signal rather than a trustworthy sub-sample number.
    private void AppendAlignmentConfidence(TimeAlignmentAnalysisResult result)
    {
        // The near-noise state above already declared the figures
        // non-evidence; a confident-looking percentage next to that verdict
        // would read as a contradiction.
        if (result.SignalToNoiseDecibels < AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            return;
        }

        int percent = (int)Math.Round(
            Math.Clamp(result.FirstArrivalConfidence, 0.0, 1.0) * 100.0);
        string method = result.FirstArrivalRefinedByPhat
            ? "GCC-PHAT"
            : "envelope fallback";
        Color color = !result.FirstArrivalRefinedByPhat
            ? UiPalette.TextSecondarySoft
            : result.FirstArrivalConfidence >= 0.6
                ? UiPalette.SuccessGreen
                : result.FirstArrivalConfidence >= 0.4
                    ? UiPalette.SuccessGreenSoft
                    : UiPalette.WarningAmber;
        AppendStatusText("Alignment: ", UiPalette.TextPrimarySoft);
        AppendStatusText($"{percent}% ({method})\r\n", color);
    }

    private void AppendSeparator()
    {
        AppendStatusText(
            new string('_', 54) + "\r\n",
            UiPalette.TextSecondarySoft,
            resultTableFont);
    }

    // Rows are the instants the analysis reads, columns the units. With a
    // reference (the Compare table) every cell shows its delta against the
    // Main value in parentheses, e.g. "1.006 (+0.010)". The recommended row
    // (see RecommendedRow) is printed bright and marked at its end, and named
    // on the line under the table; the others dimmed, so the eye lands on the
    // figure to align from.
    private void AppendDelayTable(
        TimeAlignmentAnalysisResult result,
        TimeAlignmentAnalysisResult? reference,
        DelayRow? recommended)
    {
        AppendStatusText(
            DelayTableText.FormatHeader() + "\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
        AppendDelayRow(
            DelayRow.FirstArrival,
            UiPalette.TimeAlignmentFirstArrival,
            result.FirstArrivalDelayMilliseconds,
            result.FirstArrivalPeakSample,
            reference?.FirstArrivalDelayMilliseconds,
            reference?.FirstArrivalPeakSample,
            recommended);
        AppendDelayRow(
            DelayRow.StrongestPeak,
            UiPalette.TimeAlignmentStrongestPeak,
            result.StrongestDelayMilliseconds,
            result.StrongestPeakSample,
            reference?.StrongestDelayMilliseconds,
            reference?.StrongestPeakSample,
            recommended);
        AppendDelayRow(
            DelayRow.EnergyOnset,
            UiPalette.TimeAlignmentEnergyOnset,
            result.EnergyOnsetDelayMilliseconds,
            result.EnergyOnsetSample,
            reference?.EnergyOnsetDelayMilliseconds,
            reference?.EnergyOnsetSample,
            recommended);
    }

    private void AppendDelayRow(
        DelayRow row,
        Color labelColor,
        double milliseconds,
        double samples,
        double? referenceMilliseconds,
        double? referenceSamples,
        DelayRow? recommended)
    {
        bool isRecommended = recommended == row;
        // The label carries the row's accent (matching its envelope marker);
        // the cells are one segment so the click-to-copy columns stay exact.
        AppendStatusText(
            RowLabel(row).PadRight(DelayTableText.MillisecondsColumn),
            labelColor,
            resultTableFont);
        AppendStatusText(
            DelayTableText.FormatCells(
                FormatValueWithDelta(milliseconds, referenceMilliseconds, "0.000"),
                FormatValueWithDelta(samples, referenceSamples, "0.0"),
                FormatValueWithDelta(
                    DelayMeters(milliseconds),
                    referenceMilliseconds is { } referenceMs ? DelayMeters(referenceMs) : null,
                    "0.000")),
            isRecommended ? UiPalette.TextPrimarySoft : UiPalette.TextSecondarySoft,
            resultTableFont);
        if (isRecommended)
        {
            AppendStatusText(
                DelayTableText.RecommendedMarker,
                UiPalette.SuccessGreen,
                resultTableFont);
        }

        AppendStatusText("\r\n", UiPalette.TextPrimarySoft, resultTableFont);
    }

    private static double DelayMeters(double delayMilliseconds) =>
        Math.Abs(delayMilliseconds) * Acoustics.SpeedOfSoundAt20CMetersPerSecond / 1000.0;

    private static string FormatValueWithDelta(
        double value,
        double? reference,
        string valueFormat) =>
        DelayTableText.FormatValueWithDelta(value, reference, valueFormat);

    // Mic and Loopback on one line: the status box holds two full measurement
    // blocks plus their warnings, so every line of vertical budget counts.
    private void AppendLevelsLine(InputLevelMeterSnapshot levels)
    {
        AppendStatusText("Levels (peak/RMS dBFS): ", UiPalette.TextPrimarySoft);
        AppendLevelSegment("mic", levels.Microphone);
        AppendStatusText(", ", UiPalette.TextPrimarySoft);
        AppendLevelSegment("loop", levels.Loopback);
        AppendStatusText("\r\n", UiPalette.TextPrimarySoft);
    }

    private void AppendLevelSegment(string label, InputLevelMeterEntry entry)
    {
        if (!entry.Available)
        {
            AppendStatusText($"{label} unavailable", UiPalette.TextSecondarySoft);
            return;
        }

        AppendStatusText(
            $"{label} {entry.PeakDbFs:0.0}/{entry.RmsDbFs:0.0}",
            UiPalette.TextPrimarySoft);
        if (entry.Clipped)
        {
            AppendStatusText(" CLIP", UiPalette.ErrorSoft);
        }
        else if (entry.FullScaleReference)
        {
            AppendStatusText(" FULL SCALE", UiPalette.TextSecondarySoft);
        }
    }

    private void AppendStatusText(string text, Color color, Font? font = null)
    {
        statusTextBox.SelectionStart = statusTextBox.TextLength;
        statusTextBox.SelectionLength = 0;
        statusTextBox.SelectionColor = color;
        statusTextBox.SelectionFont = font ?? statusTextBox.Font;
        statusTextBox.AppendText(text);
        statusTextBox.SelectionFont = statusTextBox.Font;
        statusTextBox.SelectionColor = statusTextBox.ForeColor;
    }

    private void StatusTextBoxMouseClick(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left ||
            !TryGetCopyableStatusLine(args.Location, out string value))
        {
            return;
        }

        try
        {
            Clipboard.SetText(value);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard is a shared resource; another process may hold it
            // (remote desktop, clipboard managers). Losing one copy click must
            // not crash the app.
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        System.Media.SystemSounds.Asterisk.Play();
    }

    private bool TryGetCopyableStatusLine(Point location, out string value)
    {
        value = string.Empty;
        int index = statusTextBox.GetCharIndexFromPosition(location);
        int line = statusTextBox.GetLineFromCharIndex(index);
        if (line >= statusTextBox.Lines.Length)
        {
            return false;
        }

        string lineText = statusTextBox.Lines[line];
        if (!DelayTableText.IsDelayRow(lineText))
        {
            return false;
        }

        int lineStart = statusTextBox.GetFirstCharIndexFromLine(line);
        int column = Math.Max(0, index - lineStart);
        value = DelayTableText.CellAt(column) is { } cellStart
            ? GetDelayTableValue(lineText, cellStart)
            : string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetDelayTableValue(string line, int startColumn) =>
        DelayTableText.GetValue(line, startColumn);

    private static string FormatConfidence(double confidenceDecibels)
    {
        if (confidenceDecibels >= 45)
        {
            return "Excellent";
        }
        if (confidenceDecibels >= 34)
        {
            return "Good";
        }
        if (confidenceDecibels >= 23)
        {
            return "Fair";
        }

        return "Poor";
    }

    private static Color GetConfidenceColor(string confidence) =>
        confidence switch
        {
            "Excellent" => UiPalette.SuccessGreen,
            "Good" => UiPalette.SuccessGreenSoft,
            "Fair" => UiPalette.WarningAmber,
            _ => UiPalette.ErrorSoft
        };

    private static PlotModel CreatePreviewPlotModel(string title) =>
        new()
        {
            Background = OxyColor.FromRgb(32, 36, 46),
            PlotAreaBackground = OxyColor.FromRgb(32, 36, 46),
            TextColor = OxyColors.White,
            Title = title,
            TitleColor = OxyColors.White,
            TitleFontSize = 10
        };

    private static LinearAxis CreateDecibelAxis()
    {
        var axis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = -80,
            Maximum = 3,
            MajorStep = 20,
            Title = "dB"
        };
        ApplyPreviewAxisStyle(axis);
        return axis;
    }

    private static LinearAxis CreateMillisecondsAxis(double minimum, double maximum)
    {
        var axis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = minimum,
            AbsoluteMaximum = maximum,
            Minimum = minimum,
            Maximum = maximum,
            MajorStep = 25,
            Title = "ms from peak"
        };
        ApplyPreviewAxisStyle(axis);
        return axis;
    }

    // The shared dark-preview look of every axis on the two side plots.
    private static void ApplyPreviewAxisStyle(Axis axis)
    {
        axis.MajorGridlineColor = OxyColor.FromRgb(55, 62, 78);
        axis.MajorGridlineStyle = LineStyle.Solid;
        axis.MinorGridlineColor = OxyColor.FromRgb(48, 54, 70);
        axis.MinorGridlineStyle = LineStyle.Dot;
        axis.TextColor = OxyColors.White;
        axis.TicklineColor = OxyColors.White;
    }

}

internal readonly record struct TimeAlignmentCompareMeasurement(
    string DisplayName,
    MeasurementHistorySnapshot Snapshot);

internal readonly record struct TimeAlignmentCompareAnalysis(
    TimeAlignmentAnalysisSource Source,
    TimeAlignmentAnalysisResult Result);

internal readonly record struct TimeAlignmentAnalysisSource(
    string Kind,
    string DisplayName,
    int SampleRate,
    int Bits,
    double SweepDurationSeconds,
    PlaybackChannel PlayChannel,
    SweepMeasurementMode MeasurementMode,
    double[] TransferImpulseResponse,
    // The γ² half spectrum that produced TransferImpulseResponse (null for <2
    // averages or a snapshot without it). Fed to the GCC-PHAT refinement so
    // low-coherence bins carry less weight in the sub-sample alignment.
    double[]? TransferCoherence,
    InputLevelMeterSnapshot Levels);

internal sealed class StatusRichTextBox : RichTextBox
{
    private const int WmSetCursor = 0x20;
    private const int WmSetRedraw = 0x0B;
    private int updateDepth;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<Point, bool>? UseHandCursorAt { get; set; }

    public void BeginUpdate()
    {
        if (updateDepth++ == 0 && IsHandleCreated)
        {
            SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void EndUpdate()
    {
        if (updateDepth == 0)
        {
            return;
        }

        updateDepth--;
        if (updateDepth == 0 && IsHandleCreated)
        {
            SendMessage(Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            Invalidate();
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmSetCursor)
        {
            Point point = PointToClient(Cursor.Position);
            Cursor.Current = UseHandCursorAt?.Invoke(point) == true
                ? Cursors.Hand
                : Cursors.Default;
            message.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref message);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);
}
