using Resonalyze.Dsp;

namespace Resonalyze;

public partial class VirtualCrossoverChannelControl
{
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
}
