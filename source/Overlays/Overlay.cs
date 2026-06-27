using System.Diagnostics;
using OxyPlot;
using OxyPlot.Series;
using Button = System.Windows.Forms.Button;
using CheckBox = System.Windows.Forms.CheckBox;
using ToolTip = System.Windows.Forms.ToolTip;

namespace Resonalyze;

public sealed class OverlayCollection
{
    private readonly List<Overlay> overlays = new();
    private readonly Action notifyPlotChanged;

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
        Button templateSettingsButton = templatePanel.Controls
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "buttonOverlaySettings1")
            ?? throw new InvalidOperationException(
                "Overlay template settings button is missing.");
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
            templateSettingsButton,
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
            Button settingsButton = CreateSettingsButton(templateSettingsButton, index);
            Button captureButton = CreateCaptureButton(templateCaptureButton, index);

            panel.Controls.Add(checkBox);
            panel.Controls.Add(offset);
            panel.Controls.Add(settingsButton);
            panel.Controls.Add(captureButton);

            overlays.Add(new Overlay(
                panel,
                captureButton,
                settingsButton,
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
        foreach (Overlay overlay in overlays)
        {
            if (overlay.Checked && overlay.SeriesMode == OverlayModeFor(mode))
            {
                overlay.Show();
            }
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

    public static bool SupportsMode(Mode mode)
    {
        return mode is
            Mode.ImpulseResponse or
            Mode.FrequencyResponse or
            Mode.PhaseResponse or
            Mode.GroupDelay or
            Mode.LiveSpectrum or
            Mode.Autocorrelation;
    }

    // Frequency Response and Live Spectrum share the same frequency/dB axes, so
    // they share a single set of overlay slots and on-disk storage. Both map to a
    // single canonical mode used for every overlay comparison, path, and tag.
    public static Mode OverlayModeFor(Mode mode) =>
        mode == Mode.LiveSpectrum ? Mode.FrequencyResponse : mode;

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

    private static Button CreateSettingsButton(Button templateSettingsButton, int index)
    {
        return new Button
        {
            FlatStyle = templateSettingsButton.FlatStyle,
            BackColor = templateSettingsButton.BackColor,
            ForeColor = templateSettingsButton.ForeColor,
            Location = templateSettingsButton.Location,
            Name = $"buttonOverlaySettings{index}",
            Size = templateSettingsButton.Size,
            Text = templateSettingsButton.Text,
            UseVisualStyleBackColor = templateSettingsButton.UseVisualStyleBackColor,
            UseCompatibleTextRendering = templateSettingsButton.UseCompatibleTextRendering
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
    private readonly Button settingsButton;
    private readonly DarkNumericUpDown offsetControl;
    private readonly CheckBox checkBox;
    private readonly Color defaultColor;
    private readonly decimal defaultOffset;
    private readonly ContextMenuStrip captureMenu;
    private readonly ToolStripMenuItem captureCurveMenuItem;
    private readonly ToolStripItem exportDeviationMenuItem;
    // True only while processing the very mouse press that dismissed the capture
    // menu, so that same press toggles it closed instead of immediately reopening.
    private bool captureMenuClosedByPress;

    private OverlayKind kind = OverlayKind.Captured;
    private bool updatingControls;

    // Presentation (all kinds).
    private double strokeThickness = 2;
    private OverlayLineStyle lineStyle = OverlayLineStyle.Solid;
    private int opacityPercent = 100;
    private int smoothingInverseOctaves;

    // Captured kind.
    private DataPoint[]? sourcePoints;
    private DataPoint[]? drawPoints;

    // Operation kind.
    private bool operationConfigured;
    private int sourceSlotA;
    private int sourceSlotB;
    private OverlayOperation operation = OverlayOperation.AMinusB;
    private double blendFrequencyHz = 1_000;
    private double blendWidthOctaves = 1;
    private bool useAmplitudeSpace;

    // Target kind.
    private bool targetConfigured;
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
        Button settingsButton,
        DarkNumericUpDown offsetControl,
        CheckBox checkBox,
        int index,
        ToolTip toolTip,
        OverlayCollection collection)
    {
        this.panel = panel;
        this.captureButton = captureButton;
        this.settingsButton = settingsButton;
        this.offsetControl = offsetControl;
        this.checkBox = checkBox;
        this.collection = collection;
        defaultColor = panel.BackColor;
        defaultOffset = offsetControl.Value;
        Index = index;

        captureMenu = BuildCaptureMenu(
            out captureCurveMenuItem,
            out exportDeviationMenuItem);
        captureMenu.Closed += CaptureMenuClosed;

        toolTip.SetToolTip(offsetControl, "Overlay vertical offset (dB)");
        toolTip.SetToolTip(checkBox, "Show / hide this overlay");
        toolTip.SetToolTip(
            captureButton,
            "Capture a curve into this slot, or switch it to a calculated overlay");
        toolTip.SetToolTip(
            settingsButton,
            "Overlay name, kind, color, line style and clearing");

        checkBox.CheckedChanged += CheckBoxChanged;
        captureButton.MouseDown += CaptureButtonMouseDown;
        settingsButton.Click += SettingsButtonClick;
        offsetControl.ValueChanged += OffsetValueChanged;

        ResetState();
    }

    public int Index { get; }
    public string Title { get; private set; } = "";
    public Mode SeriesMode { get; private set; }
    public bool Checked => checkBox.Checked;
    public OverlayKind Kind => kind;
    public bool HasCaptureData => sourcePoints is { Length: > 1 };

    // The overlay mode for the current view; Frequency Response and Live Spectrum
    // collapse to one shared mode so they use the same slots and storage.
    private Mode CurrentOverlayMode =>
        OverlayCollection.OverlayModeFor(collection.Form.CurrentMode);

    public void Prepare(Mode mode)
    {
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
            _ => AddCurveSeries(model, "curve", drawPoints)
        };

        if (drawn)
        {
            SetChecked(true);
            RefreshPlot(model);
        }
        else
        {
            SetChecked(false);
        }
    }

    private bool AddCurveSeries(PlotModel model, string part, DataPoint[]? points)
    {
        if (points == null || points.Length < 2)
        {
            return false;
        }

        Color color = panel.BackColor;
        byte alpha = (byte)Math.Round(opacityPercent / 100.0 * 255);
        var series = new LineSeries
        {
            Color = OxyColor.FromArgb(alpha, color.R, color.G, color.B),
            StrokeThickness = strokeThickness,
            LineStyle = ToOxyLineStyle(lineStyle),
            Title = Title,
            Tag = GetTag(part)
        };
        string? trackerFormat = OverlayCollection.GetTrackerFormatString(SeriesMode);
        if (!string.IsNullOrEmpty(trackerFormat))
        {
            series.TrackerFormatString = trackerFormat;
        }
        series.Points.AddRange(points);
        model.Series.Add(series);
        return true;
    }

    private bool AddTargetSeries(PlotModel model)
    {
        OverlayPoint[]? source = ResolveTargetSource();
        if (source == null || source.Length < 2)
        {
            return false;
        }

        double offset = (double)offsetControl.Value;
        TargetCurveResult result = OverlayMath.BuildTarget(
            source,
            CurrentTargetSpec(),
            offset,
            targetToleranceDb,
            smoothingInverseOctaves,
            targetDeviationMode);
        if (result.Target.Length < 2)
        {
            return false;
        }

        Color color = panel.BackColor;
        byte alpha = (byte)Math.Round(opacityPercent / 100.0 * 255);
        OxyColor lineColor = OxyColor.FromArgb(alpha, color.R, color.G, color.B);
        string? trackerFormat = OverlayCollection.GetTrackerFormatString(SeriesMode);

        // Tolerance band first so the curves draw on top of it.
        if (result.ToleranceUpper.Length >= 2 &&
            result.ToleranceLower.Length == result.ToleranceUpper.Length)
        {
            var band = new OxyPlot.Series.AreaSeries
            {
                Color = OxyColors.Transparent,
                Fill = OxyColor.FromArgb(40, color.R, color.G, color.B),
                StrokeThickness = 0,
                Tag = GetTag("tolerance")
            };
            band.Points.AddRange(result.ToleranceUpper.Select(p => new DataPoint(p.X, p.Y)));
            band.Points2.AddRange(result.ToleranceLower.Select(p => new DataPoint(p.X, p.Y)));
            model.Series.Add(band);
        }

        var targetSeries = new LineSeries
        {
            Color = lineColor,
            StrokeThickness = strokeThickness,
            LineStyle = ToOxyLineStyle(lineStyle),
            Title = $"{Title} (target)",
            Tag = GetTag("target")
        };
        if (!string.IsNullOrEmpty(trackerFormat))
        {
            targetSeries.TrackerFormatString = trackerFormat;
        }
        targetSeries.Points.AddRange(result.Target.Select(p => new DataPoint(p.X, p.Y)));
        model.Series.Add(targetSeries);

        if (result.Deviation.Length >= 2)
        {
            string deviationLabel = targetDeviationMode == TargetDeviationMode.Correction
                ? "EQ correction"
                : "deviation";
            var deviationSeries = new LineSeries
            {
                Color = lineColor,
                StrokeThickness = Math.Max(1.0, strokeThickness - 1.0),
                LineStyle = LineStyle.Solid,
                Title = $"{Title} ({deviationLabel})",
                Tag = GetTag("deviation")
            };
            if (!string.IsNullOrEmpty(trackerFormat))
            {
                deviationSeries.TrackerFormatString = trackerFormat;
            }
            deviationSeries.Points.AddRange(
                result.Deviation.Select(p => new DataPoint(p.X, p.Y)));
            model.Series.Add(deviationSeries);
        }

        return true;
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

    private OverlayPoint[]? ResolveTargetSource()
    {
        if (targetSourceSlot != 0)
        {
            return collection.TryGetCaptureSource(
                targetSourceSlot,
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

        LineSeries? primary = model.Series
            .OfType<LineSeries>()
            .FirstOrDefault(series => Equals(series.Tag, "live-spectrum:primary"));
        primary ??= model.Series
            .OfType<LineSeries>()
            .FirstOrDefault(series =>
                series.Tag is not string tag ||
                (!tag.StartsWith("overlay:", StringComparison.Ordinal) &&
                 !tag.StartsWith("live-spectrum:", StringComparison.Ordinal)));
        if (primary == null || primary.Points.Count < 2)
        {
            return null;
        }

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
        bool available = operationConfigured && TryGetSources(out _, out _);
        ApplyCalculatedAvailability(
            available,
            operationConfigured,
            wasChecked);
    }

    private void RefreshTargetSources()
    {
        bool wasChecked = Checked;
        bool available = targetConfigured &&
            (targetSourceSlot == 0 || ResolveTargetSource() is { Length: > 1 });
        ApplyCalculatedAvailability(
            available,
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
        settingsButton.Enabled = SeriesMode != Mode.None;

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
                .ToArray());
    }

    private ContextMenuStrip BuildCaptureMenu(
        out ToolStripMenuItem captureCurveItem,
        out ToolStripItem exportDeviationItem)
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
        return menu;
    }

    private void CaptureMenuClosed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        // A press on the button while the menu is open is treated by WinForms as a
        // click outside the menu and closes it. Flag that here and clear the flag
        // once the current message has been fully processed, so the same press is
        // recognised as a toggle-close in CaptureButtonMouseDown regardless of
        // whether Closed fires before or after MouseDown.
        captureMenuClosedByPress = true;
        captureButton.BeginInvoke(() => captureMenuClosedByPress = false);
    }

    private void CaptureButtonMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // Toggle closed if this press is dismissing an open menu: either the menu
        // is still visible (MouseDown ran before the outside-click close) or it has
        // just been closed by this very press (Closed ran first).
        if (captureMenu.Visible || captureMenuClosedByPress)
        {
            return;
        }

        // Open after the current mouse message has been processed. Showing a
        // dropdown synchronously from within the button's mouse-down handler can be
        // swallowed by the focus/activation change the press triggers, leaving the
        // button visibly pressed but no menu on screen.
        captureButton.BeginInvoke(ShowCaptureMenu);
    }

    private void ShowCaptureMenu()
    {
        if (captureMenu.Visible)
        {
            return;
        }

        RebuildCaptureCurveMenu();
        // The deviation export only applies to a target slot, and its label
        // reflects the current deviation mode.
        exportDeviationMenuItem.Visible = kind == OverlayKind.Target;
        exportDeviationMenuItem.Text =
            targetDeviationMode == TargetDeviationMode.Correction
                ? "Export EQ correction…"
                : "Export deviation…";
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

        OverlayPoint[]? source = ResolveTargetSource();
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
                OverlayPoint[]? source = ResolveTargetSource();
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

    private void SettingsButtonClick(object? sender, EventArgs e)
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
            sources);
        if (dialog.ShowDialog(collection.Form) != DialogResult.OK)
        {
            return;
        }

        Hide();
        kind = OverlayKind.Target;
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
        using var dialog = new OverlaySettingsDialog(
            SeriesMode,
            Title,
            panel.BackColor,
            strokeThickness,
            lineStyle,
            opacityPercent,
            smoothingInverseOctaves);
        if (dialog.ShowDialog(collection.Form) != DialogResult.OK)
        {
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
        using var dialog = new OverlayOperationSettingsDialog(
            SeriesMode,
            operationConfigured ? Title : $"Calculated overlay {Index}",
            sourceSlotA,
            sourceSlotB,
            operation,
            blendFrequencyHz,
            blendWidthOctaves,
            useAmplitudeSpace,
            kind == OverlayKind.Operation ? panel.BackColor : defaultColor,
            strokeThickness,
            kind == OverlayKind.Operation ? lineStyle : OverlayLineStyle.Dash,
            opacityPercent,
            smoothingInverseOctaves,
            sources);
        if (dialog.ShowDialog(collection.Form) != DialogResult.OK)
        {
            return;
        }

        Hide();
        kind = OverlayKind.Operation;
        Title = dialog.OverlayName;
        sourceSlotA = dialog.SourceSlotA;
        sourceSlotB = dialog.SourceSlotB;
        operation = dialog.Operation;
        blendFrequencyHz = dialog.BlendFrequencyHz;
        blendWidthOctaves = dialog.BlendWidthOctaves;
        useAmplitudeSpace = dialog.UseAmplitudeSpace;
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
        TrySaveCurrentState("Overlay changes could not be saved.");
        if (wasChecked)
        {
            Show();
        }
        collection.NotifyCapturedOverlayChanged();
    }

    private void CheckBoxChanged(object? sender, EventArgs e)
    {
        if (updatingControls)
        {
            return;
        }

        if (checkBox.Checked)
        {
            if (SeriesMode == CurrentOverlayMode)
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
        strokeThickness = file.StrokeThickness;
        lineStyle = file.LineStyle;
        opacityPercent = file.OpacityPercent;
        smoothingInverseOctaves = file.SmoothingInverseOctaves;

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
            operation = file.Operation;
            blendFrequencyHz = file.BlendFrequencyHz;
            blendWidthOctaves = file.BlendWidthOctaves;
            useAmplitudeSpace = file.UseAmplitudeSpace;
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
            Offset = (double)offsetControl.Value,
            ColorArgb = panel.BackColor.ToArgb(),
            StrokeThickness = strokeThickness,
            LineStyle = lineStyle,
            OpacityPercent = opacityPercent,
            SmoothingInverseOctaves = smoothingInverseOctaves
        };

        if (kind == OverlayKind.Operation)
        {
            file.SourceSlotA = sourceSlotA;
            file.SourceSlotB = sourceSlotB;
            file.Operation = operation;
            file.BlendFrequencyHz = blendFrequencyHz;
            file.BlendWidthOctaves = blendWidthOctaves;
            file.UseAmplitudeSpace = useAmplitudeSpace;
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
        }

        return file;
    }

    private DataPoint[]? BuildOperationPoints()
    {
        if (!TryGetSources(
                out OverlayOperationSource? sourceA,
                out OverlayOperationSource? sourceB))
        {
            return null;
        }

        OverlayPoint[] points = OverlayMath.CalculateOperation(
            sourceA!.Points,
            sourceB!.Points,
            operation,
            blendFrequencyHz,
            blendWidthOctaves,
            useAmplitudeSpace);
        points = OverlayMath.SmoothByOctaves(points, smoothingInverseOctaves);
        if (points.Length < 2)
        {
            return null;
        }

        double offset = (double)offsetControl.Value;
        return points
            .Select(point => new DataPoint(point.X, point.Y + offset))
            .ToArray();
    }

    private bool TryGetSources(
        out OverlayOperationSource? sourceA,
        out OverlayOperationSource? sourceB)
    {
        bool hasA = collection.TryGetCaptureSource(sourceSlotA, out sourceA);
        bool hasB = collection.TryGetCaptureSource(sourceSlotB, out sourceB);
        return hasA && hasB && sourceA != null && sourceB != null;
    }

    private void UpdateDrawPoints()
    {
        if (sourcePoints == null)
        {
            drawPoints = null;
            return;
        }

        OverlayPoint[] smoothed = OverlayMath.SmoothByOctaves(
            sourcePoints.Select(point => new OverlayPoint(point.X, point.Y)).ToArray(),
            smoothingInverseOctaves);
        double offset = (double)offsetControl.Value;
        drawPoints = smoothed
            .Select(point => new DataPoint(point.X, point.Y + offset))
            .ToArray();
    }

    private void ResetState()
    {
        kind = OverlayKind.Captured;
        sourcePoints = null;
        drawPoints = null;
        operationConfigured = false;
        sourceSlotA = 0;
        sourceSlotB = 0;
        operation = OverlayOperation.AMinusB;
        blendFrequencyHz = 1_000;
        blendWidthOctaves = 1;
        useAmplitudeSpace = false;
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
        settingsButton.Enabled = true;
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
        settingsButton.Enabled = available;
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
    IReadOnlyList<OverlayPoint> Points);
