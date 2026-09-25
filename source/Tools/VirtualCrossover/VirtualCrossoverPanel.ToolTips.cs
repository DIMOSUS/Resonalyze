namespace Resonalyze;

/// <summary>Tooltips of the panel's own controls; a block's come from <see cref="VirtualCrossoverChannelControl"/>.</summary>
public partial class VirtualCrossoverPanel
{
    private void InitializeToolTips()
    {
        toolTip.SetToolTip(
            checkBoxShowSum,
            "The complex (vector) sum of the processed channels —\r\n" +
            "the physically correct prediction of all drivers\r\n" +
            "playing together.");
        toolTip.SetToolTip(
            labelSumLoss,
            "How many dB the complex sum falls short of the magnitude sum\r\n" +
            "(<= 0): 0 dB is perfectly in phase. The selector beside it\r\n" +
            "picks the window it is read through.");
        toolTip.SetToolTip(
            buttonAddChannel,
            "Add a channel block to the bottom of the list.");
        toolTip.SetToolTip(
            buttonRemoveChannel,
            "Drop the last channel block, with whatever is loaded in it.");
        toolTip.SetToolTip(
            buttonResetChannels,
            $"Start over: {DefaultChannelCount} empty default blocks, and the\r\n" +
            "panel's own settings; calibration and the EQ target stay.\r\n" +
            "Asks first, and copies the session aside so Load session…\r\n" +
            "brings it back.");
        toolTip.SetToolTip(
            radioViewMagnitude,
            "Show the magnitude of the channels, the sum,\r\n" +
            "and the sum loss.");
        toolTip.SetToolTip(
            radioViewPhase,
            "Show the phase of the processed channels and the sum.\r\n" +
            "Well-aligned channels track each other through\r\n" +
            "the crossover region.");
        toolTip.SetToolTip(
            radioViewImpulse,
            "Show each channel's processed impulse response around\r\n" +
            "the phase gate, every trace normalized to its own peak.\r\n" +
            "Well-aligned drivers start together.");
        toolTip.SetToolTip(
            radioViewGroupDelay,
            "Show each processed channel's group delay and the Sum's\r\n" +
            "through the phase gate: the arrival time of the energy\r\n" +
            "inside the window, in ms from the record's start.\r\n" +
            "Well-aligned drivers meet through the crossover.");
        toolTip.SetToolTip(
            radioViewStep,
            "Show each processed channel's step response and the Sum's\r\n" +
            "around the phase gate, all on one common scale.\r\n" +
            "A driver in the wrong polarity steps the other way first.");
        toolTip.SetToolTip(
            comboBoxGroupView,
            "Which part of the installation the plot shows; the curves, the\r\n" +
            "Sum, the loss and the read-out follow it. Groups sums one line\r\n" +
            "per zone. A centre is drawn but never summed.");
        toolTip.SetToolTip(
            comboBoxSmoothing,
            "Fractional-octave smoothing of the magnitude curves and the\r\n" +
            "Sum loss read. Psychoacoustic: 1/3–1/6 octave with peak\r\n" +
            "weighting. The junction metrics stay unsmoothed.");
        toolTip.SetToolTip(
            comboBoxSumLoss,
            "The window the Sum loss is read through. FDW-8: the direct\r\n" +
            "sound, as the Junction phase block reads it. Full: the\r\n" +
            "steady-state sum the cabin hears. The two are not comparable.");
        toolTip.SetToolTip(
            radioDspGroupDelay,
            "What the lower plot shows for each channel's DSP chain:\r\n" +
            "Magnitude, Phase, or filter Group delay (the crossover/PEQ\r\n" +
            "group delay in ms, excluding the channel's bulk delay).");
        toolTip.SetToolTip(
            radioDspCorrelation,
            "Junction correlation of the selected pair, and its acoustic\r\n" +
            "score, against an extra delay on the upper channel in both\r\n" +
            "polarities. 0 ms is the alignment as it stands.");
        toolTip.SetToolTip(
            comboBoxCorrelationPair,
            "Which adjacent channel pair the correlation view analyzes\r\n" +
            "(active side, ordered along the spectrum).");
        toolTip.SetToolTip(
            checkBoxShowTarget,
            "Draw the EQ target over the predicted sum: the SAME target the\r\n" +
            "EQ Wizard is set to, so a shape tuned in either place is the\r\n" +
            "one shape this app aims at. It is a magnitude reference, so it\r\n" +
            "is offered on the Magnitude view only.");
        toolTip.SetToolTip(
            numericTargetLevel,
            "The level the target hangs at. These are transfer-function\r\n" +
            "dB with no absolute reference, so the target has no level of\r\n" +
            "its own here: set it where you read the sum. Stored with the\r\n" +
            "session, not with the target, so retuning the shape leaves it.");
        toolTip.SetToolTip(
            buttonTargetSettings,
            "Shape the target — a parametric shape or an imported house\r\n" +
            "curve — previewed on the Magnitude view. It is the SAME target\r\n" +
            "the EQ Wizard equalizes towards.");
        toolTip.SetToolTip(
            comboBoxCalibration,
            "Microphone calibration for the magnitude curves and the Sum\r\n" +
            "loss read. Optional — the measurement is loopback-referenced.\r\n" +
            "Saved into the session as the curve itself.");
        toolTip.SetToolTip(
            buttonAutoDelay,
            "Align the channels: first arrivals, then a phase search for\r\n" +
            "delays and polarity, reviewed as before/after. Set the\r\n" +
            "crossovers first — the search reads their overlap.");
        toolTip.SetToolTip(
            radioSideLeft,
            "Show and edit the LEFT side of every channel pair.\r\n" +
            "● — at least one source is loaded on this side.\r\n" +
            "Keys: L picks this side, ` (US layout) swaps sides.");
        toolTip.SetToolTip(
            radioSideRight,
            "Show and edit the RIGHT side of every channel pair.\r\n" +
            "● — at least one source is loaded on this side.\r\n" +
            "Keys: R picks this side, ` (US layout) swaps sides.");
        toolTip.SetToolTip(
            buttonCopyLeftToRight,
            "Copy the LEFT side onto the RIGHT side: a dialog picks the\r\n" +
            "channels and the parts of the chain — crossover and PEQ by\r\n" +
            "default, gain, delay, polarity and the all-pass on request.\r\n" +
            "Sources stay with their side; mono channels are not\r\n" +
            "offered.");
        toolTip.SetToolTip(
            buttonCopyRightToLeft,
            "Copy the RIGHT side onto the LEFT side: a dialog picks the\r\n" +
            "channels and the parts of the chain — crossover and PEQ by\r\n" +
            "default, gain, delay, polarity and the all-pass on request.\r\n" +
            "Sources stay with their side; mono channels are not\r\n" +
            "offered.");
        toolTip.SetToolTip(
            checkBoxSideLock,
            "Keep both sides' crossover and polarity in step: a change\r\n" +
            "on the side shown is written onto the other side as it is\r\n" +
            "made. Gain, delay, phase and PEQ are not locked.");
        toolTip.SetToolTip(
            buttonDspProcessor,
            "The processor this project is designed for: its processing\r\n" +
            "rate, which every simulated filter is built at, and its PEQ Q\r\n" +
            "convention, which only restates the tuning sheet.");
        toolTip.SetToolTip(
            buttonAi,
            "Work with a chat assistant through the clipboard: Copy for AI\r\n" +
            "puts the tune on it, Import AI proposal reads the reply back\r\n" +
            "and shows every change before it is applied.\r\n" +
            "Nothing is sent anywhere by Resonalyze.");
        toolTip.SetToolTip(
            buttonTuneJunction,
            "Refine ONE junction: the lower block's low-pass and the upper\r\n" +
            "block's high-pass, each candidate read on the coherent sum after\r\n" +
            "the re-alignment Auto delay would give it. Optionally aims at an\r\n" +
            "acoustic slope. Nothing is written until Apply.");
        toolTip.SetToolTip(
            buttonAutoSetup,
            "Crossover wizard: confirm each channel's driver type and order,\r\n" +
            "then it searches corners, slopes, families and polarity for the\r\n" +
            "flattest sum, with cut-only gains. A starting point: run Auto\r\n" +
            "delay afterward to phase-align it.");
        toolTip.SetToolTip(
            buttonPhaseGate,
            "Gate for the phase and impulse views: offset, fades and an IR\r\n" +
            "preview. The magnitude view takes only the offset. Offset and\r\n" +
            "detrend belong to the side shown; lengths and mode are shared.");
        toolTip.SetToolTip(
            buttonSessionExport,
            "Save the whole session (sources, DSP chains, gate, view)\r\n" +
            "to a file to share or archive it.");
        toolTip.SetToolTip(
            buttonSessionImport,
            "Load a saved session file, replacing the current state.\r\n" +
            "Sources are re-resolved from history or their file paths.");
        toolTip.SetToolTip(
            buttonTools,
            "The occasional tools, off the main path: audition the tune\r\n" +
            "through a music file, or capture the current sum as an overlay\r\n" +
            "to compare a later one against. Each says what it does in the\r\n" +
            "menu.");
    }
}
