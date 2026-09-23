namespace Resonalyze;

internal sealed partial class VirtualCrossoverJunctionTuneDialog
{
    private void Tips()
    {
        toolTip.SetToolTip(
            comboBoxJunction,
            "Which junction to refine: the lower block's low-pass and the" + "\r\n" +
            "upper block's high-pass. Everything else in both chains stays.");
        numericMinHz.ApplyToolTip(
            toolTip,
            "Corner frequencies the search may put the handover at." + "\r\n" +
            "Wider costs candidates; half an octave each way is the default.");
        numericMaxHz.ApplyToolTip(toolTip, "See the lower bound.");
        foreach (ThemedComboBox window in new[] { comboBoxMinSlope, comboBoxMaxSlope })
        {
            toolTip.SetToolTip(
                window,
                "Electrical slopes the search may use when it is tuning for the\r\n" +
                "best summation: narrow it to hold the junction near a steepness\r\n" +
                "you want. An acoustic goal states the answer instead, so the\r\n" +
                "window is left out of that mode altogether.");
        }

        toolTip.SetToolTip(
            checkBoxIndependentSlopes,
            "Let the two sides take different slopes. On by default: the" + "\r\n" +
            "drivers' own falls rarely match, and the search then costs" + "\r\n" +
            "slopes squared per corner.");
        foreach (CheckBox family in FamilyBoxes)
        {
            toolTip.SetToolTip(
                family,
                "Filter families the search may use. Only what your processor" + "\r\n" +
                "can run belongs here; the tune writes one of these into both" + "\r\n" +
                "blocks.");
        }
        toolTip.SetToolTip(
            comboBoxGoalFamily,
            "The ACOUSTIC crossover this junction should add up to, driver" + "\r\n" +
            "and filter together. The search prefers the filter that lands" + "\r\n" +
            "on it among candidates within the budget of the best sum.");
        numericSumBudget.ApplyToolTip(
            toolTip,
            "How much summation score the goal may cost against the best" + "\r\n" +
            "candidate, every one read after re-aligning. 0.2 keeps the sum;" + "\r\n" +
            "1.0 lets a slope that lands on the goal in at a moderate price.");
        toolTip.SetToolTip(
            buttonRun,
            "Read every allowed filter on this junction and report what each" + "\r\n" +
            "would sum to. Nothing is written until Apply.");
        toolTip.SetToolTip(
            buttonApply,
            "Write the found crossover into both sides of both blocks, and" + "\r\n" +
            "a stated goal onto the cards. Undo last Apply puts them back.");
    }
}
