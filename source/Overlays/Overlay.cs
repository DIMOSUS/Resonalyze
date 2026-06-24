using System.Diagnostics;
using OxyPlot;
using OxyPlot.Series;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;
using Button = System.Windows.Forms.Button;
using CheckBox = System.Windows.Forms.CheckBox;
using ToolTip = System.Windows.Forms.ToolTip;

namespace Resonalyze;

public sealed class OverlayCollection
{
    private readonly List<Overlay> overlays = new();
    private readonly List<CalculatedOverlay> calculatedOverlays = new();
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
        Button templateSaveButton = templatePanel.Controls
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "buttonSaveOverlay")
            ?? throw new InvalidOperationException(
                "Overlay template save button is missing.");
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
            templateSaveButton,
            templateSettingsButton,
            templateOffset,
            templateCheckBox,
            1,
            toolTip,
            this));

        form.SuspendLayout();
        container.SuspendLayout();

        var random = new Random(3);
        for (int index = 2; index <= OverlayOperationFile.MaximumSlot; index++)
        {
            Panel panel = CreatePanel(templatePanel, index, random);
            CheckBox checkBox = CreateCheckBox(templateCheckBox, index);
            DarkNumericUpDown offset = CreateOffset(templateOffset, index);
            Button settingsButton = CreateSettingsButton(templateSettingsButton, index);

            panel.Controls.Add(checkBox);
            panel.Controls.Add(offset);
            panel.Controls.Add(settingsButton);

            if (index <= OverlayFile.MaximumSlotCount)
            {
                Button saveButton = CreateSaveButton(
                    templateSaveButton,
                    index);
                panel.Controls.Add(saveButton);
                overlays.Add(new Overlay(
                    panel,
                    saveButton,
                    settingsButton,
                    offset,
                    checkBox,
                    index,
                    toolTip,
                    this));
            }
            else
            {
                var operationLabel = new Label
                {
                    AutoSize = false,
                    BackColor = UiPalette.DialogBackground,
                    Font = templateSaveButton.Font,
                    ForeColor = Color.White,
                    Location = templateSaveButton.Location,
                    Name = $"operationLabel{index}",
                    Size = templateSaveButton.Size,
                    Text = "--",
                    TextAlign = ContentAlignment.MiddleCenter
                };
                panel.Controls.Add(operationLabel);
                calculatedOverlays.Add(new CalculatedOverlay(
                    panel,
                    operationLabel,
                    settingsButton,
                    offset,
                    checkBox,
                    index,
                    toolTip,
                    this));
            }

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

        foreach (CalculatedOverlay overlay in calculatedOverlays)
        {
            overlay.Prepare(mode);
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

        foreach (CalculatedOverlay overlay in calculatedOverlays)
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

        foreach (CalculatedOverlay overlay in calculatedOverlays)
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

    internal IReadOnlyList<OverlaySlotOption> GetOrdinaryOverlayOptions()
    {
        return overlays
            .Where(overlay =>
                overlay.SeriesMode == Form.CurrentMode &&
                overlay.HasData)
            .Select(overlay => new OverlaySlotOption(
                overlay.Index,
                overlay.Title))
            .ToArray();
    }

    internal bool TryGetOrdinaryOverlay(
        int slot,
        out OverlayOperationSource? source)
    {
        Overlay? overlay = overlays.FirstOrDefault(
            candidate =>
                candidate.Index == slot &&
                candidate.SeriesMode == Form.CurrentMode);
        source = overlay?.CreateOperationSource();
        return source != null;
    }

    internal void NotifyOrdinaryOverlayChanged()
    {
        foreach (CalculatedOverlay overlay in calculatedOverlays)
        {
            overlay.RefreshSources();
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
                "{0}\n{2:0.0} Hz\n{4:0.0}\u00B0", // u00B0 degree char
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

    private static Button CreateSaveButton(Button template, int index)
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

public sealed class Overlay
{
    private readonly OverlayCollection collection;
    private readonly Panel panel;
    private readonly DarkNumericUpDown offsetControl;
    private readonly Button settingsButton;
    private readonly CheckBox checkBox;
    private readonly Color defaultColor;
    private readonly decimal defaultOffset;

    private DataPoint[]? sourcePoints;
    private DataPoint[]? drawPoints;
    private bool updatingControls;
    private double strokeThickness = 2;
    private OverlayLineStyle lineStyle = OverlayLineStyle.Solid;
    private int opacityPercent = 100;
    private int smoothingInverseOctaves;

    public Overlay(
        Panel panel,
        Button saveButton,
        Button settingsButton,
        DarkNumericUpDown offsetControl,
        CheckBox checkBox,
        int index,
        ToolTip toolTip,
        OverlayCollection collection)
    {
        this.panel = panel;
        this.settingsButton = settingsButton;
        this.offsetControl = offsetControl;
        this.checkBox = checkBox;
        this.collection = collection;
        defaultColor = panel.BackColor;
        defaultOffset = offsetControl.Value;
        Index = index;

        toolTip.SetToolTip(offsetControl, "Overlay offset");
        toolTip.SetToolTip(checkBox, "Show/Hide overlay");
        toolTip.SetToolTip(saveButton, "Create an overlay based on a curve");
        toolTip.SetToolTip(
            settingsButton,
            "Overlay name, color, line style and clearing");

        checkBox.CheckedChanged += CheckBoxChanged;
        saveButton.Click += SaveButtonClick;
        settingsButton.Click += SettingsButtonClick;
        offsetControl.ValueChanged += OffsetValueChanged;

        ResetState();
    }

    public int Index { get; }
    public string Title { get; private set; } = "";
    public Mode SeriesMode { get; private set; }
    public bool Checked => checkBox.Checked;
    public bool HasData => sourcePoints is { Length: > 1 };

    public void Prepare(Mode mode)
    {
        ResetState();
        if (mode == Mode.None)
        {
            return;
        }

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
        if (model == null || drawPoints == null || Title == "")
        {
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
        series.Points.AddRange(drawPoints);
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

    internal OverlayOperationSource? CreateOperationSource()
    {
        if (drawPoints == null || Title == "")
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

    private void SaveButtonClick(object? sender, EventArgs e)
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

        try
        {
            CreateFile(mode, title, points).Save();
        }
        catch (Exception exception)
        {
            ShowStorageError("Overlay could not be saved.", exception);
            return;
        }

        Hide();
        sourcePoints = points;
        SeriesMode = mode;
        Title = title;
        UpdateDrawPoints();
        SetAvailability(true);
        Show();
        collection.NotifyOrdinaryOverlayChanged();
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
        collection.NotifyOrdinaryOverlayChanged();
    }

    private void SettingsButtonClick(object? sender, EventArgs e)
    {
        if (!HasData || SeriesMode != collection.Form.CurrentMode)
        {
            return;
        }

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
        collection.NotifyOrdinaryOverlayChanged();
    }

    private void OffsetValueChanged(object? sender, EventArgs e)
    {
        if (updatingControls || sourcePoints == null)
        {
            return;
        }

        bool wasChecked = Checked;
        UpdateDrawPoints();
        SaveCurrentState();
        if (wasChecked)
        {
            Show();
        }
        collection.NotifyOrdinaryOverlayChanged();
    }

    private void CheckBoxChanged(object? sender, EventArgs e)
    {
        if (updatingControls)
        {
            return;
        }

        if (checkBox.Checked)
        {
            if (HasData && SeriesMode == collection.Form.CurrentMode)
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
        sourcePoints = file.Points
            .Select(point => new DataPoint(point.X, point.Y))
            .ToArray();
        SeriesMode = file.Mode;
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

        UpdateDrawPoints();
        SetAvailability(true);
    }

    private OverlayFile CreateFile(
        Mode mode,
        string title,
        IReadOnlyList<DataPoint> points)
    {
        return new OverlayFile
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Mode = mode,
            Slot = Index,
            Title = title,
            Offset = (double)offsetControl.Value,
            ColorArgb = panel.BackColor.ToArgb(),
            StrokeThickness = strokeThickness,
            LineStyle = lineStyle,
            OpacityPercent = opacityPercent,
            SmoothingInverseOctaves = smoothingInverseOctaves,
            Points = points
                .Select(point => new OverlayPoint(point.X, point.Y))
                .ToArray()
        };
    }

    private void SaveCurrentState()
    {
        if (sourcePoints == null || SeriesMode == Mode.None || Title == "")
        {
            return;
        }

        try
        {
            CreateFile(SeriesMode, Title, sourcePoints).Save();
        }
        catch (Exception exception)
        {
            ShowStorageError("Overlay changes could not be saved.", exception);
        }
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
        sourcePoints = null;
        drawPoints = null;
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

    private string GetTag() => $"overlay:{SeriesMode}:{Index}:curve";

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

internal sealed class CalculatedOverlay
{
    private readonly OverlayCollection collection;
    private readonly Panel panel;
    private readonly Label operationLabel;
    private readonly Button settingsButton;
    private readonly DarkNumericUpDown offsetControl;
    private readonly CheckBox checkBox;
    private readonly Color defaultColor;
    private readonly decimal defaultOffset;

    private bool configured;
    private bool updatingControls;
    private int sourceSlotA;
    private int sourceSlotB;
    private OverlayOperation operation = OverlayOperation.AMinusB;
    private double blendFrequencyHz = 1_000;
    private double blendWidthOctaves = 1;
    private bool useAmplitudeSpace;
    private double strokeThickness = 2;
    private OverlayLineStyle lineStyle = OverlayLineStyle.Dash;
    private int opacityPercent = 100;
    private int smoothingInverseOctaves;

    public CalculatedOverlay(
        Panel panel,
        Label operationLabel,
        Button settingsButton,
        DarkNumericUpDown offsetControl,
        CheckBox checkBox,
        int index,
        ToolTip toolTip,
        OverlayCollection collection)
    {
        this.panel = panel;
        this.operationLabel = operationLabel;
        this.settingsButton = settingsButton;
        this.offsetControl = offsetControl;
        this.checkBox = checkBox;
        this.collection = collection;
        defaultColor = panel.BackColor;
        defaultOffset = offsetControl.Value;
        Index = index;

        toolTip.SetToolTip(operationLabel, "Current overlay operation");
        toolTip.SetToolTip(offsetControl, "Calculated overlay offset");
        toolTip.SetToolTip(checkBox, "Show/Hide calculated overlay");
        toolTip.SetToolTip(settingsButton, "Configure calculated overlay");

        checkBox.CheckedChanged += CheckBoxChanged;
        settingsButton.Click += SettingsButtonClick;
        offsetControl.ValueChanged += OffsetValueChanged;
        ResetState();
    }

    public int Index { get; }
    public string Title { get; private set; } = "";
    public Mode SeriesMode { get; private set; }
    public bool Checked => checkBox.Checked;

    public void Prepare(Mode mode)
    {
        ResetState();
        settingsButton.Enabled = mode != Mode.None;
        if (mode == Mode.None)
        {
            return;
        }

        SeriesMode = mode;
        try
        {
            OverlayOperationFile? file =
                OverlayOperationFile.Load(mode, Index);
            if (file != null)
            {
                ApplyFile(file);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Failed to load calculated overlay {Index} for {mode}: {exception}");
        }

        RefreshSources();
    }

    public void RefreshSources()
    {
        bool wasChecked = Checked;
        bool available = configured &&
            TryGetSources(out _, out _);
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

    public void Show()
    {
        PlotModel? model = collection.PlotView.Model;
        if (model == null ||
            !TryGetSources(out OverlayOperationSource? sourceA, out OverlayOperationSource? sourceB))
        {
            SetChecked(false);
            return;
        }

        OverlayPoint[] points = OverlayMath.CalculateOperation(
            sourceA!.Points,
            sourceB!.Points,
            operation,
            blendFrequencyHz,
            blendWidthOctaves,
            useAmplitudeSpace);
        points = OverlayMath.SmoothByOctaves(
            points,
            smoothingInverseOctaves);
        if (points.Length < 2)
        {
            SetChecked(false);
            return;
        }

        RemoveSeries(model);
        SetChecked(true);

        double offset = (double)offsetControl.Value;
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
        series.Points.AddRange(points.Select(
            point => new DataPoint(point.X, point.Y + offset)));
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

    private void SettingsButtonClick(object? sender, EventArgs e)
    {
        IReadOnlyList<OverlaySlotOption> sources =
            collection.GetOrdinaryOverlayOptions();
        if (sources.Count < 2)
        {
            MessageBox.Show(
                collection.Form,
                "Save at least two ordinary overlays in slots 1-10 first.",
                "Calculated overlay",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OverlayOperationSettingsDialog(
            SeriesMode,
            configured ? Title : $"Calculated overlay {Index}",
            sourceSlotA,
            sourceSlotB,
            operation,
            blendFrequencyHz,
            blendWidthOctaves,
            useAmplitudeSpace,
            panel.BackColor,
            strokeThickness,
            lineStyle,
            opacityPercent,
            smoothingInverseOctaves,
            sources);
        if (dialog.ShowDialog(collection.Form) != DialogResult.OK)
        {
            return;
        }

        Hide();
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
        configured = true;
        operationLabel.Text =
            OverlayOperationLabels.GetCompactLabel(operation);

        SaveCurrentState();
        RefreshSources();
        if (checkBox.Enabled)
        {
            Show();
        }
    }

    private void OffsetValueChanged(object? sender, EventArgs e)
    {
        if (updatingControls || !configured)
        {
            return;
        }

        bool wasChecked = Checked;
        SaveCurrentState();
        if (wasChecked)
        {
            Show();
        }
    }

    private void CheckBoxChanged(object? sender, EventArgs e)
    {
        if (updatingControls)
        {
            return;
        }

        if (checkBox.Checked)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    private bool TryGetSources(
        out OverlayOperationSource? sourceA,
        out OverlayOperationSource? sourceB)
    {
        bool hasA = collection.TryGetOrdinaryOverlay(
            sourceSlotA,
            out sourceA);
        bool hasB = collection.TryGetOrdinaryOverlay(
            sourceSlotB,
            out sourceB);
        return hasA && hasB && sourceA != null && sourceB != null;
    }

    private void ApplyFile(OverlayOperationFile file)
    {
        configured = true;
        SeriesMode = file.Mode;
        Title = file.Title;
        sourceSlotA = file.SourceSlotA;
        sourceSlotB = file.SourceSlotB;
        operation = file.Operation;
        blendFrequencyHz = file.BlendFrequencyHz;
        blendWidthOctaves = file.BlendWidthOctaves;
        useAmplitudeSpace = file.UseAmplitudeSpace;
        strokeThickness = file.StrokeThickness;
        lineStyle = file.LineStyle;
        opacityPercent = file.OpacityPercent;
        smoothingInverseOctaves = file.SmoothingInverseOctaves;
        operationLabel.Text =
            OverlayOperationLabels.GetCompactLabel(operation);

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
    }

    private void SaveCurrentState()
    {
        if (!configured || SeriesMode == Mode.None)
        {
            return;
        }

        try
        {
            new OverlayOperationFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = SeriesMode,
                Slot = Index,
                Title = Title,
                SourceSlotA = sourceSlotA,
                SourceSlotB = sourceSlotB,
                Operation = operation,
                BlendFrequencyHz = blendFrequencyHz,
                BlendWidthOctaves = blendWidthOctaves,
                UseAmplitudeSpace = useAmplitudeSpace,
                Offset = (double)offsetControl.Value,
                ColorArgb = panel.BackColor.ToArgb(),
                StrokeThickness = strokeThickness,
                LineStyle = lineStyle,
                OpacityPercent = opacityPercent,
                SmoothingInverseOctaves = smoothingInverseOctaves
            }.Save();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                collection.Form,
                $"Calculated overlay could not be saved.{Environment.NewLine}{exception.Message}",
                "Overlay storage",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ResetState()
    {
        configured = false;
        Title = "";
        SeriesMode = Mode.None;
        sourceSlotA = 0;
        sourceSlotB = 0;
        operation = OverlayOperation.AMinusB;
        blendFrequencyHz = 1_000;
        blendWidthOctaves = 1;
        useAmplitudeSpace = false;
        strokeThickness = 2;
        lineStyle = OverlayLineStyle.Dash;
        opacityPercent = 100;
        smoothingInverseOctaves = 0;
        operationLabel.Text = "--";

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

        checkBox.Enabled = false;
        offsetControl.Enabled = false;
        settingsButton.Enabled = false;
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

    private string GetTag() =>
        $"overlay:{SeriesMode}:{Index}:calculated";

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
}

internal sealed record OverlayOperationSource(
    int Slot,
    string Title,
    IReadOnlyList<OverlayPoint> Points);
