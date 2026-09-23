using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The processor the DSP processor dialog names: a catalog model, or Custom keeping its own rate and convention
/// while the user looks at models; the phase-control and FIR answers; the AI notes. See
/// docs/tech/virtual-dsp-panel.md#dsp-processor-code-map.</summary>
internal sealed class DspProcessorSession
{
    private const int FallbackSampleRateHz = 48_000;

    public const int MaximumNotesLength = 8_000;

    private readonly List<int> sampleRates;
    private int customSampleRateHz;
    private PeqQConvention customQConvention;
    private bool customFollowsMeasurements;
    private bool customPhaseControl;

    // True while a tick is an answer for the device on screen; naming another model clears it.
    private bool phaseControlChosen;
    private bool firFiltersChosen;

    /// <param name="measurementSampleRateHz">The measured rate, 0 without a measurement; never a default.</param>
    /// <param name="phaseControl">The stored answer, or null if never asked (the model answers it); the same for
    /// <paramref name="firFilters"/>.</param>
    public DspProcessorSession(
        DspProcessorProfile profile,
        bool followsMeasurements,
        int measurementSampleRateHz,
        bool? phaseControl,
        bool? firFilters)
    {
        ArgumentNullException.ThrowIfNull(profile);
        MeasurementSampleRateHz = measurementSampleRateHz;
        customSampleRateHz = profile.SampleRateHz > 0 ? profile.SampleRateHz : Fallback(measurementSampleRateHz);
        customQConvention = profile.QConvention;
        customFollowsMeasurements = followsMeasurements && profile.IsCustom;
        phaseControlChosen = phaseControl.HasValue;
        customPhaseControl = phaseControl ?? false;
        firFiltersChosen = firFilters.HasValue;
        sampleRates = DspProcessorCatalog.SelectableSampleRatesHz.ToList();
        // An unlisted rate joins the list so opening the dialog cannot silently round the project's rate.
        foreach (int rate in new[] { profile.SampleRateHz, measurementSampleRateHz })
        {
            if (rate > 0 && !sampleRates.Contains(rate))
            {
                sampleRates.Add(rate);
            }
        }

        Model = DspProcessorCatalog.Preset(profile.ModelId);
        SampleRate = customFollowsMeasurements ? null : customSampleRateHz;
        QConvention = customQConvention;
        PhaseControl = phaseControl ?? false;
        FirFilters = firFilters ?? false;
        ApplyModel();
    }

    public int MeasurementSampleRateHz { get; }

    /// <summary>Null is Custom.</summary>
    public DspProcessorPreset? Model { get; private set; }

    /// <summary>The rates offered after "follow the measurements"; a model with a rate the list lacks adds it.</summary>
    public IReadOnlyList<int> SampleRates => sampleRates;

    /// <summary>The rate the selector shows; null follows the measurements.</summary>
    public int? SampleRate { get; private set; }

    public PeqQConvention QConvention { get; private set; }

    /// <summary>Not locked to the model: the model list only proposes an answer until the user gives one.</summary>
    public bool PhaseControl { get; private set; }

    public bool FirFilters { get; private set; }

    /// <summary>Installation notes for an AI assistant (see <see cref="VirtualCrossoverProjectFile.AiNotes"/>); null when empty.</summary>
    public string? Notes { get; set; }

    /// <summary>Only Custom states its own rate and convention.</summary>
    public bool CustomFields => Model == null;

    /// <summary>Always a number, the rate simulated now, even when <see cref="FollowsMeasurements"/> stores the intent.</summary>
    public int SampleRateHz => SampleRate ?? Fallback(MeasurementSampleRateHz);

    public bool FollowsMeasurements => Model == null && SampleRate == null;

    public DspProcessorProfile Profile =>
        Model is { } preset ? preset.ToProfile() : DspProcessorProfile.Custom(SampleRateHz, QConvention);

    /// <summary>A new model proposes its own answers: the stored one outranks the catalog, so otherwise phase rotations
    /// would carry over to a device without the control.</summary>
    public void SelectModel(DspProcessorPreset? model)
    {
        if (Equals(Model, model))
        {
            return;
        }

        Model = model;
        phaseControlChosen = false;
        firFiltersChosen = false;
        ApplyModel();
    }

    /// <summary>Custom's rate; null follows the measurements. The intent is kept, not just the number.</summary>
    public void SelectSampleRate(int? rateHz)
    {
        if (!CustomFields || SampleRate == rateHz)
        {
            return;
        }

        SampleRate = rateHz;
        KeepCustom();
    }

    public void SelectQConvention(PeqQConvention convention)
    {
        if (!CustomFields || QConvention == convention)
        {
            return;
        }

        QConvention = convention;
        KeepCustom();
    }

    public void SetPhaseControl(bool shown)
    {
        if (PhaseControl == shown)
        {
            return;
        }

        PhaseControl = shown;
        phaseControlChosen = true;
        if (Model == null)
        {
            customPhaseControl = shown;
        }
    }

    /// <summary>No Custom copy: the FIR tick is the user's across every model.</summary>
    public void SetFirFilters(bool loaded)
    {
        if (FirFilters == loaded)
        {
            return;
        }

        FirFilters = loaded;
        firFiltersChosen = true;
    }

    private void KeepCustom()
    {
        customFollowsMeasurements = FollowsMeasurements;
        customSampleRateHz = SampleRateHz;
        customQConvention = QConvention;
    }

    private void ApplyModel()
    {
        if (Model is not { } preset)
        {
            SampleRate = customFollowsMeasurements ? null : customSampleRateHz;
            QConvention = customQConvention;
        }
        else
        {
            if (!sampleRates.Contains(preset.SampleRateHz))
            {
                sampleRates.Add(preset.SampleRateHz);
            }

            SampleRate = preset.SampleRateHz;
            QConvention = preset.QConvention;
        }

        if (!phaseControlChosen)
        {
            PhaseControl = Model?.PhaseControl ?? customPhaseControl;
        }
        // Proposed only one way: catalog false means "not known", and an untick detaches every loaded kernel.
        if (!firFiltersChosen && Model is { FirFilters: true })
        {
            FirFilters = true;
        }
    }

    private static int Fallback(int measurementSampleRateHz) =>
        measurementSampleRateHz > 0 ? measurementSampleRateHz : FallbackSampleRateHz;
}
