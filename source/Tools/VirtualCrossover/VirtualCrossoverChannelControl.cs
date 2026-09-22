using System.ComponentModel;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze;

/// <summary>One channel block of the Virtual DSP tool; source resolution and curve rendering stay with the host panel.</summary>
public partial class VirtualCrossoverChannelControl : UserControl
{
    private string channelName = "A";
    private bool suppressChangeEvents;
    private bool muted;
    private bool collapsed;
    private double peqPreampDb;
    private int bottomMargin;
    private bool phaseControlShown;
    private bool firControlShown;
    private int processorSampleRateHz = 48_000;
    private string? firSourceName;
    private FirFilter? firKernel;
    private FirCrossoverDesign? firDesign;

    public VirtualCrossoverChannelControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        InitializeComponent();
        // Designer height is the tall one (phase + FIR rows); the block ends at its lowest shown row plus this margin.
        // Measured once here in designer units: a hidden optional row would make it read zero later.
        bottomMargin = Math.Max(
            0, MaximumSize.Height - Controls.Cast<Control>().Max(child => child.Bottom));
        numericHighPassRipple.Maximum = (decimal)CrossoverFilter.MaximumChebyshevRippleDb;
        numericLowPassRipple.Maximum = (decimal)CrossoverFilter.MaximumChebyshevRippleDb;
        PopulateCrossoverCombos();
        WireEvents();
        UpdateZoneAvailability();
        UpdateCrossoverAvailability();
        UpdateDelayTooltip();
        UpdateTotalGain();
        // Applied here, not by the host: the flow list measures the block as soon as it is added.
        ApplyOptionalRows();
    }

    /// <summary>PEQ preamp (dB) pushed by the host, folded into the gain readout.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double PeqPreampDb
    {
        get => peqPreampDb;
        set
        {
            if (peqPreampDb == value)
            {
                return;
            }

            peqPreampDb = value;
            UpdateTotalGain();
        }
    }

    public event EventHandler? SettingsChanged;

    public event EventHandler? SourceClicked;

    public event EventHandler? SpatialAverageClicked;

    public event EventHandler? PeqMenuClicked;

    public event EventHandler? FirClicked;

    public event EventHandler? AcousticGoalClicked;

    /// <summary>Separate from <see cref="SettingsChanged"/>: the fold is persisted without recomputing curves.</summary>
    public event EventHandler? CollapsedChanged;

    public event EventHandler? MoveUpClicked;

    public event EventHandler? MoveDownClicked;

    public void SetMoveAvailability(bool canMoveUp, bool canMoveDown)
    {
        buttonMoveUp.Enabled = canMoveUp;
        buttonMoveDown.Enabled = canMoveDown;
    }

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

    /// <summary>Shows the attached spatial average in the button text, since it gates the hybrid view.</summary>
    /// <param name="resolved">False for a capture the session refers to but could not read.</param>
    internal void SetSpatialAverage(
        string? title,
        double? integratedSeconds,
        bool resolved,
        VirtualCrossoverSpatialAverageMode mode,
        DateTimeOffset? measuredAtUtc = null)
    {
        bool present = !string.IsNullOrWhiteSpace(title);
        string label = mode switch
        {
            VirtualCrossoverSpatialAverageMode.MicArray => "Array",
            VirtualCrossoverSpatialAverageMode.MovingMic => "MMM",
            _ => "Avg off"
        };
        buttonSpatialAverage.Text = mode == VirtualCrossoverSpatialAverageMode.Off
            ? label
            : !present ? label : resolved ? $"{label} ✓" : $"{label} ⚠";
        buttonSpatialAverage.ForeColor = !present
            ? UiPalette.TextPrimary
            : resolved ? UiPalette.Success : UiPalette.Warning;
        string newLine = Environment.NewLine;
        spatialAverageTooltip = !present
            ? mode switch
            {
                VirtualCrossoverSpatialAverageMode.MicArray =>
                    "This channel was measured with one microphone, so the hybrid " +
                    "draws it from that POINT measurement." + newLine + newLine +
                    "Legitimate where a point and an average are the same thing — " +
                    "below the cabin's first mode they are — but its dips are this " +
                    "one spot's, and an equalizer fitted to them is fitted to a " +
                    "place nobody's head occupies." + newLine + newLine +
                    "Click to change the method the project reads.",
                VirtualCrossoverSpatialAverageMode.Off =>
                    "The project draws no spatial average." + newLine + newLine +
                    "Click to change the method it reads.",
                _ =>
                    "No spatial average for this channel." + newLine + newLine +
                    "Click to attach a moving-microphone capture. The hybrid view " +
                    "needs one on every channel that plays."
            }
            : resolved
            ? $"Spatial average: {title}" +
                (integratedSeconds is { } seconds
                    ? $"{newLine}{seconds:0} s integrated"
                    : string.Empty) +
                (measuredAtUtc is { } measured
                    ? $"{newLine}measured {measured.ToLocalTime():g}"
                    : string.Empty) +
                newLine + newLine +
                "Click to replace it, or to detach it."
            : $"Missing spatial average: {title}" + newLine +
                "The session still refers to it, but the file could not be read." +
                newLine + newLine +
                "Click to attach it again, or to detach it.";
        tooltipHost?.SetToolTip(buttonSpatialAverage, spatialAverageTooltip);
    }
    internal ThemedNumericUpDown GainInput => numericGain;
    internal ThemedNumericUpDown DelayInput => numericDelay;
    internal CheckBox InvertCheckBox => checkBoxInvert;
    internal CheckBox MonoCheckBox => checkBoxMono;
    internal ThemedComboBox ZoneComboBox => comboBoxZone;
    internal ThemedComboBox CrossoverKindComboBox => comboBoxCrossoverKind;
    internal ThemedNumericUpDown HighPassFrequencyInput => numericHighPassHz;
    internal ThemedComboBox HighPassFamilyComboBox => comboBoxHighPassFamily;
    internal ThemedComboBox HighPassSlopeComboBox => comboBoxHighPassSlope;
    internal ThemedNumericUpDown LowPassFrequencyInput => numericLowPassHz;
    internal ThemedComboBox LowPassFamilyComboBox => comboBoxLowPassFamily;
    internal ThemedComboBox LowPassSlopeComboBox => comboBoxLowPassSlope;
    internal ThemedNumericUpDown HighPassRippleInput => numericHighPassRipple;
    internal ThemedNumericUpDown LowPassRippleInput => numericLowPassRipple;
    internal Button MuteButton => buttonMute;
    internal Button CollapseButton => buttonCollapse;
    internal ThemedNumericUpDown PhaseInput => numericPhase;
    internal Label PhaseLabel => labelPhase;
    internal Label PhaseInfoLabel => labelPhaseInfo;
    internal Button FirButton => buttonFir;
    internal Button AcousticGoalButton => buttonAcousticGoal;
    internal Label FirLabel => labelFir;
    internal Label FirInfoLabel => labelFirInfo;
    internal Label PeqInfoLabel => labelPeqInfo;
    internal Button PeqMenuButton => buttonPeqMenu;
    internal Label TotalGainLabel => labelTotalGain;
    internal CheckBox ShowRawCheckBox => checkBoxShowRaw;
    internal CheckBox ShowProcessedCheckBox => checkBoxShowProcessed;
    internal CheckBox BypassCheckBox => checkBoxBypass;

    public CrossoverKind SelectedCrossoverKind =>
        comboBoxCrossoverKind.SelectedItem is CrossoverKind kind
            ? kind
            : CrossoverKind.Off;

    public VirtualCrossoverZone SelectedZone =>
        comboBoxZone.SelectedItem is VirtualCrossoverZone zone
            ? zone
            : VirtualCrossoverZone.Front;

    public CrossoverEdge HighPassEdge => ReadEdge(
        numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope, numericHighPassRipple);

    public CrossoverEdge LowPassEdge => ReadEdge(
        numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope, numericLowPassRipple);

    /// <summary>Hiding the row does not clear an angle already dialled in; it stays in the project and simulated.</summary>
    [DefaultValue(false)]
    public bool PhaseControlShown
    {
        get => phaseControlShown;
        set
        {
            if (phaseControlShown == value)
            {
                return;
            }

            phaseControlShown = value;
            ApplyOptionalRows();
        }
    }

    /// <summary>Hiding the row does not detach a loaded kernel.</summary>
    [DefaultValue(false)]
    public bool FirControlShown
    {
        get => firControlShown;
        set
        {
            if (firControlShown == value)
            {
                return;
            }

            firControlShown = value;
            ApplyOptionalRows();
        }
    }

    /// <summary>Processor rate: the all-pass corner and the FIR length in time depend on it.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ProcessorSampleRateHz
    {
        get => processorSampleRateHz;
        set
        {
            if (processorSampleRateHz == value || value <= 0)
            {
                return;
            }

            processorSampleRateHz = value;
            UpdatePhaseReadout();
            UpdateFirReadout();
        }
    }

    /// <summary>A goal for an edge the channel does not run is not shown as stated; the tooltip names it as kept.</summary>
    internal void SetAcousticGoal(
        JunctionAcousticTarget? highPass,
        JunctionAcousticTarget? lowPass,
        bool highPassRuns = true,
        bool lowPassRuns = true)
    {
        VirtualCrossoverChannelGoalReadout readout =
            VirtualCrossoverChannelGoalReadout.Read(highPass, lowPass, highPassRuns, lowPassRuns);
        buttonAcousticGoal.Text = readout.Text;
        buttonAcousticGoal.ForeColor = readout.Color;
        tooltipHost?.SetToolTip(buttonAcousticGoal, readout.Tooltip);
    }

    internal void SetFir(FirFilter? kernel, string? sourceName, FirCrossoverDesign? design = null)
    {
        firKernel = kernel;
        firSourceName = kernel == null || string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        firDesign = kernel == null ? null : design;
        UpdateFirReadout();
    }

    /// <summary>Why the FIR button is red (FIR crossover at a stale rate, or beside an IIR crossover), or null.</summary>
    internal string? FirConflict => VirtualCrossoverChannelFirReadout.ConflictOf(
        firKernel, firDesign, processorSampleRateHz, SelectedCrossoverKind);

    private double PhaseReferenceHz => VirtualCrossoverChannelPhaseReadout.ReferenceHz(
        SelectedZone, (double)numericHighPassHz.Value, (double)numericLowPassHz.Value);

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

    [DefaultValue(false)]
    public bool Muted
    {
        get => muted;
        set
        {
            muted = value;
            buttonMute.Text = value ? "🔇" : "🔈";
            buttonMute.ForeColor = value
                ? UiPalette.Error
                : UiPalette.TextPrimary;
        }
    }

    [DefaultValue(false)]
    public bool Collapsed
    {
        get => collapsed;
        set
        {
            if (collapsed == value)
            {
                return;
            }

            collapsed = value;
            ApplyCollapsedState();
        }
    }

    // Read off live controls, not pixel literals: rows are DPI-scaled.
    private int FoldLine => comboBoxCrossoverKind.Top;

    private int RowPitch => numericPhase.Top - buttonPeqMenu.Top;

    // Hidden rather than clipped: a clipped field stays in the tab order and counts towards the fold height.
    private bool IsPhaseRow(Control child) =>
        ReferenceEquals(child, labelPhase) ||
        ReferenceEquals(child, numericPhase) ||
        ReferenceEquals(child, labelPhaseInfo);

    private bool IsFirRow(Control child) =>
        ReferenceEquals(child, labelFir) ||
        ReferenceEquals(child, buttonFir) ||
        ReferenceEquals(child, labelFirInfo);

    // The FIR row moves up into the phase row's place when that row is hidden.
    private void PlaceFirRow()
    {
        int offset = phaseControlShown ? RowPitch : 0;
        labelFir.Top = labelPhase.Top + offset;
        buttonFir.Top = numericPhase.Top + offset;
        labelFirInfo.Top = numericPhase.Top + offset;
    }

    private void ApplyCollapsedState(bool raiseChanged = true)
    {
        buttonCollapse.Text = collapsed ? "+" : "−";
        int keptBottom = 0;
        SuspendLayout();
        PlaceFirRow();
        foreach (Control child in Controls)
        {
            bool kept = (!collapsed || child.Top < FoldLine) &&
                (phaseControlShown || !IsPhaseRow(child)) &&
                (firControlShown || !IsFirRow(child));
            child.Visible = kept;
            if (kept)
            {
                keptBottom = Math.Max(keptBottom, child.Bottom);
            }
        }

        ResumeLayout(false);
        // The border is painted inside the client area, so end one designer margin below the lowest shown control,
        // measured off live children (rows scale and round independently at other DPIs).
        int height = keptBottom + bottomMargin;
        // One suspended parent layout: a list reflowed against a half-moved pin stacks the next block over this one.
        Control? parent = Parent;
        parent?.SuspendLayout();
        try
        {
            // Move the bound in the way first (Min == Max pins the block). Never through zero: the flow list reads it as no height.
            if (height < MinimumSize.Height)
            {
                MinimumSize = new Size(MinimumSize.Width, height);
                MaximumSize = new Size(MaximumSize.Width, height);
            }
            else
            {
                MaximumSize = new Size(MaximumSize.Width, height);
                MinimumSize = new Size(MinimumSize.Width, height);
            }

            Height = height;
        }
        finally
        {
            parent?.ResumeLayout(performLayout: true);
        }

        if (raiseChanged)
        {
            CollapsedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // The base does not scale the parked margin; follow only calls that actually scale height.
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        if ((specified & BoundsSpecified.Height) != 0)
        {
            bottomMargin = (int)Math.Round(bottomMargin * factor.Height);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        RoundedSurface.Paint(
            this,
            e.Graphics,
            RoundedSurface.DefaultCornerRadius,
            UiPalette.Border);

        base.OnPaint(e);
    }

    /// <summary>Acoustic polarity read from the measured IR, independent of the Invert switch.</summary>
    public void SetMeasuredPolarity(PolarityEstimate polarity)
    {
        (labelMeasuredPolarity.Text, labelMeasuredPolarity.ForeColor) = polarity switch
        {
            PolarityEstimate.Positive => ("IR: Normal", UiPalette.Success),
            PolarityEstimate.Negative => ("IR: Inverted", UiPalette.Error),
            _ => ("IR: Unknown", UiPalette.TextMuted)
        };
    }

    private WrappingToolTip? tooltipHost;
    private string spatialAverageTooltip = string.Empty;

    public void ApplyTooltips(WrappingToolTip toolTip)
    {
        tooltipHost = toolTip;
        if (spatialAverageTooltip.Length > 0)
        {
            toolTip.SetToolTip(buttonSpatialAverage, spatialAverageTooltip);
        }

        ArgumentNullException.ThrowIfNull(toolTip);
        // Also registered on the inner editor so the tip shows while editing.
        numericGain.ApplyToolTip(
            toolTip,
            "Channel gain (dB).\r\n" +
            "Relative levels are only honest when the measurements\r\n" +
            "were captured through the same playback chain;\r\n" +
            "compensate any difference here.");
        toolTip.SetToolTip(
            labelTotalGain,
            "Channel gain with the loaded PEQ's preamp folded in —\r\n" +
            "the single level to dial in on a DSP whose equalizer\r\n" +
            "has no preamp of its own.\r\n" +
            "Shown only when the PEQ carries a preamp; the bands\r\n" +
            "themselves are frequency-dependent and not part of it.");
        toolTip.SetToolTip(
            checkBoxInvert,
            "Invert the channel polarity — the DSP polarity switch.\r\n" +
            "Also the null test: with polarity flipped, the deepest\r\n" +
            "notch at the crossover frequency marks perfect alignment.");
        numericDelay.ApplyToolTip(toolTip, VirtualCrossoverChannelDelayReadout.Tooltip((double)numericDelay.Value));
        numericPhase.ApplyToolTip(toolTip, PhaseTooltip());
        UpdateFirReadout();
        toolTip.SetToolTip(
            labelPhaseInfo,
            "What the angle beside it actually builds: the crossover it is\r\n" +
            "stated at, and the all-pass corner the device places for it.\r\n" +
            "Amber when the corner would have to go higher than the device\r\n" +
            "will place one — then the setting delivers the angle shown\r\n" +
            "instead, and every smaller setting delivers the same filter.");
        toolTip.SetToolTip(
            buttonCollapse,
            "Fold the block down to its header — source, gain, delay\r\n" +
            "and polarity stay visible, the filter chain is hidden.\r\n" +
            "Nothing is bypassed: a folded channel plays and counts\r\n" +
            "exactly as before.");
        foreach (Control arrow in new Control[] { buttonMoveUp, buttonMoveDown })
        {
            toolTip.SetToolTip(
                arrow,
                "Move this block one place up or down. Blocks are lettered by\r\n" +
                "position, so the ones that move are re-lettered and\r\n" +
                "recoloured; sources and settings travel with them.");
        }
        toolTip.SetToolTip(
            buttonMute,
            "Mute the channel: exclude it from the sum, the loss,\r\n" +
            "the metric, Auto delay and both plots — a quick\r\n" +
            "\"what changes without this driver\" check.\r\n" +
            "Shared by both sides — the driver pair is muted as one.");
        toolTip.SetToolTip(
            checkBoxBypass,
            "Bypass the DSP chain: feed the raw measured signal with\r\n" +
            "no gain, delay, polarity, crossover or PEQ —\r\n" +
            "the driver's natural band-pass, for an A/B against the\r\n" +
            "processed result.\r\n" +
            "Shared by both sides, like Mute.");
        toolTip.SetToolTip(
            checkBoxMono,
            "One physical driver serving both sides (typically the\r\n" +
            "subwoofer): a single set of settings participates in the\r\n" +
            "L and R views and calculations alike. The stereo Auto\r\n" +
            "delay tunes it with the left side and reports the right\r\n" +
            "junction it pins.");
        toolTip.SetToolTip(
            comboBoxZone,
            "Which part of the installation this block is: Front, Rear or\r\n" +
            "Center. The grouped views and Auto delay's staging follow it.\r\n" +
            "Center forces Mono — it has no side.");
        toolTip.SetToolTip(
            labelMeasuredPolarity,
            "Acoustic polarity read from the measured IR: Normal pushes\r\n" +
            "toward the mic first, Inverted pulls first, Unknown has no\r\n" +
            "source. Independent of the Invert switch.");
        toolTip.SetToolTip(
            comboBoxCrossoverKind,
            "This driver's crossover role:\r\n" +
            "Off — full range; High-pass — only above the HP corner;\r\n" +
            "Low-pass — only below the LP corner; Band-pass — both.\r\n" +
            "Only the edges the role uses stay editable.");

        const string familyTip =
            "Filter alignment for this edge:\r\n" +
            "Linkwitz-Riley — -6 dB at the corner, two edges sum flat\r\n" +
            "(the car-audio default); LR12 and LR36 sum flat only with\r\n" +
            "one side inverted — use Invert on one of the channels;\r\n" +
            "Butterworth — maximally flat passband, -3 dB at the corner;\r\n" +
            "Bessel — gentlest phase and transient, shallowest knee;\r\n" +
            "Chebyshev — steepest knee, at the cost of passband ripple.";
        const string slopeTip =
            "Filter slope (dB/oct): steeper isolates the band harder\r\n" +
            "but rotates phase more around the corner.\r\n" +
            "The available slopes follow the chosen family\r\n" +
            "(Linkwitz-Riley only 12/24/36/48).";
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
            buttonPeqMenu,
            "This channel's parametric EQ: load it from a file, edit it in\r\n" +
            "the EQ Wizard, or clear it. All-pass filters live here too,\r\n" +
            "as bands of the bank.");

        toolTip.SetToolTip(
            checkBoxShowRaw,
            "Plot this channel's raw measured response — the driver\r\n" +
            "before the DSP chain, drawn translucent for an A/B\r\n" +
            "against the processed trace.\r\n" +
            "The toggle is shared by both sides; each side draws its\r\n" +
            "own measurement.");
        toolTip.SetToolTip(
            checkBoxShowProcessed,
            "Plot this channel's processed response — the measured\r\n" +
            "driver after gain, delay, polarity, the crossover and PEQ.\r\n" +
            "The toggle is shared by both sides; each side draws its\r\n" +
            "own measurement.");
    }

    /// <summary>Applies stored values without firing SettingsChanged per field; the host redraws once afterward.</summary>
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

        UpdateZoneAvailability();
        UpdateCrossoverAvailability();
        UpdateDelayTooltip();
        UpdateTotalGain();
    }

    private void PopulateCrossoverCombos()
    {
        comboBoxZone.Items.AddRange([.. VirtualCrossoverZones.All.Cast<object>()]);
        comboBoxZone.Format += (_, args) =>
        {
            if (args.ListItem is VirtualCrossoverZone zone)
            {
                args.Value = VirtualCrossoverZones.DisplayName(zone);
            }
        };
        comboBoxZone.SelectedItem = VirtualCrossoverZone.Front;

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

    private void InitializeFamilyCombo(ThemedComboBox familyComboBox, ThemedComboBox slopeComboBox)
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

    // LR exists only in 12/24/36/48; the current slope is kept when the new family supports it.
    private static void PopulateSlopes(ThemedComboBox familyComboBox, ThemedComboBox slopeComboBox)
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
        buttonSpatialAverage.Click +=
            (_, _) => SpatialAverageClicked?.Invoke(this, EventArgs.Empty);
        buttonMute.Click += (_, _) =>
        {
            Muted = !Muted;
            RaiseSettingsChanged();
        };
        buttonCollapse.Click += (_, _) => Collapsed = !Collapsed;
        buttonMoveUp.Click += (_, _) => MoveUpClicked?.Invoke(this, EventArgs.Empty);
        buttonMoveDown.Click += (_, _) => MoveDownClicked?.Invoke(this, EventArgs.Empty);
        buttonPeqMenu.Click += (_, _) => PeqMenuClicked?.Invoke(this, EventArgs.Empty);
        buttonFir.Click += (_, _) => FirClicked?.Invoke(this, EventArgs.Empty);
        buttonAcousticGoal.Click += (_, _) => AcousticGoalClicked?.Invoke(this, EventArgs.Empty);

        numericGain.ValueChanged += (_, _) =>
        {
            UpdateTotalGain();
            RaiseSettingsChanged();
        };
        numericDelay.ValueChanged += (_, _) =>
        {
            UpdateDelayTooltip();
            RaiseSettingsChanged();
        };
        checkBoxInvert.CheckedChanged += (_, _) => RaiseSettingsChanged();
        checkBoxMono.CheckedChanged += (_, _) => RaiseSettingsChanged();
        comboBoxZone.SelectedIndexChanged += (_, _) =>
        {
            UpdateZoneAvailability();
            UpdatePhaseReadout();
            RaiseSettingsChanged();
        };
        numericPhase.ValueChanged += (_, _) =>
        {
            // The device has 64 positions, so a user-typed angle snaps (re-entrant: the snapped value is already on the grid).
            // Host-loaded values pass through unsnapped: the file format accepts any angle, and silently rewriting it would desync field and project.
            if (suppressChangeEvents)
            {
                UpdatePhaseReadout();
                return;
            }

            var snapped = (decimal)PhaseRotationControl.SnapToGrid((double)numericPhase.Value);
            if (snapped != numericPhase.Value)
            {
                numericPhase.Value = snapped;
                return;
            }

            UpdatePhaseReadout();
            RaiseSettingsChanged();
        };
        comboBoxCrossoverKind.SelectedIndexChanged += (_, _) =>
        {
            UpdateCrossoverAvailability();
            UpdateFirReadout();
            RaiseSettingsChanged();
        };
        WireEdgeEvents(
            numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope, numericHighPassRipple);
        WireEdgeEvents(
            numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope, numericLowPassRipple);
        checkBoxShowRaw.CheckedChanged += (_, _) => RaiseSettingsChanged();
        checkBoxShowProcessed.CheckedChanged += (_, _) => RaiseSettingsChanged();
        checkBoxBypass.CheckedChanged += (_, _) => RaiseSettingsChanged();
    }

    private void WireEdgeEvents(
        ThemedNumericUpDown frequencyInput,
        ThemedComboBox familyComboBox,
        ThemedComboBox slopeComboBox,
        ThemedNumericUpDown rippleInput)
    {
        frequencyInput.ValueChanged += (_, _) =>
        {
            UpdatePhaseReadout();
            RaiseSettingsChanged();
        };
        familyComboBox.SelectedIndexChanged += (_, _) =>
        {
            PopulateSlopes(familyComboBox, slopeComboBox);
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

    // A centre channel is derived from L and R, so Mono is forced on and locked; set through the normal path so the project stores it.
    private void UpdateZoneAvailability()
    {
        bool forced = VirtualCrossoverZones.RequiresMono(SelectedZone);
        if (forced)
        {
            checkBoxMono.Checked = true;
        }

        checkBoxMono.Enabled = !forced;
        UiStyle.SetTextEnabledLook(checkBoxMono, !forced, interactive: true);
    }

    // Greyed out, not hidden, so the layout never shifts.
    private void UpdateCrossoverAvailability()
    {
        VirtualCrossoverChannelAvailability available = VirtualCrossoverChannelAvailability.Of(
            SelectedCrossoverKind,
            comboBoxHighPassFamily.SelectedItem as CrossoverFilterFamily?,
            comboBoxLowPassFamily.SelectedItem as CrossoverFilterFamily?);

        UiStyle.SetTextEnabledLook(labelHighPass, available.HighPass);
        numericHighPassHz.Enabled = available.HighPass;
        comboBoxHighPassFamily.Enabled = available.HighPass;
        comboBoxHighPassSlope.Enabled = available.HighPass;

        UiStyle.SetTextEnabledLook(labelLowPass, available.LowPass);
        numericLowPassHz.Enabled = available.LowPass;
        comboBoxLowPassFamily.Enabled = available.LowPass;
        comboBoxLowPassSlope.Enabled = available.LowPass;

        numericHighPassRipple.Enabled = available.HighPassRipple;
        numericLowPassRipple.Enabled = available.LowPassRipple;
    }

    private void UpdateTotalGain() =>
        labelTotalGain.Text = VirtualCrossoverChannelTotalGain.Text((double)numericGain.Value, peqPreampDb);

    // Runs through the collapse path: moving the size pin anywhere else races the flow list's reflow.
    private void ApplyOptionalRows()
    {
        // Without the fold event: the host would persist a fold state nobody asked for.
        ApplyCollapsedState(raiseChanged: false);
        UpdatePhaseReadout();
        UpdateFirReadout();
    }

    private void UpdateFirReadout()
    {
        VirtualCrossoverChannelFirReadout readout = VirtualCrossoverChannelFirReadout.Read(
            firKernel, firSourceName, firDesign, processorSampleRateHz, SelectedCrossoverKind);
        buttonFir.Text = readout.ButtonText;
        buttonFir.ForeColor = readout.ButtonColor;
        labelFirInfo.Text = readout.Info;
        labelFirInfo.ForeColor = readout.InfoColor;
        if (tooltipHost is { } host)
        {
            host.SetToolTip(buttonFir, readout.ButtonTip);
            host.SetToolTip(labelFirInfo, readout.InfoTip);
        }
    }

    private void UpdatePhaseReadout()
    {
        VirtualCrossoverChannelPhaseReadout readout = VirtualCrossoverChannelPhaseReadout.Read(
            (double)numericPhase.Value, PhaseReferenceHz, processorSampleRateHz);
        labelPhaseInfo.Text = readout.Text;
        labelPhaseInfo.ForeColor = readout.Color;
        if (tooltipHost is { } host)
        {
            numericPhase.ApplyToolTip(host, PhaseTooltip());
        }
    }

    private string PhaseTooltip() => VirtualCrossoverChannelPhaseReadout.Tooltip(
        (double)numericPhase.Value, PhaseReferenceHz, processorSampleRateHz);

    // The tooltip host arrives after construction; whichever comes second applies the text.
    private void UpdateDelayTooltip()
    {
        if (tooltipHost is { } host)
        {
            numericDelay.ApplyToolTip(host, VirtualCrossoverChannelDelayReadout.Tooltip((double)numericDelay.Value));
        }
    }

    private static CrossoverEdge ReadEdge(
        ThemedNumericUpDown frequencyInput,
        ThemedComboBox familyComboBox,
        ThemedComboBox slopeComboBox,
        ThemedNumericUpDown rippleInput)
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
