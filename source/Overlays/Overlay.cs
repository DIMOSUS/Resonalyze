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
        foreach (Overlay overlay in overlays)
        {
            overlay.Prepare(mode);
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
            if (overlay.Checked && overlay.SeriesMode == mode)
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

    internal IReadOnlyList<OverlaySlotOption> GetCaptureSourceOptions()
    {
        return overlays
            .Where(overlay =>
                overlay.Kind == OverlayKind.Captured &&
                overlay.SeriesMode == Form.CurrentMode &&
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
                candidate.SeriesMode == Form.CurrentMode);
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

        captureMenu = BuildCaptureMenu();

        toolTip.SetToolTip(offsetControl, "Overlay vertical offset (dB)");
        toolTip.SetToolTip(checkBox, "Show / hide this overlay");
        toolTip.SetToolTip(
            captureButton,
            "Capture a curve into this slot, or switch it to a calculated overlay");
        toolTip.SetToolTip(
            settingsButton,
            "Overlay name, kind, color, line style and clearing");

        checkBox.CheckedChanged += CheckBoxChanged;
        captureButton.Click += CaptureButtonClick;
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
        if (model == null)
        {
            return;
        }

        DataPoint[]? points = kind == OverlayKind.Operation
            ? BuildOperationPoints()
            : drawPoints;
        if (points == null || points.Length < 2 || Title == "")
        {
            SetChecked(false);
            return;
        }

        RemoveSeries(model);
        SetChecked(true);

        Color color = panel.BackColor;
        byte alpha = (byte)Math.Round(opacityPercent / 100.0 * 255);
        var series = new LineSeries
        {
            Color = OxyColor.FromArgb(alpha, color.R, color.G, color.B),
            StrokeThickness = strokeThickness,
            LineStyle = ToOxyLineStyle(lineStyle),
            Title = Title,
            Tag = GetTag()
        };
        string? trackerFormat = OverlayCollection.GetTrackerFormatString(SeriesMode);
        if (!string.IsNullOrEmpty(trackerFormat))
        {
            series.TrackerFormatString = trackerFormat;
        }
        series.Points.AddRange(points);
        model.Series.Add(series);
        RefreshPlot(model);
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
    /// Re-evaluates availability of an operation slot when its captured sources
    /// change. Captured slots are unaffected.
    /// </summary>
    public void RefreshSources()
    {
        if (kind != OverlayKind.Operation)
        {
            return;
        }

        bool wasChecked = Checked;
        bool available = operationConfigured &&
            TryGetSources(out _, out _);
        checkBox.Enabled = available;
        offsetControl.Enabled = operationConfigured;
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

    private ContextMenuStrip BuildCaptureMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capture curve…", null, (_, _) => CaptureFromPlot());
        menu.Items.Add(
            "Calculated overlay…",
            null,
            (_, _) => ConfigureOperation());
        return menu;
    }

    private void CaptureButtonClick(object? sender, EventArgs e)
    {
        captureMenu.Show(captureButton, new Point(0, captureButton.Height));
    }

    private void CaptureFromPlot()
    {
        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return;
        }

        List<LineSeries> candidates = model.Series
            .OfType<LineSeries>()
            .Where(series => series.Tag is not string tag ||
                !tag.StartsWith("overlay:", StringComparison.Ordinal))
            .ToList();
        int selection = SelectSeriesIndex(candidates);
        if (selection < 0)
        {
            return;
        }

        LineSeries selected = candidates[selection];
        if (selected.Points.Count < 2)
        {
            return;
        }

        var points = new DataPoint[selected.Points.Count];
        selected.Points.CopyTo(points);
        string title = $"Overlay {Index}: {selected.Title ?? string.Empty}";
        Mode mode = collection.Form.CurrentMode;

        Hide();
        kind = OverlayKind.Captured;
        operationConfigured = false;
        sourcePoints = points;
        SeriesMode = mode;
        Title = title;
        UpdateDrawPoints();

        try
        {
            SaveCurrentState();
        }
        catch (Exception exception)
        {
            ShowStorageError("Overlay could not be saved.", exception);
            return;
        }

        SetAvailability(true);
        Show();
        collection.NotifyCapturedOverlayChanged();
    }

    private void SettingsButtonClick(object? sender, EventArgs e)
    {
        if (SeriesMode != collection.Form.CurrentMode)
        {
            return;
        }

        if (kind == OverlayKind.Operation)
        {
            ConfigureOperation();
            return;
        }

        if (!HasCaptureData)
        {
            return;
        }

        ConfigureCaptured();
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
        SaveCurrentState();

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

        SaveCurrentState();
        RefreshSources();
        if (checkBox.Enabled)
        {
            Show();
        }
    }

    private void ClearOverlay()
    {
        if (SeriesMode != collection.Form.CurrentMode)
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

        bool wasChecked = Checked;
        if (kind == OverlayKind.Captured)
        {
            UpdateDrawPoints();
        }
        SaveCurrentState();
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
            if (SeriesMode == collection.Form.CurrentMode)
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
        else
        {
            sourcePoints = file.Points
                .Select(point => new DataPoint(point.X, point.Y))
                .ToArray();
            UpdateDrawPoints();
            SetAvailability(true);
        }
    }

    private void SaveCurrentState()
    {
        if (SeriesMode == Mode.None || Title == "")
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

        try
        {
            CreateFile().Save();
        }
        catch (Exception exception)
        {
            ShowStorageError("Overlay changes could not be saved.", exception);
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
        OxyPlot.Series.Series? existing = model.Series.FirstOrDefault(
            series => Equals(series.Tag, GetTag()));
        if (existing != null)
        {
            model.Series.Remove(existing);
        }
    }

    private string GetTag() => $"overlay:{SeriesMode}:{Index}";

    private int SelectSeriesIndex(IReadOnlyList<LineSeries> candidates)
    {
        if (candidates.Count == 1)
        {
            return 0;
        }
        if (candidates.Count == 0)
        {
            return -1;
        }

        using var dialog = new SelectSeries();
        foreach (LineSeries series in candidates)
        {
            dialog.AddOption(series.Title ?? string.Empty);
        }
        dialog.SetSelection(0);
        return dialog.ShowDialog(collection.Form) == DialogResult.OK
            ? dialog.GetSelect()
            : -1;
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
