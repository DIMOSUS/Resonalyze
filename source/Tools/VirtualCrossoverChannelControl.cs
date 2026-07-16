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

    private string channelName = "A";
    private bool suppressChangeEvents;
    private bool muted;
    private double sampleRateHz = 48_000;

    public VirtualCrossoverChannelControl()
    {
        InitializeComponent();
        // The ripple cap is the DSP's single source of truth (above it the Chebyshev
        // pole math is undefined); the designer value is only a default.
        numericHighPassRipple.Maximum = (decimal)CrossoverFilter.MaximumChebyshevRippleDb;
        numericLowPassRipple.Maximum = (decimal)CrossoverFilter.MaximumChebyshevRippleDb;
        // The all-pass Q ceiling is a sane range rather than a maths limit, so it lives
        // with the project model the validator shares — not in the DSP.
        numericAllPassQ.Maximum = (decimal)VirtualCrossoverChannelSettings.MaximumAllPassQ;
        PopulateCrossoverCombos();
        PopulateAllPassCombo();
        WireEvents();
        UpdateCrossoverAvailability();
        UpdateAllPassAvailability();
        UpdateAllPassGroupDelay();
        UpdateDelayDistance();
    }

    /// <summary>
    /// The project's sample rate, pushed by the host so the all-pass group-delay readout
    /// reflects the digital filter the chain actually runs rather than the analog ideal.
    /// Holds 48 kHz until a source resolves; the figure barely moves between rates, but
    /// the readout and the plots should never disagree.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double SampleRateHz
    {
        get => sampleRateHz;
        set
        {
            if (!(value > 0) || sampleRateHz == value)
            {
                return;
            }

            sampleRateHz = value;
            UpdateAllPassGroupDelay();
        }
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
    internal CheckBox MonoCheckBox => checkBoxMono;
    internal DarkComboBox CrossoverKindComboBox => comboBoxCrossoverKind;
    internal DarkNumericUpDown HighPassFrequencyInput => numericHighPassHz;
    internal DarkComboBox HighPassFamilyComboBox => comboBoxHighPassFamily;
    internal DarkComboBox HighPassSlopeComboBox => comboBoxHighPassSlope;
    internal DarkNumericUpDown LowPassFrequencyInput => numericLowPassHz;
    internal DarkComboBox LowPassFamilyComboBox => comboBoxLowPassFamily;
    internal DarkComboBox LowPassSlopeComboBox => comboBoxLowPassSlope;
    internal DarkNumericUpDown HighPassRippleInput => numericHighPassRipple;
    internal DarkNumericUpDown LowPassRippleInput => numericLowPassRipple;
    internal DarkComboBox AllPassTypeComboBox => comboAllPassType;
    internal DarkNumericUpDown AllPassFrequencyInput => numericAllPassFreq;
    internal DarkNumericUpDown AllPassQInput => numericAllPassQ;
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
        numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope, numericHighPassRipple);

    public CrossoverEdge LowPassEdge => ReadEdge(
        numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope, numericLowPassRipple);

    public AllPassType SelectedAllPassType =>
        comboAllPassType.SelectedItem is AllPassType type ? type : AllPassType.Off;

    public AllPassSpec AllPassStage => new(
        SelectedAllPassType,
        (double)numericAllPassFreq.Value,
        (double)numericAllPassQ.Value);

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
    /// Registers the per-field help text with the host's shared tooltip. The
    /// block owns the descriptions of its own sub-controls, so the host no longer
    /// reaches through into each input to set them.
    /// </summary>
    public void ApplyTooltips(ToolTip toolTip)
    {
        ArgumentNullException.ThrowIfNull(toolTip);
        // The numeric fields register on their inner editor too (via ApplyToolTip) so
        // the tip still shows while the value is being edited, not only when idle.
        numericGain.ApplyToolTip(
            toolTip,
            "Channel gain (dB).\r\n" +
            "Relative levels are only honest when the measurements\r\n" +
            "were captured through the same playback chain;\r\n" +
            "compensate any difference here.");
        toolTip.SetToolTip(
            checkBoxInvert,
            "Invert the channel polarity — the DSP polarity switch.\r\n" +
            "Also the null test: with polarity flipped, the deepest\r\n" +
            "notch at the crossover frequency marks perfect alignment.");
        numericDelay.ApplyToolTip(
            toolTip,
            "Channel delay (ms) — the value you would dial into\r\n" +
            "this DSP channel.\r\n" +
            "The mm readout is the equivalent distance in air.");
        toolTip.SetToolTip(
            buttonMute,
            "Mute the channel: exclude it from the sum, the loss,\r\n" +
            "the metric, Auto delay and both plots — a quick\r\n" +
            "\"what changes without this driver\" check.");
        toolTip.SetToolTip(
            checkBoxBypass,
            "Bypass the DSP chain: feed the raw measured signal with\r\n" +
            "no gain, delay, polarity, crossover or PEQ — the driver's\r\n" +
            "natural band-pass, for an A/B against the processed result.");
        toolTip.SetToolTip(
            checkBoxMono,
            "One physical driver serving both sides (typically the\r\n" +
            "subwoofer): a single set of settings participates in the\r\n" +
            "L and R views and calculations alike. The stereo Auto\r\n" +
            "delay tunes it with the left side and reports the right\r\n" +
            "junction it pins.");
        toolTip.SetToolTip(
            labelMeasuredPolarity,
            "Acoustic polarity read from the measured IR\r\n" +
            "(the sign of its first significant excursion).\r\n" +
            "Normal — the driver pushes toward the mic first.\r\n" +
            "Inverted — it pulls first (wired in reverse).\r\n" +
            "Unknown — no source selected.\r\n" +
            "Independent of the Invert switch.");
        toolTip.SetToolTip(
            comboBoxCrossoverKind,
            "This driver's crossover role:\r\n" +
            "Off — full range; High-pass — only above the HP corner;\r\n" +
            "Low-pass — only below the LP corner; Band-pass — both.\r\n" +
            "Only the edges the role uses stay editable.");

        // The family, slope and ripple descriptions are identical for the high- and
        // low-pass edges, so both rows share one text each.
        const string familyTip =
            "Filter alignment for this edge:\r\n" +
            "Linkwitz-Riley — -6 dB at the corner, two edges sum flat\r\n" +
            "(the car-audio default);\r\n" +
            "Butterworth — maximally flat passband, -3 dB at the corner;\r\n" +
            "Bessel — gentlest phase and transient, shallowest knee;\r\n" +
            "Chebyshev — steepest knee, at the cost of passband ripple.";
        const string slopeTip =
            "Filter slope (dB/oct): steeper isolates the band harder\r\n" +
            "but rotates phase more around the corner.\r\n" +
            "The available slopes follow the chosen family\r\n" +
            "(Linkwitz-Riley only 12/24/48).";
        string rippleTip =
            "Chebyshev passband ripple (dB): trades passband flatness\r\n" +
            "for a steeper knee — more ripple, steeper cut.\r\n" +
            "Editable only for a Chebyshev edge; capped at " +
            $"{CrossoverFilter.MaximumChebyshevRippleDb:0.#} dB, above\r\n" +
            "which the filter's pole math is undefined.";

        numericHighPassHz.ApplyToolTip(
            toolTip,
            "High-pass corner (Hz): this driver plays only above it.\r\n" +
            "A tweeter/midrange low-cut that keeps excursion and\r\n" +
            "distortion out of the band it should not reproduce.");
        toolTip.SetToolTip(comboBoxHighPassFamily, familyTip);
        toolTip.SetToolTip(comboBoxHighPassSlope, slopeTip);
        numericHighPassRipple.ApplyToolTip(toolTip, rippleTip);

        numericLowPassHz.ApplyToolTip(
            toolTip,
            "Low-pass corner (Hz): this driver plays only below it.\r\n" +
            "A woofer/midbass high-cut so it hands off cleanly to the\r\n" +
            "driver above instead of beaming or breaking up.");
        toolTip.SetToolTip(comboBoxLowPassFamily, familyTip);
        toolTip.SetToolTip(comboBoxLowPassSlope, slopeTip);
        numericLowPassRipple.ApplyToolTip(toolTip, rippleTip);

        toolTip.SetToolTip(
            comboAllPassType,
            "All-pass: rotates phase without touching the magnitude —\r\n" +
            "the tool for lining drivers up through a crossover region.\r\n" +
            "Unlike a delay (constant everywhere) or a polarity flip\r\n" +
            "(180° everywhere), it turns the phase locally.\r\n" +
            "1st — 180° of swing, -90° at the corner, no Q.\r\n" +
            "2nd — 360° of swing, -180° at the corner, Q sets the width.\r\n" +
            "Its own stage: it runs even with the crossover off.");
        numericAllPassFreq.ApplyToolTip(
            toolTip,
            "All-pass corner (Hz): where the phase rotation is centred.\r\n" +
            "Usually parked on the crossover point being aligned —\r\n" +
            "the sub-to-midbass hand-off at 60-100 Hz is the classic\r\n" +
            "case. Pair it with the Invert null test to find the spot.");
        numericAllPassQ.ApplyToolTip(
            toolTip,
            "All-pass Q — how abruptly the phase turns (2nd order only).\r\n" +
            "A higher Q turns it harder over a narrower band and piles\r\n" +
            "up more group delay at the corner. It does NOT change the\r\n" +
            "total 360° swing, only how fast it happens.");
        toolTip.SetToolTip(
            labelAllpassBand,
            "Group delay this all-pass adds at its own corner\r\n" +
            "(≈ 4Q/ω₀), read from the digital filter, not the analog ideal.\r\n" +
            "This is why the stage works — and its risk: on a low corner\r\n" +
            "it reaches many milliseconds and cannot be undone by EQ.\r\n" +
            "Rough audibility: ~10 ms in the bass, ~2 ms up top.");
        toolTip.SetToolTip(
            buttonPeqLoad,
            "Load a parametric EQ for this channel from a file\r\n" +
            "(an EQ Wizard export or a compatible PEQ list); the bands\r\n" +
            "add on top of the crossover in the processed response.");
        toolTip.SetToolTip(
            buttonPeqClear,
            "Remove the loaded PEQ from this channel.");

        toolTip.SetToolTip(
            checkBoxShowRaw,
            "Plot this channel's raw measured response — the driver\r\n" +
            "before the DSP chain, drawn translucent for an A/B\r\n" +
            "against the processed trace.");
        toolTip.SetToolTip(
            checkBoxShowProcessed,
            "Plot this channel's processed response — the measured\r\n" +
            "driver after gain, delay, polarity, the crossover and PEQ.");
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
        UpdateAllPassAvailability();
        UpdateAllPassGroupDelay();
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

    // The order names stay terse ("1st"/"2nd") because the block is dense and the combo
    // is narrow; the tooltip carries what each one actually does.
    private void PopulateAllPassCombo()
    {
        comboAllPassType.Items.AddRange(
        [
            AllPassType.Off,
            AllPassType.FirstOrder,
            AllPassType.SecondOrder
        ]);
        comboAllPassType.Format += (_, args) =>
        {
            if (args.ListItem is AllPassType type)
            {
                args.Value = type switch
                {
                    AllPassType.FirstOrder => "1st",
                    AllPassType.SecondOrder => "2nd",
                    _ => "Off"
                };
            }
        };
        comboAllPassType.SelectedIndex = 0;
    }

    private void InitializeFamilyCombo(DarkComboBox familyComboBox, DarkComboBox slopeComboBox)
    {
        familyComboBox.Items.AddRange(
        [
            CrossoverFilterFamily.LinkwitzRiley,
            CrossoverFilterFamily.Butterworth,
            CrossoverFilterFamily.Bessel,
            CrossoverFilterFamily.Chebyshev
        ]);
        familyComboBox.Format += (_, args) =>
        {
            if (args.ListItem is CrossoverFilterFamily family)
            {
                args.Value = family switch
                {
                    CrossoverFilterFamily.LinkwitzRiley => "Linkwitz-Riley",
                    CrossoverFilterFamily.Bessel => "Bessel",
                    CrossoverFilterFamily.Chebyshev => "Chebyshev",
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
        checkBoxMono.CheckedChanged += (_, _) => RaiseSettingsChanged();
        comboBoxCrossoverKind.SelectedIndexChanged += (_, _) =>
        {
            UpdateCrossoverAvailability();
            RaiseSettingsChanged();
        };
        WireEdgeEvents(
            numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope, numericHighPassRipple);
        WireEdgeEvents(
            numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope, numericLowPassRipple);
        comboAllPassType.SelectedIndexChanged += (_, _) =>
        {
            UpdateAllPassAvailability();
            UpdateAllPassGroupDelay();
            RaiseSettingsChanged();
        };
        numericAllPassFreq.ValueChanged += (_, _) =>
        {
            UpdateAllPassGroupDelay();
            RaiseSettingsChanged();
        };
        numericAllPassQ.ValueChanged += (_, _) =>
        {
            UpdateAllPassGroupDelay();
            RaiseSettingsChanged();
        };
        checkBoxShowRaw.CheckedChanged += (_, _) => RaiseSettingsChanged();
        checkBoxShowProcessed.CheckedChanged += (_, _) => RaiseSettingsChanged();
        checkBoxBypass.CheckedChanged += (_, _) => RaiseSettingsChanged();
    }

    private void WireEdgeEvents(
        DarkNumericUpDown frequencyInput,
        DarkComboBox familyComboBox,
        DarkComboBox slopeComboBox,
        DarkNumericUpDown rippleInput)
    {
        frequencyInput.ValueChanged += (_, _) => RaiseSettingsChanged();
        familyComboBox.SelectedIndexChanged += (_, _) =>
        {
            PopulateSlopes(familyComboBox, slopeComboBox);
            // The ripple field is editable only for Chebyshev, so it follows the family.
            UpdateCrossoverAvailability();
            RaiseSettingsChanged();
        };
        slopeComboBox.SelectedIndexChanged += (_, _) => RaiseSettingsChanged();
        rippleInput.ValueChanged += (_, _) => RaiseSettingsChanged();
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

        UpdateRippleAvailability(numericHighPassRipple, comboBoxHighPassFamily, highPass);
        UpdateRippleAvailability(numericLowPassRipple, comboBoxLowPassFamily, lowPass);
    }

    // The passband ripple only means anything for a Chebyshev edge, so it is greyed
    // out (disabled) for any other family or an inactive edge, and editable only for
    // an active Chebyshev edge.
    private static void UpdateRippleAvailability(
        DarkNumericUpDown rippleInput,
        DarkComboBox familyComboBox,
        bool edgeActive)
    {
        bool chebyshev = familyComboBox.SelectedItem is CrossoverFilterFamily.Chebyshev;
        rippleInput.Enabled = edgeActive && chebyshev;
    }

    // The all-pass is its own stage, so nothing here depends on the crossover kind: the
    // frequency is live whenever the stage is on, and Q only for a second-order section —
    // a first-order one has a single real pole and no Q at all.
    private void UpdateAllPassAvailability()
    {
        AllPassType type = SelectedAllPassType;
        UiStyle.SetTextEnabledLook(labelAllpass, type != AllPassType.Off);
        numericAllPassFreq.Enabled = type != AllPassType.Off;
        numericAllPassQ.Enabled = type == AllPassType.SecondOrder;
    }

    // The delay the all-pass piles up at its own corner (tau ~ 4Q/w0) — the reason the
    // stage works, and on a low corner its main risk. Read from the digital biquad at the
    // project's rate so the readout cannot disagree with the plots.
    private void UpdateAllPassGroupDelay()
    {
        AllPassSpec stage = AllPassStage;
        if (stage.Type == AllPassType.Off)
        {
            labelAllpassBand.Text = string.Empty;
            return;
        }

        double milliseconds =
            AllPassFilter.GroupDelaySeconds(stage, stage.FrequencyHz, sampleRateHz) * 1_000.0;
        // Two decimals at the values that matter (a 60-100 Hz alignment lands near
        // 5-15 ms), but drop them past 100 ms: a low corner with a high Q reaches
        // four digits (10 Hz at Q 20 is over a second) and would overflow the label.
        labelAllpassBand.Text = milliseconds >= 100
            ? $"= {milliseconds:0} ms"
            : $"= {milliseconds:0.00} ms";
    }

    // The ruler-check readout: the delay expressed as a distance in air.
    private void UpdateDelayDistance()
    {
        double millimeters = (double)numericDelay.Value * Acoustics.SpeedOfSoundAt20CMetersPerSecond;
        labelDelayMm.Text = $"= {millimeters:0.#} mm";
    }

    private static CrossoverEdge ReadEdge(
        DarkNumericUpDown frequencyInput,
        DarkComboBox familyComboBox,
        DarkComboBox slopeComboBox,
        DarkNumericUpDown rippleInput)
    {
        CrossoverFilterFamily family =
            familyComboBox.SelectedItem is CrossoverFilterFamily selected
                ? selected
                : CrossoverFilterFamily.LinkwitzRiley;
        int slope = slopeComboBox.SelectedItem is int value ? value : 24;
        return new CrossoverEdge(
            family, (double)frequencyInput.Value, slope, (double)rippleInput.Value);
    }
}
