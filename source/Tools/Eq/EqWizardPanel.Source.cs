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
    // A fat analysis window so the low end reads clearly (finer LF resolution than
    // the short measurement-preview window). Zero-padded when the IR is shorter.
    private const int SourceWindow = 32768;
    private const int SourceLeftTukey = 256;
    private const int SourceRightTukey = 2048;

    private const int DefaultSampleRateHz = 48_000;

    private static readonly int[] SelectableSampleRatesHz =
        { 44_100, 48_000, 88_200, 96_000, 176_400, 192_000 };

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

    private Func<MicrophoneCalibrationMode, CalibrationFile?>? calibrationResolver;
    private bool hasZeroDegreeCalibration;
    private bool hasNinetyDegreeCalibration;
    // The effective mode of the loaded source (may be Own). Distinct from the persisted
    // impulse-response preference below: loading a curve forces this to Own/Off, which
    // must NOT overwrite what the user chose for impulse responses. See EqWizardCalibration.
    private EqWizardCalibrationMode calibrationMode = EqWizardCalibrationMode.Off;
    // The user's standing file-backed choice for impulse responses; the only one persisted.
    private MicrophoneCalibrationMode preferredIrCalibrationMode = MicrophoneCalibrationMode.Off;
    private bool suppressCalibrationEvents;
    private bool suppressSampleRateEvents;
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
        sourceMenu.Show(buttonLoadIr, new Point(0, buttonLoadIr.Height));
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
                ToolTipText = entry.Metadata.BuildToolTipText(entry.Timestamp)
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
                ToolTipText = slot.Description
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
        loadedSource = source;

        // Settle every selector that feeds the curve, fit the axis, and place the target
        // before drawing, all with redraws suppressed: the axis fit and the offset both
        // need the source curve, so this computes it once (cached) instead of once per
        // side effect, and the single draw at the end paints the finished state.
        suppressRedraw = true;
        try
        {
            calibrationMode = ChooseCalibrationMode(source);
            comboBoxSmooth.Enabled = source.SupportsSmoothing;
            PopulateCalibrationCombo();
            RefreshSampleRateCombo();
            InvalidateSourceCurve();

            ApplyAxisForSource();
            SuggestTargetOffset();
        }
        finally
        {
            suppressRedraw = false;
        }

        buttonLoadIr.Text = source.DisplayName;
        toolTip.SetToolTip(
            buttonLoadIr,
            $"{source.Description}\r\nClick to load another source.");

        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    // The calibration a freshly loaded source starts on:
    //  - a curve that stored its own correction defaults to reproducing it (Own);
    //  - an impulse response restores the user's standing file-backed preference,
    //    regardless of what a previously loaded curve forced the effective mode to;
    //  - a curve with no uncalibrated reference cannot be re-calibrated at all (Off).
    private EqWizardCalibrationMode ChooseCalibrationMode(EqWizardCurveSource source)
    {
        if (source.RawSpectrum != null)
        {
            return EqWizardCalibrationMode.Own;
        }
        if (source.Kind == EqWizardSourceKind.ImpulseResponse)
        {
            return EqWizardCalibration.FromMicrophoneMode(preferredIrCalibrationMode);
        }

        return EqWizardCalibrationMode.Off;
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

    private void InvalidateSourceCurve() => sourceCurveDirty = true;

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
        // Only a file-backed calibration can be applied while computing an FR; "own"
        // belongs to an imported curve and never reaches here.
        MicrophoneCalibrationMode mode = EqWizardCalibration.ToMicrophoneMode(calibrationMode);
        var options = new FrequencyResponseOptions
        {
            Window = SourceWindow,
            LeftTukeyWindow = SourceLeftTukey,
            RightTukeyWindow = SourceRightTukey,
            SmoothingInverseOctaves = SourceSmoothingInverseOctaves,
            Offset = 0,
            CalibrationMode = mode
        };
        CalibrationFile? calibration = mode == MicrophoneCalibrationMode.Off
            ? null
            : calibrationResolver?.Invoke(mode);

        IReadOnlyList<AnalysisCurve> curves = DataHelper.GetSpectrum(
            source.Measurement!, options, calibration, SpectrumCurves.Primary);
        return curves.Count > 0 ? curves[0].Points : Array.Empty<SignalPoint>();
    }

    // An imported curve is already a finished response. When its uncalibrated reference
    // was stored it is re-rendered exactly the way the mode it came from would — same
    // resampler, so the mode's smoothing reproduces the on-screen reference. Without that
    // reference (a dB SPL RTA, a text curve) the stored display points are the only
    // truth and are used as-is.
    private IReadOnlyList<SignalPoint> ComputeImportedCurve(EqWizardCurveSource source)
    {
        if (source.RawSpectrum is not { Count: >= 2 } raw)
        {
            return source.Points;
        }

        return RawCurveRenderer.Render(
            raw,
            ResolveCurveCalibrationCorrection(source),
            SourceSmoothingInverseOctaves);
    }

    // The correction subtracted after smoothing: none, the one frozen at capture, or a
    // configured profile re-frozen on the same output grid.
    private IReadOnlyList<double> ResolveCurveCalibrationCorrection(
        EqWizardCurveSource source) => calibrationMode switch
        {
            EqWizardCalibrationMode.Own => source.OwnCalibrationCorrectionDb,
            EqWizardCalibrationMode.Degrees0 or EqWizardCalibrationMode.Degrees90 =>
                RawCurveRenderer.CaptureCalibrationCorrection(
                    calibrationResolver?.Invoke(
                        EqWizardCalibration.ToMicrophoneMode(calibrationMode))),
            _ => Array.Empty<double>()
        };

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
            ToOxyLineStyle(targetLineStyle),
            points);
    }

    private EqWizardCurve BuildSourcePlusEqCurve(
        IReadOnlyList<DataPoint> sourcePoints,
        EqualizationCurve eq)
    {
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
        EqWizardAxisRange range = loadedSource is { Measurement: null }
            ? EqWizardPlotFit.ForCurve(
                GetSourceCurve()?.Points.Select(point => new SignalPoint(point.X, point.Y))
                    ?? Enumerable.Empty<SignalPoint>())
            : EqWizardPlotFit.ImpulseResponseRange;

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

    // Lands the target on the freshly loaded source's own level. Most important for an
    // absolute (SPL) curve, which starts tens of dB from a relative target — but every
    // source needs it: it also clears the previous source's offset, so switching from an
    // 80 dB SPL curve back to a near-0 dB impulse response cannot strand the target high.
    private void SuggestTargetOffset()
    {
        if (GetSourceCurve() is not { Points.Count: >= 2 } source)
        {
            return;
        }

        (double minHz, double maxHz) = GetFrequencyWindow();
        double offset = EqWizardPlotFit.SuggestTargetOffsetDb(
            source.Points.Select(point => new SignalPoint(point.X, point.Y)),
            targetSpec.Evaluate,
            minHz,
            maxHz);
        NumericTargetOffset.Value = NumericTargetOffset.ClampValue(offset);
    }

    // ---------------------------------------------------------------- target

    private void OnTargetOffsetChanged()
    {
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    // Reuses the overlay target dialog in isolated mode (no source picker, no
    // overlay side effects); its live preview redraws the wizard plot. Cancel
    // reverts the previewed changes.
    private void OpenTargetSettings()
    {
        (TargetPreset preset,
            TargetCurveSpec spec,
            double tolerance,
            TargetDeviationMode deviation,
            Color color,
            double thickness,
            OverlayLineStyle style,
            int smoothing) before = (
            targetPreset, targetSpec, targetToleranceDb, targetDeviationMode,
            targetColor, targetStrokeThickness, targetLineStyle, targetSmoothingInverseOctaves);

        using var dialog = new OverlayTargetSettingsDialog(
            Mode.EqWizard,
            "EQ target",
            0,
            targetPreset,
            targetSpec,
            targetToleranceDb,
            targetDeviationMode,
            targetColor,
            targetStrokeThickness,
            targetLineStyle,
            100,
            targetSmoothingInverseOctaves,
            Array.Empty<OverlaySlotOption>(),
            ApplyTargetPreview,
            isolatedTarget: true);

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            targetPreset = before.preset;
            targetSpec = before.spec;
            targetToleranceDb = before.tolerance;
            targetDeviationMode = before.deviation;
            targetColor = before.color;
            targetStrokeThickness = before.thickness;
            targetLineStyle = before.style;
            targetSmoothingInverseOctaves = before.smoothing;
            DrawSelectedCurves();
            return;
        }

        targetPreset = dialog.Preset;
        targetSpec = dialog.Spec;
        targetToleranceDb = dialog.ToleranceDb;
        targetDeviationMode = dialog.DeviationMode;
        targetColor = dialog.SelectedColor;
        targetStrokeThickness = dialog.StrokeThickness;
        targetLineStyle = dialog.LineStyle;
        targetSmoothingInverseOctaves = dialog.SmoothingInverseOctaves;
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
        Func<MicrophoneCalibrationMode, CalibrationFile?> resolver,
        bool hasZeroDegree,
        bool hasNinetyDegree)
    {
        calibrationResolver = resolver;
        hasZeroDegreeCalibration = hasZeroDegree;
        hasNinetyDegreeCalibration = hasNinetyDegree;
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
                    option.Mode == calibrationMode)
                {
                    index = i;
                    break;
                }
            }

            // BuildCalibrationOptions always yields at least "Off" and always includes
            // the current mode, so index is found; the 0 fallback is only for the
            // impossible empty list.
            comboBoxCalibration.SelectedIndex = index >= 0 ? index : 0;
            comboBoxCalibration.Enabled =
                comboBoxCalibration.Items.Count > 1 &&
                (loadedSource?.SupportsCalibration ?? true);
        }
        finally
        {
            suppressCalibrationEvents = false;
        }

        calibrationMode = GetSelectedCalibrationMode();
    }

    // Off and the configured profiles are always offered; "own" only exists for a curve
    // that stored the correction it was captured with.
    private IReadOnlyList<EqWizardCalibrationOption> BuildCalibrationOptions()
    {
        var options = new List<EqWizardCalibrationOption>
        {
            new(EqWizardCalibrationMode.Off, "Off")
        };

        if (loadedSource is { RawSpectrum: not null })
        {
            options.Add(new EqWizardCalibrationOption(
                EqWizardCalibrationMode.Own, "Own (as captured)"));
        }
        if (hasZeroDegreeCalibration || calibrationMode == EqWizardCalibrationMode.Degrees0)
        {
            options.Add(new EqWizardCalibrationOption(
                EqWizardCalibrationMode.Degrees0,
                hasZeroDegreeCalibration ? "0 degrees" : "0 degrees (file missing)"));
        }
        if (hasNinetyDegreeCalibration || calibrationMode == EqWizardCalibrationMode.Degrees90)
        {
            options.Add(new EqWizardCalibrationOption(
                EqWizardCalibrationMode.Degrees90,
                hasNinetyDegreeCalibration ? "90 degrees" : "90 degrees (file missing)"));
        }

        return options;
    }

    private EqWizardCalibrationMode GetSelectedCalibrationMode() =>
        comboBoxCalibration.SelectedItem is EqWizardCalibrationOption option
            ? option.Mode
            : EqWizardCalibrationMode.Off;

    private void OnCalibrationChanged()
    {
        if (suppressCalibrationEvents)
        {
            return;
        }

        calibrationMode = GetSelectedCalibrationMode();
        // A file-backed choice made against an impulse response (or with nothing loaded)
        // becomes the standing IR preference; one made against a curve does not.
        preferredIrCalibrationMode = EqWizardCalibration.UpdatedIrPreference(
            preferredIrCalibrationMode, loadedSource?.Kind, calibrationMode);
        InvalidateSourceCurve();
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    private sealed record EqWizardCalibrationOption(
        EqWizardCalibrationMode Mode,
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
            if (loadedSource is { Kind: EqWizardSourceKind.ImpulseResponse, SampleRateHz: int rate })
            {
                return rate;
            }

            return comboBoxSampleRate.SelectedItem is int selected
                ? selected
                : manualSampleRateHz;
        }
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

        // An impulse response is authoritative, so its rate is shown but locked; an
        // imported curve only suggests one, so it stays editable for a differing DSP.
        comboBoxSampleRate.Enabled =
            loadedSource is not { Kind: EqWizardSourceKind.ImpulseResponse };
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
            targetPreset = settings.Preset;
            targetSpec = new TargetCurveSpec(
                settings.TiltDbPerOctave,
                settings.BassShelfGainDb,
                settings.BassShelfFrequencyHz,
                settings.BassShelfWidthOctaves,
                settings.TrebleShelfGainDb,
                settings.TrebleShelfFrequencyHz,
                settings.TrebleShelfWidthOctaves,
                settings.PresenceGainDb,
                settings.PresenceFrequencyHz,
                settings.PresenceWidthOctaves);
            targetToleranceDb = settings.ToleranceDb;
            targetDeviationMode = settings.DeviationMode;
            targetColor = Color.FromArgb(settings.TargetColorArgb);
            targetStrokeThickness = settings.TargetStrokeThickness;
            targetLineStyle = settings.TargetLineStyle;
            targetSmoothingInverseOctaves = settings.TargetSmoothingInverseOctaves;
            // Only the file-backed impulse-response preference is persisted: "own" belongs
            // to an imported curve, and no source is restored, so the effective mode simply
            // starts from that preference.
            preferredIrCalibrationMode = settings.CalibrationMode;
            calibrationMode =
                EqWizardCalibration.FromMicrophoneMode(preferredIrCalibrationMode);
            manualSampleRateHz = settings.ManualSampleRateHz > 0
                ? settings.ManualSampleRateHz
                : DefaultSampleRateHz;

            NumericTargetOffset.Value = NumericTargetOffset.ClampValue(settings.TargetOffsetDb);
            numericGainMin.Value = numericGainMin.ClampValue(settings.GainMinDb);
            numericGainMax.Value = numericGainMax.ClampValue(settings.GainMaxDb);
            checkBoxCutsOnly.Checked = settings.CutsOnly;
            SetSourceSmoothing(settings.SourceSmoothingInverseOctaves);
            darkComboBoxBands.SelectedIndex =
                Math.Clamp(settings.BandCount, 1, MaxPeqSlotCount) - 1;

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
        BandCount = Math.Clamp(activeBandCount, 1, MaxPeqSlotCount),
        SourceSmoothingInverseOctaves = SourceSmoothingInverseOctaves,
        CalibrationMode = preferredIrCalibrationMode,
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

    private static LineStyle ToOxyLineStyle(OverlayLineStyle style) =>
        Enum.TryParse(style.ToString(), out LineStyle parsed) ? parsed : LineStyle.Solid;
}
