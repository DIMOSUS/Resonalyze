using System.ComponentModel;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze;

/// <summary>
/// One channel block of the Virtual DSP tool: the source picker and the
/// DSP chain controls (gain, delay, polarity, crossover edges, PEQ) plus the
/// per-channel curve visibility. The control owns only self-contained behavior
/// (slope lists per family, greying out unused edges, the delay-to-distance
/// readout); source resolution and curve rendering stay with the host panel.
/// </summary>
public partial class VirtualCrossoverChannelControl : UserControl
{
    // The speed of sound at ~20 °C; the delay readout in millimeters is the
    // sanity check against a ruler measurement of the physical driver offset.
    private const double SpeedOfSoundMillimetersPerMs = 343.0;

    private string channelName = "A";
    private bool suppressChangeEvents;
    private bool muted;

    public VirtualCrossoverChannelControl()
    {
        InitializeComponent();
        PopulateCrossoverCombos();
        WireEvents();
        UpdateCrossoverAvailability();
        UpdateDelayDistance();
    }

    /// <summary>Raised on any change that affects the channel's DSP chain or curves.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Raised when the user clicks the source button; the host shows the picker menu.</summary>
    public event EventHandler? SourceClicked;

    /// <summary>Raised when the user clicks Load next to PEQ; the host shows the file dialog.</summary>
    public event EventHandler? PeqLoadClicked;

    /// <summary>Raised when the user clicks Clear next to PEQ.</summary>
    public event EventHandler? PeqClearClicked;

    [DefaultValue("A")]
    public string ChannelName
    {
        get => channelName;
        set
        {
            channelName = value;
            labelChannel.Text = $"Channel {value}";
        }
    }

    internal Button SourceButton => buttonSource;
    internal DarkNumericUpDown GainInput => numericGain;
    internal DarkNumericUpDown DelayInput => numericDelay;
    internal CheckBox InvertCheckBox => checkBoxInvert;
    internal DarkComboBox CrossoverKindComboBox => comboBoxCrossoverKind;
    internal DarkNumericUpDown HighPassFrequencyInput => numericHighPassHz;
    internal DarkComboBox HighPassFamilyComboBox => comboBoxHighPassFamily;
    internal DarkComboBox HighPassSlopeComboBox => comboBoxHighPassSlope;
    internal DarkNumericUpDown LowPassFrequencyInput => numericLowPassHz;
    internal DarkComboBox LowPassFamilyComboBox => comboBoxLowPassFamily;
    internal DarkComboBox LowPassSlopeComboBox => comboBoxLowPassSlope;
    internal Label MeasuredPolarityLabel => labelMeasuredPolarity;
    internal Button MuteButton => buttonMute;
    internal Button PeqLoadButton => buttonPeqLoad;
    internal Button PeqClearButton => buttonPeqClear;
    internal Label PeqInfoLabel => labelPeqInfo;
    internal CheckBox ShowRawCheckBox => checkBoxShowRaw;
    internal CheckBox ShowProcessedCheckBox => checkBoxShowProcessed;
    internal CheckBox BypassCheckBox => checkBoxBypass;

    public CrossoverKind SelectedCrossoverKind =>
        comboBoxCrossoverKind.SelectedItem is CrossoverKind kind
            ? kind
            : CrossoverKind.Off;

    public CrossoverEdge HighPassEdge => ReadEdge(
        numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope);

    public CrossoverEdge LowPassEdge => ReadEdge(
        numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope);

    /// <summary>
    /// Ties the channel block to its plot curves: the header and the Processed
    /// checkbox take the channel's full curve color, Raw a dimmed blend of it —
    /// matching the translucent raw trace on the plot.
    /// </summary>
    public void SetAccentColor(Color color)
    {
        labelChannel.ForeColor = color;
        checkBoxShowProcessed.ForeColor = color;
        checkBoxShowRaw.ForeColor = Blend(color, BackColor, 0.55);
    }

    private static Color Blend(Color foreground, Color background, double amount) =>
        Color.FromArgb(
            (int)(foreground.R * amount + background.R * (1 - amount)),
            (int)(foreground.G * amount + background.G * (1 - amount)),
            (int)(foreground.B * amount + background.B * (1 - amount)));

    /// <summary>
    /// A muted channel is excluded from the sum, the loss, the metric and the
    /// plots entirely — the quick "what changes without this driver" check. The
    /// state lives in the project's Enabled flag; the button only flips the glyph.
    /// </summary>
    [DefaultValue(false)]
    public bool Muted
    {
        get => muted;
        set
        {
            muted = value;
            buttonMute.Text = value ? "🔇" : "🔈";
            buttonMute.ForeColor = value
                ? Color.FromArgb(255, 120, 120)
                : Color.White;
        }
    }

    /// <summary>
    /// Shows the acoustic polarity read from the channel's measured IR — the
    /// as-measured wiring of the driver, independent of the Invert switch. Green
    /// "Normal" for a positive-going arrival, red "Inverted" for a negative-going
    /// one, muted "Unknown" when no source is set or the IR is too symmetric to
    /// call.
    /// </summary>
    public void SetMeasuredPolarity(PolarityEstimate polarity)
    {
        (labelMeasuredPolarity.Text, labelMeasuredPolarity.ForeColor) = polarity switch
        {
            PolarityEstimate.Positive => ("IR: Normal", Color.FromArgb(96, 210, 120)),
            PolarityEstimate.Negative => ("IR: Inverted", Color.FromArgb(255, 120, 120)),
            _ => ("IR: Unknown", Color.FromArgb(170, 176, 190))
        };
    }

    /// <summary>
    /// Applies stored values to the controls without firing SettingsChanged for
    /// every field; the host redraws once afterward.
    /// </summary>
    public void RunBatchUpdate(Action update)
    {
        suppressChangeEvents = true;
        try
        {
            update();
        }
        finally
        {
            suppressChangeEvents = false;
        }

        UpdateCrossoverAvailability();
        UpdateDelayDistance();
    }

    private void PopulateCrossoverCombos()
    {
        comboBoxCrossoverKind.Items.AddRange(
        [
            CrossoverKind.Off,
            CrossoverKind.LowPass,
            CrossoverKind.HighPass,
            CrossoverKind.BandPass
        ]);
        comboBoxCrossoverKind.Format += (_, args) =>
        {
            if (args.ListItem is CrossoverKind kind)
            {
                args.Value = kind switch
                {
                    CrossoverKind.LowPass => "Low-pass",
                    CrossoverKind.HighPass => "High-pass",
                    CrossoverKind.BandPass => "Band-pass",
                    _ => "Off"
                };
            }
        };
        comboBoxCrossoverKind.SelectedIndex = 0;

        InitializeFamilyCombo(comboBoxHighPassFamily, comboBoxHighPassSlope);
        InitializeFamilyCombo(comboBoxLowPassFamily, comboBoxLowPassSlope);
    }

    private void InitializeFamilyCombo(DarkComboBox familyComboBox, DarkComboBox slopeComboBox)
    {
        familyComboBox.Items.AddRange(
        [
            CrossoverFilterFamily.LinkwitzRiley,
            CrossoverFilterFamily.Butterworth,
            CrossoverFilterFamily.Bessel
        ]);
        familyComboBox.Format += (_, args) =>
        {
            if (args.ListItem is CrossoverFilterFamily family)
            {
                args.Value = family switch
                {
                    CrossoverFilterFamily.LinkwitzRiley => "Linkwitz-Riley",
                    CrossoverFilterFamily.Bessel => "Bessel",
                    _ => "Butterworth"
                };
            }
        };
        slopeComboBox.Format += (_, args) =>
        {
            if (args.ListItem is int slope)
            {
                args.Value = $"{slope} dB/oct";
            }
        };
        familyComboBox.SelectedIndex = 0;
        PopulateSlopes(familyComboBox, slopeComboBox);
    }

    // Each family offers its own slope list (LR only exists in 12/24/48); the
    // current slope is kept when the other family supports it too.
    private static void PopulateSlopes(DarkComboBox familyComboBox, DarkComboBox slopeComboBox)
    {
        CrossoverFilterFamily family =
            familyComboBox.SelectedItem is CrossoverFilterFamily selected
                ? selected
                : CrossoverFilterFamily.LinkwitzRiley;
        int? previousSlope = slopeComboBox.SelectedItem as int?;

        slopeComboBox.Items.Clear();
        foreach (int slope in CrossoverFilter.SupportedSlopes(family))
        {
            slopeComboBox.Items.Add(slope);
        }

        int index = previousSlope.HasValue
            ? slopeComboBox.Items.IndexOf(previousSlope.Value)
            : -1;
        slopeComboBox.SelectedIndex = index >= 0
            ? index
            : slopeComboBox.Items.IndexOf(24);
    }

    private void WireEvents()
    {
        buttonSource.Click += (_, _) => SourceClicked?.Invoke(this, EventArgs.Empty);
        buttonMute.Click += (_, _) =>
        {
            Muted = !Muted;
            RaiseSettingsChanged();
        };
        buttonPeqLoad.Click += (_, _) => PeqLoadClicked?.Invoke(this, EventArgs.Empty);
        buttonPeqClear.Click += (_, _) => PeqClearClicked?.Invoke(this, EventArgs.Empty);

        numericGain.ValueChanged += (_, _) => RaiseSettingsChanged();
        numericDelay.ValueChanged += (_, _) =>
        {
            UpdateDelayDistance();
            RaiseSettingsChanged();
        };
        checkBoxInvert.CheckedChanged += (_, _) => RaiseSettingsChanged();
        comboBoxCrossoverKind.SelectedIndexChanged += (_, _) =>
        {
            UpdateCrossoverAvailability();
            RaiseSettingsChanged();
        };
        WireEdgeEvents(numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope);
        WireEdgeEvents(numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope);
        checkBoxShowRaw.CheckedChanged += (_, _) => RaiseSettingsChanged();
        checkBoxShowProcessed.CheckedChanged += (_, _) => RaiseSettingsChanged();
        checkBoxBypass.CheckedChanged += (_, _) => RaiseSettingsChanged();
    }

    private void WireEdgeEvents(
        DarkNumericUpDown frequencyInput,
        DarkComboBox familyComboBox,
        DarkComboBox slopeComboBox)
    {
        frequencyInput.ValueChanged += (_, _) => RaiseSettingsChanged();
        familyComboBox.SelectedIndexChanged += (_, _) =>
        {
            PopulateSlopes(familyComboBox, slopeComboBox);
            RaiseSettingsChanged();
        };
        slopeComboBox.SelectedIndexChanged += (_, _) => RaiseSettingsChanged();
    }

    private void RaiseSettingsChanged()
    {
        if (!suppressChangeEvents)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Only the edges the crossover kind uses stay interactive; the rest are
    // greyed out (not hidden) so the layout never shifts.
    private void UpdateCrossoverAvailability()
    {
        CrossoverKind kind = SelectedCrossoverKind;
        bool highPass = kind is CrossoverKind.HighPass or CrossoverKind.BandPass;
        bool lowPass = kind is CrossoverKind.LowPass or CrossoverKind.BandPass;

        UiStyle.SetTextEnabledLook(labelHighPass, highPass);
        numericHighPassHz.Enabled = highPass;
        comboBoxHighPassFamily.Enabled = highPass;
        comboBoxHighPassSlope.Enabled = highPass;

        UiStyle.SetTextEnabledLook(labelLowPass, lowPass);
        numericLowPassHz.Enabled = lowPass;
        comboBoxLowPassFamily.Enabled = lowPass;
        comboBoxLowPassSlope.Enabled = lowPass;
    }

    // The ruler-check readout: the delay expressed as a distance in air.
    private void UpdateDelayDistance()
    {
        double millimeters = (double)numericDelay.Value * SpeedOfSoundMillimetersPerMs;
        labelDelayMm.Text = $"= {millimeters:0.#} mm";
    }

    private static CrossoverEdge ReadEdge(
        DarkNumericUpDown frequencyInput,
        DarkComboBox familyComboBox,
        DarkComboBox slopeComboBox)
    {
        CrossoverFilterFamily family =
            familyComboBox.SelectedItem is CrossoverFilterFamily selected
                ? selected
                : CrossoverFilterFamily.LinkwitzRiley;
        int slope = slopeComboBox.SelectedItem is int value ? value : 24;
        return new CrossoverEdge(family, (double)frequencyInput.Value, slope);
    }
}
