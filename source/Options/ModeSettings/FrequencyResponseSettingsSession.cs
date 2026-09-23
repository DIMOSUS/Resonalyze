using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>The Frequency Response settings panel's state, each field as its control shows it. See
/// docs/tech/mode-settings.md#code-map.</summary>
internal sealed class FrequencyResponseSettingsSession
{
    public TukeyFades Fades { get; } = new();

    public WindowModeChoice WindowMode { get; set; } = WindowModeChoice.From(PhaseWindowMode.Fixed, 4);

    public int SmoothingInverseOctaves { get; set; }

    /// <summary>A missing id stays listed and selected, so the next apply keeps it.</summary>
    public IReadOnlyList<MicrophoneCalibrationOption> Calibrations { get; private set; } = [];

    public int CalibrationIndex { get; set; } = -1;

    public CurveVisibilityOptions Curves { get; private set; } = new();

    public bool Spl { get; set; }

    public ModeSettingsMeasurement Measurement { get; private set; } = new(null, 0);

    public SampleWindowPreview Preview =>
        new(Measurement.Result, Fades.Window, Fades.Left, Fades.Right, 0, IrPreviewSource.PrimaryAtStart);

    public void Load(
        FrequencyResponseOptions options,
        CurveVisibilityOptions visibility,
        IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(visibility);
        WindowMode = WindowModeChoice.From(options.MagnitudeWindowMode, options.MagnitudeFdwCycles);
        Fades.Load(
            (int)ModeSettingsLimits.FrequencyResponseWindow.Assign(options.Window),
            options.LeftTukeyWindow,
            options.RightTukeyWindow);
        SmoothingInverseOctaves = SmoothingPresetOptions.Normalize(options.SmoothingInverseOctaves);
        SelectCalibration(options.CalibrationId, calibrationEntries);
        Curves = visibility.Copy();
        Spl = options.MagnitudeScale == MagnitudeScale.SoundPressureLevel;
    }

    public void Follow(ModeSettingsMeasurement measurement) => Measurement = measurement;

    public void SelectCalibration(string? calibrationId, IReadOnlyList<MicrophoneCalibrationEntry> entries)
    {
        Calibrations = MicrophoneCalibrationChoices.BuildOptions(calibrationId, entries);
        CalibrationIndex = MicrophoneCalibrationChoices.FindIndex(Calibrations, calibrationId);
    }

    public string? CalibrationId =>
        CalibrationIndex >= 0 && CalibrationIndex < Calibrations.Count ? Calibrations[CalibrationIndex].CalibrationId : null;

    public void WriteTo(FrequencyResponseOptions options, CurveVisibilityOptions visibility)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(visibility);
        options.MagnitudeWindowMode = WindowMode.Mode;
        options.MagnitudeFdwCycles = WindowMode.Cycles;
        options.Window = Fades.Window;
        options.LeftTukeyWindow = Fades.Left;
        options.RightTukeyWindow = Fades.Right;
        options.SmoothingInverseOctaves = SmoothingInverseOctaves;
        options.CalibrationId = CalibrationId;
        visibility.ShowPrimary = Curves.ShowPrimary;
        visibility.ShowCoherence = Curves.ShowCoherence;
        visibility.ShowArrayAverage = Curves.ShowArrayAverage;
        visibility.ShowArrayMicrophones = Curves.ShowArrayMicrophones;
        visibility.ShowArraySpread = Curves.ShowArraySpread;
        visibility.ShowHd2 = Curves.ShowHd2;
        visibility.ShowHd3 = Curves.ShowHd3;
        visibility.ShowHd4 = Curves.ShowHd4;
        visibility.ShowThdPlusNoise = Curves.ShowThdPlusNoise;
        visibility.ShowNoiseFloor = Curves.ShowNoiseFloor;
        options.MagnitudeScale = Spl ? MagnitudeScale.SoundPressureLevel : MagnitudeScale.Relative;
    }
}
