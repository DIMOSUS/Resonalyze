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
    private bool suppressEvents;

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
    public DspProcessorDialog(
        DspProcessorProfile profile,
        bool followsMeasurements,
        int measurementSampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(profile);
        InitializeComponent();

        this.measurementSampleRateHz = measurementSampleRateHz;
        customSampleRateHz = profile.SampleRateHz > 0
            ? profile.SampleRateHz
            : Fallback(measurementSampleRateHz);
        customQConvention = profile.QConvention;
        customFollowsMeasurements = followsMeasurements && profile.IsCustom;

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
        }
        finally
        {
            suppressEvents = false;
        }

        comboBoxModel.SelectedIndexChanged += (_, _) => OnModelChanged();
        comboBoxSampleRate.SelectedIndexChanged += (_, _) => OnCustomValueChanged();
        comboBoxQConvention.SelectedIndexChanged += (_, _) => OnCustomValueChanged();

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
        labelStatus.Text = band + "\r\n" + convention + follow;
    }
}
