using System.Diagnostics;
using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;
using Button = System.Windows.Forms.Button;
using CheckBox = System.Windows.Forms.CheckBox;

namespace Resonalyze;

/// <summary>
/// Captured curve for the plot: uncalibrated oversampled spectrum plus calibration frozen on the display grid,
/// kept separate to preserve smooth-then-calibrate order. <paramref name="Spectrum"/> may be empty (no raw form).
/// </summary>
/// <param name="PointsCalibration">No-raw captures only: calibration baked into the drawn points, per point, so a consumer can undo it.</param>
public readonly record struct RawCurveCapture(
    IReadOnlyList<SignalPoint> Spectrum,
    IReadOnlyList<double> CalibrationCorrectionDb,
    int SmoothingCode,
    int? SampleRateHz = null,
    CalibrationFile? PointsCalibration = null,
    // Spectrum is stored unmasked (masking would let smoothing straddle the break), so the band travels with it.
    MeasuredBand Band = default);

internal static class RawCurveRenderer
{
    public const double StartFrequency = 20.0;
    public const double StopFrequency = 20_000.0;
    public const int PointCount = 1024;

    /// <summary>Calibration sampled per drawn point (no other grid for no-raw captures); null calibration yields zeros.</summary>
    public static double[] CaptureCalibrationCorrectionAt(
        CalibrationFile? calibration,
        IReadOnlyList<DataPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var correction = new double[points.Count];
        if (calibration == null)
        {
            return correction;
        }

        for (int i = 0; i < correction.Length; i++)
        {
            correction[i] = calibration.GetDecibelCorrection(points[i].X);
        }

        return correction;
    }

    public static double[] CaptureCalibrationCorrection(CalibrationFile? calibration)
    {
        if (calibration == null)
        {
            return Array.Empty<double>();
        }

        var correction = new double[PointCount];
        for (int i = 0; i < correction.Length; i++)
        {
            double frequency = DataHelper.LogPositionToFrequency(
                i / (PointCount - 1.0),
                StartFrequency,
                StopFrequency);
            correction[i] = calibration.GetDecibelCorrection(frequency);
        }

        return correction;
    }

    /// <param name="band">Applied to the finished curve, since the spectrum is stored unmasked. Default = whole range.</param>
    public static List<SignalPoint> Render(
        IReadOnlyList<SignalPoint> spectrum,
        IReadOnlyList<double> calibrationCorrectionDb,
        int smoothing,
        MeasuredBand band = default)
    {
        List<SignalPoint> input = spectrum as List<SignalPoint> ?? spectrum.ToList();
        List<SignalPoint> result = DataHelper.LogarithmicResample(
            input,
            StartFrequency,
            StopFrequency,
            PointCount,
            calibration: null,
            SpectrumSmoothing.SmoothingOctaves(smoothing),
            dBUnpack: true,
            psychoacoustic: SpectrumSmoothing.IsPsychoacoustic(smoothing));

        if (calibrationCorrectionDb.Count == 0)
        {
            return Mask(result, band);
        }
        if (calibrationCorrectionDb.Count != result.Count)
        {
            throw new ArgumentException(
                "Calibration correction must match the rendered curve grid.",
                nameof(calibrationCorrectionDb));
        }

        for (int i = 0; i < result.Count; i++)
        {
            SignalPoint point = result[i];
            result[i] = new SignalPoint(
                point.X,
                point.Y - calibrationCorrectionDb[i]);
        }

        return Mask(result, band);
    }

    // Last: a break is not a level and must not be corrected, smoothed, or leak into a neighbour's mean.
    private static List<SignalPoint> Mask(List<SignalPoint> curve, MeasuredBand band)
    {
        double low = band.LowEdgeHz;
        double high = band.HighEdgeHz;
        if (!(low > 0.0) && double.IsPositiveInfinity(high))
        {
            return curve;
        }

        for (int i = 0; i < curve.Count; i++)
        {
            if (curve[i].X < low || curve[i].X > high)
            {
                curve[i] = new SignalPoint(curve[i].X, double.NaN);
            }
        }

        return curve;
    }
}

public sealed class OverlayCollection
{
    private readonly List<Overlay> overlays = new();
    private readonly Func<Mode> currentMode;
    private readonly Action notifyPlotChanged;
    private Func<MagnitudeScale>? getCurrentMagnitudeScale;
    private Func<CurveTag, RawCurveCapture?>? rawCurveProvider;
    private ComplexSumOverlayBuilder? complexSumProvider;

    /// <param name="currentMode">The mode on the plot; overlays show and capture in its slots.</param>
    public OverlayCollection(
        Form form,
        Func<Mode> currentMode,
        Panel container,
        OxyPlot.WindowsForms.PlotView plotView,
        WrappingToolTip toolTip,
        Action notifyPlotChanged)
    {
        Form = form;
        this.currentMode = currentMode;
        PlotView = plotView;
        this.notifyPlotChanged = notifyPlotChanged;

        toolTip.InitialDelay = 600;
        toolTip.ReshowDelay = 150;
        toolTip.AutoPopDelay = 6_000;
        toolTip.ShowAlways = true;

        RoundedPanel templatePanel = container.Controls
            .OfType<RoundedPanel>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Overlay template panel is missing.");
        Button templateCaptureButton = templatePanel.Controls
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "buttonSaveOverlay")
            ?? throw new InvalidOperationException(
                "Overlay template capture button is missing.");
        ThemedNumericUpDown templateOffset = templatePanel.Controls
            .OfType<ThemedNumericUpDown>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Overlay template offset control is missing.");
        CheckBox templateCheckBox = templatePanel.Controls
            .OfType<CheckBox>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Overlay template checkbox is missing.");
        Label templateNameLabel = templatePanel.Controls
            .OfType<Label>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Overlay template name label is missing.");

        overlays.Add(new Overlay(
            templatePanel,
            templateCaptureButton,
            templateOffset,
            templateCheckBox,
            templateNameLabel,
            1,
            toolTip,
            this));

        form.SuspendLayout();
        container.SuspendLayout();

        for (int index = 2; index <= OverlayFile.MaximumSlotCount; index++)
        {
            RoundedPanel panel = CreatePanel(templatePanel, index);
            CheckBox checkBox = CreateCheckBox(templateCheckBox, index);
            ThemedNumericUpDown offset = CreateOffset(templateOffset, index);
            Button captureButton = CreateCaptureButton(templateCaptureButton, index);
            Label nameLabel = CreateNameLabel(templateNameLabel, index);

            panel.Controls.Add(checkBox);
            panel.Controls.Add(offset);
            panel.Controls.Add(captureButton);
            panel.Controls.Add(nameLabel);

            overlays.Add(new Overlay(
                panel,
                captureButton,
                offset,
                checkBox,
                nameLabel,
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

    /// <summary>The colour a slot shows before it holds a capture; a captured slot carries its own.</summary>
    internal static Color SlotDefaultColor(int slot) =>
        UiPalette.OverlaySlotDefaults[(slot - 1) % UiPalette.OverlaySlotDefaults.Count];

    public OxyPlot.WindowsForms.PlotView PlotView { get; }

    /// <summary>Owns the overlay dialogs.</summary>
    public Form Form { get; }

    public Mode CurrentMode => currentMode();

    /// <summary>Null while unavailable (the overlay stays armed); showLoss asks for the sum-loss gap instead.</summary>
    internal delegate OverlayPoint[]? ComplexSumOverlayBuilder(
        double compareDelayMs,
        bool invertComparePolarity,
        bool showLoss,
        double? lossSmoothingInverseOctaves);

    internal void SetComplexSumProvider(ComplexSumOverlayBuilder provider) =>
        complexSumProvider = provider;

    internal OverlayPoint[]? BuildComplexSumOverlayPoints(
        double compareDelayMs,
        bool invertComparePolarity,
        bool showLoss,
        double? lossSmoothingInverseOctaves) =>
        complexSumProvider?.Invoke(
            compareDelayMs, invertComparePolarity, showLoss, lossSmoothingInverseOctaves);

    public void SetMagnitudeScaleProvider(Func<MagnitudeScale> provider) =>
        getCurrentMagnitudeScale = provider;

    public MagnitudeScale CurrentMagnitudeScale =>
        getCurrentMagnitudeScale?.Invoke() ?? MagnitudeScale.Relative;

    // Recomputes a captured curve without display smoothing so the overlay re-smooths itself; null = no raw form.
    public void SetRawCurveProvider(Func<CurveTag, RawCurveCapture?> provider) =>
        rawCurveProvider = provider;

    private Func<CurveTag, ImpulseOverlayCapture?>? impulseCaptureProvider;
    private Func<ImpulseOverlayFrame?>? impulseFrameProvider;

    internal RawCurveCapture? TryGetRawCapture(CurveTag tag) =>
        rawCurveProvider?.Invoke(tag);

    internal void SetImpulseCaptureProvider(Func<CurveTag, ImpulseOverlayCapture?> provider) =>
        impulseCaptureProvider = provider;

    internal void SetImpulseFrameProvider(Func<ImpulseOverlayFrame?> provider) =>
        impulseFrameProvider = provider;

    internal ImpulseOverlayCapture? TryGetImpulseCapture(CurveTag tag) =>
        impulseCaptureProvider?.Invoke(tag);

    internal ImpulseOverlayFrame? TryGetImpulseFrame() =>
        impulseFrameProvider?.Invoke();

    // Called on close so an offset changed within the debounce window still persists.
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

        foreach (Overlay overlay in overlays)
        {
            overlay.RefreshSources();
        }
    }

    public void Show(Mode mode)
    {
        Mode overlayMode = OverlayModeFor(mode);
        foreach (Overlay overlay in overlays)
        {
            if (!overlay.Checked || overlay.SeriesMode != overlayMode)
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

    public bool RefreshCurrentMeasurementTargets()
    {
        bool any = false;
        foreach (Overlay overlay in overlays)
        {
            any |= overlay.RedrawCurrentMeasurementTarget();
        }

        return any;
    }

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

    // FR, Live Spectrum and EQ Wizard share axes, so they share one set of overlay slots and storage.
    public static Mode OverlayModeFor(Mode mode) =>
        mode is Mode.LiveSpectrum or Mode.EqWizard ? Mode.FrequencyResponse : mode;

    internal IReadOnlyList<OverlaySlotOption> GetCaptureSourceOptions()
    {
        return overlays
            .Where(overlay =>
                overlay.Kind == OverlayKind.Captured &&
                overlay.SeriesMode == OverlayModeFor(CurrentMode) &&
                overlay.HasCaptureData)
            .Select(overlay => new OverlaySlotOption(
                overlay.Index,
                overlay.Title,
                overlay.SlotSemantics))
            .ToArray();
    }

    // Separate from the source because the draw gate asks this of every checked slot on every rebuild.
    internal OverlayCurveSemantics OperandSemantics(string? curveKey, int slot)
    {
        if (curveKey != null)
        {
            return FindLiveCurve(curveKey) is { } series
                ? LiveCurveSemantics(series)
                : OverlayCurveSemantics.None;
        }

        return FindCaptureSlot(slot)?.SlotSemantics ?? OverlayCurveSemantics.None;
    }

    internal bool TryGetCaptureSource(
        int slot,
        out OverlayOperationSource? source)
    {
        source = FindCaptureSlot(slot)?.CreateOperationSource();
        return source != null;
    }

    private Overlay? FindCaptureSlot(int slot) =>
        overlays.FirstOrDefault(
            candidate =>
                candidate.Index == slot &&
                candidate.Kind == OverlayKind.Captured &&
                candidate.SeriesMode == OverlayModeFor(CurrentMode));

    private LineSeries? FindLiveCurve(string key) =>
        PlotView.Model?.Series
            .OfType<LineSeries>()
            .FirstOrDefault(series =>
                series.Tag is CurveTag tag && tag.Key == key && series.Points.Count >= 2);

    // A live curve states the scale of the axis showing now (re-read each rebuild), not "no scale".
    private OverlayCurveSemantics LiveCurveSemantics(LineSeries series) =>
        OverlayCurveSemantics.ForCurve(CurrentMagnitudeScale, series.YAxisKey);

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
                return new LiveCurveOption(
                    tag.Key,
                    tag.Label,
                    LiveCurveSemantics(series));
            })
            .ToArray();
    }

    internal bool TryGetLiveCurveSource(string key, out OverlayOperationSource? source)
    {
        source = null;
        LineSeries? match = FindLiveCurve(key);
        if (match == null)
        {
            return false;
        }

        var curveTag = (CurveTag)match.Tag!;
        source = new OverlayOperationSource(
            0,
            curveTag.Label,
            match.Points.Select(point => new OverlayPoint(point.X, point.Y)).ToArray(),
            curveTag.PhaseUnwrapped,
            LiveCurveSemantics(match));
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

    internal List<int> CaptureActiveSlots(Mode mode)
    {
        Mode overlayMode = OverlayModeFor(mode);
        return overlays
            .Where(overlay => overlay.Checked && overlay.SeriesMode == overlayMode)
            .Select(overlay => overlay.Index)
            .ToList();
    }

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
            // Show() applies the magnitude-axis rule, so an SPL capture does not reappear on the relative axis.
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
                "{0}\n{2:0.0} Hz\n{4:0.0}°",
            Mode.GroupDelay =>
                "{0}\n{2:0.0} Hz\n{4:0.000} ms",
            Mode.ImpulseResponse =>
                "{0}\n{2:0} sample\n{4:0.00000000}",
            Mode.Autocorrelation =>
                "{0}\n{2:0.000} ms\n{4:0.000}",
            _ => null
        };
    }

    private static RoundedPanel CreatePanel(RoundedPanel template, int index)
    {
        return new RoundedPanel
        {
            BackColor = SlotDefaultColor(index),
            BorderColor = template.BorderColor,
            CornerRadius = template.CornerRadius,
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
        return new ReleaseClickCheckBox
        {
            BackColor = template.BackColor,
            FlatStyle = template.FlatStyle,
            AutoSize = template.AutoSize,
            Location = template.Location,
            Name = $"checkBox{index}",
            Size = template.Size
        };
    }

    private static ThemedNumericUpDown CreateOffset(
        ThemedNumericUpDown template,
        int index)
    {
        return new ThemedNumericUpDown
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

    // Cloned from the designer template to inherit its font and DPI-scaled coordinates.
    private static Label CreateNameLabel(Label template, int index)
    {
        return new Label
        {
            AutoEllipsis = template.AutoEllipsis,
            AutoSize = template.AutoSize,
            BackColor = template.BackColor,
            Font = template.Font,
            ForeColor = template.ForeColor,
            Location = template.Location,
            Name = $"labelOverlay{index}",
            Size = template.Size,
            TextAlign = template.TextAlign,
            UseCompatibleTextRendering = template.UseCompatibleTextRendering
        };
    }

    private static Button CreateCaptureButton(Button template, int index)
    {
        return new ReleaseClickButton
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

/// <summary>One universal overlay slot: captured curve, operation between slots, or target.</summary>
public sealed class Overlay
{
    private readonly OverlayCollection collection;
    private readonly Panel panel;
    private readonly Button captureButton;
    private readonly ThemedNumericUpDown offsetControl;
    private readonly CheckBox checkBox;
    private readonly Label nameLabel;
    private readonly WrappingToolTip toolTip;
    private readonly Color defaultColor;
    private readonly decimal defaultOffset;
    private readonly ContextMenuStrip captureMenu;
    private readonly ToolStripMenuItem captureCurveMenuItem;
    private readonly ToolStripItem exportDeviationMenuItem;
    private readonly ToolStripItem targetMenuItem;
    private readonly ToolStripItem settingsMenuItem;
    private readonly ToolStripItem clearSlotMenuItem;
    private readonly System.Windows.Forms.Timer longPressTimer;
    private readonly System.Windows.Forms.Timer offsetSaveTimer;
    private bool longPressTriggered;
    private string title = "";

    private OverlayKind kind = OverlayKind.Captured;
    private bool updatingControls;
    // Keeps periodic redraws (live target refresh) from stomping a settings dialog's preview.
    private bool previewActive;

    private double strokeThickness = 2;
    private OverlayLineStyle lineStyle = OverlayLineStyle.Solid;
    private int opacityPercent = 100;
    private int smoothingInverseOctaves;

    private DataPoint[]? sourcePoints;
    private DataPoint[]? drawPoints;
    private string? capturedYAxisKey;
    private MagnitudeScale capturedMagnitudeScale = MagnitudeScale.Relative;
    private bool? phaseUnwrapped;
    // Null for imported text or legacy files. Gates magnitude-only (psychoacoustic) smoothing.
    private Resonalyze.Dsp.AnalysisCurveKind? capturedCurveKind;
    // Captured FR only: re-smoothed by the same LogarithmicResample as the mode, so any width reproduces it exactly.
    private List<SignalPoint>? rawSpectrumPoints;
    // Kept separate because the primary FR smooths first and calibrates afterwards.
    private double[] rawCalibrationCorrectionDb = Array.Empty<double>();
    // Slot outlives the measurement and the spectrum is unmasked, so it carries the band itself.
    private MeasuredBand capturedMeasuredBand;
    // No-raw captures only: correction baked into sourcePoints, for consumers outside the plot.
    private double[] pointsCalibrationCorrectionDb = Array.Empty<double>();
    // Smoothing baked into sourcePoints (0 = none, null = unknown); the display smoothing is applied on top.
    private int? capturedSmoothingCode;
    private int? capturedSampleRateHz;
    // Absolute samples and raw linear values, so the trace re-draws under the view's current framing.
    private ImpulseOverlayCapture? impulseCapture;

    // An operand with SourceCurveKeyA/B set is a live curve resolved by CurveTag Key on every rebuild.
    private bool operationConfigured;
    private int sourceSlotA;
    private int sourceSlotB;
    private string? sourceCurveKeyA;
    private string? sourceCurveKeyB;
    private OverlayOperation operation = OverlayOperation.AMinusB;
    private double blendFrequencyHz = 1_000;
    private double blendWidthOctaves = 1;
    private bool useAmplitudeSpace;
    // dB/octave slope hinged at the pivot, compensating a sloped excitation.
    private bool tiltEnabled;
    private double tiltDbPerOctave = OverlayFile.DefaultTiltDbPerOctave;
    private double tiltPivotHz = OverlayFile.DefaultTiltPivotHz;
    private double compareDelayMs;
    private bool compareInvertPolarity;

    private bool targetConfigured;
    private readonly TargetOverlayCurveBuilder targetCurveBuilder = new();
    private int targetSourceSlot;
    private TargetPreset targetPreset = OverlayTargets.DefaultPreset;
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
        ThemedNumericUpDown offsetControl,
        CheckBox checkBox,
        Label nameLabel,
        int index,
        WrappingToolTip toolTip,
        OverlayCollection collection)
    {
        this.panel = panel;
        this.captureButton = captureButton;
        this.offsetControl = offsetControl;
        this.checkBox = checkBox;
        this.nameLabel = nameLabel;
        this.toolTip = toolTip;
        this.collection = collection;
        defaultColor = panel.BackColor;
        defaultOffset = offsetControl.Value;
        Index = index;

        captureMenu = BuildCaptureMenu(
            out captureCurveMenuItem,
            out exportDeviationMenuItem,
            out targetMenuItem,
            out settingsMenuItem,
            out clearSlotMenuItem);

        // Long press (>0.5 s) opens settings; a click opens the capture menu.
        longPressTimer = new System.Windows.Forms.Timer { Interval = 500 };
        longPressTimer.Tick += LongPressTimerTick;

        // Debounced: saving serializes every point and flushes to disk; the redraw stays immediate.
        offsetSaveTimer = new System.Windows.Forms.Timer { Interval = 500 };
        offsetSaveTimer.Tick += OffsetSaveTimerTick;

        toolTip.SetToolTip(offsetControl, "Overlay vertical offset (dB)");
        toolTip.SetToolTip(checkBox, "Show / hide this overlay");
        toolTip.SetToolTip(
            captureButton,
            "Click for the overlay menu; hold to open this slot's settings");

        checkBox.CheckedChanged += CheckBoxChanged;
        captureButton.Click += (_, _) => OpenCaptureMenu();
        captureButton.MouseDown += CaptureButtonMouseDown;
        captureButton.MouseUp += CaptureButtonMouseUp;
        offsetControl.ValueChanged += OffsetValueChanged;

        ResetState();
    }

    public int Index { get; }

    public string Title
    {
        get => title;
        private set
        {
            title = value;
            nameLabel.Text = OverlaySlotName.Shorten(value, Index);
            toolTip.SetToolTip(
                nameLabel,
                value.Length > 0 ? value : "Empty overlay slot");
        }
    }

    public Mode SeriesMode { get; private set; }
    public bool Checked => checkBox.Checked;
    public OverlayKind Kind => kind;

    internal bool DrawsOnMagnitudeScale(MagnitudeScale scale) =>
        SlotSemantics.DrawsOn(SeriesMode, scale);

    // A capture states its measured scale; an operation carries its operands'; a target states nothing.
    internal OverlayCurveSemantics SlotSemantics => kind switch
    {
        OverlayKind.Captured => OverlayCurveSemantics.ForCurve(
            capturedMagnitudeScale,
            capturedYAxisKey),
        OverlayKind.Operation => ResultFor(CurrentOperationSnapshot()).Curve,
        _ => OverlayCurveSemantics.None
    };

    // dB SPL vs relative dB, or coherence vs dB, yields a meaningless number; the slot stays unavailable.
    private bool OperationIsDefined =>
        kind != OverlayKind.Operation ||
        ResultFor(CurrentOperationSnapshot()).IsDefined;

    private OverlayOperationResult ResultFor(OverlayOperationPreview settings) =>
        OverlayCurveSemantics.ForOperation(
            settings.Operation,
            collection.OperandSemantics(settings.SourceCurveKeyA, settings.SourceSlotA),
            settings.Operation == OverlayOperation.CurveA
                ? OverlayCurveSemantics.None
                : collection.OperandSemantics(settings.SourceCurveKeyB, settings.SourceSlotB));

    public bool HasCaptureData => sourcePoints is { Length: > 1 };

    private Mode CurrentOverlayMode =>
        OverlayCollection.OverlayModeFor(collection.CurrentMode);

    public void Prepare(Mode mode)
    {
        // Flush first, or the last spinner change is dropped when state is replaced from disk.
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

    // Set aside so the next capture does not silently overwrite a damaged file, and it warns once.
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

        // Every draw path lands here, so the axis rule is asked once. An off-axis slot stays checked and undrawn,
        // so flipping the axis back restores it; CheckBoxChanged refuses the tick separately.
        if (!DrawsOnMagnitudeScale(collection.CurrentMagnitudeScale))
        {
            if (RemoveSeries(model))
            {
                RefreshPlot(model);
            }

            return;
        }

        RemoveSeries(model);
        bool drawn = kind switch
        {
            OverlayKind.Target => AddTargetSeries(model),
            OverlayKind.Operation => AddCurveSeries(
                model,
                "curve",
                BuildOperationPoints(),
                SlotSemantics.YAxisKey),
            _ => AddCurveSeries(model, "curve", drawPoints, capturedYAxisKey)
        };

        if (drawn)
        {
            SetChecked(true);
            RefreshPlot(model);
        }
        else if (IsCurrentMeasurementTarget || ReferencesLiveCurve || IsComplexSumOperation)
        {
            // Live-sourced targets/operations stay armed while their source is absent; they redraw once data appears.
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
        (sourceCurveKeyA != null || (UsesOperandB && sourceCurveKeyB != null));

    // "A only": stale operand B takes no part in availability, resolution or validation.
    private bool UsesOperandB => operation != OverlayOperation.CurveA;

    // Reads Main/Compare transfer IRs, not operands; recomputes each rebuild.
    private bool IsComplexSumOperation =>
        kind == OverlayKind.Operation &&
        operation is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss;

    private OxyColor ToOverlayColor()
    {
        Color color = panel.BackColor;
        byte alpha = (byte)Math.Round(opacityPercent / 100.0 * 255);
        return OxyColor.FromArgb(alpha, color.R, color.G, color.B);
    }

    internal bool RedrawCurrentMeasurementTarget()
    {
        // Asked anyway: this draws directly, and skipped axis checks are what put SPL on a relative axis.
        if (!Checked ||
            !IsCurrentMeasurementTarget ||
            previewActive ||
            !DrawsOnMagnitudeScale(collection.CurrentMagnitudeScale))
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
            LineStyle = OverlayLineStyles.ToOxy(style),
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

        // Builder caches shape and tolerance so the ~30 fps live redraw skips the grid math.
        TargetOverlayShape shape = targetCurveBuilder.BuildShape(
            spec,
            offset,
            toleranceDb);
        if (shape.Target.Length < 2)
        {
            return false;
        }

        // Clipped to where the source has data (gaps where coherence is below threshold).
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
            LineStyle = OverlayLineStyles.ToOxy(style),
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

    private void PreviewTarget(OverlayTargetPreview settings)
    {
        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return;
        }

        RemoveSeries(model);
        if (!DrawsOnMagnitudeScale(collection.CurrentMagnitudeScale))
        {
            RefreshPlot(model);
            return;
        }

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

        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return null;
        }

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

        // Keep NaN gaps so the deviation curve breaks over unreliable bands instead of bridging them.
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
        // A live operand may be momentarily absent; keep a configured operation available. Slot-only ones need captures.
        bool available = operationConfigured &&
            OperationIsDefined &&
            (ReferencesLiveCurve || IsComplexSumOperation || TryGetSources(out _, out _));
        ApplyCalculatedAvailability(
            available,
            operationConfigured,
            wasChecked);
    }

    private void RefreshTargetSources()
    {
        bool wasChecked = Checked;
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
            phaseUnwrapped,
            SlotSemantics);
    }

    private ContextMenuStrip BuildCaptureMenu(
        out ToolStripMenuItem captureCurveItem,
        out ToolStripItem exportDeviationItem,
        out ToolStripItem targetItem,
        out ToolStripItem settingsItem,
        out ToolStripItem clearSlotItem)
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
            "\u0192  Calculated overlay…",
            null,
            (_, _) => ConfigureOperation());
        targetItem = menu.Items.Add(
            "\u25B3  Target…",
            null,
            (_, _) => ConfigureTarget());
        menu.Items.Add(new ToolStripSeparator());
        settingsItem = menu.Items.Add(
            "\u2699  Settings\u2026",
            null,
            (_, _) => OpenSettings());
        clearSlotItem = menu.Items.Add(
            "\u2715  Clear slot",
            null,
            (_, _) => ClearSlot());
        return menu;
    }

    private void ClearSlot()
    {
        Mode mode = CurrentOverlayMode;
        if (mode == Mode.None)
        {
            return;
        }

        try
        {
            OverlayFile.Delete(mode, Index);
            // Stop only after the delete succeeded, or a failed delete silently drops the offset.
            offsetSaveTimer.Stop();
        }
        catch (Exception exception)
        {
            ShowStorageError("Overlay slot could not be cleared.", exception);
            return;
        }

        Hide();
        ResetState();
        collection.NotifyCapturedOverlayChanged();
        // Hide() refreshed before the reset; re-notify so Show/Hide All and labels see the cleared slot.
        collection.NotifyPlotChanged();
    }

    internal void CloseCaptureMenu()
    {
        if (captureMenu.Visible)
        {
            captureMenu.Close();
        }
    }

    private void OpenCaptureMenu()
    {
        if (longPressTriggered)
        {
            longPressTriggered = false;
            return;
        }

        // Open on Click, not mouse-down: the mouse-up would land outside the new menu and close it.
        if (captureMenu.Visible)
        {
            captureMenu.Close();
            return;
        }

        ShowCaptureMenu();
    }

    private void CaptureButtonMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

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

        if (captureMenu.Visible)
        {
            captureMenu.Close();
        }

        // Only a hold that actually opened something swallows its click (empty slots open nothing).
        longPressTriggered = OpenSettings();
    }

    private void ShowCaptureMenu()
    {
        if (captureMenu.Visible)
        {
            return;
        }

        // Programmatic Close (CloseCalled) bypasses the focus-close guard, leaving one menu open.
        collection.CloseCaptureMenus();

        RebuildCaptureCurveMenu();
        exportDeviationMenuItem.Visible = kind == OverlayKind.Target;
        exportDeviationMenuItem.Text =
            targetDeviationMode == TargetDeviationMode.Correction
                ? "Export EQ correction…"
                : "Export deviation…";
        targetMenuItem.Visible = OverlayTargets.SupportsMode(CurrentOverlayMode);
        settingsMenuItem.Enabled =
            SeriesMode == CurrentOverlayMode && HasConfiguredContent();
        clearSlotMenuItem.Enabled = settingsMenuItem.Enabled;
        DropDownMenu.ShowUnder(captureButton, captureMenu);
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

        CurveTag? tag = selected.Tag as CurveTag;

        // Prefer the raw reference so the overlay's own smoothing starts from true data; no-raw curves capture as drawn.
        RawCurveCapture? raw = tag != null ? collection.TryGetRawCapture(tag) : null;
        ImpulseOverlayCapture? impulse =
            tag != null ? collection.TryGetImpulseCapture(tag) : null;
        DataPoint[] points;
        List<SignalPoint>? spectrum;
        double[] calibrationCorrectionDb;
        double[] pointsCorrectionDb;
        int seedSmoothing;
        // Smoothing baked into the points, distinct from display smoothing. Null when unknown, so consumers do not smooth twice.
        int? bakedSmoothing;
        // Rate describes the measurement, so it survives a fallback capture.
        int? sampleRateHz = raw?.SampleRateHz;
        MeasuredBand measuredBand = raw?.Band ?? default;
        if (raw is { } rawCapture && rawCapture.Spectrum.Count >= 2)
        {
            spectrum = rawCapture.Spectrum as List<SignalPoint> ?? rawCapture.Spectrum.ToList();
            calibrationCorrectionDb = rawCapture.CalibrationCorrectionDb.ToArray();
            points = SmoothRawSpectrum(
                spectrum, calibrationCorrectionDb, 0, measuredBand);
            pointsCorrectionDb = Array.Empty<double>();
            seedSmoothing = rawCapture.SmoothingCode;
            bakedSmoothing = 0;
        }
        else
        {
            points = new DataPoint[selected.Points.Count];
            selected.Points.CopyTo(points);
            spectrum = null;
            calibrationCorrectionDb = Array.Empty<double>();
            // No raw form: the drawn points are the reference, so freeze their baked correction per point.
            pointsCorrectionDb = raw is { } describedCapture
                ? RawCurveRenderer.CaptureCalibrationCorrectionAt(
                    describedCapture.PointsCalibration, points)
                : Array.Empty<double>();
            // Source smoothing is already baked in; applying it again would compound.
            seedSmoothing = 0;
            bakedSmoothing = raw?.SmoothingCode;
        }

        // An occupied slot keeps its (possibly user-renamed) name; evaluate before state is overwritten.
        string title = OverlaySlotName.ForSave(
            HasConfiguredContent(), Title, Index, selected.Title ?? string.Empty);
        Mode mode = CurrentOverlayMode;

        Hide();
        kind = OverlayKind.Captured;
        operationConfigured = false;
        sourcePoints = points;
        rawSpectrumPoints = spectrum;
        rawCalibrationCorrectionDb = calibrationCorrectionDb;
        capturedMeasuredBand = measuredBand;
        pointsCalibrationCorrectionDb = pointsCorrectionDb;
        capturedSmoothingCode = bakedSmoothing;
        capturedSampleRateHz = sampleRateHz ?? impulse?.SampleRateHz;
        impulseCapture = impulse;
        capturedMagnitudeScale = collection.CurrentMagnitudeScale;
        capturedYAxisKey = string.IsNullOrEmpty(selected.YAxisKey)
            ? null
            : selected.YAxisKey;
        phaseUnwrapped = tag?.PhaseUnwrapped;
        capturedCurveKind = tag?.Kind;
        SeriesMode = mode;
        Title = title;
        // Psychoacoustic smoothing is magnitude-only; MagnitudeSmoothingSemantics reads the fields set above.
        smoothingInverseOctaves = MagnitudeSmoothingSemantics
            ? seedSmoothing
            : Dsp.SpectrumSmoothing.EquivalentInverseOctaves(seedSmoothing);
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

        OverlayTextCurve imported;
        try
        {
            imported = OverlayTextFile.ImportCurve(dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowStorageError("Overlay could not be imported.", exception);
            return;
        }

        // Refuse non-response roles: a captured slot has no role field, so a derived shape would re-export as a response.
        if (imported.Metadata.Role is { } role && role != OverlayCurveRole.Response)
        {
            MessageBox.Show(
                collection.Form,
                "This file holds a " + DescribeImportedRole(role) + " curve, not a " +
                "measured response, so it cannot be imported as an overlay. Import the " +
                "response it was derived from instead.",
                "Import from text",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Same rule as a capture: an occupied slot keeps its name, only an empty one
        // is named after the file. Evaluated before the state below is overwritten.
        string importedTitle = OverlaySlotName.ForSave(
            HasConfiguredContent(),
            Title,
            Index,
            System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));

        Hide();
        kind = OverlayKind.Captured;
        operationConfigured = false;
        targetConfigured = false;
        phaseUnwrapped = null;
        capturedCurveKind = imported.Metadata.CurveKind;
        rawSpectrumPoints = null;
        rawCalibrationCorrectionDb = Array.Empty<double>();
        capturedMeasuredBand = default;
        pointsCalibrationCorrectionDb = Array.Empty<double>();
        capturedSmoothingCode = null;
        capturedSampleRateHz = imported.Metadata.SampleRateHz;
        impulseCapture = null;
        capturedMagnitudeScale =
            imported.Metadata.Scale ?? collection.CurrentMagnitudeScale;
        capturedYAxisKey = null;
        sourcePoints = imported.Points
            .Select(point => new DataPoint(point.X, point.Y))
            .ToArray();
        SeriesMode = mode;
        Title = importedTitle;
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
            OverlayTextFile.Export(dialog.FileName, points, BuildExportMetadata());
        }
        catch (Exception exception)
        {
            ShowStorageError("Overlay could not be exported.", exception);
        }
    }

    // The slot kind decides the role, so a derived shape cannot re-enter as a measured response via text.
    private OverlayTextMetadata BuildExportMetadata() =>
        OverlayTextFile.BuildCurveMetadata(
            kind,
            capturedCurveKind,
            capturedMagnitudeScale,
            capturedSampleRateHz,
            Title);

    private static string DescribeImportedRole(OverlayCurveRole role) => role switch
    {
        OverlayCurveRole.Deviation => "deviation",
        OverlayCurveRole.EqCorrection => "EQ-correction",
        OverlayCurveRole.Target => "target",
        OverlayCurveRole.Calculated => "calculated",
        _ => "derived"
    };

    // Drop response-only metadata, or a stale curve kind could mislabel an exported target as a Primary response.
    private void ClearCapturedResponseMetadata()
    {
        capturedCurveKind = null;
        rawSpectrumPoints = null;
        rawCalibrationCorrectionDb = Array.Empty<double>();
        capturedMeasuredBand = default;
        pointsCalibrationCorrectionDb = Array.Empty<double>();
        capturedSmoothingCode = null;
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
            // A deviation is a difference, not a response; saying so keeps curve equalizers from using it.
            OverlayTextFile.Export(
                dialog.FileName,
                result.Deviation,
                new OverlayTextMetadata(
                    Role: exportMode == TargetDeviationMode.Correction
                        ? OverlayCurveRole.EqCorrection
                        : OverlayCurveRole.Deviation,
                    Scale: MagnitudeScale.Relative,
                    SampleRateHz: capturedSampleRateHz,
                    Title: $"{Title} - {suffix}"));
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

    /// <returns>False for an empty or other-mode slot; the long press then leaves the click to the menu.</returns>
    private bool OpenSettings()
    {
        if (SeriesMode != CurrentOverlayMode)
        {
            return false;
        }

        if (kind == OverlayKind.Operation)
        {
            ConfigureOperation();
            return true;
        }

        if (kind == OverlayKind.Target)
        {
            ConfigureTarget();
            return true;
        }

        if (!HasCaptureData)
        {
            return false;
        }

        ConfigureCaptured();
        return true;
    }

    private bool HasConfiguredContent() => kind switch
    {
        OverlayKind.Operation => operationConfigured,
        OverlayKind.Target => targetConfigured,
        _ => HasCaptureData
    };

    private void ConfigureTarget()
    {
        if (!OverlayTargets.SupportsMode(SeriesMode))
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        IReadOnlyList<OverlaySlotOption> sources =
            collection.GetCaptureSourceOptions();
        TargetCurveSpec spec = targetConfigured
            ? CurrentTargetSpec()
            : TargetCurveSpec.FromPreset(OverlayTargets.DefaultPreset);

        bool previewShown = false;
        bool wasCheckedBefore = Checked;
        using var dialog = new OverlayTargetSettingsDialog(
            SeriesMode,
            targetConfigured ? Title : $"Target {Index}",
            targetConfigured ? targetSourceSlot : 0,
            targetConfigured ? targetPreset : OverlayTargets.DefaultPreset,
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
        ClearCapturedResponseMetadata();
        // Targets and operations are defined in relative dB.
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
        SetPanelColor(dialog.SelectedColor);
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
            },
            allowPsychoacousticSmoothing: MagnitudeSmoothingSemantics);
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
        SetPanelColor(dialog.SelectedColor);
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
            operationConfigured ? useAmplitudeSpace : true,
            tiltEnabled,
            tiltDbPerOctave,
            tiltPivotHz,
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
        ClearCapturedResponseMetadata();
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
        tiltEnabled = dialog.TiltEnabled;
        tiltDbPerOctave = dialog.TiltDbPerOctave;
        tiltPivotHz = dialog.TiltPivotHz;
        compareDelayMs = dialog.CompareDelayMs;
        compareInvertPolarity = dialog.CompareInvertPolarity;
        SetPanelColor(dialog.SelectedColor);
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
        // Only captured slots feed other overlays; a target/operation offset cannot change any input.
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
            // Same rule as the post-rebuild redraw; disagreement made a slot appear on Save yet refuse the checkbox.
            if (SeriesMode == CurrentOverlayMode &&
                DrawsOnMagnitudeScale(collection.CurrentMagnitudeScale))
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
            SetPanelColor(Color.FromArgb(file.ColorArgb));
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
            tiltEnabled = file.TiltEnabled;
            tiltDbPerOctave = file.TiltDbPerOctave;
            tiltPivotHz = file.TiltPivotHz;
            compareDelayMs = file.CompareDelayMs;
            compareInvertPolarity = file.CompareInvertPolarity;
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
            capturedCurveKind = file.CapturedCurveKind;
            rawSpectrumPoints = file.RawSpectrum.Length >= 2
                ? file.RawSpectrum.Select(point => new SignalPoint(point.X, point.Y)).ToList()
                : null;
            rawCalibrationCorrectionDb = file.RawCalibrationCorrectionDb.ToArray();
            capturedMeasuredBand = new MeasuredBand(
                file.MeasuredLowFrequencyHz, file.MeasuredHighFrequencyHz);
            pointsCalibrationCorrectionDb = file.PointsCalibrationCorrectionDb.ToArray();
            capturedSmoothingCode = file.CapturedSmoothingCode;
            capturedSampleRateHz = file.SampleRateHz;
            impulseCapture = file.RawImpulse.Length >= 2
                ? new ImpulseOverlayCapture(
                    file.RawImpulse
                        .Select(point => new SignalPoint(point.X, point.Y))
                        .ToArray(),
                    file.CapturedCurveKind ?? Resonalyze.Dsp.AnalysisCurveKind.Primary,
                    file.RawImpulsePeakReference ?? 0.0,
                    file.SampleRateHz ?? 0)
                : null;
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
            file.TiltEnabled = tiltEnabled;
            file.TiltDbPerOctave = tiltDbPerOctave;
            file.TiltPivotHz = tiltPivotHz;
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
            file.CapturedCurveKind = capturedCurveKind;
            file.RawSpectrum = rawSpectrumPoints != null
                ? rawSpectrumPoints.Select(point => new OverlayPoint(point.X, point.Y)).ToArray()
                : Array.Empty<OverlayPoint>();
            file.RawCalibrationCorrectionDb = rawCalibrationCorrectionDb.ToArray();
            file.MeasuredLowFrequencyHz = capturedMeasuredBand.LowestHz;
            file.MeasuredHighFrequencyHz = capturedMeasuredBand.HighestHz;
            file.PointsCalibrationCorrectionDb = pointsCalibrationCorrectionDb.ToArray();
            file.CapturedSmoothingCode = capturedSmoothingCode;
            file.SampleRateHz = capturedSampleRateHz;
            file.RawImpulse = impulseCapture is { } impulse
                ? impulse.Samples
                    .Select(point => new OverlayPoint(point.X, point.Y))
                    .ToArray()
                : Array.Empty<OverlayPoint>();
            file.RawImpulsePeakReference = impulseCapture?.PeakReference;
            file.CapturedYAxisKey = capturedYAxisKey;
        }

        return file;
    }

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
        tiltEnabled,
        tiltDbPerOctave,
        tiltPivotHz,
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
        // Computed from Main/Compare transfer IRs by the pipeline; sum-loss is smoothed once there at this slot's width.
        if (settings.Operation is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss)
        {
            return BuildComplexSumPoints(
                settings,
                showLoss: settings.Operation == OverlayOperation.ComplexSumLoss);
        }

        OverlayOperationResult result = ResultFor(settings);
        if (!result.IsDefined)
        {
            return null;
        }

        bool usesB = settings.Operation != OverlayOperation.CurveA;
        OverlayOperationSource? sourceA =
            ResolveOperand(settings.SourceCurveKeyA, settings.SourceSlotA);
        OverlayOperationSource? sourceB = usesB
            ? ResolveOperand(settings.SourceCurveKeyB, settings.SourceSlotB)
            : null;
        if (sourceA == null || (usesB && sourceB == null))
        {
            return null;
        }

        // Wrapped operands need the wrapped (shortest-angle) difference; unwrapped keep raw subtraction to preserve delay slope.
        bool wrapPhaseDifference = SeriesMode == Mode.PhaseResponse &&
            (sourceA.PhaseUnwrapped == false || sourceB?.PhaseUnwrapped == false);

        OverlayPoint[] points = OverlayMath.CalculateOperation(
            sourceA.Points,
            sourceB?.Points ?? Array.Empty<OverlayPoint>(),
            settings.Operation,
            settings.BlendFrequencyHz,
            settings.BlendWidthOctaves,
            settings.UseAmplitudeSpace && result.Curve.IsDecibels,
            wrapPhaseDifference);
        points = OverlayMath.SmoothByOctaves(
            points,
            settings.SmoothingInverseOctaves,
            psychoacousticMagnitude: MagnitudeSmoothingSemantics);
        if (points.Length < 2)
        {
            return null;
        }

        return ApplyOffsetAndTilt(points, settings, result.Curve);
    }

    // Offset and tilt last, after smoothing, and only on decibels (dB/octave is meaningless on coherence).
    private DataPoint[] ApplyOffsetAndTilt(
        IReadOnlyList<OverlayPoint> points,
        OverlayOperationPreview settings,
        OverlayCurveSemantics semantics)
    {
        double offset = (double)offsetControl.Value;
        bool tilted = settings.TiltEnabled && semantics.IsDecibels;
        var result = new DataPoint[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            OverlayPoint point = points[i];
            double tilt = tilted
                ? OverlayMath.TiltDb(
                    point.X,
                    settings.TiltDbPerOctave,
                    settings.TiltPivotHz)
                : 0;
            result[i] = new DataPoint(point.X, point.Y + offset + tilt);
        }

        return result;
    }

    private DataPoint[]? BuildComplexSumPoints(
        OverlayOperationPreview settings,
        bool showLoss = false)
    {
        int smoothing = settings.SmoothingInverseOctaves;
        // A ratio, smoothed once by the pipeline at this slot's width (DataHelper.SmoothRatioLevels); smoothing here
        // would double-smooth and flatten the psychoacoustic mode's variable bandwidth to 1/6 octave.
        OverlayPoint[]? sumPoints = collection.BuildComplexSumOverlayPoints(
            settings.CompareDelayMs,
            settings.CompareInvertPolarity,
            showLoss,
            showLoss ? smoothing : null);
        if (sumPoints == null || sumPoints.Length < 2)
        {
            return null;
        }

        OverlayPoint[] smoothed = showLoss
            ? sumPoints
            : OverlayMath.SmoothByOctaves(sumPoints, smoothing);
        return ApplyOffsetAndTilt(smoothed, settings, OverlayCurveSemantics.None);
    }

    private void PreviewOperation(OverlayOperationPreview settings)
    {
        PlotModel? model = collection.PlotView.Model;
        if (model == null)
        {
            return;
        }

        RemoveSeries(model);
        OverlayCurveSemantics semantics = ResultFor(settings).Curve;
        DataPoint[]? points = semantics.DrawsOn(SeriesMode, collection.CurrentMagnitudeScale)
            ? BuildOperationPointsFor(settings)
            : null;
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
                settings.Name.Length > 0 ? settings.Name : Title,
                semantics.YAxisKey);
        }

        RefreshPlot(model);
    }

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
        sourceB = UsesOperandB
            ? ResolveOperand(sourceCurveKeyB, sourceSlotB)
            : null;
        return sourceA != null && (sourceB != null || !UsesOperandB);
    }

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

    // Psychoacoustic cubic averaging needs dB magnitude; axis is read off the slot so operations use the inherited axis.
    private bool MagnitudeSmoothingSemantics =>
        OverlayMath.SupportsAmplitudeSpace(SeriesMode) &&
        SlotSemantics.YAxisKey != PlotModelFactory.CoherenceAxisKey &&
        capturedCurveKind is not (
            Resonalyze.Dsp.AnalysisCurveKind.MinimumPhase or
            Resonalyze.Dsp.AnalysisCurveKind.ExcessPhase);

    private DataPoint[]? BuildCapturedPoints(int smoothing)
    {
        double offset = (double)offsetControl.Value;

        // Time-domain capture re-drawn under the current framing; octave smoothing does not apply.
        if (impulseCapture is { Samples.Count: > 1 } capture &&
            collection.TryGetImpulseFrame() is { } frame)
        {
            DataPoint[] framed = ImpulseOverlayRenderer.Render(capture, frame);
            if (offset != 0.0)
            {
                for (int i = 0; i < framed.Length; i++)
                {
                    framed[i] = new DataPoint(framed[i].X, framed[i].Y + offset);
                }
            }

            return framed;
        }

        if (rawSpectrumPoints != null)
        {
            DataPoint[] exact = SmoothRawSpectrum(
                rawSpectrumPoints,
                rawCalibrationCorrectionDb,
                smoothing,
                capturedMeasuredBand);
            if (exact.Length < 2)
            {
                return null;
            }

            for (int i = 0; i < exact.Length; i++)
            {
                exact[i] = new DataPoint(exact[i].X, exact[i].Y + offset);
            }

            return exact;
        }

        if (sourcePoints == null)
        {
            return null;
        }

        OverlayPoint[] smoothed = OverlayMath.SmoothByOctaves(
            sourcePoints.Select(point => new OverlayPoint(point.X, point.Y)).ToArray(),
            smoothing,
            psychoacousticMagnitude: MagnitudeSmoothingSemantics);
        return smoothed
            .Select(point => new DataPoint(point.X, point.Y + offset))
            .ToArray();
    }

    /// <summary>Re-smooths a stored raw spectrum, masked after smoothing so the break does not slide with width.</summary>
    private static DataPoint[] SmoothRawSpectrum(
        List<SignalPoint> spectrum,
        IReadOnlyList<double> calibrationCorrectionDb,
        int smoothing,
        MeasuredBand band)
    {
        List<SignalPoint> resampled = RawCurveRenderer.Render(
            spectrum,
            calibrationCorrectionDb,
            smoothing,
            band);
        var result = new DataPoint[resampled.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new DataPoint(resampled[i].X, resampled[i].Y);
        }

        return result;
    }

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
            DrawsOnMagnitudeScale(collection.CurrentMagnitudeScale)
                ? BuildCapturedPoints(settings.SmoothingInverseOctaves)
                : null,
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
        capturedCurveKind = null;
        rawSpectrumPoints = null;
        rawCalibrationCorrectionDb = Array.Empty<double>();
        capturedMeasuredBand = default;
        pointsCalibrationCorrectionDb = Array.Empty<double>();
        capturedSmoothingCode = null;
        capturedSampleRateHz = null;
        impulseCapture = null;
        operationConfigured = false;
        sourceSlotA = 0;
        sourceSlotB = 0;
        sourceCurveKeyA = null;
        sourceCurveKeyB = null;
        operation = OverlayOperation.AMinusB;
        blendFrequencyHz = 1_000;
        blendWidthOctaves = 1;
        useAmplitudeSpace = false;
        tiltEnabled = false;
        tiltDbPerOctave = OverlayFile.DefaultTiltDbPerOctave;
        tiltPivotHz = OverlayFile.DefaultTiltPivotHz;
        compareDelayMs = 0;
        compareInvertPolarity = false;
        targetConfigured = false;
        targetSourceSlot = 0;
        targetPreset = OverlayTargets.DefaultPreset;
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
            SetPanelColor(defaultColor);
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

    // Name drawn in black or white, whichever stays legible on the user-editable slot colour.
    private void SetPanelColor(Color color)
    {
        panel.BackColor = color;
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        nameLabel.ForeColor = luminance > 0.55 ? Color.Black : Color.White;
    }

    private void UpdateKindGlyph()
    {
        captureButton.Text = kind switch
        {
            OverlayKind.Operation => $"{Index}\u0192",
            OverlayKind.Target => $"{Index}\u25B3",
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

    private bool RemoveSeries(PlotModel model)
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

        return existing.Count > 0;
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
    bool? PhaseUnwrapped = null,
    // Operations reuse the points verbatim, so the result inherits these semantics.
    OverlayCurveSemantics Semantics = default);
