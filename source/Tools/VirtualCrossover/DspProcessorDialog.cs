using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Names the processor a Virtual DSP project is designed for: pick a model and its
/// properties come from the catalog, or pick Custom and state them by hand.
/// <para>
/// The processing rate is the one thing here that changes what the tool computes.
/// Filters are built at THAT rate, never at the measurement's, so a 48 kHz sound
/// card can simulate a 96 kHz processor exactly — the bilinear warping is the
/// device's own (see <see cref="PreparedDspResponse"/>). The Q convention changes
/// only how a band's Q is stated where numbers leave for the device.
/// </para>
/// </summary>
internal sealed partial class DspProcessorDialog : Form
{
    // What Custom falls back to when the project has no measurement to follow yet.
    private const int FallbackSampleRateHz = 48_000;

    // The item standing for "no model", so the model list can hold presets alone.
    private static readonly object CustomItem = new();

    // The rate entry meaning "whatever the measurements are", kept apart from the
    // fixed rates beside it: a project set up before its measurements, or re-sourced
    // at another rate later, should follow them — while a user who deliberately
    // states 48 kHz keeps 48 kHz even when the measurements move.
    private static readonly object FollowItem = new();

    private readonly int measurementSampleRateHz;

    // The user's own numbers, kept while a preset is selected so switching back to
    // Custom restores what they had rather than the preset they just left.
    private int customSampleRateHz;
    private PeqQConvention customQConvention;
    private bool customFollowsMeasurements;
    private bool customPhaseControl;
    private bool suppressEvents;
    // True while the answer on screen is one somebody gave for the device on screen —
    // the project's stored one when the dialog opens, or the user's own tick. Naming
    // another model clears it, because that answer was about another device.
    private bool phaseControlChosen;

    /// <param name="profile">The project's current processor.</param>
    /// <param name="followsMeasurements">
    /// Whether the project's rate is stored as "follow the measurements" rather than
    /// as a number. Only a Custom profile can.
    /// </param>
    /// <param name="measurementSampleRateHz">
    /// The rate the project's measurements were taken at, shown beside the choice so
    /// the band the simulation can speak for is visible. Zero when the project has no
    /// source yet.
    /// </param>
    /// <param name="phaseControl">
    /// The project's stored answer to "do the blocks show a phase control", or null
    /// where it has never been asked - in which case the selected model answers it.
    /// </param>
    public DspProcessorDialog(
        DspProcessorProfile profile,
        bool followsMeasurements,
        int measurementSampleRateHz,
        bool? phaseControl)
    {
        ArgumentNullException.ThrowIfNull(profile);
        InitializeComponent();

        this.measurementSampleRateHz = measurementSampleRateHz;
        customSampleRateHz = profile.SampleRateHz > 0
            ? profile.SampleRateHz
            : Fallback(measurementSampleRateHz);
        customQConvention = profile.QConvention;
        customFollowsMeasurements = followsMeasurements && profile.IsCustom;
        // A project that has never been asked leaves this null, and the model list
        // answers it below; one that HAS been asked keeps its answer, including a
        // deliberate "no" on a device that offers the control.
        phaseControlChosen = phaseControl.HasValue;
        customPhaseControl = phaseControl ?? false;

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
                // Kept the way the rate and the convention are kept: a user who ticks
                // it for a device the catalog does not list, looks at a preset and
                // comes back to Custom finds their own answer again.
                if (SelectedPreset == null)
                {
                    customPhaseControl = checkBoxPhaseControl.Checked;
                }
            }

            UpdateStatus();
        };

        ApplySelectedModel();
    }

    /// <summary>
    /// The processor the user settled on. Its rate is always a NUMBER — the rate the
    /// simulation runs at right now — even when <see cref="FollowsMeasurements"/> says
    /// the project should store the intent rather than the number.
    /// </summary>
    public DspProcessorProfile Profile =>
        SelectedPreset is { } preset
            ? preset.ToProfile()
            : DspProcessorProfile.Custom(SelectedSampleRateHz, SelectedQConvention);

    /// <summary>
    /// Whether the rate was left to the measurements rather than stated. A named model
    /// never does — it brings its own rate.
    /// </summary>
    public bool FollowsMeasurements =>
        SelectedPreset == null && ReferenceEquals(comboBoxSampleRate.SelectedItem, FollowItem);

    /// <summary>
    /// Whether the blocks should show the channel phase control. Unlike the rate and
    /// the Q convention this is not locked to the preset — a Custom profile standing
    /// in for a device the catalog does not list still has the control if the device
    /// does — so the model list only proposes an answer until the user gives one.
    /// </summary>
    public bool PhaseControl => checkBoxPhaseControl.Checked;

    /// <summary>
    /// The user's description of the installation for an AI assistant (see
    /// <see cref="VirtualCrossoverProjectFile.AiNotes"/>): set before showing, read
    /// back after OK. Null when the field is empty, matching what the project stores.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Notes
    {
        get => string.IsNullOrWhiteSpace(textBoxNotes.Text) ? null : textBoxNotes.Text;
        set => textBoxNotes.Text = value ?? string.Empty;
    }

    /// <summary>
    /// The most the notes may hold. Generous for a paragraph per driver plus the car
    /// and the goals, and small next to the diagnostic package the notes travel in.
    /// </summary>
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

        // A rate the list does not offer — a project saved against an unusual device,
        // or a measurement at a non-standard rate the Custom default followed — joins
        // it, so opening the dialog cannot silently round the project's own rate.
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

        // Whatever answer stood, it was about the device that was selected a moment
        // ago. Naming another one is new information about a different device, so the
        // catalog proposes again — without this a project moved off a HELIX kept a
        // control the new model is not known to have, and (since the project's stored
        // answer outranks the catalog) kept every phase rotation with it, on a device
        // that cannot dial one. The user can tick it back in the same breath; what
        // they cannot do is carry the old device's answer over by not looking.
        phaseControlChosen = false;
        ApplySelectedModel();
    }

    // A preset's numbers are the device's, so the fields show them and stop taking
    // input; Custom hands the fields back with the user's own numbers restored.
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

            // Proposed from the catalog for the model on screen: picking a HELIX
            // offers the control, picking a device not known to have one takes the
            // offer back, and Custom answers with whatever the user last said for
            // Custom. Only an answer given for THIS model stands in the way.
            if (!phaseControlChosen)
            {
                checkBoxPhaseControl.Checked = preset?.PhaseControl ?? customPhaseControl;
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

        // The INTENT is part of what is remembered, not just the number: a user who
        // picked "follow" and then looked at a preset must come back to "follow", and
        // one who stated a rate must come back to that rate. Restoring the wrong one
        // silently changes the rate the simulation runs at.
        customFollowsMeasurements = FollowsMeasurements;
        customSampleRateHz = SelectedSampleRateHz;
        customQConvention = SelectedQConvention;
        UpdateStatus();
    }

    // What the choice means for THIS project: where the simulation is honest, and
    // whether the Q column will be restated on the way out.
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
        // Two answers can name the same rate today and part company tomorrow, so the
        // one that is not a number says so.
        string follow = FollowsMeasurements
            ? "\r\nThe rate is not stated: it follows the project's measurements, " +
              "including after they are replaced at another rate."
            : string.Empty;
        string phase = checkBoxPhaseControl.Checked
            ? "\r\nEach block gets a Phase field, stated at that channel's own " +
              "crossover: move the crossover and the same angle builds another filter."
            : string.Empty;
        labelStatus.Text = band + "\r\n" + convention + follow + phase;
    }
}
