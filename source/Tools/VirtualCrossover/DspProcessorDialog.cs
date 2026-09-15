using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Names the processor a Virtual DSP project targets (catalog model or Custom). Filters are built at its rate, not the
/// measurement's, so bilinear warping is the device's own (see <see cref="PreparedDspResponse"/>).</summary>
internal sealed partial class DspProcessorDialog : Form
{
    private const int FallbackSampleRateHz = 48_000;

    private static readonly object CustomItem = new();

    // "Follow the measurements" is kept apart from fixed rates: a stated 48 kHz stays 48 kHz when measurements change.
    private static readonly object FollowItem = new();

    private readonly int measurementSampleRateHz;

    private int customSampleRateHz;
    private PeqQConvention customQConvention;
    private bool customFollowsMeasurements;
    private bool customPhaseControl;
    private bool suppressEvents;
    // True while the tick is an answer for the device on screen; naming another model clears it.
    private bool phaseControlChosen;
    private bool firFiltersChosen;

    /// <param name="phaseControl">Stored answer, or null if never asked (the selected model answers it); same for <paramref name="firFilters"/>.</param>
    public DspProcessorDialog(
        DspProcessorProfile profile,
        bool followsMeasurements,
        int measurementSampleRateHz,
        bool? phaseControl,
        bool? firFilters)
    {
        ArgumentNullException.ThrowIfNull(profile);
        InitializeComponent();

        this.measurementSampleRateHz = measurementSampleRateHz;
        customSampleRateHz = profile.SampleRateHz > 0
            ? profile.SampleRateHz
            : Fallback(measurementSampleRateHz);
        customQConvention = profile.QConvention;
        customFollowsMeasurements = followsMeasurements && profile.IsCustom;
        phaseControlChosen = phaseControl.HasValue;
        customPhaseControl = phaseControl ?? false;
        firFiltersChosen = firFilters.HasValue;

        AcceptButton = buttonOk;
        CancelButton = buttonCancel;

        PopulateModels();
        PopulateSampleRates(profile.SampleRateHz);
        PopulateQConventions();

        suppressEvents = true;
        try
        {
            comboBoxModel.SelectedItem =
                (object?)DspProcessorCatalog.Preset(profile.ModelId) ?? CustomItem;
            comboBoxSampleRate.SelectedItem =
                customFollowsMeasurements ? FollowItem : customSampleRateHz;
            comboBoxQConvention.SelectedItem = customQConvention;
            checkBoxPhaseControl.Checked = phaseControl ?? false;
            checkBoxFirFilters.Checked = firFilters ?? false;
        }
        finally
        {
            suppressEvents = false;
        }

        comboBoxModel.SelectedIndexChanged += (_, _) => OnModelChanged();
        comboBoxSampleRate.SelectedIndexChanged += (_, _) => OnCustomValueChanged();
        comboBoxQConvention.SelectedIndexChanged += (_, _) => OnCustomValueChanged();
        checkBoxPhaseControl.CheckedChanged += (_, _) =>
        {
            if (!suppressEvents)
            {
                phaseControlChosen = true;
                if (SelectedPreset == null)
                {
                    customPhaseControl = checkBoxPhaseControl.Checked;
                }
            }

            UpdateStatus();
        };
        // No per-Custom copy: the FIR tick is the user's across every model (see ApplySelectedModel).
        checkBoxFirFilters.CheckedChanged += (_, _) =>
        {
            if (!suppressEvents)
            {
                firFiltersChosen = true;
            }

            UpdateStatus();
        };

        ApplySelectedModel();
    }

    /// <summary>Always a number (the rate simulated now), even when <see cref="FollowsMeasurements"/> stores the intent.</summary>
    public DspProcessorProfile Profile =>
        SelectedPreset is { } preset
            ? preset.ToProfile()
            : DspProcessorProfile.Custom(SelectedSampleRateHz, SelectedQConvention);

    public bool FollowsMeasurements =>
        SelectedPreset == null && ReferenceEquals(comboBoxSampleRate.SelectedItem, FollowItem);

    /// <summary>Not locked to the preset: the model list only proposes an answer until the user gives one.</summary>
    public bool PhaseControl => checkBoxPhaseControl.Checked;

    public bool FirFilters => checkBoxFirFilters.Checked;

    /// <summary>Installation notes for an AI assistant (see <see cref="VirtualCrossoverProjectFile.AiNotes"/>); null when empty.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Notes
    {
        get => string.IsNullOrWhiteSpace(textBoxNotes.Text) ? null : textBoxNotes.Text;
        set => textBoxNotes.Text = value ?? string.Empty;
    }

    public const int MaximumNotesLength = 8_000;

    private DspProcessorPreset? SelectedPreset =>
        comboBoxModel.SelectedItem as DspProcessorPreset;

    private int SelectedSampleRateHz
    {
        get
        {
            if (ReferenceEquals(comboBoxSampleRate.SelectedItem, FollowItem))
            {
                return Fallback(measurementSampleRateHz);
            }

            return comboBoxSampleRate.SelectedItem is int rate && rate > 0
                ? rate
                : customSampleRateHz;
        }
    }

    private PeqQConvention SelectedQConvention =>
        comboBoxQConvention.SelectedItem is PeqQConvention convention
            ? convention
            : customQConvention;

    private static int Fallback(int measurementSampleRateHz) =>
        measurementSampleRateHz > 0 ? measurementSampleRateHz : FallbackSampleRateHz;

    private void PopulateModels()
    {
        comboBoxModel.FormattingEnabled = true;
        comboBoxModel.Format += (_, args) =>
        {
            if (ReferenceEquals(args.ListItem, CustomItem))
            {
                args.Value = "Custom";
            }
        };
        comboBoxModel.Items.Add(CustomItem);
        foreach (DspProcessorPreset preset in DspProcessorCatalog.Presets)
        {
            comboBoxModel.Items.Add(preset);
        }
    }

    private void PopulateSampleRates(int currentRateHz)
    {
        comboBoxSampleRate.FormattingEnabled = true;
        comboBoxSampleRate.Format += (_, args) =>
        {
            if (ReferenceEquals(args.ListItem, FollowItem))
            {
                args.Value = measurementSampleRateHz > 0
                    ? $"Follow measurements ({measurementSampleRateHz / 1000.0:0.###} kHz)"
                    : "Follow measurements";
            }
            else if (args.ListItem is int rate)
            {
                args.Value = $"{rate / 1000.0:0.###} kHz";
            }
        };
        comboBoxSampleRate.Items.Add(FollowItem);
        foreach (int rate in DspProcessorCatalog.SelectableSampleRatesHz)
        {
            comboBoxSampleRate.Items.Add(rate);
        }

        // An unlisted rate joins the list so opening the dialog cannot silently round the project's rate.
        foreach (int rate in new[] { currentRateHz, measurementSampleRateHz })
        {
            if (rate > 0 && !comboBoxSampleRate.Items.Contains(rate))
            {
                comboBoxSampleRate.Items.Add(rate);
            }
        }
    }

    private void PopulateQConventions()
    {
        comboBoxQConvention.FormattingEnabled = true;
        comboBoxQConvention.Format += (_, args) =>
        {
            if (args.ListItem is PeqQConvention convention)
            {
                args.Value = PeqQConventions.Describe(convention);
            }
        };
        foreach (PeqQConvention convention in DspProcessorCatalog.SelectableQConventions)
        {
            comboBoxQConvention.Items.Add(convention);
        }
    }

    private void OnModelChanged()
    {
        if (suppressEvents)
        {
            return;
        }

        // A new model re-proposes: the stored answer outranks the catalog, so otherwise phase rotations carried over to a device without the control.
        phaseControlChosen = false;
        firFiltersChosen = false;
        ApplySelectedModel();
    }

    private void ApplySelectedModel()
    {
        DspProcessorPreset? preset = SelectedPreset;
        suppressEvents = true;
        try
        {
            if (preset == null)
            {
                comboBoxSampleRate.SelectedItem = customFollowsMeasurements
                    ? FollowItem
                    : customSampleRateHz;
                comboBoxQConvention.SelectedItem = customQConvention;
            }
            else
            {
                if (!comboBoxSampleRate.Items.Contains(preset.SampleRateHz))
                {
                    comboBoxSampleRate.Items.Add(preset.SampleRateHz);
                }

                comboBoxSampleRate.SelectedItem = preset.SampleRateHz;
                comboBoxQConvention.SelectedItem = preset.QConvention;
            }

            if (!phaseControlChosen)
            {
                checkBoxPhaseControl.Checked = preset?.PhaseControl ?? customPhaseControl;
            }
            // Proposed only one way: catalog false means "not known", and an untick detaches every loaded kernel.
            if (!firFiltersChosen && preset is { FirFilters: true })
            {
                checkBoxFirFilters.Checked = true;
            }
        }
        finally
        {
            suppressEvents = false;
        }

        bool custom = preset == null;
        comboBoxSampleRate.Enabled = custom;
        comboBoxQConvention.Enabled = custom;
        UiStyle.SetTextEnabledLook(labelSampleRate, custom);
        UiStyle.SetTextEnabledLook(labelQConvention, custom);
        UpdateStatus();
    }

    private void OnCustomValueChanged()
    {
        if (suppressEvents)
        {
            return;
        }

        // Remember the intent (follow vs stated), not just the number.
        customFollowsMeasurements = FollowsMeasurements;
        customSampleRateHz = SelectedSampleRateHz;
        customQConvention = SelectedQConvention;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int processorRate = SelectedSampleRateHz;
        string band = measurementSampleRateHz > 0
            ? $"Filters are designed at {processorRate / 1000.0:0.###} kHz; the " +
              $"measurements stay at {measurementSampleRateHz / 1000.0:0.###} kHz, so " +
              $"the simulation speaks for everything up to " +
              $"{Math.Min(processorRate, measurementSampleRateHz) / 2000.0:0.#} kHz."
            : $"Filters are designed at {processorRate / 1000.0:0.###} kHz. The project " +
              "has no measurement yet, so nothing bounds the simulated band.";
        string convention = SelectedQConvention == PeqQConvention.Rbj
            ? "Q is stated as the RBJ cookbook defines it, which is what the bands here are."
            : $"Tuning sheets restate Q as {PeqQConventions.DescribeShort(SelectedQConvention)} " +
              "for this device; the filters themselves do not move.";
        string follow = FollowsMeasurements
            ? "\r\nThe rate is not stated: it follows the project's measurements, " +
              "including after they are replaced at another rate."
            : string.Empty;
        string phase = checkBoxPhaseControl.Checked
            ? "\r\nEach block gets a Phase field, stated at that channel's own " +
              "crossover: move the crossover and the same angle builds another filter."
            : string.Empty;
        string fir = checkBoxFirFilters.Checked
            ? $"\r\nEach block gets a FIR button. A kernel is convolved at {processorRate / 1000.0:0.###} kHz " +
              "whatever rate its file states, so design it for this processor."
            : string.Empty;
        labelStatus.Text = band + "\r\n" + convention + follow + phase + fir;
    }
}
