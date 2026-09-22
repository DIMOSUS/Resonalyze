using Resonalyze.Dsp;

namespace Resonalyze;

public partial class VirtualCrossoverChannelControl
{
    public CrossoverKind SelectedCrossoverKind =>
        comboBoxCrossoverKind.SelectedItem is CrossoverKind kind
            ? kind
            : CrossoverKind.Off;

    public CrossoverEdge HighPassEdge => ReadEdge(
        numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope, numericHighPassRipple);

    public CrossoverEdge LowPassEdge => ReadEdge(
        numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope, numericLowPassRipple);

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
