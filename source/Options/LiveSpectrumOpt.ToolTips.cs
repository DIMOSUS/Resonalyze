namespace Resonalyze.Options
{
    public partial class LiveSpectrumOpt
    {
        private void InitializeToolTips()
        {
            toolTip.SetToolTip(
                radioModeRta,
                "Reference-free RTA: the magnitude spectrum of the microphone input " +
                "alone, no loopback division. The only mode with an absolute dB SPL " +
                "scale and the noise slope compensation.");
            toolTip.SetToolTip(
                radioModeMmm,
                "Moving-microphone capture: the same reference-free analyzer, pinned " +
                "to the one recipe a spatial average is valid under — periodic pink, " +
                "Infinite averaging, banded dB SPL, slope compensation on, smoothing " +
                "off. Walk the microphone through the listening volume while it " +
                "integrates, then Save.\r\n\r\n" +
                "Measure each driver RAW: bypass EQ, crossovers and delays in your " +
                "own DSP and leave only the configured protective high-pass. Virtual " +
                "DSP adds the channel's chain to the capture itself, so a capture " +
                "taken through a chain gets that chain applied twice — and the result " +
                "still looks entirely plausible.");
            // radioModeTransfer, labelSpl and checkSpl take their tooltips in PresentLooks.
            toolTip.SetToolTip(
                signalTypeComboBox,
                "Excitation noise. Pink (periodic): one looped FFT period,\r\n" +
                "leakage-free — recommended. Pink: continuous. Brown: more LF\r\n" +
                "drive. White: equal energy per hertz. Silent: RTA only, no\r\n" +
                "excitation.");
            toolTip.SetToolTip(
                sequenceLengthComboBox,
                "Sets the FFT block size. Longer sequences give finer frequency resolution but slower visual updates.");
            toolTip.SetToolTip(
                overlapComboBox,
                "Overlaps successive analysis frames by sliding the FFT window a fraction of its size. Higher overlap gives faster, smoother averaging at the cost of more CPU.\r\nForced to Off for periodic pink noise, where overlapped frames are correlated and add no averaging.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies fractional-octave smoothing to the displayed Live Spectrum curve.");
            toolTip.SetToolTip(
                windowComboBox,
                "Analysis window applied before the FFT. Hann is a good\r\n" +
                "default; Flat Top for amplitude accuracy; Blackman-Harris\r\n" +
                "against leakage; Rectangular leaves the block unwindowed.\r\n" +
                "Forced to Rectangular for periodic pink noise.");
            toolTip.SetToolTip(
                averagingComboBox,
                "Averaging speed. Fast/Medium/Slow set the exponential time constant; Infinite integrates indefinitely until you reset it.");
            toolTip.SetToolTip(
                checkMainCurve,
                "Shows the main live trace (the spectrum / transfer-function curve).");
            toolTip.SetToolTip(
                checkInputMagnitude,
                "Overlays a reference-free RTA curve: the plain magnitude spectrum of the microphone input alone, with no division by the loopback reference. Independent of coherence.");
            toolTip.SetToolTip(
                checkPeakHold,
                "Overlays a peak-hold envelope that retains the maximum level seen on the curve until reset.");
            toolTip.SetToolTip(
                checkCoherence,
                "Shows the coherence (\u03B3\u00B2) curve on a 0-to-1 axis in Transfer Function mode.");
            toolTip.SetToolTip(
                coherenceLimitComboBox,
                "Frequencies whose coherence falls below this limit are drawn dimmed and dashed to flag where the transfer function is unreliable. Off disables the marking.");
            toolTip.SetToolTip(
                buttonResetAverage,
                "Clears the running average and peak-hold envelope without restarting the measurement.");
            toolTip.SetToolTip(
                comboCalibration,
                "Applies the selected microphone calibration file to Live Spectrum.");
            string tiltDescription =
                "Compensates the spectral slope of the excitation noise itself, so a " +
                "flat system reads flat whatever the noise colour (pink otherwise " +
                "falls -3 dB per octave on the per-bin dB axis, and even a flat white " +
                "PSD tilts +3 dB per octave on the banded dB SPL display). Pinned to " +
                "the level at 1 kHz. Unavailable for Silent, whose excitation " +
                "spectrum is unknown.";
            toolTip.SetToolTip(labelTilt, tiltDescription);
            toolTip.SetToolTip(checkTilt, tiltDescription);
        }
    }
}
