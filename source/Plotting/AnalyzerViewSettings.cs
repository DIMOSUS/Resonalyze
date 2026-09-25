using Resonalyze.Dsp;
using Resonalyze.History;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>
/// How the analyzer shows a measurement: every mode's options, edited in place by the mode settings panels and read by
/// the plot builds. The settings file keeps one copy, and each history entry keeps the copy it was left with.
/// </summary>
/// <remarks>Held by reference everywhere: a copy taken out would stop following the panels.</remarks>
internal sealed class AnalyzerViewSettings
{
    public FrequencyResponseOptions FrequencyResponse { get; init; } = new();

    public CurveVisibilityOptions FrequencyResponseVisibility { get; init; } = new();

    public FrequencyResponseOptions PhaseResponse { get; init; } = new()
    {
        SmoothingInverseOctaves = FrequencyResponseOptions.DefaultPhaseSmoothingInverseOctaves,
    };

    public CurveVisibilityOptions PhaseResponseVisibility { get; init; } = new();

    public FrequencyResponseOptions GroupDelay { get; init; } = new()
    {
        SmoothingInverseOctaves = FrequencyResponseOptions.DefaultGroupDelaySmoothingInverseOctaves,
    };

    public CurveVisibilityOptions GroupDelayVisibility { get; init; } = new();

    public ImpulseResponseOptions ImpulseResponse { get; init; } = new();

    public WaterfallGenerateOptions Waterfall { get; init; } = new()
    {
        WaterfallMode = WaterfallMode.Fourier,
    };

    public WaterfallGenerateOptions BurstDecay { get; init; } = new()
    {
        WaterfallMode = WaterfallMode.BurstDecay,
        Window = 1024,
        LeftTukeyWindow = 8,
        RightTukeyWindow = 128,
        SmoothingInverseOctaves = 6,
    };

    public LiveSpectrumOptions LiveSpectrum { get; init; } = new();

    public TimeAlignmentOptions TimeAlignment { get; init; } = new();

    /// <summary>What a history entry keeps of the view, with the mode and overlay slots it was left in.</summary>
    public MeasurementSessionSnapshot CaptureSession(ModeTab activeMode, List<int> activeOverlaySlots) =>
        new()
        {
            ActiveMode = activeMode,
            FrequencyResponse =
                MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                    FrequencyResponse, FrequencyResponseVisibility),
            PhaseResponse =
                MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                    PhaseResponse, PhaseResponseVisibility),
            GroupDelay =
                MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                    GroupDelay, GroupDelayVisibility),
            ImpulseResponse = MeasurementSettingsFile.ImpulseResponseSettings.Capture(ImpulseResponse),
            Waterfall = MeasurementSettingsFile.WaterfallSettings.Capture(Waterfall),
            BurstDecay = MeasurementSettingsFile.WaterfallSettings.Capture(BurstDecay),
            LiveSpectrum = MeasurementSettingsFile.LiveSpectrumSettings.Capture(LiveSpectrum),
            TimeAlignment = MeasurementSettingsFile.TimeAlignmentSettings.Capture(TimeAlignment),
            ActiveOverlaySlots = activeOverlaySlots
        };

    /// <param name="sampleRate">The rate Time Alignment's ASIO channels are checked against.</param>
    /// <returns>The modes whose magnitude axis now means something else (dB against dB SPL): a zoom kept there would
    /// frame the other axis's numbers.</returns>
    public IReadOnlyList<Mode> ApplySession(MeasurementSessionSnapshot session, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(session);
        MagnitudeScale frequencyScale = FrequencyResponse.MagnitudeScale;
        MagnitudeScale liveScale = LiveSpectrum.MagnitudeScale;
        session.FrequencyResponse.ApplyTo(FrequencyResponse, FrequencyResponseVisibility);
        session.PhaseResponse.ApplyTo(PhaseResponse, PhaseResponseVisibility);
        session.GroupDelay.ApplyTo(GroupDelay, GroupDelayVisibility);
        session.ImpulseResponse.ApplyTo(ImpulseResponse);
        session.Waterfall.ApplyTo(Waterfall, WaterfallMode.Fourier);
        session.BurstDecay.ApplyTo(BurstDecay, WaterfallMode.BurstDecay);
        // A live capture is corrected by the rig's microphone calibration, which no entry carries.
        string? rigCalibrationId = LiveSpectrum.CalibrationId;
        session.LiveSpectrum.ApplyTo(LiveSpectrum);
        LiveSpectrum.CalibrationId = rigCalibrationId;
        session.TimeAlignment.ApplyTo(TimeAlignment, sampleRate);

        var rescaled = new List<Mode>();
        if (FrequencyResponse.MagnitudeScale != frequencyScale)
        {
            rescaled.Add(Mode.FrequencyResponse);
        }
        if (LiveSpectrum.MagnitudeScale != liveScale)
        {
            rescaled.Add(Mode.LiveSpectrum);
        }

        return rescaled;
    }
}
