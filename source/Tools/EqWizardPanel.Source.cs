using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

// The self-contained half of the EQ Wizard: the mode owns its source (a loaded
// impulse response, whose frequency response is computed here with microphone
// calibration) and its target curve (edited through a reused, isolated instance
// of the overlay target dialog). Nothing here reaches into overlays or the
// current measurement.
public partial class EqWizardPanel
{
    // A fat analysis window so the low end reads clearly (finer LF resolution than
    // the short measurement-preview window). Zero-padded when the IR is shorter.
    private const int SourceWindow = 32768;
    private const int SourceLeftTukey = 256;
    private const int SourceRightTukey = 2048;

    private const string NoIrHint =
        "Load an impulse response to equalize.\n" +
        "Use Target… to shape the goal curve.";

    // The source has no overlay behind it, so its colours are fixed and chosen to
    // read against the target and the Source + EQ curve.
    private static readonly OxyColor SourceCurveColor = OxyColor.FromRgb(180, 190, 205);
    private static readonly OxyColor SourcePlusEqColor = OxyColor.FromRgb(0, 209, 255);

    // The 20 Hz .. 20 kHz grid the target is drawn on when there is no source to
    // borrow frequencies from.
    private static readonly double[] DefaultTargetGrid =
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 512).ToArray();

    private IImpulseMeasurement? loadedIr;
    // The loaded IR's per-frequency coherence (γ²), present only for a loopback-transfer
    // source; used to gate Auto Tune boosts to reliable regions. Null for a plain sweep
    // deconvolution, which carries no coherence.
    private IReadOnlyList<SignalPoint>? sourceCoherence;
    private EqWizardCurve? cachedSourceCurve;
    private bool sourceCurveDirty = true;
    private int irLoadGeneration;

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
    private MicrophoneCalibrationMode calibrationMode = MicrophoneCalibrationMode.Off;
    private bool suppressCalibrationEvents;
    private bool suppressSettingsSave;

    /// <summary>Raised when a persisted setting changes so the host can save.</summary>
    internal event Action? SettingsChanged;

    // ------------------------------------------------------------- source (IR)

    private async void LoadIr()
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

        // Guard against overlapping loads: a slow earlier LoadAsync must not
        // overwrite a newer selection (or report its error) when it finally lands.
        int generation = ++irLoadGeneration;
        ImpulseResponseFile file;
        try
        {
            file = await ImpulseResponseFile.LoadAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            if (generation == irLoadGeneration && !IsDisposed)
            {
                ShowFileError("The impulse response could not be loaded.", exception);
            }

            return;
        }

        if (generation != irLoadGeneration || IsDisposed)
        {
            return;
        }

        (loadedIr, sourceCoherence) = CreateMeasurement(file);
        InvalidateSourceCurve();
        buttonLoadIr.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        toolTip.SetToolTip(
            buttonLoadIr,
            $"Loaded: {dialog.FileName}\r\nClick to load another impulse response.");
        DrawSelectedCurves();
    }

    // Mirrors the history preview: a loopback-transfer file equalizes its transfer
    // IR; everything else uses the sweep-deconvolution IR. The transfer path also
    // carries per-frequency coherence (γ²); the sweep path has none.
    private static (IImpulseMeasurement Measurement, IReadOnlyList<SignalPoint>? Coherence)
        CreateMeasurement(ImpulseResponseFile file)
    {
        Complex[]? transfer = file.GetTransferImpulseResponse();
        if (file.MeasurementMode == SweepMeasurementMode.LoopbackTransfer &&
            transfer is { Length: > 0 } &&
            file.TransferPeakIndex is int transferPeak)
        {
            return (
                new ImpulseMeasurementView(transfer, transferPeak, file.SampleRate),
                ExtractTransferCoherence(file));
        }

        return (
            new ImpulseMeasurementView(
                file.GetSweepDeconvolutionImpulseResponse(),
                file.SweepDeconvolutionPeakIndex,
                file.SampleRate),
            null);
    }

    // Converts the raw half-spectrum coherence bins stored with a loopback-transfer
    // measurement into an ascending (Hz, γ²) curve, dropping the DC bin (undefined on
    // a log axis). Returns null when the file carries no coherence.
    private static IReadOnlyList<SignalPoint>? ExtractTransferCoherence(ImpulseResponseFile file)
    {
        if (file.TransferCoherence is not { Length: > 1 } coherence || file.SampleRate <= 0)
        {
            return null;
        }

        int fftLength = (coherence.Length - 1) * 2;
        var points = new List<SignalPoint>(coherence.Length - 1);
        for (int k = 1; k < coherence.Length; k++)
        {
            double frequency = (double)k * file.SampleRate / fftLength;
            double gammaSquared = coherence[k];
            if (double.IsFinite(frequency) && frequency > 0 && double.IsFinite(gammaSquared))
            {
                points.Add(new SignalPoint(frequency, gammaSquared));
            }
        }

        return points.Count >= 2 ? points : null;
    }

    // The source FR is an expensive FFT that only changes with the loaded IR, the
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
        if (loadedIr == null)
        {
            return null;
        }

        var options = new FrequencyResponseOptions
        {
            Window = SourceWindow,
            LeftTukeyWindow = SourceLeftTukey,
            RightTukeyWindow = SourceRightTukey,
            SmoothingInverseOctaves = SourceSmoothingInverseOctaves,
            Offset = 0,
            CalibrationMode = calibrationMode
        };
        CalibrationFile? calibration = calibrationMode == MicrophoneCalibrationMode.Off
            ? null
            : calibrationResolver?.Invoke(calibrationMode);

        IReadOnlyList<AnalysisCurve> curves = DataHelper.GetSpectrum(
            loadedIr, options, calibration, SpectrumCurves.Primary);
        if (curves.Count == 0)
        {
            return null;
        }

        DataPoint[] points = curves[0].Points
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y) && point.X > 0)
            .Select(point => new DataPoint(point.X, point.Y))
            .ToArray();
        return points.Length >= 2
            ? new EqWizardCurve("Source", SourceCurveColor, 1.5, LineStyle.Solid, points)
            : null;
    }

    // Builds everything the plot draws from the loaded IR and the local target,
    // without any overlay. The target is always present; the source (and therefore
    // Source + EQ) exists only once an IR is loaded.
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
        hintAnnotation.Text = loadedIr == null ? NoIrHint : string.Empty;
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
        suppressCalibrationEvents = true;
        try
        {
            MicrophoneCalibrationComboHelper.Configure(
                comboBoxCalibration,
                calibrationMode,
                hasZeroDegreeCalibration,
                hasNinetyDegreeCalibration);
        }
        finally
        {
            suppressCalibrationEvents = false;
        }

        calibrationMode = MicrophoneCalibrationComboHelper.GetSelectedMode(comboBoxCalibration);
        InvalidateSourceCurve();
        DrawSelectedCurves();
    }

    private void OnCalibrationChanged()
    {
        if (suppressCalibrationEvents)
        {
            return;
        }

        calibrationMode = MicrophoneCalibrationComboHelper.GetSelectedMode(comboBoxCalibration);
        InvalidateSourceCurve();
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
            calibrationMode = settings.CalibrationMode;

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
        CalibrationMode = calibrationMode,
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
