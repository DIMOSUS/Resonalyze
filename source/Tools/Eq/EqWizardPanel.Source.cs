using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.History;
using Resonalyze.Options;

namespace Resonalyze;

// The self-contained half of the EQ Wizard: the mode owns its source (an impulse
// response, or a measured curve imported from an overlay slot or a text file) and
// its target curve (edited through a reused, isolated instance of the overlay
// target dialog). Importing a curve is a SNAPSHOT — nothing here keeps a link to
// the slot, history entry or file it came from, and nothing reaches into the
// overlay UI or the current measurement.
public partial class EqWizardPanel
{
    private const int DefaultSampleRateHz = 48_000;

    private static readonly int[] SelectableSampleRatesHz =
        { 44_100, 48_000, 88_200, 96_000, 176_400, 192_000 };

    private static readonly PeqQConvention[] SelectableQConventions =
        { PeqQConvention.Rbj, PeqQConvention.Symmetric, PeqQConvention.Classic };

    private const string NoSourceHint =
        "Load a source to equalize — an impulse response,\n" +
        "or a measured curve from an overlay slot or a text file.\n" +
        "Use Target… to shape the goal curve.";

    // The source has no overlay behind it, so its colours are fixed and chosen to
    // read against the target and the Source + EQ curve.
    private static readonly OxyColor SourceCurveColor = OxyColor.FromRgb(180, 190, 205);
    private static readonly OxyColor SourcePlusEqColor = OxyColor.FromRgb(0, 209, 255);

    // The 20 Hz .. 20 kHz grid the target is drawn on when there is no source to
    // borrow frequencies from.
    private static readonly double[] DefaultTargetGrid =
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 512).ToArray();

    private readonly EqWizardSourceResolver sourceResolver = new();
    private readonly EqWizardPreviewOrchestrator previewOrchestrator = new();
    private EqWizardCurveSource? loadedSource;
    private EqWizardCurve? cachedSourceCurve;
    private bool sourceCurveDirty = true;
    private int sourceLoadGeneration;
    private ContextMenuStrip? sourceMenu;

    private TargetPreset targetPreset = TargetPreset.Flat;
    private TargetCurveSpec targetSpec = TargetCurveSpec.FromPreset(TargetPreset.Flat);
    private double targetToleranceDb = 3;
    private TargetDeviationMode targetDeviationMode = TargetDeviationMode.Deviation;
    private Color targetColor = Color.FromArgb(0x37, 0xC8, 0xA0);
    private double targetStrokeThickness = 2;
    private OverlayLineStyle targetLineStyle = OverlayLineStyle.Dash;
    private int targetSmoothingInverseOctaves;

    private Func<string?, CalibrationFile?>? calibrationResolver;
    private IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries = [];
    // The effective choice for the loaded source (may be Own). Distinct from the persisted
    // impulse-response preference below: loading a curve forces this to Own/Off, which
    // must NOT overwrite what the user chose for impulse responses. See EqWizardCalibration.
    private EqWizardCalibrationChoice calibrationChoice = EqWizardCalibrationChoice.Off;
    // The user's standing configured choice for impulse responses; the only one persisted.
    private string? preferredIrCalibrationId;
    private bool suppressCalibrationEvents;
    private bool suppressSampleRateEvents;
    private bool suppressQConventionEvents;
    private bool suppressSettingsSave;
    // The rate used when the source does not state one; persisted, unlike the source.
    private int manualSampleRateHz = DefaultSampleRateHz;

    /// <summary>Raised when a persisted setting changes so the host can save.</summary>
    internal event Action? SettingsChanged;

    /// <summary>
    /// Measurement history, so an impulse response already recorded can be equalized
    /// without exporting it first. Wired by the host form; history is simply absent
    /// from the source menu until then.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal MeasurementHistoryService? HistoryService { get; set; }

    // ------------------------------------------------------------------ source menu

    // Opened from the source button. Rebuilt on every click because both lists behind
    // it change while the panel is open (a new measurement, a fresh overlay capture).
    private void ShowSourceMenu()
    {
        if (sourceMenu is { Visible: true })
        {
            sourceMenu.Close();
            return;
        }

        sourceMenu?.Dispose();
        sourceMenu = BuildSourceMenu();
        DropDownFocusGuard.Attach(sourceMenu);
        sourceMenu.Show(buttonSource, new Point(0, buttonSource.Height));
    }

    private ContextMenuStrip BuildSourceMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Impulse response from file…", null, (_, _) => _ = LoadIrFromFileAsync());

        var historyItem = new ToolStripMenuItem("Impulse response from history");
        PopulateHistoryMenu(historyItem);
        menu.Items.Add(historyItem);

        menu.Items.Add(new ToolStripSeparator());

        var slotItem = new ToolStripMenuItem("Curve from overlay slot");
        PopulateSlotMenu(slotItem);
        menu.Items.Add(slotItem);

        menu.Items.Add("Curve from text file…", null, (_, _) => LoadCurveFromTextFile());
        return menu;
    }

    private void PopulateHistoryMenu(ToolStripMenuItem historyItem)
    {
        IReadOnlyList<MeasurementHistoryEntry> entries =
            HistoryService?.Entries ?? Array.Empty<MeasurementHistoryEntry>();
        if (entries.Count == 0)
        {
            historyItem.Enabled = false;
            return;
        }

        foreach (MeasurementHistoryEntry entry in entries)
        {
            var entryItem = new ToolStripMenuItem(MenuText.Trim(entry.FileNameOrDisplayName))
            {
                Tag = entry.Id,
                ToolTipText = MeasurementHistoryToolTip.Build(entry.Metadata, entry.Timestamp)
            };
            entryItem.Click += (_, _) =>
            {
                if (entryItem.Tag is Guid entryId)
                {
                    _ = LoadIrFromHistoryAsync(entryId, entry.FileNameOrDisplayName);
                }
            };
            historyItem.DropDownItems.Add(entryItem);
        }
    }

    private void PopulateSlotMenu(ToolStripMenuItem slotItem)
    {
        IReadOnlyList<EqWizardSlotOption> slots = sourceResolver.ListEligibleSlots();
        if (slots.Count == 0)
        {
            slotItem.Enabled = false;
            slotItem.ToolTipText =
                "No overlay slot holds a captured frequency-response or RTA curve.";
            return;
        }

        foreach (EqWizardSlotOption slot in slots)
        {
            var item = new ToolStripMenuItem(MenuText.Trim($"{slot.Slot}: {slot.Title}"))
            {
                // A menu item's tooltip is drawn by the ToolStrip, not by the app's
                // wrapping tooltip, and a slot description can carry a full file path.
                ToolTipText = ToolTipTextWrapper.Wrap(slot.Description)
            };
            item.Click += (_, _) => LoadCurveFromSlot(slot.Slot);
            slotItem.DropDownItems.Add(item);
        }
    }

    // ------------------------------------------------------------- source loading

    private async Task LoadIrFromFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
            Title = "Load impulse response"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        // Guard against overlapping loads: a slow earlier load must not overwrite a
        // newer selection (or report its error) when it finally lands.
        int generation = ++sourceLoadGeneration;
        ImpulseResponseFile file;
        try
        {
            file = await ImpulseResponseFile.LoadAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            if (generation == sourceLoadGeneration && !IsDisposed)
            {
                ShowFileError("The impulse response could not be loaded.", exception);
            }

            return;
        }

        if (generation != sourceLoadGeneration || IsDisposed)
        {
            return;
        }

        ApplySource(EqWizardSourceResolver.CreateFromImpulseResponse(
            file,
            System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            $"Impulse response: {dialog.FileName}"));
    }

    private async Task LoadIrFromHistoryAsync(Guid entryId, string displayName)
    {
        if (HistoryService == null)
        {
            return;
        }

        int generation = ++sourceLoadGeneration;
        MeasurementHistorySnapshot? snapshot;
        try
        {
            snapshot = await HistoryService.GetSnapshotAsync(entryId);
        }
        catch (Exception exception)
        {
            if (generation == sourceLoadGeneration && !IsDisposed)
            {
                ShowFileError("The history entry could not be loaded.", exception);
            }

            return;
        }

        if (generation != sourceLoadGeneration || IsDisposed)
        {
            return;
        }

        // The entry can be deleted between opening the menu and choosing it; that is a
        // silent no-op, exactly like the Compare picker.
        if (snapshot == null)
        {
            return;
        }

        ApplySource(EqWizardSourceResolver.CreateFromImpulseResponse(
            snapshot.ToImpulseResponseFile(),
            displayName,
            $"History: {displayName}"));
    }

    private void LoadCurveFromSlot(int slot)
    {
        // Bumped for a synchronous load too, so an in-flight file or history load
        // cannot land on top of the slot the user just chose.
        sourceLoadGeneration++;
        EqWizardCurveSource? source = sourceResolver.TryCreateFromOverlaySlot(slot);
        if (source == null)
        {
            MessageBox.Show(
                FindForm(),
                $"Overlay slot {slot} no longer holds a curve that can be equalized.",
                "EQ Wizard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        ApplySource(source);
    }

    private void LoadCurveFromTextFile()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Measured curve (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Load measured curve"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        sourceLoadGeneration++;
        EqWizardCurveSource source;
        try
        {
            source = EqWizardSourceResolver.CreateFromTextCurve(
                OverlayTextFile.ImportCurve(dialog.FileName),
                dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowFileError("The curve could not be loaded.", exception);
            return;
        }

        ApplySource(source);
    }

    // Installs a freshly imported source and re-derives everything that depends on what
    // the source IS: which selectors apply, the sample rate, the axis, and where the
    // target starts.
    private void ApplySource(EqWizardCurveSource source)
    {
        // Installing a source ends any Virtual DSP handoff: the Return button must
        // never send a bank tuned against some OTHER curve back to a channel. A
        // handoff itself re-establishes its session right after this call.
        EndVirtualDspHandoff();
        loadedSource = source;
        // Before anything draws: the phase view reads its window from here, and a
        // window left over from the previous source would open on an arrival this one
        // does not have.
        SeedPhaseContext(source);

        // Settle every selector that feeds the curve and fit the axis before drawing,
        // all with redraws suppressed, so the single draw at the end paints the
        // finished state. The Target Level is deliberately NOT touched: it is the
        // user's knob alone, wherever the new source lands relative to it — a Virtual
        // DSP handoff carries its own panel's level in, and every other source keeps
        // whatever the user last set.
        suppressRedraw = true;
        try
        {
            calibrationChoice = ChooseCalibration(source);
            comboBoxSmooth.Enabled = source.SupportsSmoothing;
            PopulateCalibrationCombo();
            RefreshSampleRateCombo();
            InvalidateSourceCurve();

            ApplyAxisForSource();
        }
        finally
        {
            suppressRedraw = false;
        }

        buttonSource.Text = source.DisplayName;
        toolTip.SetToolTip(
            buttonSource,
            $"{source.Description}\r\nClick to load another source.");

        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    // The calibration a freshly loaded source starts on:
    //  - a curve that stored its own correction defaults to reproducing it (Own);
    //  - an impulse response restores the user's standing configured preference,
    //    regardless of what a previously loaded curve forced the effective choice to;
    //  - a curve with no uncalibrated reference cannot be re-calibrated at all (Off).
    private EqWizardCalibrationChoice ChooseCalibration(EqWizardCurveSource source)
    {
        if (source.HasOwnCalibration)
        {
            return EqWizardCalibrationChoice.OwnCapture;
        }
        if (source.Kind == EqWizardSourceKind.ImpulseResponse)
        {
            return EqWizardCalibrationChoice.Microphone(preferredIrCalibrationId);
        }
        // A Virtual DSP channel is pinned to the correction its panel renders with:
        // a PEQ fitted under one calibration and summed under another would break the
        // handoff's identity. The selector is disabled (SupportsCalibration is false)
        // and the standing IR preference stays untouched. The panel's Off is Off.
        if (source.Kind == EqWizardSourceKind.VirtualDspChannel)
        {
            return source.PinnedCalibration == null
                ? EqWizardCalibrationChoice.Off
                : EqWizardCalibrationChoice.PinnedToSource;
        }

        return EqWizardCalibrationChoice.Off;
    }

    // ------------------------------------------------------------- source curve

    // The source FR is an expensive FFT that only changes with the loaded source, the
    // source smoothing or the calibration — never with band/fader/target edits. It
    // is cached so a fader drag (many redraws per second) does not recompute it.
    private EqWizardCurve? GetSourceCurve()
    {
        if (sourceCurveDirty)
        {
            cachedSourceCurve = ComputeSourceCurve();
            sourceCurveDirty = false;
        }

        return cachedSourceCurve;
    }

    private void InvalidateSourceCurve()
    {
        sourceCurveDirty = true;
        // The corrected preview is built from the same measurement, gate and
        // calibration, so whatever invalidated the bare curve invalidated it too.
        InvalidateGatedPreview();
    }

    // One render request for the loaded gated source: the panel's live gate,
    // calibration and smoothing, plus the bank to substitute (null for the bare
    // curve). Captured here, on the UI thread, so the render itself touches no control.
    private EqWizardGatedPreviewRequest BuildGatedPreviewRequest(
        EqWizardCurveSource source, EqualizationCurve? bank) =>
        new(
            source.PreviewImpulseResponse!,
            source.PreviewChain!,
            bank,
            source.Measurement!.PeakIndex,
            source.Measurement.SampleRate,
            source.GateSettings!,
            ResolveChosenCalibration(),
            SourceSmoothingInverseOctaves);

    // The curve the current choice corrects with: the one the source arrived pinned
    // to, or the configured entry the choice names (none for Off and for Own, whose
    // correction is read off the curve itself, see ResolveCurveCalibrationCorrection).
    private CalibrationFile? ResolveChosenCalibration() =>
        calibrationChoice.Pinned
            ? loadedSource?.PinnedCalibration
            : calibrationResolver?.Invoke(calibrationChoice.MicrophoneCalibrationId);

    private EqWizardCurve? ComputeSourceCurve()
    {
        if (loadedSource is not { } source)
        {
            return null;
        }

        IReadOnlyList<SignalPoint> points = source.Measurement != null
            ? ComputeImpulseResponseSpectrum(source)
            : ComputeImportedCurve(source);
        return BuildSourceCurve(points, keepGaps: source.Measurement == null);
    }

    private IReadOnlyList<SignalPoint> ComputeImpulseResponseSpectrum(
        EqWizardCurveSource source)
    {
        // Only a configured calibration can be applied while computing an FR; "own"
        // belongs to an imported curve and never reaches here.
        string? calibrationId = calibrationChoice.MicrophoneCalibrationId;

        // A Virtual DSP channel reads through the gate it arrived with — the same
        // DataHelper call, template and offset the DSP panel's magnitude view uses —
        // so the wizard shows the very curve the user just left on that plot. The bare
        // curve is the corrected one's own path with no bank, so the two cannot drift.
        if (source.IsGated)
        {
            return EqWizardGatedPreview.Render(BuildGatedPreviewRequest(source, bank: null));
        }

        // The same steady-state window every magnitude curve in the Virtual DSP tool
        // reads — one definition in milliseconds, realized here as sample counts at
        // this measurement's rate. Long, so the low end resolves and a bass EQ band's
        // full depth is visible; zero-padded when the IR is shorter.
        (int window, int leftTukey, int rightTukey) =
            FrequencyResponseOptions.SteadyStateWindowSamples(
                source.Measurement!.SampleRate);
        var options = new FrequencyResponseOptions
        {
            Window = window,
            LeftTukeyWindow = leftTukey,
            RightTukeyWindow = rightTukey,
            SmoothingInverseOctaves = SourceSmoothingInverseOctaves,
            Offset = 0,
            CalibrationId = calibrationId
        };
        CalibrationFile? calibration = ResolveChosenCalibration();

        IReadOnlyList<AnalysisCurve> curves = DataHelper.GetSpectrum(
            source.Measurement!, options, calibration, SpectrumCurves.Primary);
        return curves.Count > 0 ? curves[0].Points : Array.Empty<SignalPoint>();
    }

    // An imported curve is already a finished response. When its uncalibrated reference
    // was stored it is re-rendered exactly the way the mode it came from would — same
    // resampler, so the mode's smoothing reproduces the on-screen reference. Without that
    // reference (a dB SPL capture) the curve's own points are the reference: the
    // correction frozen onto them comes back out, the width applies on their own grid, and
    // the chosen correction goes on instead. A curve that declared neither (a text import,
    // a legacy slot) has nothing to undo and is drawn as stored.
    private IReadOnlyList<SignalPoint> ComputeImportedCurve(EqWizardCurveSource source)
    {
        if (source.RawSpectrum is not { Count: >= 2 } raw)
        {
            return EqWizardImportedCurve.Render(
                source.Points,
                source.PointsCalibrationCorrectionDb,
                ResolvePointsCalibrationCorrection(source),
                source.SupportsSmoothing ? SourceSmoothingInverseOctaves : 0);
        }

        return RawCurveRenderer.Render(
            raw,
            ResolveCurveCalibrationCorrection(source),
            SourceSmoothingInverseOctaves);
    }

    // The same choice as ResolveCurveCalibrationCorrection, but frozen on the curve's own
    // points instead of the raw output grid — the only frequencies a no-raw capture has.
    private IReadOnlyList<double> ResolvePointsCalibrationCorrection(
        EqWizardCurveSource source)
    {
        if (calibrationChoice.Own)
        {
            return source.PointsCalibrationCorrectionDb;
        }

        return calibrationChoice.IsOff
            ? Array.Empty<double>()
            : EqWizardImportedCurve.SampleCorrection(
                ResolveChosenCalibration(),
                source.Points);
    }

    // The correction subtracted after smoothing: none, the one frozen at capture, or a
    // configured profile re-frozen on the same output grid.
    private IReadOnlyList<double> ResolveCurveCalibrationCorrection(
        EqWizardCurveSource source)
    {
        if (calibrationChoice.Own)
        {
            return source.OwnCalibrationCorrectionDb;
        }

        return calibrationChoice.IsOff
            ? Array.Empty<double>()
            : RawCurveRenderer.CaptureCalibrationCorrection(
                ResolveChosenCalibration());
    }

    // A measured curve keeps its NaN gaps: they mark bands the measurement could not
    // trust, and the fitter reads them instead of bridging them. A computed FR has no
    // such convention, so a non-finite value there is just noise and is dropped.
    private static EqWizardCurve? BuildSourceCurve(
        IReadOnlyList<SignalPoint> points,
        bool keepGaps)
    {
        var result = new List<DataPoint>(points.Count);
        foreach (SignalPoint point in points)
        {
            if (!double.IsFinite(point.X) || point.X <= 0)
            {
                continue;
            }
            if (!double.IsFinite(point.Y) && !keepGaps)
            {
                continue;
            }

            result.Add(new DataPoint(point.X, point.Y));
        }

        return result.Count >= 2
            ? new EqWizardCurve("Source", SourceCurveColor, 1.5, LineStyle.Solid, result)
            : null;
    }

    // Builds everything the plot draws from the loaded source and the local target,
    // without any overlay. The target is always present; the source (and therefore
    // Source + EQ) exists only once a source is loaded.
    private EqWizardRenderSet BuildRenderSet(EqualizationCurve eq)
    {
        EqWizardCurve? source = GetSourceCurve();
        double offset = (double)NumericTargetOffset.Value;

        EqWizardCurve target;
        EqWizardCurve? sourcePlusEq = null;
        if (source is { Points.Count: >= 2 })
        {
            double[] frequencies = source.Points.Select(point => point.X).ToArray();
            target = BuildTargetCurve(frequencies, offset);
            sourcePlusEq = BuildSourcePlusEqCurve(source.Points, eq);
        }
        else
        {
            target = BuildTargetCurve(DefaultTargetGrid, offset);
        }

        return new EqWizardRenderSet(target, source, sourcePlusEq);
    }

    private EqWizardCurve BuildTargetCurve(IReadOnlyList<double> frequencies, double offset)
    {
        var points = new DataPoint[frequencies.Count];
        for (int i = 0; i < frequencies.Count; i++)
        {
            double frequency = frequencies[i];
            points[i] = new DataPoint(frequency, targetSpec.Evaluate(frequency) + offset);
        }

        return new EqWizardCurve(
            "Target",
            ToOxyColor(targetColor),
            targetStrokeThickness,
            OverlayLineStyles.ToOxy(targetLineStyle),
            points);
    }

    private EqWizardCurve? BuildSourcePlusEqCurve(
        IReadOnlyList<DataPoint> sourcePoints,
        EqualizationCurve eq)
    {
        // A gated source is filtered and THEN windowed — a window does not commute with
        // a filter, and at the Virtual DSP gate lengths the difference reaches several
        // dB in the bass (see EqWizardGatedPreview). That render is far too heavy for a
        // fader frame, so it runs asynchronously and the last landed one is drawn while
        // the next is in flight.
        if (loadedSource is { IsGated: true } gated)
        {
            RequestGatedPreview(gated, eq);
            return landedGatedPreview == null
                ? null
                : new EqWizardCurve(
                    "Source + EQ",
                    SourcePlusEqColor,
                    2,
                    LineStyle.Solid,
                    landedGatedPreview);
        }

        var points = new DataPoint[sourcePoints.Count];
        for (int i = 0; i < sourcePoints.Count; i++)
        {
            DataPoint point = sourcePoints[i];
            points[i] = new DataPoint(
                point.X,
                point.Y + DigitalEqualizationResponse.MagnitudeDbAt(
                    eq, point.X, EqSampleRate));
        }

        return new EqWizardCurve("Source + EQ", SourcePlusEqColor, 2, LineStyle.Solid, points);
    }

    // ------------------------------------------------- gated corrected preview

    // The last render that landed, in plot coordinates, and the bank it belongs to.
    // Kept on screen while a newer render is in flight: blanking the curve on every
    // keystroke would strobe it.
    private IReadOnlyList<DataPoint>? landedGatedPreview;
    private PeqBankState? landedGatedPreviewBank;
    private bool gatedPreviewInFlight;

    /// <summary>
    /// Becoming visible is what starts a gated preview: it is deliberately not started
    /// while the panel is hidden (see <see cref="RequestGatedPreview"/>), so a handoff
    /// installed on the way in has nothing drawn for its corrected curve until here.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && IsHandleCreated && loadedSource is { IsGated: true })
        {
            DrawSelectedCurves();
        }
    }

    private void InvalidateGatedPreview()
    {
        previewOrchestrator.Invalidate();
        landedGatedPreview = null;
        landedGatedPreviewBank = null;
        // The phase view reads the same measurement through the same chain, so
        // whatever invalidated the magnitude preview invalidated it too — including
        // the neighbours, which are gated with it.
        InvalidatePhaseCurves();
    }

    // Starts a render unless the landed one already answers for this bank. The bank is
    // the identity: two redraws for the same filters (a target nudge, a selection
    // change) must not re-run a pair of transforms.
    private void RequestGatedPreview(EqWizardCurveSource source, EqualizationCurve eq)
    {
        // Nothing is started before the panel exists on screen. A handoff installs its
        // source while the wizard is still the hidden mode (the shell hands over, THEN
        // switches tabs), and making the window pumps messages — a render landing inside
        // that pump would draw into a half-created control. Becoming visible redraws,
        // and the render starts from there.
        if (!IsHandleCreated)
        {
            return;
        }

        var bank = new PeqBankState(eq.Bands, eq.PreampDb);
        if (gatedPreviewInFlight || bank.Equals(landedGatedPreviewBank))
        {
            return;
        }

        EqWizardGatedPreviewRequest request = BuildGatedPreviewRequest(source, eq);
        gatedPreviewInFlight = true;
        _ = RenderGatedPreviewAsync(request, bank);
    }

    private async Task RenderGatedPreviewAsync(
        EqWizardGatedPreviewRequest request, PeqBankState bank)
    {
        try
        {
            IReadOnlyList<SignalPoint>? points =
                await previewOrchestrator.RenderLatestAsync(request);
            // A render started before the panel was ever shown can finish before its
            // handle exists — the wizard is built while another mode is on screen, and
            // a handoff installs its source on the way in. Touching the plot then
            // forces creation out of order; the curve is simply picked up by the first
            // real draw instead.
            if (IsDisposed || !IsHandleCreated || points == null)
            {
                return;
            }

            landedGatedPreview = points
                .Select(point => new DataPoint(point.X, point.Y))
                .ToArray();
            landedGatedPreviewBank = bank;
        }
        catch (Exception exception)
        {
            // A preview that throws must not take the panel with it: the curve simply
            // stays as it was, and the bank is still exportable.
            System.Diagnostics.Debug.WriteLine($"EQ Wizard preview failed: {exception}");
        }
        finally
        {
            gatedPreviewInFlight = false;
        }

        if (!IsDisposed && IsHandleCreated)
        {
            // The bank may have moved on while this rendered; drawing now both paints
            // what landed and starts the follow-up render for the newer bank.
            DrawSelectedCurves();
        }
    }

    private void UpdateSourceHint()
    {
        hintAnnotation.Text = loadedSource == null ? NoSourceHint : string.Empty;
    }

    // ------------------------------------------------------------------- plot axis

    // Puts the dB axis where the source actually lives. An imported dB SPL curve sits
    // near 80 dB, far outside the impulse-response bounds — and those are ABSOLUTE
    // limits, so without this the curve cannot even be panned into view.
    private void ApplyAxisForSource()
    {
        if (plotWizard.Model is not { } model ||
            model.Axes.FirstOrDefault(axis =>
                axis.Position == OxyPlot.Axes.AxisPosition.Left) is not { } axis)
        {
            return;
        }

        bool splCurve = loadedSource is
        {
            Measurement: null,
            Scale: MagnitudeScale.SoundPressureLevel
        };
        EqWizardAxisRange range = ComputeAxisRangeForSource();

        // Widen the absolute bounds before the view, so setting the view can never be
        // clipped by limits left over from the previous source.
        axis.AbsoluteMinimum = double.NegativeInfinity;
        axis.AbsoluteMaximum = double.PositiveInfinity;
        axis.Minimum = range.Minimum;
        axis.Maximum = range.Maximum;
        axis.AbsoluteMinimum = range.AbsoluteMinimum;
        axis.AbsoluteMaximum = range.AbsoluteMaximum;
        axis.Title = splCurve ? "dB SPL" : "dB";
        axis.Reset();
    }

    // The default view the plot gets for the current source — and the yardstick for
    // whether the target is still visible after a source switch.
    private EqWizardAxisRange ComputeAxisRangeForSource() =>
        loadedSource is { Measurement: null }
            ? EqWizardPlotFit.ForCurve(
                GetSourceCurve()?.Points.Select(point => new SignalPoint(point.X, point.Y))
                    ?? Enumerable.Empty<SignalPoint>())
            : EqWizardPlotFit.ImpulseResponseRange;

    // ---------------------------------------------------------------- target

    private void OnTargetOffsetChanged()
    {
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    /// <summary>
    /// The target curve as one value. The wizard owns and persists it; the host
    /// hands the same definition to the Virtual DSP tool, which draws it over
    /// its predicted sum and can edit it back through
    /// <see cref="ApplyTargetCurve"/>.
    /// </summary>
    internal EqTargetCurve TargetCurve => new(
        targetPreset,
        targetSpec,
        targetToleranceDb,
        targetDeviationMode,
        targetColor,
        targetStrokeThickness,
        targetLineStyle,
        targetSmoothingInverseOctaves);

    /// <summary>
    /// Takes a target edited elsewhere (the Virtual DSP tool's own Target
    /// dialog). Redraws and persists exactly as an edit made here would, and
    /// ignores a value equal to the current one, so the host can push on every
    /// settings change without looping.
    /// </summary>
    internal void ApplyTargetCurve(EqTargetCurve value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (TargetCurve == value)
        {
            return;
        }

        AssignTargetCurve(value);
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    private void AssignTargetCurve(EqTargetCurve value)
    {
        targetPreset = value.Preset;
        targetSpec = value.Spec;
        targetToleranceDb = value.ToleranceDb;
        targetDeviationMode = value.DeviationMode;
        targetColor = value.Color;
        targetStrokeThickness = value.StrokeThickness;
        targetLineStyle = value.LineStyle;
        targetSmoothingInverseOctaves = value.SmoothingInverseOctaves;
    }

    // Reuses the overlay target dialog in isolated mode (no source picker, no
    // overlay side effects); its live preview redraws the wizard plot. Cancel
    // reverts the previewed changes.
    private void OpenTargetSettings()
    {
        EqTargetCurve before = TargetCurve;
        using var dialog = new OverlayTargetSettingsDialog(
            Mode.EqWizard,
            "EQ target",
            0,
            before.Preset,
            before.Spec,
            before.ToleranceDb,
            before.DeviationMode,
            before.Color,
            before.StrokeThickness,
            before.LineStyle,
            100,
            before.SmoothingInverseOctaves,
            Array.Empty<OverlaySlotOption>(),
            ApplyTargetPreview,
            isolatedTarget: true);

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            AssignTargetCurve(before);
            DrawSelectedCurves();
            return;
        }

        AssignTargetCurve(new EqTargetCurve(
            dialog.Preset,
            dialog.Spec,
            dialog.ToleranceDb,
            dialog.DeviationMode,
            dialog.SelectedColor,
            dialog.StrokeThickness,
            dialog.LineStyle,
            dialog.SmoothingInverseOctaves));
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    private void ApplyTargetPreview(OverlayTargetPreview preview)
    {
        targetSpec = preview.Spec;
        targetToleranceDb = preview.ToleranceDb;
        targetDeviationMode = preview.DeviationMode;
        targetColor = preview.Color;
        targetStrokeThickness = preview.StrokeThickness;
        targetLineStyle = preview.LineStyle;
        targetSmoothingInverseOctaves = preview.SmoothingInverseOctaves;
        DrawSelectedCurves();
    }

    // ---------------------------------------------------------- calibration

    /// <summary>
    /// Wires the microphone-calibration resolver and available profiles, then
    /// rebuilds the selector. Called again whenever the configured files change.
    /// </summary>
    internal void ConfigureCalibration(
        Func<string?, CalibrationFile?> resolver,
        IReadOnlyList<MicrophoneCalibrationEntry> entries)
    {
        calibrationResolver = resolver;
        calibrationEntries = entries;
        RefreshCalibrationCombo();
    }

    private void RefreshCalibrationCombo()
    {
        PopulateCalibrationCombo();
        InvalidateSourceCurve();
        DrawSelectedCurves();
    }

    // Rebuilds the selector's items and selection without invalidating or redrawing, so
    // a caller mid-way through installing a source (ApplySource) can settle the combo
    // and then compute the curve and fit the axis exactly once.
    private void PopulateCalibrationCombo()
    {
        suppressCalibrationEvents = true;
        try
        {
            comboBoxCalibration.Items.Clear();
            comboBoxCalibration.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (EqWizardCalibrationOption option in BuildCalibrationOptions())
            {
                comboBoxCalibration.Items.Add(option);
            }

            int index = -1;
            for (int i = 0; i < comboBoxCalibration.Items.Count; i++)
            {
                if (comboBoxCalibration.Items[i] is EqWizardCalibrationOption option &&
                    option.Choice == calibrationChoice)
                {
                    index = i;
                    break;
                }
            }

            // BuildCalibrationOptions always yields at least "Off" and always includes
            // the current choice, so index is found; the 0 fallback is only for the
            // impossible empty list.
            comboBoxCalibration.SelectedIndex = index >= 0 ? index : 0;
            comboBoxCalibration.Enabled =
                comboBoxCalibration.Items.Count > 1 &&
                (loadedSource?.SupportsCalibration ?? true);
            // The disabled selector still SHOWS a Virtual DSP channel's pinned
            // correction; the tooltip says why it cannot be changed from here.
            toolTip.SetToolTip(
                comboBoxCalibration,
                loadedSource is { Kind: EqWizardSourceKind.VirtualDspChannel }
                    ? "Follows the Virtual DSP panel's calibration selector while a " +
                      "DSP channel is loaded — change it there."
                    : string.Empty);
        }
        finally
        {
            suppressCalibrationEvents = false;
        }

        calibrationChoice = GetSelectedCalibration();
    }

    // Off and every configured calibration are always offered; "own" only exists for a
    // curve that stored the correction it was captured with. An entry that currently
    // resolves to nothing stays listed and marked, and so does a selection the list no
    // longer holds — dropping either would silently rewrite the user's choice.
    private IReadOnlyList<EqWizardCalibrationOption> BuildCalibrationOptions()
    {
        var options = new List<EqWizardCalibrationOption>
        {
            new(EqWizardCalibrationChoice.Off, "Off")
        };

        if (loadedSource is { HasOwnCalibration: true })
        {
            options.Add(new EqWizardCalibrationOption(
                EqWizardCalibrationChoice.OwnCapture, "Own (as captured)"));
        }

        // A Virtual DSP channel's correction is listed under the name its panel
        // shows for it — which may be a curve the session carries, absent from the
        // wizard's own list — so the disabled selector still says what applies.
        if (loadedSource is { Kind: EqWizardSourceKind.VirtualDspChannel, PinnedCalibration: not null } pinned)
        {
            options.Add(new EqWizardCalibrationOption(
                EqWizardCalibrationChoice.PinnedToSource,
                pinned.PinnedCalibrationName ?? "Virtual DSP"));
        }

        foreach (MicrophoneCalibrationEntry entry in calibrationEntries)
        {
            options.Add(new EqWizardCalibrationOption(
                EqWizardCalibrationChoice.Microphone(entry.Id),
                entry.Available ? entry.Name : $"{entry.Name} (unavailable)"));
        }

        if (!calibrationChoice.Own &&
            !calibrationChoice.IsOff &&
            !calibrationEntries.Any(entry => string.Equals(
                entry.Id,
                calibrationChoice.CalibrationId,
                StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new EqWizardCalibrationOption(
                calibrationChoice,
                "Deleted calibration (missing)"));
        }

        return options;
    }

    private EqWizardCalibrationChoice GetSelectedCalibration() =>
        comboBoxCalibration.SelectedItem is EqWizardCalibrationOption option
            ? option.Choice
            : EqWizardCalibrationChoice.Off;

    private void OnCalibrationChanged()
    {
        if (suppressCalibrationEvents)
        {
            return;
        }

        calibrationChoice = GetSelectedCalibration();
        // A configured choice made against an impulse response (or with nothing loaded)
        // becomes the standing IR preference; one made against a curve does not.
        preferredIrCalibrationId = EqWizardCalibration.UpdatedIrPreference(
            preferredIrCalibrationId, loadedSource?.Kind, calibrationChoice);
        InvalidateSourceCurve();
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    private sealed record EqWizardCalibrationOption(
        EqWizardCalibrationChoice Choice,
        string Label)
    {
        public override string ToString() => Label;
    }

    // --------------------------------------------------------------- sample rate

    // The rate the fitted biquads are realized at, and the one written into an exported
    // profile. An impulse response OWNS its rate — it is the measurement — so that exact
    // value is used regardless of what standard rate the (locked) selector rounds to. An
    // imported curve only suggests a rate: its capture rate need not be the rate of the
    // DSP the profile is bound for, so the selector wins there.
    private int EqSampleRate
    {
        get
        {
            // Every measurement-backed source states its own rate exactly, and the
            // filters are realized at it downstream — the gated preview filters the
            // IR at the measurement's rate, and Virtual DSP runs the returned bank at
            // the project's. Letting the combo answer instead would design and export
            // biquads for one rate while two other places realize them at another.
            if (loadedSource is
                {
                    Kind: EqWizardSourceKind.ImpulseResponse
                        or EqWizardSourceKind.VirtualDspChannel,
                    SampleRateHz: int rate
                })
            {
                return rate;
            }

            return comboBoxSampleRate.SelectedItem is int selected
                ? selected
                : manualSampleRateHz;
        }
    }

    /// <summary>
    /// How the DSP being tuned defines the Q of a peaking band. This moves the numbers
    /// on the tuning sheet ONLY: the fit, the on-screen curve and the profile-file
    /// exports all stay in the RBJ convention the library realizes, so switching it
    /// never changes the tune that was designed — just how it has to be typed in for
    /// the hardware to reproduce it.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal PeqQConvention TargetDspQConvention
    {
        get => comboBoxQConvention.SelectedItem is PeqQConvention convention
            ? convention
            : PeqQConvention.Rbj;
        set
        {
            suppressQConventionEvents = true;
            try
            {
                comboBoxQConvention.SelectedItem = value;
            }
            finally
            {
                suppressQConventionEvents = false;
            }
        }
    }

    private void InitializeQConventionComboBox()
    {
        comboBoxQConvention.Format += (_, args) =>
        {
            if (args.ListItem is PeqQConvention convention)
            {
                args.Value = PeqQConventions.DescribeShort(convention);
            }
        };
        foreach (PeqQConvention convention in SelectableQConventions)
        {
            comboBoxQConvention.Items.Add(convention);
        }

        // Selected before the handler is attached, so building the panel does not look
        // like the user changing the setting.
        comboBoxQConvention.SelectedItem = PeqQConvention.Rbj;
        comboBoxQConvention.SelectedIndexChanged += (_, _) =>
        {
            if (!suppressQConventionEvents)
            {
                RaiseSettingsChanged();
            }
        };
    }

    private void InitializeSampleRateComboBox()
    {
        comboBoxSampleRate.Format += (_, args) =>
        {
            if (args.ListItem is int rate)
            {
                args.Value = $"{rate / 1000.0:0.###} kHz";
            }
        };
        comboBoxSampleRate.SelectedIndexChanged += (_, _) => OnSampleRateChanged();
        RefreshSampleRateCombo();
    }

    private void RefreshSampleRateCombo()
    {
        int? sourceRate = loadedSource?.SampleRateHz;
        int selectRate = sourceRate ?? manualSampleRateHz;

        suppressSampleRateEvents = true;
        try
        {
            comboBoxSampleRate.Items.Clear();
            foreach (int rate in SelectableSampleRatesHz)
            {
                comboBoxSampleRate.Items.Add(rate);
            }

            // A measurement at a non-standard rate joins the list so the selector shows
            // the true rate rather than the nearest standard one — the tune must be
            // realized at exactly the rate the source states.
            if (!SelectableSampleRatesHz.Contains(selectRate))
            {
                comboBoxSampleRate.Items.Add(selectRate);
            }

            comboBoxSampleRate.SelectedItem = selectRate;
        }
        finally
        {
            suppressSampleRateEvents = false;
        }

        // A measurement is authoritative, so its rate is shown but locked — an
        // impulse response and a Virtual DSP channel alike (see EqSampleRate). An
        // imported curve only suggests one, so it stays editable for a differing DSP.
        comboBoxSampleRate.Enabled = loadedSource is not
        {
            Kind: EqWizardSourceKind.ImpulseResponse
                or EqWizardSourceKind.VirtualDspChannel
        };
    }

    private void OnSampleRateChanged()
    {
        if (suppressSampleRateEvents)
        {
            return;
        }

        // A manual pick is the user's preference and is persisted; it also becomes the
        // rate used for the current imported curve (an IR ignores it, being locked).
        if (comboBoxSampleRate.SelectedItem is int rate)
        {
            manualSampleRateHz = rate;
        }

        // Only the next fit reads the rate, but an in-flight one was computed against
        // the old value, so orphan it. The EQ response itself is rate-dependent too.
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    // ---------------------------------------------------------- persistence

    internal void ApplyPersistedSettings(MeasurementSettingsFile.EqWizardSettings settings)
    {
        suppressSettingsSave = true;
        try
        {
            // Normalized like every target that comes off disk: the settings file
            // can hold a non-finite number or an undefined enum, and this target
            // goes on to fill the settings dialog, which cannot take either.
            AssignTargetCurve(new EqTargetCurve(
                settings.Preset,
                new TargetCurveSpec(
                    settings.TiltDbPerOctave,
                    settings.BassShelfGainDb,
                    settings.BassShelfFrequencyHz,
                    settings.BassShelfWidthOctaves,
                    settings.TrebleShelfGainDb,
                    settings.TrebleShelfFrequencyHz,
                    settings.TrebleShelfWidthOctaves,
                    settings.PresenceGainDb,
                    settings.PresenceFrequencyHz,
                    settings.PresenceWidthOctaves),
                settings.ToleranceDb,
                settings.DeviationMode,
                Color.FromArgb(settings.TargetColorArgb),
                settings.TargetStrokeThickness,
                settings.TargetLineStyle,
                settings.TargetSmoothingInverseOctaves).Normalized());
            // Only the configured impulse-response preference is persisted: "own" belongs
            // to an imported curve, and no source is restored, so the effective choice
            // simply starts from that preference.
            preferredIrCalibrationId = settings.ResolveCalibrationId();
            calibrationChoice =
                EqWizardCalibrationChoice.Microphone(preferredIrCalibrationId);
            manualSampleRateHz = settings.ManualSampleRateHz > 0
                ? settings.ManualSampleRateHz
                : DefaultSampleRateHz;

            NumericTargetOffset.Value = NumericTargetOffset.ClampValue(settings.TargetOffsetDb);
            numericGainMin.Value = numericGainMin.ClampValue(settings.GainMinDb);
            numericGainMax.Value = numericGainMax.ClampValue(settings.GainMaxDb);
            checkBoxCutsOnly.Checked = settings.CutsOnly;
            SetSourceSmoothing(settings.SourceSmoothingInverseOctaves);
            ApplyPersistedBank(settings);

            InvalidateSourceCurve();
            ApplyGainRange();
            RefreshCalibrationCombo();
            RefreshSampleRateCombo();
            DrawSelectedCurves();
        }
        finally
        {
            suppressSettingsSave = false;
        }
    }

    internal MeasurementSettingsFile.EqWizardSettings CaptureSettings() => new()
    {
        Preset = targetPreset,
        TiltDbPerOctave = targetSpec.TiltDbPerOctave,
        BassShelfGainDb = targetSpec.BassShelfGainDb,
        BassShelfFrequencyHz = targetSpec.BassShelfFrequencyHz,
        BassShelfWidthOctaves = targetSpec.BassShelfWidthOctaves,
        TrebleShelfGainDb = targetSpec.TrebleShelfGainDb,
        TrebleShelfFrequencyHz = targetSpec.TrebleShelfFrequencyHz,
        TrebleShelfWidthOctaves = targetSpec.TrebleShelfWidthOctaves,
        PresenceGainDb = targetSpec.PresenceGainDb,
        PresenceFrequencyHz = targetSpec.PresenceFrequencyHz,
        PresenceWidthOctaves = targetSpec.PresenceWidthOctaves,
        ToleranceDb = targetToleranceDb,
        DeviationMode = targetDeviationMode,
        TargetColorArgb = targetColor.ToArgb(),
        TargetStrokeThickness = targetStrokeThickness,
        TargetLineStyle = targetLineStyle,
        TargetSmoothingInverseOctaves = targetSmoothingInverseOctaves,
        TargetOffsetDb = (double)NumericTargetOffset.Value,
        GainMinDb = (double)numericGainMin.Value,
        GainMaxDb = (double)numericGainMax.Value,
        Bands = CaptureBands(),
        PreampDb = (double)NumericGain.Value,
        BandCount = peqSlots.Count,
        SourceSmoothingInverseOctaves = SourceSmoothingInverseOctaves,
        CalibrationId = preferredIrCalibrationId,
        ManualSampleRateHz = manualSampleRateHz,
        CutsOnly = checkBoxCutsOnly.Checked
    };

    private void RaiseSettingsChanged()
    {
        if (!suppressSettingsSave)
        {
            SettingsChanged?.Invoke();
        }
    }

    private void SetSourceSmoothing(int inverseOctaves)
    {
        for (int i = 0; i < comboBoxSmooth.Items.Count; i++)
        {
            if (comboBoxSmooth.Items[i] is int value && value == inverseOctaves)
            {
                comboBoxSmooth.SelectedIndex = i;
                return;
            }
        }
    }

    private static OxyColor ToOxyColor(Color color) =>
        OxyColor.FromArgb(color.A, color.R, color.G, color.B);
}
