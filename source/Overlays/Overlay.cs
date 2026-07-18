using System.Diagnostics;
using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;
using Button = System.Windows.Forms.Button;
using CheckBox = System.Windows.Forms.CheckBox;
using ToolTip = System.Windows.Forms.ToolTip;

namespace Resonalyze;

public sealed class OverlayCollection
{
    private readonly List<Overlay> overlays = new();
    private readonly Action notifyPlotChanged;
    private Func<MagnitudeScale>? getCurrentMagnitudeScale;

    public OverlayCollection(
        Form1 form,
        Panel container,
        OxyPlot.WindowsForms.PlotView plotView,
        ToolTip toolTip,
        Action notifyPlotChanged)
    {
        Form = form;
        PlotView = plotView;
        this.notifyPlotChanged = notifyPlotChanged;

        toolTip.InitialDelay = 600;
        toolTip.ReshowDelay = 150;
        toolTip.AutoPopDelay = 6_000;
        toolTip.ShowAlways = true;

        Panel templatePanel = container.Controls
            .OfType<Panel>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Overlay template panel is missing.");
        Button templateCaptureButton = templatePanel.Controls
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "buttonSaveOverlay")
            ?? throw new InvalidOperationException(
                "Overlay template capture button is missing.");
        DarkNumericUpDown templateOffset = templatePanel.Controls
            .OfType<DarkNumericUpDown>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Overlay template offset control is missing.");
        CheckBox templateCheckBox = templatePanel.Controls
            .OfType<CheckBox>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Overlay template checkbox is missing.");

        overlays.Add(new Overlay(
            templatePanel,
            templateCaptureButton,
            templateOffset,
            templateCheckBox,
            1,
            toolTip,
            this));

        form.SuspendLayout();
        container.SuspendLayout();

        var random = new Random(3);
        for (int index = 2; index <= OverlayFile.MaximumSlotCount; index++)
        {
            Panel panel = CreatePanel(templatePanel, index, random);
            CheckBox checkBox = CreateCheckBox(templateCheckBox, index);
            DarkNumericUpDown offset = CreateOffset(templateOffset, index);
            Button captureButton = CreateCaptureButton(templateCaptureButton, index);

            panel.Controls.Add(checkBox);
            panel.Controls.Add(offset);
            panel.Controls.Add(captureButton);

            overlays.Add(new Overlay(
                panel,
                captureButton,
                offset,
                checkBox,
                index,
                toolTip,
                this));

            panel.ResumeLayout(false);
            panel.PerformLayout();
            container.Controls.Add(panel);
        }

        container.ResumeLayout(false);
        form.ResumeLayout(false);
    }

    public OxyPlot.WindowsForms.PlotView PlotView { get; }
    public Form1 Form { get; }

    // The magnitude scale the plot is currently drawn in, so a captured overlay is
    // tagged with its unit and only shown again on a matching axis. Defaults to
    // Relative until the shell provides the live value.
    public void SetMagnitudeScaleProvider(Func<MagnitudeScale> provider) =>
        getCurrentMagnitudeScale = provider;

    public MagnitudeScale CurrentMagnitudeScale =>
        getCurrentMagnitudeScale?.Invoke() ?? MagnitudeScale.Relative;

    // Lands any debounced offset saves immediately; the shell calls this on
    // close so an offset changed within the debounce window still persists.
    public void FlushPendingSaves()
    {
        foreach (Overlay overlay in overlays)
        {
            overlay.FlushPendingOffsetSave();
        }
    }

    public void Prepare(Mode mode)
    {
        Mode overlayMode = OverlayModeFor(mode);
        foreach (Overlay overlay in overlays)
        {
            overlay.Prepare(overlayMode);
        }

        // Resolve operation sources once every captured slot is loaded.
        foreach (Overlay overlay in overlays)
        {
            overlay.RefreshSources();
        }
    }

    public void Show(Mode mode)
    {
        Mode overlayMode = OverlayModeFor(mode);
        MagnitudeScale scale = CurrentMagnitudeScale;
        foreach (Overlay overlay in overlays)
        {
            if (!overlay.Checked || overlay.SeriesMode != overlayMode)
            {
                continue;
            }

            // In the magnitude mode an overlay shows only on the axis it was captured
            // on: dBr/dBc overlays in Relative, dB SPL overlays in SPL.
            if (overlayMode == Mode.FrequencyResponse &&
                overlay.CapturedMagnitudeScale != scale)
            {
                continue;
            }

            overlay.Show();
        }

        notifyPlotChanged();
    }

    public void HideAll()
    {
        foreach (Overlay overlay in overlays)
        {
            overlay.Hide();
        }

        notifyPlotChanged();
    }

    // Redraws shown Target overlays bound to the current measurement so they
    // track a live-updating trace (for example the running Live Spectrum curve).
    // Returns true if any such overlay was redrawn.
    public bool RefreshCurrentMeasurementTargets()
    {
        bool any = false;
        foreach (Overlay overlay in overlays)
        {
            any |= overlay.RedrawCurrentMeasurementTarget();
        }

        return any;
    }

    // True when at least one slot for this mode is populated, so the bulk
    // Show all / Hide all controls have something to act on.
    public bool HasOverlays(Mode mode)
    {
        Mode overlayMode = OverlayModeFor(mode);
        return overlays.Any(overlay =>
            overlay.SeriesMode == overlayMode && overlay.Title.Length > 0);
    }

    public static bool SupportsMode(Mode mode)
    {
        return mode is
            Mode.ImpulseResponse or
            Mode.FrequencyResponse or
            Mode.PhaseResponse or
            Mode.GroupDelay or
            Mode.LiveSpectrum or
            Mode.EqWizard or
            Mode.Autocorrelation;
    }

    // Frequency Response and Live Spectrum share the same frequency/dB axes, so
    // they share a single set of overlay slots and on-disk storage. Both map to a
    // single canonical mode used for every overlay comparison, path, and tag.
    public static Mode OverlayModeFor(Mode mode) =>
        mode is Mode.LiveSpectrum or Mode.EqWizard ? Mode.FrequencyResponse : mode;

    internal IReadOnlyList<OverlaySlotOption> GetCaptureSourceOptions()
    {
        return overlays
            .Where(overlay =>
                overlay.Kind == OverlayKind.Captured &&
                overlay.SeriesMode == OverlayModeFor(Form.CurrentMode) &&
                overlay.HasCaptureData)
            .Select(overlay => new OverlaySlotOption(
                overlay.Index,
                overlay.Title))
            .ToArray();
    }

    internal bool TryGetCaptureSource(
        int slot,
        out OverlayOperationSource? source)
    {
        Overlay? overlay = overlays.FirstOrDefault(
            candidate =>
                candidate.Index == slot &&
                candidate.Kind == OverlayKind.Captured &&
                candidate.SeriesMode == OverlayModeFor(Form.CurrentMode));
        source = overlay?.CreateOperationSource();
        return source != null;
    }

    // Live analysis curves on the current plot that an operation operand can reference
    // directly (every such curve carries a CurveTag). Both Main and Compare are offered.
    internal IReadOnlyList<LiveCurveOption> GetLiveCurveOptions()
    {
        PlotModel? model = PlotView.Model;
        if (model == null)
        {
            return [];
        }

        return model.Series
            .OfType<LineSeries>()
            .Where(series => series.Tag is CurveTag && series.Points.Count >= 2)
            .Select(series =>
            {
                var tag = (CurveTag)series.Tag!;
                return new LiveCurveOption(tag.Key, tag.Label);
            })
            .ToArray();
    }

    // Resolves a live-curve operand from the current plot by its CurveTag Key. Returns
    // false when that curve is not currently drawn (e.g. its Show toggle is off).
    internal bool TryGetLiveCurveSource(string key, out OverlayOperationSource? source)
    {
        source = null;
        LineSeries? match = PlotView.Model?.Series
            .OfType<LineSeries>()
            .FirstOrDefault(series =>
                series.Tag is CurveTag tag && tag.Key == key && series.Points.Count >= 2);
        if (match == null)
        {
            return false;
        }

        var curveTag = (CurveTag)match.Tag!;
        source = new OverlayOperationSource(
            0,
            curveTag.Label,
            match.Points.Select(point => new OverlayPoint(point.X, point.Y)).ToArray(),
            curveTag.PhaseUnwrapped);
        return true;
    }

    internal void NotifyCapturedOverlayChanged()
    {
        foreach (Overlay overlay in overlays)
        {
            if (overlay.Kind != OverlayKind.Captured)
            {
                overlay.RefreshSources();
            }
        }
    }

    internal void NotifyPlotChanged() => notifyPlotChanged();

    internal void CloseCaptureMenus()
    {
        foreach (Overlay overlay in overlays)
        {
            overlay.CloseCaptureMenu();
        }
    }

    // Records which slots were active (shown) for the given mode. Overlay
    // contents are not captured here; they live in their own on-disk files.
    internal List<int> CaptureActiveSlots(Mode mode)
    {
        Mode overlayMode = OverlayModeFor(mode);
        return overlays
            .Where(overlay => overlay.Checked && overlay.SeriesMode == overlayMode)
            .Select(overlay => overlay.Index)
            .ToList();
    }

    // Shows the previously-active slots after a mode switch has already reloaded
    // every overlay from disk and left them hidden. Slots not listed stay hidden,
    // so this is a clean replace rather than a merge with prior UI state.
    internal void RestoreActiveSlots(Mode mode, IReadOnlyList<int>? activeSlots)
    {
        if (activeSlots == null || activeSlots.Count == 0)
        {
            return;
        }

        Mode overlayMode = OverlayModeFor(mode);
        foreach (int slot in activeSlots)
        {
            Overlay? overlay = overlays.FirstOrDefault(
                candidate => candidate.Index == slot && candidate.SeriesMode == overlayMode);
            overlay?.Show();
        }

        notifyPlotChanged();
    }

    internal static string? GetTrackerFormatString(Mode mode)
    {
        return mode switch
        {
            Mode.FrequencyResponse or Mode.LiveSpectrum =>
                "{0}\n{2:0.0} Hz\n{4:0.00} dB",
            Mode.PhaseResponse =>
                "{0}\n{2:0.0} Hz\n{4:0.0}°", // u00B0 degree char
            Mode.GroupDelay =>
                "{0}\n{2:0.0} Hz\n{4:0.000} ms",
            Mode.ImpulseResponse =>
                "{0}\n{2:0} sample\n{4:0.00000000}",
            Mode.Autocorrelation =>
                "{0}\n{2:0.000} ms\n{4:0.000}",
            _ => null
        };
    }

    private static Panel CreatePanel(
        Panel template,
        int index,
        Random random)
    {
        return new Panel
        {
            BackColor = Color.FromArgb(
                random.Next(255),
                random.Next(255),
                random.Next(255)),
            Location = new Point(
                template.Location.X,
                template.Location.Y +
                    (template.Size.Height + template.Margin.Top) * (index - 1)),
            Name = $"overlayPanel{index}",
            Size = template.Size
        };
    }

    private static CheckBox CreateCheckBox(CheckBox template, int index)
    {
        return new CheckBox
        {
            BackColor = template.BackColor,
            FlatStyle = template.FlatStyle,
            AutoSize = template.AutoSize,
            Location = template.Location,
            Name = $"checkBox{index}",
            Size = template.Size
        };
    }

    private static DarkNumericUpDown CreateOffset(
        DarkNumericUpDown template,
        int index)
    {
        return new DarkNumericUpDown
        {
            BackColor = template.BackColor,
            DecimalPlaces = template.DecimalPlaces,
            ForeColor = template.ForeColor,
            Increment = template.Increment,
            Location = template.Location,
            Maximum = template.Maximum,
            Minimum = template.Minimum,
            Name = $"numericUpDown{index}",
            Size = template.Size,
            TextAlign = template.TextAlign,
            ThousandsSeparator = template.ThousandsSeparator,
            Value = template.Value
        };
    }

    private static Button CreateCaptureButton(Button template, int index)
    {
        return new Button
        {
            FlatStyle = template.FlatStyle,
            BackColor = template.BackColor,
            ForeColor = template.ForeColor,
            Location = template.Location,
            Name = $"button{index}",
            Size = template.Size,
            Text = $"{index}",
            UseVisualStyleBackColor = template.UseVisualStyleBackColor,
            UseCompatibleTextRendering = template.UseCompatibleTextRendering
        };
    }

}

/// <summary>
/// A single universal overlay slot. Every slot can hold a captured curve or a
/// calculated recipe (operation between two captured slots); the kind is chosen
/// from the capture button menu or the settings dialog.
/// </summary>
public sealed class Overlay
{
    private readonly OverlayCollection collection;
    private readonly Panel panel;
    private readonly Button captureButton;
    private readonly DarkNumericUpDown offsetControl;
    private readonly CheckBox checkBox;
    private readonly Color defaultColor;
    private readonly decimal defaultOffset;
    private readonly ContextMenuStrip captureMenu;
    private readonly ToolStripMenuItem captureCurveMenuItem;
    private readonly ToolStripItem exportDeviationMenuItem;
    private readonly ToolStripItem settingsMenuItem;
    private readonly System.Windows.Forms.Timer longPressTimer;
    private readonly System.Windows.Forms.Timer offsetSaveTimer;
    private int captureMenuOpenedAt;
    private bool captureMenuSpuriousCloseGuard;
    private bool longPressTriggered;

    private OverlayKind kind = OverlayKind.Captured;
    private bool updatingControls;
    // True while a settings dialog is live-previewing candidate values on the plot;
    // keeps periodic redraws (e.g. the running Live Spectrum's current-measurement
    // target refresh) from stomping the preview with the stored state.
    private bool previewActive;

    // Presentation (all kinds).
    private double strokeThickness = 2;
    private OverlayLineStyle lineStyle = OverlayLineStyle.Solid;
    private int opacityPercent = 100;
    private int smoothingInverseOctaves;

    // Captured kind.
    private DataPoint[]? sourcePoints;
    private DataPoint[]? drawPoints;
    private string? capturedYAxisKey;
    // The magnitude scale this slot was captured in (meaningful for a captured
    // FR curve; Relative for every other kind). Gates which magnitude mode shows it.
    private MagnitudeScale capturedMagnitudeScale = MagnitudeScale.Relative;
    // Phase representation of a captured curve: true unwrapped, false wrapped, null
    // unknown. Drives the wrapped-difference choice in phase overlay operations.
    private bool? phaseUnwrapped;

    // Operation kind. Each operand is either a captured slot (SourceSlotA/B) or, when
    // SourceCurveKeyA/B is set, a live analysis curve resolved from the plot by its
    // CurveTag Key on every rebuild — so an operation over live curves recomputes as
    // the analysis changes (e.g. while tweaking window settings).
    private bool operationConfigured;
    private int sourceSlotA;
    private int sourceSlotB;
    private string? sourceCurveKeyA;
    private string? sourceCurveKeyB;
    private OverlayOperation operation = OverlayOperation.AMinusB;
    private double blendFrequencyHz = 1_000;
    private double blendWidthOctaves = 1;
    private bool useAmplitudeSpace;
    // ComplexSum only: delay (ms) and polarity flip applied to the Compare response.
    private double compareDelayMs;
    private bool compareInvertPolarity;

    // Target kind.
    private bool targetConfigured;
    private readonly TargetOverlayCurveBuilder targetCurveBuilder = new();
    private int targetSourceSlot;
    private TargetPreset targetPreset = TargetPreset.HarmanRoom;
    private double targetTiltDbPerOctave;
    private double targetBassShelfGainDb;
    private double targetBassShelfFrequencyHz = 100;
    private double targetBassShelfWidthOctaves = 1.5;
    private double targetTrebleShelfGainDb;
    private double targetTrebleShelfFrequencyHz = 5_000;
    private double targetTrebleShelfWidthOctaves = 1.5;
    private double targetPresenceGainDb;
    private double targetPresenceFrequencyHz = 3_000;
    private double targetPresenceWidthOctaves = 1.0;
    private double targetToleranceDb;
    private TargetDeviationMode targetDeviationMode = TargetDeviationMode.Deviation;

    public Overlay(
        Panel panel,
        Button captureButton,
        DarkNumericUpDown offsetControl,
        CheckBox checkBox,
        int index,
        ToolTip toolTip,
        OverlayCollection collection)
    {
        this.panel = panel;
        this.captureButton = captureButton;
        this.offsetControl = offsetControl;
        this.checkBox = checkBox;
        this.collection = collection;
        defaultColor = panel.BackColor;
        defaultOffset = offsetControl.Value;
        Index = index;

        captureMenu = BuildCaptureMenu(
            out captureCurveMenuItem,
            out exportDeviationMenuItem,
            out settingsMenuItem);
        captureMenu.Opened += (_, _) =>
        {
            captureMenuOpenedAt = Environment.TickCount;
            captureMenuSpuriousCloseGuard = true;
        };
        captureMenu.Closing += CaptureMenuClosing;

        // Holding the button for over half a second jumps straight to the slot's
        // settings; a normal click still opens the capture menu.
        longPressTimer = new System.Windows.Forms.Timer { Interval = 500 };
        longPressTimer.Tick += LongPressTimerTick;

        // Persisting the slot serializes every captured point and flushes to
        // disk; debounce it so holding the offset spinner arrow doesn't fsync
        // on each tick. The redraw itself stays immediate.
        offsetSaveTimer = new System.Windows.Forms.Timer { Interval = 500 };
        offsetSaveTimer.Tick += OffsetSaveTimerTick;

        toolTip.SetToolTip(offsetControl, "Overlay vertical offset (dB)");
        toolTip.SetToolTip(checkBox, "Show / hide this overlay");
        toolTip.SetToolTip(
            captureButton,
            "Click for the overlay menu; hold to open this slot's settings");

        checkBox.CheckedChanged += CheckBoxChanged;
        captureButton.Click += CaptureButtonClick;
        captureButton.MouseDown += CaptureButtonMouseDown;
        captureButton.MouseUp += CaptureButtonMouseUp;
        offsetControl.ValueChanged += OffsetValueChanged;

        ResetState();
    }

    public int Index { get; }
    public string Title { get; private set; } = "";
    public Mode SeriesMode { get; private set; }
    public bool Checked => checkBox.Checked;
    public OverlayKind Kind => kind;

    public MagnitudeScale CapturedMagnitudeScale => capturedMagnitudeScale;
    public bool HasCaptureData => sourcePoints is { Length: > 1 };

    // The overlay mode for the current view; Frequency Response and Live Spectrum
    // collapse to one shared mode so they use the same slots and storage.
    private Mode CurrentOverlayMode =>
        OverlayCollection.OverlayModeFor(collection.Form.CurrentMode);

    public void Prepare(Mode mode)
    {
        // A pending debounced offset save must land before the slot state is
        // replaced from disk, or the last spinner change would be dropped.
        FlushPendingOffsetSave();
        ResetState();
        if (mode == Mode.None)
        {
            return;
        }

        SeriesMode = mode;
        try
        {
            OverlayFile? file = OverlayFile.Load(mode, Index);
            if (file != null)
            {
                ApplyFile(file);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Failed to load overlay slot {Index} for {mode}: {exception}");
            QuarantineCorruptSlot(mode, exception);
        }
    }

    // A slot file that fails to load used to present as an empty slot and the
    // next capture silently overwrote it. Setting it aside keeps the damaged
    // data recoverable and makes each broken file warn exactly once.
    private void QuarantineCorruptSlot(Mode mode, Exception error)
    {
        try
        {
            string? quarantinePath = OverlayFile.QuarantineCorruptFile(mode, Index);
            if (quarantinePath != null)
            {
                ShowStorageError(
                    $"Overlay slot {Index} for {mode} could not be loaded; " +
                    $"the file was kept as {Path.GetFileName(quarantinePath)}.",
                    error);
            }
        }
        catch
        {
            // A transiently locked file stays in place and is retried on the
            // next mode switch; quarantining must never break the switch itself.
        }
    }

    public void Show()
    {
        PlotModel? model = collection.PlotView.Model;
        if (model == null || Title == "")
        {
            SetChecked(false);
            return;
        }

        RemoveSeries(model);
        bool drawn = kind switch
        {
            OverlayKind.Target => AddTargetSeries(model),
            OverlayKind.Operation => AddCurveSeries(model, "curve", BuildOperationPoints()),
            _ => AddCurveSeries(model, "curve", drawPoints, capturedYAxisKey)
        };

        if (drawn)
        {
            SetChecked(true);
            RefreshPlot(model);
        }
        else if (IsCurrentMeasurementTarget || ReferencesLiveCurve || IsComplexSumOperation)
        {
            // A target bound to the current measurement — or an operation over a live
            // curve or the Main/Compare transfer IRs — stays armed even when its source
            // is not available yet (e.g. the running Live Spectrum before its first
            // frame, a curve whose Show toggle is momentarily off, or no Compare
            // selected); it redraws once the data appears.
            SetChecked(true);
            RefreshPlot(model);
        }
        else
        {
            SetChecked(false);
        }
    }

    private bool IsCurrentMeasurementTarget =>
        kind == OverlayKind.Target && targetSourceSlot == 0;

    private bool ReferencesLiveCurve =>
        kind == OverlayKind.Operation &&
        (sourceCurveKeyA != null || sourceCurveKeyB != null);

    // Complex sum reads the Main and Compare transfer IRs from the measurement, not
    // from operands; like a live-curve operation it recomputes on every rebuild and
    // stays armed while the Compare data is absent.
    private bool IsComplexSumOperation =>
        kind == OverlayKind.Operation &&
        operation is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss;

    private OxyColor ToOverlayColor()
    {
        Color color = panel.BackColor;
        byte alpha = (byte)Math.Round(opacityPercent / 100.0 * 255);
        return OxyColor.FromArgb(alpha, color.R, color.G, color.B);
    }

    // Redraws a shown current-measurement Target overlay so it follows a
    // live-updating source such as the running Live Spectrum trace. Returns true
    // if this overlay is such a target. The caller invalidates the plot.
    internal bool RedrawCurrentMeasurementTarget()
    {
        if (!Checked || !IsCurrentMeasurementTarget || previewActive)
        {
            return false;
        }

        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return false;
        }

        RemoveSeries(model);
        AddTargetSeries(model);
        return true;
    }

    private bool AddCurveSeries(
        PlotModel model,
        string part,
        DataPoint[]? points,
        string? yAxisKey = null) =>
        AddCurveSeries(
            model,
            part,
            points,
            panel.BackColor,
            opacityPercent,
            strokeThickness,
            lineStyle,
            Title,
            yAxisKey);

    // Style-parameterized so the settings dialog's live preview can render candidate
    // presentation values without committing them to the slot first.
    private bool AddCurveSeries(
        PlotModel model,
        string part,
        DataPoint[]? points,
        Color color,
        int opacity,
        double thickness,
        OverlayLineStyle style,
        string title,
        string? yAxisKey = null)
    {
        if (points == null || points.Length < 2)
        {
            return false;
        }

        if (yAxisKey == PlotModelFactory.CoherenceAxisKey)
        {
            PlotModelFactory.AddCoherenceAxis(model);
        }

        byte alpha = (byte)Math.Round(opacity / 100.0 * 255);
        var series = new LineSeries
        {
            Color = OxyColor.FromArgb(alpha, color.R, color.G, color.B),
            StrokeThickness = thickness,
            LineStyle = ToOxyLineStyle(style),
            Title = title,
            Tag = GetTag(part)
        };
        if (!string.IsNullOrEmpty(yAxisKey))
        {
            series.YAxisKey = yAxisKey;
        }

        string? trackerFormat = yAxisKey == PlotModelFactory.CoherenceAxisKey
            ? "{0}\n{2:0.0} Hz\n{4:0.00} \u03B3\u00B2"
            : OverlayCollection.GetTrackerFormatString(SeriesMode);
        if (!string.IsNullOrEmpty(trackerFormat))
        {
            series.TrackerFormatString = trackerFormat;
        }
        series.Points.AddRange(points);
        model.Series.Add(series);
        return true;
    }

    private bool AddTargetSeries(PlotModel model) =>
        AddTargetSeries(
            model,
            CurrentTargetSpec(),
            targetToleranceDb,
            targetDeviationMode,
            targetSourceSlot,
            smoothingInverseOctaves,
            panel.BackColor,
            opacityPercent,
            strokeThickness,
            lineStyle,
            Title);

    // Fully parameterized so the settings dialog's live preview can render candidate
    // target settings without committing them to the slot first.
    private bool AddTargetSeries(
        PlotModel model,
        TargetCurveSpec spec,
        double toleranceDb,
        TargetDeviationMode deviationMode,
        int sourceSlot,
        int smoothing,
        Color color,
        int opacity,
        double thickness,
        OverlayLineStyle style,
        string title)
    {
        double offset = (double)offsetControl.Value;

        // The shape and tolerance band are constant between edits; the builder
        // caches them so the ~30 fps live redraw does not rebuild the grid math.
        TargetOverlayShape shape = targetCurveBuilder.BuildShape(
            spec,
            offset,
            toleranceDb);
        if (shape.Target.Length < 2)
        {
            return false;
        }

        // Deviation / EQ correction compares against the incoming curve, so it is
        // built from the source and clipped to wherever that curve has data (gaps
        // appear where, for example, coherence is below the threshold).
        DataPoint[] deviation =
            deviationMode != TargetDeviationMode.None &&
            ResolveTargetSource(sourceSlot) is { Length: >= 2 } source
                ? TargetOverlayCurveBuilder.BuildDeviation(
                    source,
                    spec,
                    offset,
                    smoothing,
                    deviationMode)
                : Array.Empty<DataPoint>();

        byte alpha = (byte)Math.Round(opacity / 100.0 * 255);
        OxyColor lineColor = OxyColor.FromArgb(alpha, color.R, color.G, color.B);
        string? trackerFormat = OverlayCollection.GetTrackerFormatString(SeriesMode);

        // Tolerance band first so the curves draw on top of it.
        if (shape.ToleranceUpper.Length >= 2 &&
            shape.ToleranceLower.Length == shape.ToleranceUpper.Length)
        {
            var band = new OxyPlot.Series.AreaSeries
            {
                Color = OxyColors.Transparent,
                Fill = OxyColor.FromArgb(40, color.R, color.G, color.B),
                StrokeThickness = 0,
                Tag = GetTag("tolerance")
            };
            band.Points.AddRange(shape.ToleranceUpper);
            band.Points2.AddRange(shape.ToleranceLower);
            model.Series.Add(band);
        }

        var targetSeries = new LineSeries
        {
            Color = lineColor,
            StrokeThickness = thickness,
            LineStyle = ToOxyLineStyle(style),
            Title = $"{title} (target)",
            Tag = GetTag("target")
        };
        if (!string.IsNullOrEmpty(trackerFormat))
        {
            targetSeries.TrackerFormatString = trackerFormat;
        }
        targetSeries.Points.AddRange(shape.Target);
        model.Series.Add(targetSeries);

        if (deviation.Length >= 2)
        {
            string deviationLabel = deviationMode == TargetDeviationMode.Correction
                ? "EQ correction"
                : "deviation";
            var deviationSeries = new LineSeries
            {
                Color = lineColor,
                StrokeThickness = Math.Max(1.0, thickness - 1.0),
                LineStyle = LineStyle.Solid,
                Title = $"{title} ({deviationLabel})",
                Tag = GetTag("deviation")
            };
            if (!string.IsNullOrEmpty(trackerFormat))
            {
                deviationSeries.TrackerFormatString = trackerFormat;
            }
            deviationSeries.Points.AddRange(deviation);
            model.Series.Add(deviationSeries);
        }

        return true;
    }

    // Redraws this slot's series with the target dialog's candidate settings while
    // it is open; nothing is committed, so Cancel can restore cleanly.
    private void PreviewTarget(OverlayTargetPreview settings)
    {
        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return;
        }

        RemoveSeries(model);
        AddTargetSeries(
            model,
            settings.Spec,
            settings.ToleranceDb,
            settings.DeviationMode,
            settings.SourceSlot,
            settings.SmoothingInverseOctaves,
            settings.Color,
            settings.OpacityPercent,
            settings.StrokeThickness,
            settings.LineStyle,
            settings.Name.Length > 0 ? settings.Name : Title);
        RefreshPlot(model);
    }

    private TargetCurveSpec CurrentTargetSpec() => new(
        targetTiltDbPerOctave,
        targetBassShelfGainDb,
        targetBassShelfFrequencyHz,
        targetBassShelfWidthOctaves,
        targetTrebleShelfGainDb,
        targetTrebleShelfFrequencyHz,
        targetTrebleShelfWidthOctaves,
        targetPresenceGainDb,
        targetPresenceFrequencyHz,
        targetPresenceWidthOctaves);

    private OverlayPoint[]? ResolveTargetSource(int sourceSlot)
    {
        if (sourceSlot != 0)
        {
            return collection.TryGetCaptureSource(
                sourceSlot,
                out OverlayOperationSource? source) && source != null
                ? source.Points.ToArray()
                : null;
        }

        // Current measurement: prefer the live trace, else the main analysis
        // curve (any non-overlay, non-live-helper line series).
        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return null;
        }

        // Every analysis curve carries a CurveTag; the current-measurement primary is
        // the main Primary-kind curve (the live transfer function while running, or the
        // mode's main trace otherwise). Overlay and live-helper series are skipped.
        LineSeries? primary = model.Series
            .OfType<LineSeries>()
            .FirstOrDefault(series =>
                series.Tag is CurveTag
                {
                    Source: CurveSource.Main,
                    Kind: Resonalyze.Dsp.AnalysisCurveKind.Primary
                });
        if (primary == null || primary.Points.Count < 2)
        {
            return null;
        }

        // Keep NaN gaps (e.g. where live coherence is below the threshold): they
        // make the deviation / EQ-correction curve break over unreliable bands
        // instead of bridging them.
        return primary.Points
            .Select(point => new OverlayPoint(point.X, point.Y))
            .ToArray();
    }

    public void Hide()
    {
        PlotModel? model = collection.PlotView.Model;
        if (model != null)
        {
            RemoveSeries(model);
            RefreshPlot(model);
        }

        SetChecked(false);
    }

    /// <summary>
    /// Re-evaluates availability of calculated slots when their captured
    /// sources change. Captured slots are unaffected.
    /// </summary>
    public void RefreshSources()
    {
        if (kind == OverlayKind.Operation)
        {
            RefreshOperationSources();
        }
        else if (kind == OverlayKind.Target)
        {
            RefreshTargetSources();
        }
    }

    private void RefreshOperationSources()
    {
        bool wasChecked = Checked;
        // A live-curve operand may not be on the plot at this instant (mode just loaded,
        // its Show toggle off), and the complex sum's Compare data may not be selected
        // yet; keep such an operation available as long as it is configured, like a
        // target. Slot-only operations still require their captures.
        bool available = operationConfigured &&
            (ReferencesLiveCurve || IsComplexSumOperation || TryGetSources(out _, out _));
        ApplyCalculatedAvailability(
            available,
            operationConfigured,
            wasChecked);
    }

    private void RefreshTargetSources()
    {
        bool wasChecked = Checked;
        // A configured target can always draw its shape and tolerance band; the
        // comparison source only governs whether the deviation curve appears.
        ApplyCalculatedAvailability(
            targetConfigured,
            targetConfigured,
            wasChecked);
    }

    private void ApplyCalculatedAvailability(
        bool available,
        bool configured,
        bool wasChecked)
    {
        checkBox.Enabled = available;
        offsetControl.Enabled = configured;

        if (!available)
        {
            Hide();
            return;
        }
        if (wasChecked)
        {
            Show();
        }
    }

    internal OverlayOperationSource? CreateOperationSource()
    {
        if (kind != OverlayKind.Captured || drawPoints == null || Title == "")
        {
            return null;
        }

        return new OverlayOperationSource(
            Index,
            Title,
            drawPoints
                .Select(point => new OverlayPoint(point.X, point.Y))
                .ToArray(),
            phaseUnwrapped);
    }

    private ContextMenuStrip BuildCaptureMenu(
        out ToolStripMenuItem captureCurveItem,
        out ToolStripItem exportDeviationItem,
        out ToolStripItem settingsItem)
    {
        var menu = new ContextMenuStrip();
        captureCurveItem = new ToolStripMenuItem("Capture curve…");
        captureCurveItem.Click += CaptureCurveMenuItemClick;
        captureCurveItem.DropDownOpening += CaptureCurveMenuItemDropDownOpening;
        menu.Items.Add(captureCurveItem);
        menu.Items.Add("Import from text…", null, (_, _) => ImportFromText());
        menu.Items.Add("Export to text…", null, (_, _) => ExportToText());
        exportDeviationItem = menu.Items.Add(
            "Export deviation…",
            null,
            (_, _) => ExportDeviationToText());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(
            "\u0192  Calculated overlay…", // \u0192 f
            null,
            (_, _) => ConfigureOperation());
        menu.Items.Add("\u25B3  Target…", null, (_, _) => ConfigureTarget()); // \u25B3 triangle
        menu.Items.Add(new ToolStripSeparator());
        settingsItem = menu.Items.Add(
            "\u2699  Settings\u2026", // \u2699 gear
            null,
            (_, _) => OpenSettings());
        return menu;
    }

    private void CaptureMenuClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        // A borderless custom-chrome window can emit a spurious activation change the
        // instant the dropdown opens, which closes it immediately — the "button
        // pressed, no menu" symptom that no show-timing change could fix. Cancel that
        // single focus-change close if it arrives right after opening, but only once
        // per open so a genuine later dismissal (e.g. clicking another slot) is not
        // swallowed too. Clicking elsewhere (AppClicked), Esc (Keyboard), choosing an
        // item, and programmatic Close (CloseCalled) are all unaffected.
        if (e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange &&
            captureMenuSpuriousCloseGuard &&
            Environment.TickCount - captureMenuOpenedAt < 250)
        {
            captureMenuSpuriousCloseGuard = false;
            e.Cancel = true;
        }
    }

    internal void CloseCaptureMenu()
    {
        if (captureMenu.Visible)
        {
            captureMenu.Close();
        }
    }

    private void CaptureButtonClick(object? sender, EventArgs e)
    {
        // A long press already opened the settings dialog, so swallow the click that
        // ends the hold instead of also opening the menu.
        if (longPressTriggered)
        {
            longPressTriggered = false;
            return;
        }

        // Open on Click (which fires after the mouse-up) and defer with BeginInvoke.
        // Two WinForms quirks made the menu intermittently fail to appear when shown
        // from mouse-down: showing synchronously inside the mouse message was
        // swallowed by the focus/activation change, and showing on mouse-down let the
        // click's own mouse-up land outside the just-opened menu and immediately
        // close it. Showing on Click, posted after the current message, avoids both.
        if (captureMenu.Visible)
        {
            captureMenu.Close();
            return;
        }

        captureButton.BeginInvoke(ShowCaptureMenu);
    }

    private void CaptureButtonMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // Each fresh press clears the flag so a click that follows a held-but-aborted
        // press (e.g. no settings to show) is still treated as a long press here, and
        // a later genuine click is never wrongly swallowed.
        longPressTriggered = false;
        longPressTimer.Start();
    }

    private void CaptureButtonMouseUp(object? sender, MouseEventArgs e)
    {
        longPressTimer.Stop();
    }

    private void LongPressTimerTick(object? sender, EventArgs e)
    {
        longPressTimer.Stop();
        longPressTriggered = true;

        // The click that opens the menu only fires on mouse-up, so the menu is not
        // up yet during the hold; close it defensively in case of odd ordering.
        if (captureMenu.Visible)
        {
            captureMenu.Close();
        }

        OpenSettings();
    }

    private void ShowCaptureMenu()
    {
        if (captureMenu.Visible)
        {
            return;
        }

        // Make sure no other slot's menu stays open. A programmatic Close uses
        // reason CloseCalled, which bypasses the spurious-focus-close guard, so this
        // reliably leaves only one capture menu open at a time.
        collection.CloseCaptureMenus();

        RebuildCaptureCurveMenu();
        // The deviation export only applies to a target slot, and its label
        // reflects the current deviation mode.
        exportDeviationMenuItem.Visible = kind == OverlayKind.Target;
        exportDeviationMenuItem.Text =
            targetDeviationMode == TargetDeviationMode.Correction
                ? "Export EQ correction…"
                : "Export deviation…";
        settingsMenuItem.Enabled =
            SeriesMode == CurrentOverlayMode && HasConfiguredContent();
        captureMenu.Show(captureButton, new Point(0, captureButton.Height));
    }

    private void RebuildCaptureCurveMenu()
    {
        captureCurveMenuItem.DropDownItems.Clear();

        List<LineSeries> candidates = GetCaptureCandidates();
        captureCurveMenuItem.Enabled = candidates.Count > 0;
        if (candidates.Count <= 1)
        {
            return;
        }

        foreach (LineSeries series in candidates)
        {
            var item = new ToolStripMenuItem(GetCaptureCandidateTitle(series));
            item.Click += (_, _) => CaptureSeries(series);
            captureCurveMenuItem.DropDownItems.Add(item);
        }
    }

    private void CaptureCurveMenuItemClick(object? sender, EventArgs e)
    {
        List<LineSeries> candidates = GetCaptureCandidates();
        if (candidates.Count == 1)
        {
            CaptureSeries(candidates[0]);
        }
    }

    private void CaptureCurveMenuItemDropDownOpening(object? sender, EventArgs e)
    {
        captureCurveMenuItem.DropDownDirection = ShouldOpenCaptureSubmenuLeft()
            ? ToolStripDropDownDirection.Left
            : ToolStripDropDownDirection.Right;
    }

    private bool ShouldOpenCaptureSubmenuLeft()
    {
        if (captureCurveMenuItem.DropDownItems.Count == 0)
        {
            return false;
        }

        Rectangle screen = Screen.FromControl(captureButton).WorkingArea;
        Point menuRight = captureMenu.PointToScreen(new Point(captureMenu.Width, 0));
        int submenuWidth = captureCurveMenuItem.DropDown.GetPreferredSize(
            Size.Empty).Width;
        return menuRight.X + submenuWidth > screen.Right;
    }

    private List<LineSeries> GetCaptureCandidates()
    {
        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return [];
        }

        return model.Series
            .OfType<LineSeries>()
            .Where(series => series.Tag is not string tag ||
                !tag.StartsWith("overlay:", StringComparison.Ordinal))
            .ToList();
    }

    private void CaptureSeries(LineSeries selected)
    {
        if (selected.Points.Count < 2)
        {
            return;
        }

        var points = new DataPoint[selected.Points.Count];
        selected.Points.CopyTo(points);
        string title = $"Overlay {Index}: {selected.Title ?? string.Empty}";
        Mode mode = CurrentOverlayMode;

        Hide();
        kind = OverlayKind.Captured;
        operationConfigured = false;
        sourcePoints = points;
        // The curve was drawn in the plot's current scale, so it carries that unit.
        capturedMagnitudeScale = collection.CurrentMagnitudeScale;
        capturedYAxisKey = string.IsNullOrEmpty(selected.YAxisKey)
            ? null
            : selected.YAxisKey;
        phaseUnwrapped = selected.Tag is CurveTag curveTag
            ? curveTag.PhaseUnwrapped
            : null;
        SeriesMode = mode;
        Title = title;
        UpdateKindGlyph();
        UpdateDrawPoints();

        if (!TrySaveCurrentState("Overlay could not be saved."))
        {
            return;
        }

        SetAvailability(true);
        Show();
        collection.NotifyCapturedOverlayChanged();
    }

    private void ImportFromText()
    {
        Mode mode = CurrentOverlayMode;
        if (mode == Mode.None)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Overlay points (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Import overlay points"
        };
        if (dialog.ShowDialog(collection.Form) != DialogResult.OK)
        {
            return;
        }

        OverlayPoint[] imported;
        try
        {
            imported = OverlayTextFile.Import(dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowStorageError("Overlay could not be imported.", exception);
            return;
        }

        Hide();
        kind = OverlayKind.Captured;
        operationConfigured = false;
        targetConfigured = false;
        // Imported text gives no wrap/unwrap hint; leave it unknown.
        phaseUnwrapped = null;
        // Imported points are assumed to be in the current view's unit.
        capturedMagnitudeScale = collection.CurrentMagnitudeScale;
        capturedYAxisKey = null;
        sourcePoints = imported
            .Select(point => new DataPoint(point.X, point.Y))
            .ToArray();
        SeriesMode = mode;
        Title = $"Overlay {Index}: " +
            System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        UpdateKindGlyph();
        UpdateDrawPoints();

        if (!TrySaveCurrentState("Overlay could not be saved."))
        {
            return;
        }

        SetAvailability(true);
        Show();
        collection.NotifyCapturedOverlayChanged();
    }

    private void ExportToText()
    {
        OverlayPoint[]? points = CollectExportablePoints();
        if (points == null || points.Length < 2)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "txt",
            FileName = $"{SanitizeFileName(Title)}.txt",
            Filter = "Overlay points (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Export overlay points"
        };
        if (dialog.ShowDialog(collection.Form) != DialogResult.OK)
        {
            return;
        }

        try
        {
            OverlayTextFile.Export(dialog.FileName, points);
        }
        catch (Exception exception)
        {
            ShowStorageError("Overlay could not be exported.", exception);
        }
    }

    private void ExportDeviationToText()
    {
        if (kind != OverlayKind.Target)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        OverlayPoint[]? source = ResolveTargetSource(targetSourceSlot);
        if (source == null || source.Length < 2)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        // Export the deviation even if the displayed mode hides it; default to
        // plain deviation when no curve mode is selected.
        TargetDeviationMode exportMode = targetDeviationMode == TargetDeviationMode.None
            ? TargetDeviationMode.Deviation
            : targetDeviationMode;
        TargetCurveResult result = OverlayMath.BuildTarget(
            source,
            CurrentTargetSpec(),
            (double)offsetControl.Value,
            targetToleranceDb,
            smoothingInverseOctaves,
            exportMode);
        if (result.Deviation.Length < 2)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        string suffix = exportMode == TargetDeviationMode.Correction
            ? "EQ correction"
            : "deviation";
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "txt",
            FileName = $"{SanitizeFileName(Title)} - {suffix}.txt",
            Filter = "Overlay points (*.txt)|*.txt|All files (*.*)|*.*",
            Title = $"Export {suffix}"
        };
        if (dialog.ShowDialog(collection.Form) != DialogResult.OK)
        {
            return;
        }

        try
        {
            OverlayTextFile.Export(dialog.FileName, result.Deviation);
        }
        catch (Exception exception)
        {
            ShowStorageError("Deviation could not be exported.", exception);
        }
    }

    private OverlayPoint[]? CollectExportablePoints()
    {
        switch (kind)
        {
            case OverlayKind.Operation:
                return BuildOperationPoints()?
                    .Select(point => new OverlayPoint(point.X, point.Y))
                    .ToArray();
            case OverlayKind.Target:
                OverlayPoint[]? source = ResolveTargetSource(targetSourceSlot);
                if (source == null || source.Length < 2)
                {
                    return null;
                }

                return OverlayMath.BuildTarget(
                    source,
                    CurrentTargetSpec(),
                    (double)offsetControl.Value,
                    targetToleranceDb,
                    smoothingInverseOctaves).Target;
            default:
                return sourcePoints?
                    .Select(point => new OverlayPoint(point.X, point.Y))
                    .ToArray();
        }
    }

    private static string SanitizeFileName(string title)
    {
        string trimmed = string.IsNullOrWhiteSpace(title) ? "overlay" : title.Trim();
        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(invalid, '_');
        }

        return trimmed;
    }

    // Routes the capture menu's "Settings…" entry to the editor for the slot's
    // current kind. Captured slots open the curve settings (name, color, clear);
    // calculated and target slots reopen their own configuration dialogs.
    private void OpenSettings()
    {
        if (SeriesMode != CurrentOverlayMode)
        {
            return;
        }

        if (kind == OverlayKind.Operation)
        {
            ConfigureOperation();
            return;
        }

        if (kind == OverlayKind.Target)
        {
            ConfigureTarget();
            return;
        }

        if (!HasCaptureData)
        {
            return;
        }

        ConfigureCaptured();
    }

    // The Settings entry only applies to a slot that already holds content.
    private bool HasConfiguredContent() => kind switch
    {
        OverlayKind.Operation => operationConfigured,
        OverlayKind.Target => targetConfigured,
        _ => HasCaptureData
    };

    private void ConfigureTarget()
    {
        if (SeriesMode is not (Mode.FrequencyResponse or Mode.LiveSpectrum))
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        IReadOnlyList<OverlaySlotOption> sources =
            collection.GetCaptureSourceOptions();
        TargetCurveSpec spec = targetConfigured
            ? CurrentTargetSpec()
            : TargetCurveSpec.FromPreset(TargetPreset.HarmanRoom);

        // Live preview while the dialog is open: the target shape, tolerance band,
        // and deviation curve redraw on the main plot as the parameters change, so
        // a target can be shaped against the real measurement. Cancel restores the
        // stored rendering.
        bool previewShown = false;
        bool wasCheckedBefore = Checked;
        using var dialog = new OverlayTargetSettingsDialog(
            SeriesMode,
            targetConfigured ? Title : $"Target {Index}",
            targetConfigured ? targetSourceSlot : 0,
            targetConfigured ? targetPreset : TargetPreset.HarmanRoom,
            spec,
            targetConfigured ? targetToleranceDb : 3,
            targetConfigured ? targetDeviationMode : TargetDeviationMode.Deviation,
            kind == OverlayKind.Target ? panel.BackColor : defaultColor,
            strokeThickness,
            kind == OverlayKind.Target ? lineStyle : OverlayLineStyle.Dash,
            opacityPercent,
            smoothingInverseOctaves,
            sources,
            settings =>
            {
                previewShown = true;
                previewActive = true;
                PreviewTarget(settings);
            });
        DialogResult result = dialog.ShowDialog(collection.Form);
        previewActive = false;
        if (result != DialogResult.OK)
        {
            if (previewShown)
            {
                RestoreAfterPreview(wasCheckedBefore);
            }

            return;
        }

        Hide();
        kind = OverlayKind.Target;
        capturedYAxisKey = null;
        // Targets and operations are defined in relative dB; they belong to the
        // Relative axis until an SPL-native form exists.
        capturedMagnitudeScale = MagnitudeScale.Relative;
        Title = dialog.OverlayName;
        targetSourceSlot = dialog.SourceSlot;
        targetPreset = dialog.Preset;
        TargetCurveSpec resultSpec = dialog.Spec;
        targetTiltDbPerOctave = resultSpec.TiltDbPerOctave;
        targetBassShelfGainDb = resultSpec.BassShelfGainDb;
        targetBassShelfFrequencyHz = resultSpec.BassShelfFrequencyHz;
        targetBassShelfWidthOctaves = resultSpec.BassShelfWidthOctaves;
        targetTrebleShelfGainDb = resultSpec.TrebleShelfGainDb;
        targetTrebleShelfFrequencyHz = resultSpec.TrebleShelfFrequencyHz;
        targetTrebleShelfWidthOctaves = resultSpec.TrebleShelfWidthOctaves;
        targetPresenceGainDb = resultSpec.PresenceGainDb;
        targetPresenceFrequencyHz = resultSpec.PresenceFrequencyHz;
        targetPresenceWidthOctaves = resultSpec.PresenceWidthOctaves;
        targetToleranceDb = dialog.ToleranceDb;
        targetDeviationMode = dialog.DeviationMode;
        UpdateKindGlyph();
        panel.BackColor = dialog.SelectedColor;
        strokeThickness = dialog.StrokeThickness;
        lineStyle = dialog.LineStyle;
        opacityPercent = dialog.OpacityPercent;
        smoothingInverseOctaves = dialog.SmoothingInverseOctaves;
        targetConfigured = true;

        TrySaveCurrentState("Overlay changes could not be saved.");
        SetAvailability(true);
        Show();
    }

    private void ConfigureCaptured()
    {
        // Live preview while the dialog is open: styling and smoothing changes
        // redraw the shown curve immediately; Cancel restores the stored rendering.
        bool previewShown = false;
        bool wasCheckedBefore = Checked;
        using var dialog = new OverlaySettingsDialog(
            SeriesMode,
            Title,
            panel.BackColor,
            strokeThickness,
            lineStyle,
            opacityPercent,
            smoothingInverseOctaves,
            settings =>
            {
                previewShown = true;
                previewActive = true;
                PreviewCaptured(settings);
            });
        DialogResult result = dialog.ShowDialog(collection.Form);
        previewActive = false;
        if (result != DialogResult.OK)
        {
            if (previewShown)
            {
                RestoreAfterPreview(wasCheckedBefore);
            }

            return;
        }
        if (dialog.ClearRequested)
        {
            ClearOverlay();
            return;
        }

        bool wasChecked = Checked;
        Hide();
        Title = dialog.OverlayName;
        panel.BackColor = dialog.SelectedColor;
        strokeThickness = dialog.StrokeThickness;
        lineStyle = dialog.LineStyle;
        opacityPercent = dialog.OpacityPercent;
        smoothingInverseOctaves = dialog.SmoothingInverseOctaves;
        UpdateDrawPoints();
        TrySaveCurrentState("Overlay changes could not be saved.");

        if (wasChecked)
        {
            Show();
        }
        collection.NotifyCapturedOverlayChanged();
    }

    private void ConfigureOperation()
    {
        IReadOnlyList<OverlaySlotOption> sources =
            collection.GetCaptureSourceOptions();
        IReadOnlyList<LiveCurveOption> liveCurves =
            collection.GetLiveCurveOptions();

        // Live preview while the dialog is open: every change — operands, operation,
        // blend, complex-sum delay / polarity, styling, smoothing — redraws the
        // candidate curve immediately, so e.g. a crossover delay can be tuned by
        // watching the plot. Nothing is committed until Save; Cancel restores the
        // slot's stored rendering.
        bool previewShown = false;
        bool wasCheckedBefore = Checked;
        using var dialog = new OverlayOperationSettingsDialog(
            SeriesMode,
            operationConfigured ? Title : $"Calculated overlay {Index}",
            sourceSlotA,
            sourceCurveKeyA,
            sourceSlotB,
            sourceCurveKeyB,
            operation,
            blendFrequencyHz,
            blendWidthOctaves,
            // A brand-new calculated overlay defaults to amplitude space; editing an
            // existing one keeps whatever was saved.
            operationConfigured ? useAmplitudeSpace : true,
            compareDelayMs,
            compareInvertPolarity,
            kind == OverlayKind.Operation ? panel.BackColor : defaultColor,
            strokeThickness,
            kind == OverlayKind.Operation ? lineStyle : OverlayLineStyle.Dash,
            opacityPercent,
            smoothingInverseOctaves,
            sources,
            liveCurves,
            settings =>
            {
                previewShown = true;
                previewActive = true;
                PreviewOperation(settings);
            });
        DialogResult result = dialog.ShowDialog(collection.Form);
        previewActive = false;
        if (result != DialogResult.OK)
        {
            if (previewShown)
            {
                RestoreAfterPreview(wasCheckedBefore);
            }

            return;
        }

        Hide();
        kind = OverlayKind.Operation;
        capturedYAxisKey = null;
        capturedMagnitudeScale = MagnitudeScale.Relative;
        Title = dialog.OverlayName;
        sourceSlotA = dialog.SourceSlotA;
        sourceSlotB = dialog.SourceSlotB;
        sourceCurveKeyA = dialog.SourceCurveKeyA;
        sourceCurveKeyB = dialog.SourceCurveKeyB;
        operation = dialog.Operation;
        blendFrequencyHz = dialog.BlendFrequencyHz;
        blendWidthOctaves = dialog.BlendWidthOctaves;
        useAmplitudeSpace = dialog.UseAmplitudeSpace;
        compareDelayMs = dialog.CompareDelayMs;
        compareInvertPolarity = dialog.CompareInvertPolarity;
        panel.BackColor = dialog.SelectedColor;
        strokeThickness = dialog.StrokeThickness;
        lineStyle = dialog.LineStyle;
        opacityPercent = dialog.OpacityPercent;
        smoothingInverseOctaves = dialog.SmoothingInverseOctaves;
        operationConfigured = true;
        UpdateKindGlyph();

        TrySaveCurrentState("Overlay changes could not be saved.");
        RefreshSources();
        if (checkBox.Enabled)
        {
            Show();
        }
    }

    private void ClearOverlay()
    {
        if (SeriesMode != CurrentOverlayMode)
        {
            return;
        }

        try
        {
            OverlayFile.Delete(SeriesMode, Index);
        }
        catch (Exception exception)
        {
            ShowStorageError("Overlay could not be deleted.", exception);
            return;
        }

        Hide();
        ResetState();
        collection.NotifyCapturedOverlayChanged();
    }

    private void OffsetValueChanged(object? sender, EventArgs e)
    {
        if (updatingControls)
        {
            return;
        }
        if (kind == OverlayKind.Captured && sourcePoints == null)
        {
            return;
        }
        if (kind == OverlayKind.Operation && !operationConfigured)
        {
            return;
        }
        if (kind == OverlayKind.Target && !targetConfigured)
        {
            return;
        }

        bool wasChecked = Checked;
        if (kind == OverlayKind.Captured)
        {
            UpdateDrawPoints();
        }
        offsetSaveTimer.Stop();
        offsetSaveTimer.Start();
        if (wasChecked)
        {
            Show();
        }
        // Only captured slots feed other overlays (operations consume their
        // draw points); a target or operation offset cannot change any input.
        if (kind == OverlayKind.Captured)
        {
            collection.NotifyCapturedOverlayChanged();
        }
    }

    private void OffsetSaveTimerTick(object? sender, EventArgs e)
    {
        FlushPendingOffsetSave();
    }

    internal void FlushPendingOffsetSave()
    {
        if (!offsetSaveTimer.Enabled)
        {
            return;
        }

        offsetSaveTimer.Stop();
        TrySaveCurrentState("Overlay changes could not be saved.");
    }

    private void CheckBoxChanged(object? sender, EventArgs e)
    {
        if (updatingControls)
        {
            return;
        }

        if (checkBox.Checked)
        {
            // Show only on a matching mode and — in the magnitude mode — a matching
            // scale, so an SPL overlay is not drawn on the dBr axis or vice versa.
            if (SeriesMode == CurrentOverlayMode &&
                (SeriesMode != Mode.FrequencyResponse ||
                 capturedMagnitudeScale == collection.CurrentMagnitudeScale))
            {
                Show();
            }
            else
            {
                SetChecked(false);
            }
        }
        else
        {
            Hide();
        }
    }

    private void ApplyFile(OverlayFile file)
    {
        SeriesMode = file.Mode;
        kind = file.Kind;
        Title = file.Title;
        capturedMagnitudeScale = file.CapturedMagnitudeScale;
        strokeThickness = file.StrokeThickness;
        lineStyle = file.LineStyle;
        opacityPercent = file.OpacityPercent;
        smoothingInverseOctaves = file.SmoothingCode;

        updatingControls = true;
        try
        {
            offsetControl.Value = (decimal)Math.Clamp(
                file.Offset,
                (double)offsetControl.Minimum,
                (double)offsetControl.Maximum);
            panel.BackColor = Color.FromArgb(file.ColorArgb);
        }
        finally
        {
            updatingControls = false;
        }

        if (kind == OverlayKind.Operation)
        {
            operationConfigured = true;
            sourceSlotA = file.SourceSlotA;
            sourceSlotB = file.SourceSlotB;
            sourceCurveKeyA = file.SourceCurveKeyA;
            sourceCurveKeyB = file.SourceCurveKeyB;
            operation = file.Operation;
            blendFrequencyHz = file.BlendFrequencyHz;
            blendWidthOctaves = file.BlendWidthOctaves;
            useAmplitudeSpace = file.UseAmplitudeSpace;
            compareDelayMs = file.CompareDelayMs;
            compareInvertPolarity = file.CompareInvertPolarity;
            // Availability is resolved by RefreshSources after all slots load.
        }
        else if (kind == OverlayKind.Target)
        {
            targetConfigured = true;
            targetSourceSlot = file.TargetSourceSlot;
            targetPreset = file.TargetPreset;
            targetTiltDbPerOctave = file.TargetTiltDbPerOctave;
            targetBassShelfGainDb = file.TargetBassShelfGainDb;
            targetBassShelfFrequencyHz = file.TargetBassShelfFrequencyHz;
            targetBassShelfWidthOctaves = file.TargetBassShelfWidthOctaves;
            targetTrebleShelfGainDb = file.TargetTrebleShelfGainDb;
            targetTrebleShelfFrequencyHz = file.TargetTrebleShelfFrequencyHz;
            targetTrebleShelfWidthOctaves = file.TargetTrebleShelfWidthOctaves;
            targetPresenceGainDb = file.TargetPresenceGainDb;
            targetPresenceFrequencyHz = file.TargetPresenceFrequencyHz;
            targetPresenceWidthOctaves = file.TargetPresenceWidthOctaves;
            targetToleranceDb = file.TargetToleranceDb;
            targetDeviationMode = file.TargetDeviationMode;
            SetAvailability(true);
        }
        else
        {
            sourcePoints = file.Points
                .Select(point => new DataPoint(point.X, point.Y))
                .ToArray();
            phaseUnwrapped = file.PhaseUnwrapped;
            capturedYAxisKey = GetCapturedYAxisKey(file);
            UpdateDrawPoints();
            SetAvailability(true);
        }

        UpdateKindGlyph();
    }

    private bool TrySaveCurrentState(string errorMessage)
    {
        if (SeriesMode == Mode.None || Title == "")
        {
            return false;
        }
        if (kind == OverlayKind.Captured && sourcePoints == null)
        {
            return false;
        }
        if (kind == OverlayKind.Operation && !operationConfigured)
        {
            return false;
        }
        if (kind == OverlayKind.Target && !targetConfigured)
        {
            return false;
        }

        try
        {
            CreateFile().Save();
            return true;
        }
        catch (Exception exception)
        {
            ShowStorageError(errorMessage, exception);
            return false;
        }
    }

    private OverlayFile CreateFile()
    {
        var file = new OverlayFile
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Mode = SeriesMode,
            Slot = Index,
            Kind = kind,
            Title = Title,
            CapturedMagnitudeScale = capturedMagnitudeScale,
            Offset = (double)offsetControl.Value,
            ColorArgb = panel.BackColor.ToArgb(),
            StrokeThickness = strokeThickness,
            LineStyle = lineStyle,
            OpacityPercent = opacityPercent
        };
        file.SetSmoothingCode(smoothingInverseOctaves);

        if (kind == OverlayKind.Operation)
        {
            file.SourceSlotA = sourceSlotA;
            file.SourceSlotB = sourceSlotB;
            file.SourceCurveKeyA = sourceCurveKeyA;
            file.SourceCurveKeyB = sourceCurveKeyB;
            file.Operation = operation;
            file.BlendFrequencyHz = blendFrequencyHz;
            file.BlendWidthOctaves = blendWidthOctaves;
            file.UseAmplitudeSpace = useAmplitudeSpace;
            file.CompareDelayMs = compareDelayMs;
            file.CompareInvertPolarity = compareInvertPolarity;
        }
        else if (kind == OverlayKind.Target)
        {
            file.TargetSourceSlot = targetSourceSlot;
            file.TargetPreset = targetPreset;
            file.TargetTiltDbPerOctave = targetTiltDbPerOctave;
            file.TargetBassShelfGainDb = targetBassShelfGainDb;
            file.TargetBassShelfFrequencyHz = targetBassShelfFrequencyHz;
            file.TargetBassShelfWidthOctaves = targetBassShelfWidthOctaves;
            file.TargetTrebleShelfGainDb = targetTrebleShelfGainDb;
            file.TargetTrebleShelfFrequencyHz = targetTrebleShelfFrequencyHz;
            file.TargetTrebleShelfWidthOctaves = targetTrebleShelfWidthOctaves;
            file.TargetPresenceGainDb = targetPresenceGainDb;
            file.TargetPresenceFrequencyHz = targetPresenceFrequencyHz;
            file.TargetPresenceWidthOctaves = targetPresenceWidthOctaves;
            file.TargetToleranceDb = targetToleranceDb;
            file.TargetDeviationMode = targetDeviationMode;
        }
        else
        {
            file.Points = (sourcePoints ?? Array.Empty<DataPoint>())
                .Select(point => new OverlayPoint(point.X, point.Y))
                .ToArray();
            file.PhaseUnwrapped = phaseUnwrapped;
            file.CapturedYAxisKey = capturedYAxisKey;
        }

        return file;
    }

    // The stored slot configuration expressed as the same snapshot the settings
    // dialog fires for its live preview, so both paths share one build routine.
    private OverlayOperationPreview CurrentOperationSnapshot() => new(
        Title,
        sourceSlotA,
        sourceCurveKeyA,
        sourceSlotB,
        sourceCurveKeyB,
        operation,
        blendFrequencyHz,
        blendWidthOctaves,
        useAmplitudeSpace,
        compareDelayMs,
        compareInvertPolarity,
        panel.BackColor,
        strokeThickness,
        lineStyle,
        opacityPercent,
        smoothingInverseOctaves);

    private DataPoint[]? BuildOperationPoints() =>
        BuildOperationPointsFor(CurrentOperationSnapshot());

    private DataPoint[]? BuildOperationPointsFor(OverlayOperationPreview settings)
    {
        // Complex sum is computed from the Main and Compare transfer IRs by the
        // measurement pipeline (identical FR window / calibration / smoothing), not
        // from operand curves; only the overlay's own smoothing and offset apply here.
        if (settings.Operation is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss)
        {
            return BuildComplexSumPoints(
                settings.CompareDelayMs,
                settings.CompareInvertPolarity,
                settings.SmoothingInverseOctaves,
                showLoss: settings.Operation == OverlayOperation.ComplexSumLoss);
        }

        OverlayOperationSource? sourceA =
            ResolveOperand(settings.SourceCurveKeyA, settings.SourceSlotA);
        OverlayOperationSource? sourceB =
            ResolveOperand(settings.SourceCurveKeyB, settings.SourceSlotB);
        if (sourceA == null || sourceB == null)
        {
            return null;
        }

        // Phase is circular: subtracting two curves needs the wrapped formula whenever
        // either operand is a wrapped (-180..180) representation, so the difference is the
        // shortest angular distance instead of jumping by +/-360. Two unwrapped curves
        // (the default, plus minimum/excess phase) keep the raw subtraction so their slope
        // (and hence delay) survives. Unknown representations are treated as unwrapped.
        bool wrapPhaseDifference = SeriesMode == Mode.PhaseResponse &&
            (sourceA.PhaseUnwrapped == false || sourceB.PhaseUnwrapped == false);

        OverlayPoint[] points = OverlayMath.CalculateOperation(
            sourceA.Points,
            sourceB.Points,
            settings.Operation,
            settings.BlendFrequencyHz,
            settings.BlendWidthOctaves,
            settings.UseAmplitudeSpace,
            wrapPhaseDifference);
        points = OverlayMath.SmoothByOctaves(points, settings.SmoothingInverseOctaves);
        if (points.Length < 2)
        {
            return null;
        }

        double offset = (double)offsetControl.Value;
        return points
            .Select(point => new DataPoint(point.X, point.Y + offset))
            .ToArray();
    }

    private DataPoint[]? BuildComplexSumPoints(
        double delayMs,
        bool invertPolarity,
        int smoothing,
        bool showLoss = false)
    {
        OverlayPoint[]? sumPoints = collection.Form.BuildComplexSumOverlayPoints(
            delayMs,
            invertPolarity,
            showLoss);
        if (sumPoints == null || sumPoints.Length < 2)
        {
            return null;
        }

        OverlayPoint[] smoothed = OverlayMath.SmoothByOctaves(sumPoints, smoothing);
        double offset = (double)offsetControl.Value;
        return smoothed
            .Select(point => new DataPoint(point.X, point.Y + offset))
            .ToArray();
    }

    // Redraws this slot's series with the dialog's candidate settings — operands,
    // operation, styling, everything — while the dialog is open; nothing is
    // committed, so Cancel can restore cleanly.
    private void PreviewOperation(OverlayOperationPreview settings)
    {
        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return;
        }

        RemoveSeries(model);
        DataPoint[]? points = BuildOperationPointsFor(settings);
        if (points != null)
        {
            AddCurveSeries(
                model,
                "curve",
                points,
                settings.Color,
                settings.OpacityPercent,
                settings.StrokeThickness,
                settings.LineStyle,
                settings.Name.Length > 0 ? settings.Name : Title);
        }

        RefreshPlot(model);
    }

    // Clears a live preview after the dialog is cancelled: the stored slot state is
    // unchanged, so simply redraw it — or just remove the preview if the slot was
    // hidden before the dialog opened.
    private void RestoreAfterPreview(bool wasChecked)
    {
        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return;
        }

        RemoveSeries(model);
        if (wasChecked)
        {
            Show();
        }
        else
        {
            RefreshPlot(model);
        }
    }

    private bool TryGetSources(
        out OverlayOperationSource? sourceA,
        out OverlayOperationSource? sourceB)
    {
        sourceA = ResolveOperand(sourceCurveKeyA, sourceSlotA);
        sourceB = ResolveOperand(sourceCurveKeyB, sourceSlotB);
        return sourceA != null && sourceB != null;
    }

    // A live-curve operand (curveKey set) resolves from the current plot each time, so
    // the operation tracks the analysis; otherwise it reads the captured slot.
    private OverlayOperationSource? ResolveOperand(string? curveKey, int slot)
    {
        if (curveKey != null)
        {
            return collection.TryGetLiveCurveSource(curveKey, out OverlayOperationSource? live)
                ? live
                : null;
        }

        return collection.TryGetCaptureSource(slot, out OverlayOperationSource? captured)
            ? captured
            : null;
    }

    private void UpdateDrawPoints()
    {
        drawPoints = BuildCapturedPoints(smoothingInverseOctaves);
    }

    // Parameterized so the settings dialog's live preview can render a candidate
    // smoothing without committing it to the slot first.
    private DataPoint[]? BuildCapturedPoints(int smoothing)
    {
        if (sourcePoints == null)
        {
            return null;
        }

        OverlayPoint[] smoothed = OverlayMath.SmoothByOctaves(
            sourcePoints.Select(point => new OverlayPoint(point.X, point.Y)).ToArray(),
            smoothing);
        double offset = (double)offsetControl.Value;
        return smoothed
            .Select(point => new DataPoint(point.X, point.Y + offset))
            .ToArray();
    }

    // Redraws this slot's series with the captured-curve dialog's candidate
    // styling / smoothing while it is open; nothing is committed, so Cancel can
    // restore cleanly.
    private void PreviewCaptured(OverlayCapturedPreview settings)
    {
        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return;
        }

        RemoveSeries(model);
        AddCurveSeries(
            model,
            "curve",
            BuildCapturedPoints(settings.SmoothingInverseOctaves),
            settings.Color,
            settings.OpacityPercent,
            settings.StrokeThickness,
            settings.LineStyle,
            settings.Name.Length > 0 ? settings.Name : Title,
            capturedYAxisKey);
        RefreshPlot(model);
    }

    private void ResetState()
    {
        kind = OverlayKind.Captured;
        sourcePoints = null;
        drawPoints = null;
        capturedYAxisKey = null;
        phaseUnwrapped = null;
        operationConfigured = false;
        sourceSlotA = 0;
        sourceSlotB = 0;
        sourceCurveKeyA = null;
        sourceCurveKeyB = null;
        operation = OverlayOperation.AMinusB;
        blendFrequencyHz = 1_000;
        blendWidthOctaves = 1;
        useAmplitudeSpace = false;
        compareDelayMs = 0;
        compareInvertPolarity = false;
        targetConfigured = false;
        targetSourceSlot = 0;
        targetPreset = TargetPreset.HarmanRoom;
        targetTiltDbPerOctave = 0;
        targetBassShelfGainDb = 0;
        targetBassShelfFrequencyHz = 100;
        targetBassShelfWidthOctaves = 1.5;
        targetTrebleShelfGainDb = 0;
        targetTrebleShelfFrequencyHz = 5_000;
        targetTrebleShelfWidthOctaves = 1.5;
        targetPresenceGainDb = 0;
        targetPresenceFrequencyHz = 3_000;
        targetPresenceWidthOctaves = 1.0;
        targetToleranceDb = 0;
        targetDeviationMode = TargetDeviationMode.Deviation;
        Title = "";
        SeriesMode = Mode.None;
        strokeThickness = 2;
        lineStyle = OverlayLineStyle.Solid;
        opacityPercent = 100;
        smoothingInverseOctaves = 0;

        updatingControls = true;
        try
        {
            SetChecked(false);
            panel.BackColor = defaultColor;
            offsetControl.Value = defaultOffset;
        }
        finally
        {
            updatingControls = false;
        }

        SetAvailability(false);
        captureButton.Enabled = true;
        UpdateKindGlyph();
    }

    // Shows the slot number plus a compact kind glyph: plain number for a
    // captured curve, ƒ for an operation, △ for a target.
    private void UpdateKindGlyph()
    {
        captureButton.Text = kind switch
        {
            OverlayKind.Operation => $"{Index}\u0192", // \u0192 f
            OverlayKind.Target => $"{Index}\u25B3", // \u25B3 triangle
            _ => $"{Index}"
        };
    }

    private void SetAvailability(bool available)
    {
        checkBox.Enabled = available;
        offsetControl.Enabled = available;
        if (!available)
        {
            SetChecked(false);
        }
    }

    private void SetChecked(bool value)
    {
        updatingControls = true;
        try
        {
            checkBox.Checked = value;
        }
        finally
        {
            updatingControls = false;
        }
    }

    private void RemoveSeries(PlotModel model)
    {
        string prefix = $"overlay:{SeriesMode}:{Index}:";
        List<OxyPlot.Series.Series> existing = model.Series
            .Where(series => series.Tag is string tag &&
                tag.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        foreach (OxyPlot.Series.Series series in existing)
        {
            model.Series.Remove(series);
        }
    }

    private string GetTag(string part) => $"overlay:{SeriesMode}:{Index}:{part}";

    private static string GetCaptureCandidateTitle(LineSeries series)
    {
        return string.IsNullOrWhiteSpace(series.Title)
            ? "Untitled curve"
            : series.Title;
    }

    private static string? GetCapturedYAxisKey(OverlayFile file)
    {
        if (!string.IsNullOrEmpty(file.CapturedYAxisKey))
        {
            return file.CapturedYAxisKey;
        }

        return file.Mode is Mode.FrequencyResponse or Mode.PhaseResponse or Mode.GroupDelay or Mode.LiveSpectrum &&
            file.Title.Contains("Coherence", StringComparison.OrdinalIgnoreCase)
                ? PlotModelFactory.CoherenceAxisKey
                : null;
    }

    private void RefreshPlot(PlotModel model)
    {
        model.InvalidatePlot(true);
        collection.PlotView.Refresh();
        collection.NotifyPlotChanged();
    }

    private static LineStyle ToOxyLineStyle(OverlayLineStyle value)
    {
        return value switch
        {
            OverlayLineStyle.Dash => LineStyle.Dash,
            OverlayLineStyle.Dot => LineStyle.Dot,
            OverlayLineStyle.DashDot => LineStyle.DashDot,
            _ => LineStyle.Solid
        };
    }

    private void ShowStorageError(string message, Exception exception)
    {
        MessageBox.Show(
            collection.Form,
            $"{message}{Environment.NewLine}{exception.Message}",
            "Overlay storage",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

internal sealed record OverlayOperationSource(
    int Slot,
    string Title,
    IReadOnlyList<OverlayPoint> Points,
    bool? PhaseUnwrapped = null);
