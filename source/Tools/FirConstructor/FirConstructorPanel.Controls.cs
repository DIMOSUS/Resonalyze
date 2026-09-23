using Resonalyze.Dsp;

namespace Resonalyze;

public partial class FirConstructorPanel
{
    private void InitializeChoices()
    {
        suppressEdits = true;
        try
        {
            Fill(comboBoxType, FirConstructorChoices.Kinds);
            comboBoxType.SelectedIndex = 0;
            Fill(comboBoxMethod, FirConstructorChoices.Methods);
            comboBoxMethod.SelectedIndex = 0;
            foreach (ThemedComboBox family in new[] { comboBoxHighPassFamily, comboBoxLowPassFamily })
            {
                Fill(family, FirConstructorChoices.Families);
                family.SelectedIndex = 0;
            }

            FillSlopes(comboBoxHighPassSlope, CrossoverFilterFamily.LinkwitzRiley, FirConstructorChoices.DefaultSlope);
            FillSlopes(comboBoxLowPassSlope, CrossoverFilterFamily.LinkwitzRiley, FirConstructorChoices.DefaultSlope);
            Fill(comboBoxWindow, FirConstructorChoices.Windows);
            SelectChoice(comboBoxWindow, FirWindow.Kaiser);
            Fill(comboBoxSampleRate, FirConstructorChoices.SampleRates.Select(rate => (rate, FirConstructorChoices.RateLabel(rate))));
            SelectChoice(comboBoxSampleRate, FirConstructorChoices.DefaultRateHz);
        }
        finally
        {
            suppressEdits = false;
        }
    }

    private FirCrossoverDesign ReadControls() =>
        new(
            Selected(comboBoxType, CrossoverKind.LowPass),
            new CrossoverEdge(
                Selected(comboBoxLowPassFamily, CrossoverFilterFamily.LinkwitzRiley),
                (double)numericLowPassHz.Value,
                Selected(comboBoxLowPassSlope, FirConstructorChoices.DefaultSlope)),
            new CrossoverEdge(
                Selected(comboBoxHighPassFamily, CrossoverFilterFamily.LinkwitzRiley),
                (double)numericHighPassHz.Value,
                Selected(comboBoxHighPassSlope, FirConstructorChoices.DefaultSlope)),
            Selected(comboBoxMethod, FirCrossoverMethod.IirMagnitude),
            Selected(comboBoxWindow, FirWindow.Kaiser),
            (double)numericKaiserBeta.Value,
            (int)numericTaps.Value,
            Selected(comboBoxSampleRate, FirConstructorChoices.DefaultRateHz));

    private void WriteControls(FirCrossoverDesign source)
    {
        SelectChoice(comboBoxType, source.Kind);
        SelectChoice(comboBoxMethod, source.Method);
        WriteEdge(source.HighPassEdge, numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope);
        WriteEdge(source.LowPassEdge, numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope);
        SelectChoice(comboBoxWindow, source.Window);
        numericKaiserBeta.Value = numericKaiserBeta.ClampValue(source.KaiserBeta);
        numericTaps.Value = numericTaps.ClampValue(source.TapCount);
    }

    private void WriteSeed(CrossoverSpec seed)
    {
        if (seed.Kind is CrossoverKind.Off)
        {
            return;
        }

        SelectChoice(comboBoxType, seed.Kind);
        if (seed.HighPassEdge is { } highPass)
        {
            WriteEdge(highPass, numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope);
        }
        if (seed.LowPassEdge is { } lowPass)
        {
            WriteEdge(lowPass, numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope);
        }
    }

    private static void WriteEdge(
        CrossoverEdge edge, ThemedNumericUpDown frequency, ThemedComboBox family, ThemedComboBox slope)
    {
        frequency.Value = frequency.ClampValue(edge.FrequencyHz);
        CrossoverFilterFamily offered = FirConstructorChoices.OfferedFamily(edge.Family);
        SelectChoice(family, offered);
        FillSlopes(slope, offered, edge.SlopeDbPerOctave);
    }

    private void SelectRate(int rate)
    {
        if (!comboBoxSampleRate.Items.OfType<Choice<int>>().Any(choice => choice.Value == rate))
        {
            comboBoxSampleRate.Items.Add(new Choice<int>(rate, FirConstructorChoices.RateLabel(rate)));
        }

        SelectChoice(comboBoxSampleRate, rate);
    }

    private static void FillSlopes(ThemedComboBox slope, CrossoverFilterFamily family, int preferred)
    {
        slope.Items.Clear();
        Fill(slope, FirConstructorChoices.Slopes(family));
        SelectChoice(slope, FirConstructorChoices.NearestSlope(family, preferred));
    }

    private static void Fill<T>(ThemedComboBox combo, IEnumerable<(T Value, string Label)> choices)
    {
        foreach ((T value, string label) in choices)
        {
            combo.Items.Add(new Choice<T>(value, label));
        }
    }

    private void UpdateControlAvailability()
    {
        FirConstructorFields fields = FirConstructorAvailability.Fields(ReadControls());
        SetEnabled(fields.HighPass, labelHighPass, numericHighPassHz);
        SetEnabled(fields.HighPassShape, comboBoxHighPassFamily, comboBoxHighPassSlope);
        SetEnabled(fields.LowPass, labelLowPass, numericLowPassHz);
        SetEnabled(fields.LowPassShape, comboBoxLowPassFamily, comboBoxLowPassSlope);
        SetEnabled(fields.KaiserBeta, labelKaiserBeta, numericKaiserBeta);
    }

    private static void SetEnabled(bool enabled, params Control[] controls)
    {
        foreach (Control control in controls)
        {
            if (control is Label label)
            {
                Ui.UiStyle.SetTextEnabledLook(label, enabled);
            }
            else
            {
                control.Enabled = enabled;
            }
        }
    }

    private void UpdateSessionControls()
    {
        bool linked = session.InHandoff;
        buttonReturnToDsp.Visible = linked;
        buttonBackToDsp.Visible = linked;
        comboBoxSampleRate.Enabled = !linked;
        labelSession.Text = FirConstructorReadout.Session(session);
    }

    private void UpdateActions()
    {
        buttonExport.Enabled = FirConstructorAvailability.CanExport(session);
        buttonReturnToDsp.Enabled = FirConstructorAvailability.Return(session) != null;
    }

    private static T Selected<T>(ThemedComboBox combo, T fallback) =>
        combo.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    private static void SelectChoice<T>(ThemedComboBox combo, T value)
    {
        foreach (object? item in combo.Items)
        {
            if (item is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }
}
