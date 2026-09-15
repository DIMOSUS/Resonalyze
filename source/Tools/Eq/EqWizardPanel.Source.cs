using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.History;
using Resonalyze.Options;

namespace Resonalyze;

// The wizard owns its source and target. An imported curve is a SNAPSHOT: no link back to its slot, history entry or file.
public partial class EqWizardPanel
{
    private const int DefaultSampleRateHz = 48_000;

    private static readonly IReadOnlyList<int> SelectableSampleRatesHz =
        DspProcessorCatalog.SelectableSampleRatesHz;

    private static readonly IReadOnlyList<PeqQConvention> SelectableQConventions =
        DspProcessorCatalog.SelectableQConventions;

    private const string NoSourceHint =
        "Load a source to equalize — an impulse response, a moving-mic capture,\n" +
        "or a measured curve from an overlay slot or a text file.\n" +
        "Use Target… to shape the goal curve.";

    private static readonly OxyColor SourceCurveColor = OxyColor.FromRgb(180, 190, 205);
    private static readonly OxyColor SourcePlusEqColor = OxyColor.FromRgb(0, 209, 255);

    private static readonly double[] DefaultTargetGrid =
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 512).ToArray();

    private readonly EqWizardSourceResolver sourceResolver = new();
    private readonly EqWizardPreviewOrchestrator previewOrchestrator = new();
    private EqWizardCurveSource? loadedSource;
    private EqWizardCurve? cachedSourceCurve;
    private bool sourceCurveDirty = true;
    private int sourceLoadGeneration;
    private ContextMenuStrip? sourceMenu;
    private ContextMenuStrip? targetMenu;

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
    // Effective choice for the loaded source; loading a curve forces Own/Off without touching the persisted IR preference.
    private EqWizardCalibrationChoice calibrationChoice = EqWizardCalibrationChoice.Off;
    private string? preferredIrCalibrationId;
    private bool suppressCalibrationEvents;
    private bool suppressSampleRateEvents;
    private bool suppressQConventionEvents;
    private bool suppressSettingsSave;
    private int manualSampleRateHz = DefaultSampleRateHz;
    // The user's own convention, kept apart from the one a Virtual DSP handoff forces on the selector.
    private PeqQConvention manualQConvention = PeqQConvention.Rbj;

    internal event Action? SettingsChanged;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal MeasurementHistoryService? HistoryService { get; set; }

    // Rebuilt on every click: history and overlay slots change while the panel is open.
    private void ShowSourceMenu()
    {
        if (sourceMenu is { Visible: true })
        {
            sourceMenu.Close();
            return;
        }

        sourceMenu?.Dispose();
        sourceMenu = BuildSourceMenu();
        DropDownMenu.ShowUnder(buttonSource, sourceMenu);
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

        menu.Items.Add(
            // Moving-mic captures and mic-array measurements are both spatial averages; this entry takes either.
            "Curve from spatial average…",
            null,
            (_, _) => _ = LoadCurveFromSpatialAverageAsync());
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
                // ToolStrip draws item tooltips itself (no app wrapping), and a description can carry a full path.
                ToolTipText = ToolTipTextWrapper.Wrap(slot.Description)
            };
            item.Click += (_, _) => LoadCurveFromSlot(slot.Slot);
            slotItem.DropDownItems.Add(item);
        }
    }

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

        // A slow earlier load must not overwrite a newer selection when it lands.
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

        ApplyMeasurementSource(
            file,
            System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            $"Impulse response: {dialog.FileName}",
            EqWizardSourceResolver.DescribeArray(file, dialog.FileName));
    }

    /// <summary>
    /// Applies a measurement as a source, offering (not forcing) its microphone array first when it carries one;
    /// an average has no IR, so substituting it silently would also drop the gate preview.
    /// </summary>
    private void ApplyMeasurementSource(
        ImpulseResponseFile file,
        string displayName,
        string description,
        string arrayDescription)
    {
        EqWizardCurveSource? array =
            EqWizardSourceResolver.TryCreateFromArray(file, displayName, arrayDescription);
        if (array != null && AskToEqualizeArray(file))
        {
            ApplySource(array);
            return;
        }

        ApplySource(EqWizardSourceResolver.CreateFromImpulseResponse(
            file, displayName, description));
    }

    private bool AskToEqualizeArray(ImpulseResponseFile file)
    {
        int count = file.ArrayMicrophones?.Microphones.Count ?? 0;
        string positions = count == 1 ? "1 position" : $"{count} positions";
        return MessageBox.Show(
            FindForm(),
            $"This measurement was recorded with a microphone array of {positions}." +
                Environment.NewLine + Environment.NewLine +
                "Equalize the array's average over the listening volume, rather than " +
                "the response measured at the one position its impulse response came " +
                "from?" + Environment.NewLine + Environment.NewLine +
                "The average is the shape a tune belongs on: a single position " +
                "carries dips that are a property of its own few centimetres. The " +
                "point measurement keeps the gate preview; the average has no " +
                "impulse response behind it.",
            "EQ Wizard",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1) == DialogResult.Yes;
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

        // Deleted between opening the menu and choosing: silent no-op, like the Compare picker.
        if (snapshot == null)
        {
            return;
        }

        ImpulseResponseFile file = snapshot.ToImpulseResponseFile();
        ApplyMeasurementSource(
            file,
            displayName,
            $"History: {displayName}",
            EqWizardSourceResolver.DescribeArray(file, $"History: {displayName}"));
    }

    private void LoadCurveFromSlot(int slot)
    {
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

    private async Task LoadCurveFromSpatialAverageAsync()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter =
                "Spatial average (*.json)|*.json|All files (*.*)|*.*",
            Title = "Load spatial average"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        int generation = ++sourceLoadGeneration;
        EqWizardCurveSource? source;
        try
        {
            source = await ResolveSpatialAverageAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            if (generation == sourceLoadGeneration && !IsDisposed)
            {
                ShowFileError("The spatial average could not be loaded.", exception);
            }

            return;
        }

        if (generation != sourceLoadGeneration || IsDisposed)
        {
            return;
        }

        if (source == null)
        {
            MessageBox.Show(
                FindForm(),
                "That file carries no spatial average. Load a moving-microphone " +
                    "capture, or a measurement recorded with a microphone array.",
                "EQ Wizard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplySource(source);
    }

    private static async Task<EqWizardCurveSource?> ResolveSpatialAverageAsync(string path)
    {
        if (LiveCaptureDocument.TryLoad(path, out LiveCaptureDocument document))
        {
            return EqWizardSourceResolver.CreateFromSpatialAverage(
                document,
                EqWizardSourceResolver.DescribeSpatialAverage(document, path));
        }

        ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(path);
        return EqWizardSourceResolver.TryCreateFromArray(
            file,
            System.IO.Path.GetFileNameWithoutExtension(path),
            EqWizardSourceResolver.DescribeArray(file, path));
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

    private void ApplySource(EqWizardCurveSource source)
    {
        // Ends any handoff so Return never sends a bank tuned against another curve; a handoff re-establishes it after.
        EndVirtualDspHandoff();
        loadedSource = source;
        // Before drawing: a phase window left from the previous source would open on an arrival this one lacks.
        SeedPhaseContext(source);

        // Settle selectors and axis with redraws suppressed. Target Level is deliberately untouched: it is the user's knob.
        suppressRedraw = true;
        try
        {
            calibrationChoice = ChooseCalibration(source);
            comboBoxSmooth.Enabled = source.SupportsSmoothing;
            PopulateCalibrationCombo();
            RefreshSampleRateCombo();
            RefreshQConventionCombo();
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

    // See docs/tech/eq-auto-tuner.md#calibration-choice.
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
        // A handoff is pinned to the correction its panel renders with; the IR preference stays untouched.
        if (source.Kind == EqWizardSourceKind.VirtualDspChannel)
        {
            // Pinned whenever the panel pinned ANY correction, curve or mode (the curve alone misses the average's own correction).
            return source.PinsCorrection
                ? EqWizardCalibrationChoice.PinnedToSource
                : EqWizardCalibrationChoice.Off;
        }

        return EqWizardCalibrationChoice.Off;
    }

    // Cached: the FFT changes only with source, smoothing or calibration, never with band/fader/target edits.
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
        InvalidateGatedPreview();
    }

    // Captured on the UI thread so the render touches no control.
    private EqWizardGatedPreviewRequest BuildGatedPreviewRequest(
        EqWizardCurveSource source, EqualizationCurve? bank) =>
        new(
            source.PreviewImpulseResponse!,
            source.PreviewChain!,
            bank,
            source.Measurement!.PeakIndex,
            source.Measurement.SampleRate,
            EqProcessorSampleRate,
            source.GateSettings!,
            ResolveChosenCalibration(),
            SourceSmoothingInverseOctaves,
            new MeasuredBand(
                source.Measurement.LowestMeasuredFrequencyHz,
                source.Measurement.HighestMeasuredFrequencyHz));

    /// <summary>How a stored spatial average is read; a capture's "Own" is its own correction, not the IR's beside it.</summary>
    private SpatialAverageCalibration ResolveSpatialAverageCalibration(
        EqWizardCurveSource source) =>
        calibrationChoice.Own ? SpatialAverageCalibration.Own
        : calibrationChoice.Pinned ? source.SpatialAverageCalibration
        : calibrationChoice.IsOff ? SpatialAverageCalibration.Off
        : SpatialAverageCalibration.Specific(ResolveChosenCalibration());

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

        // A spatial average IS the magnitude when present (the IR only feeds phase). Stored curves keep NaN gaps for the fitter.
        IReadOnlyList<SignalPoint> points =
            source.SpatialAverage != null ? ComputeSpatialAverageCurve(source)
            : source.Measurement != null ? ComputeImpulseResponseSpectrum(source)
            : ComputeImportedCurve(source);
        return BuildSourceCurve(points, KeepsGaps(source));
    }

    /// <summary>
    /// Channel magnitude from its spatial average through its chain, with the edited bank substituted INTO the chain
    /// (smoothing does not commute with the bank). See docs/tech/eq-auto-tuner.md#spatial-average-sources.
    /// </summary>
    private IReadOnlyList<SignalPoint> ComputeSpatialAverageCurve(
        EqWizardCurveSource source,
        EqualizationCurve? bank = null)
    {
        LiveCaptureDocument document = source.SpatialAverage!;
        List<double> grid = document.ToCurvePoints()
            .Select(point => point.X)
            .ToList();
        List<SignalPoint>? curve = SpatialAverageHybrid.BuildChannelCurve(
            document,
            (source.PreviewChain ?? DspChannelChain.Identity) with { Peq = bank },
            EqProcessorSampleRate,
            // Pinned to the panel's calibration MODE, not only its curve, like every part of a handoff.
            ResolveSpatialAverageCalibration(source),
            grid,
            SourceSmoothingInverseOctaves);
        if (curve == null)
        {
            return Array.Empty<SignalPoint>();
        }

        // The set's scalar offset last, so the curve hangs where the panel plotted it and Target Level means the same.
        double offset = source.SpatialAverageOffsetDb;
        return offset == 0
            ? curve
            : curve.Select(point => new SignalPoint(point.X, point.Y + offset)).ToList();
    }

    private IReadOnlyList<SignalPoint> ComputeImpulseResponseSpectrum(
        EqWizardCurveSource source)
    {
        // Only a configured calibration applies to a computed FR; "own" belongs to imported curves.
        string? calibrationId = calibrationChoice.MicrophoneCalibrationId;

        // Same DataHelper call, template and offset as the DSP panel's magnitude view; the bare curve is the no-bank path.
        if (source.IsGated)
        {
            return EqWizardGatedPreview.Render(BuildGatedPreviewRequest(source, bank: null));
        }

        // The Virtual DSP steady-state window (ms), realised in samples at this rate; zero-padded when the IR is shorter.
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

    // See docs/tech/eq-auto-tuner.md#imported-curve-calibration.
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
            SourceSmoothingInverseOctaves,
            source.RawSpectrumBand);
    }

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

    private static EqWizardCurve? BuildSourceCurve(
        IReadOnlyList<SignalPoint> points,
        bool keepGaps)
    {
        List<DataPoint> result = ToPlotPoints(points, keepGaps);
        return result.Count >= 2
            ? new EqWizardCurve("Source", SourceCurveColor, 1.5, LineStyle.Solid, result)
            : null;
    }

    /// <summary>
    /// The single conversion to plot points: curves are paired BY INDEX (target, shading, fit), so every render must
    /// keep or drop the same gaps. See docs/tech/eq-auto-tuner.md#index-aligned-curves.
    /// </summary>
    private static List<DataPoint> ToPlotPoints(
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

        return result;
    }

    // Measured curves keep NaN gaps (untrusted bands); a computed FR drops non-finite values. Decided per SOURCE, not call site.
    private static bool KeepsGaps(EqWizardCurveSource source) =>
        source.SpatialAverage != null || source.Measurement == null;

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
        // Filtered THEN windowed (they do not commute; several dB in the bass). Too heavy per frame, so it renders async.
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

        // Bank substituted inside the chain, not added after smoothing; same builder keeps points aligned by index.
        if (loadedSource is { SpatialAverage: not null } average)
        {
            List<DataPoint> corrected = ToPlotPoints(
                ComputeSpatialAverageCurve(average, eq), KeepsGaps(average));
            return corrected.Count >= 2
                ? new EqWizardCurve(
                    "Source + EQ", SourcePlusEqColor, 2, LineStyle.Solid, corrected)
                : null;
        }

        var points = new DataPoint[sourcePoints.Count];
        for (int i = 0; i < sourcePoints.Count; i++)
        {
            DataPoint point = sourcePoints[i];
            points[i] = new DataPoint(
                point.X,
                point.Y + DigitalEqualizationResponse.MagnitudeDbAt(
                    eq, point.X, EqProcessorSampleRate));
        }

        return new EqWizardCurve("Source + EQ", SourcePlusEqColor, 2, LineStyle.Solid, points);
    }

    // Kept on screen while a newer render is in flight, so the curve does not strobe.
    private IReadOnlyList<DataPoint>? landedGatedPreview;
    private PeqBankState? landedGatedPreviewBank;
    private bool gatedPreviewInFlight;

    /// <summary>Gated previews start only once visible (see <see cref="RequestGatedPreview"/>).</summary>
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
        // Phase view reads the same measurement and chain, neighbours included.
        InvalidatePhaseCurves();
    }

    // The bank is the identity: redraws for the same filters must not re-run the transforms.
    private void RequestGatedPreview(EqWizardCurveSource source, EqualizationCurve eq)
    {
        // Not before the handle exists: a handoff installs while hidden, and a render landing in the creation pump draws into a half-created control.
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
        _ = RenderGatedPreviewAsync(request, bank, KeepsGaps(source));
    }

    private async Task RenderGatedPreviewAsync(
        EqWizardGatedPreviewRequest request, PeqBankState bank, bool keepGaps)
    {
        try
        {
            IReadOnlyList<SignalPoint>? points =
                await previewOrchestrator.RenderLatestAsync(request);
            if (IsDisposed || !IsHandleCreated || points == null)
            {
                return;
            }

            // Same conversion as the bare curve, so both keep the same points (see ToPlotPoints).
            landedGatedPreview = ToPlotPoints(points, keepGaps);
            landedGatedPreviewBank = bank;
        }
        catch (Exception exception)
        {
            // A failed preview leaves the curve as it was; the bank stays exportable.
            System.Diagnostics.Debug.WriteLine($"EQ Wizard preview failed: {exception}");
        }
        finally
        {
            gatedPreviewInFlight = false;
        }

        if (!IsDisposed && IsHandleCreated)
        {
            DrawSelectedCurves();
        }
    }

    private void UpdateSourceHint()
    {
        hintAnnotation.Text = loadedSource == null
            ? NoSourceHint
            : PhaseMode ? PhaseModeHint() : string.Empty;
    }

    // An imported dB SPL curve sits near 80 dB, outside the IR bounds, which are ABSOLUTE limits.
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

        axis.AbsoluteMinimum = double.NegativeInfinity;
        axis.AbsoluteMaximum = double.PositiveInfinity;
        axis.Minimum = range.Minimum;
        axis.Maximum = range.Maximum;
        axis.AbsoluteMinimum = range.AbsoluteMinimum;
        axis.AbsoluteMaximum = range.AbsoluteMaximum;
        axis.Title = splCurve ? "dB SPL" : "dB";
        axis.Reset();
    }

    private EqWizardAxisRange ComputeAxisRangeForSource() =>
        loadedSource is { Measurement: null }
            ? EqWizardPlotFit.ForCurve(
                GetSourceCurve()?.Points.Select(point => new SignalPoint(point.X, point.Y))
                    ?? Enumerable.Empty<SignalPoint>())
            : EqWizardPlotFit.ImpulseResponseRange;

    private void OnTargetOffsetChanged()
    {
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    /// <summary>The target as one value; the host shares it with the Virtual DSP tool, which edits it back via <see cref="ApplyTargetCurve"/>.</summary>
    internal EqTargetCurve TargetCurve => new(
        targetPreset,
        targetSpec,
        targetToleranceDb,
        targetDeviationMode,
        targetColor,
        targetStrokeThickness,
        targetLineStyle,
        targetSmoothingInverseOctaves);

    /// <summary>Takes a target edited elsewhere; ignores an equal value so the host can push on every change without looping.</summary>
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

    private void ShowTargetMenu()
    {
        if (targetMenu is { Visible: true })
        {
            targetMenu.Close();
            return;
        }

        targetMenu?.Dispose();
        targetMenu = TargetCurveMenu.Build(
            targetSpec.Imported,
            OpenTargetSettings,
            ImportTargetCurve);
        DropDownMenu.ShowUnder(buttonOverlaySettings, targetMenu);
    }

    private void ImportTargetCurve()
    {
        if (TargetCurveImport.Prompt(FindForm()) is not { } imported)
        {
            return;
        }

        ApplyTargetCurve(TargetCurve with
        {
            Spec = targetSpec with { Imported = imported }
        });
    }

    // Isolated overlay target dialog; Cancel reverts the preview. An imported curve rides as a preset entry so edits keep it.
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

    // No redraw here, so ApplySource computes the curve and fits the axis once.
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

            comboBoxCalibration.SelectedIndex = index >= 0 ? index : 0;
            comboBoxCalibration.Enabled =
                comboBoxCalibration.Items.Count > 1 &&
                (loadedSource?.SupportsCalibration ?? true);
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

    // Entries resolving to nothing, and a selection the list lost, stay listed: dropping them would rewrite the user's choice.
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

        // Listed under the panel's name for it (may be a session curve absent from the wizard's list).
        if (loadedSource is { Kind: EqWizardSourceKind.VirtualDspChannel, PinsCorrection: true } pinned)
        {
            options.Add(new EqWizardCalibrationOption(
                EqWizardCalibrationChoice.PinnedToSource,
                pinned.PinnedCalibrationName ??
                    (pinned.SpatialAverageCalibration.Mode == SpatialAverageCalibrationMode.Own
                        ? "Own (as measured)"
                        : "Virtual DSP")));
        }

        // An aggregate (multi-mic) correction offers only Own and Off: one mic's file would apply to positions not read through it.
        if (loadedSource is not { CalibrationIsAggregate: true })
        {
            foreach (MicrophoneCalibrationEntry entry in calibrationEntries)
            {
                options.Add(new EqWizardCalibrationOption(
                    EqWizardCalibrationChoice.Microphone(entry.Id),
                    entry.Available ? entry.Name : $"{entry.Name} (unavailable)"));
            }
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

    // Rate the biquads are REALISED at: the processor's, independent of the measurement's. See docs/tech/eq-auto-tuner.md#processor-rate-and-q-convention.
    private int EqProcessorSampleRate
    {
        get
        {
            if (loadedSource?.ProcessorProfile is { } profile)
            {
                return profile.SampleRateHz;
            }

            return comboBoxSampleRate.SelectedItem is int selected
                ? selected
                : manualSampleRateHz;
        }
    }

    /// <summary>
    /// The DSP's peaking-band Q convention. Moves the tuning-sheet numbers ONLY; fit, plot and profile exports stay RBJ.
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
            manualQConvention = value;
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

    /// <summary>The user's selected convention (persisted); a handoff's processor convention must never be saved over it.</summary>
    internal PeqQConvention ManualQConvention => manualQConvention;

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

        // Selected before the handler is attached, so construction is not a user change.
        comboBoxQConvention.SelectedItem = PeqQConvention.Rbj;
        comboBoxQConvention.SelectedIndexChanged += (_, _) =>
        {
            if (!suppressQConventionEvents)
            {
                manualQConvention = TargetDspQConvention;
                RaiseSettingsChanged();
            }
        };
    }

    // A handoff locks the convention to its project's processor, like the rate.
    private void RefreshQConventionCombo()
    {
        PeqQConvention? fromProcessor = loadedSource?.ProcessorProfile?.QConvention;
        suppressQConventionEvents = true;
        try
        {
            comboBoxQConvention.SelectedItem = fromProcessor ?? manualQConvention;
        }
        finally
        {
            suppressQConventionEvents = false;
        }

        comboBoxQConvention.Enabled = fromProcessor == null;
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
        int selectRate =
            loadedSource?.ProcessorProfile?.SampleRateHz ?? manualSampleRateHz;

        suppressSampleRateEvents = true;
        try
        {
            comboBoxSampleRate.Items.Clear();
            foreach (int rate in SelectableSampleRatesHz)
            {
                comboBoxSampleRate.Items.Add(rate);
            }

            // A non-standard processor rate joins the list: the tune must be realised at exactly that rate.
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

        comboBoxSampleRate.Enabled = loadedSource?.ProcessorProfile == null;
    }

    private void OnSampleRateChanged()
    {
        if (suppressSampleRateEvents)
        {
            return;
        }

        if (comboBoxSampleRate.SelectedItem is int rate)
        {
            manualSampleRateHz = rate;
        }

        // Orphan any in-flight fit: it was computed at the old rate.
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    internal void ApplyPersistedSettings(MeasurementSettingsFile.EqWizardSettings settings)
    {
        suppressSettingsSave = true;
        try
        {
            // Normalised: the settings file may hold non-finite numbers or undefined enums the dialog cannot take.
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
                    settings.PresenceWidthOctaves)
                {
                    // The importer returns no shape for anything unreadable.
                    Imported = ImportedTargetCurve.FromStorage(
                        settings.TargetImportedName,
                        settings.TargetImportedCurve)
                },
                settings.ToleranceDb,
                settings.DeviationMode,
                Color.FromArgb(settings.TargetColorArgb),
                settings.TargetStrokeThickness,
                settings.TargetLineStyle,
                settings.TargetSmoothingInverseOctaves).Normalized());
            // Only the configured IR preference persists; no source is restored.
            preferredIrCalibrationId = settings.ResolveCalibrationId();
            calibrationChoice =
                EqWizardCalibrationChoice.Microphone(preferredIrCalibrationId);
            manualSampleRateHz = settings.ManualSampleRateHz > 0
                ? settings.ManualSampleRateHz
                : DefaultSampleRateHz;

            NumericTargetOffset.Value = NumericTargetOffset.ClampValue(settings.TargetOffsetDb);
            numericGainMin.Value = numericGainMin.ClampValue(settings.GainMinDb);
            numericGainMax.Value = numericGainMax.ClampValue(settings.GainMaxDb);
            numericQMax.Value = numericQMax.ClampValue(settings.AutoTuneMaxQ);
            checkBoxCutsOnly.Checked = settings.CutsOnly;
            checkBoxShelves.Checked = settings.AllowShelves;
            checkBoxEqCurve.Checked = settings.ShowEqCurve;
            SetSourceSmoothing(settings.SourceSmoothingInverseOctaves);
            ApplyPersistedBank(settings);

            InvalidateSourceCurve();
            ApplyGainRange();
            RefreshCalibrationCombo();
            RefreshSampleRateCombo();
            RefreshQConventionCombo();
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
        TargetImportedName = targetSpec.Imported?.Name,
        TargetImportedCurve = targetSpec.Imported?.ToStorage(),
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
        CutsOnly = checkBoxCutsOnly.Checked,
        AllowShelves = checkBoxShelves.Checked,
        AutoTuneMaxQ = (double)numericQMax.Value,
        ShowEqCurve = checkBoxEqCurve.Checked
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
