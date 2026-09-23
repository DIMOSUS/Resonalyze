namespace Resonalyze.Options;

/// <summary>Base of the mode settings panels: follows the open measurement on the UI thread, and writes every control
/// from its session in <see cref="Present"/> with the controls' own change events ignored meanwhile.</summary>
public class ModeSettingsForm : Form
{
    private protected readonly WrappingToolTip toolTip = new();
    private AnalyzerDocument? document;
    private int configuredSampleRate;
    private bool presenting;

    public ModeSettingsForm()
    {
        Disposed += (_, _) =>
        {
            Unfollow();
            toolTip.Dispose();
        };
    }

    /// <summary>Every tooltip the panel shows, including the ones it rewrites as the session changes.</summary>
    internal WrappingToolTip ToolTips => toolTip;

    private protected AnalyzerDocument? Document => document;

    private protected ModeSettingsMeasurement OpenMeasurement => new(document?.Result, configuredSampleRate);

    private protected void Follow(AnalyzerDocument analyzerDocument, int sampleRateWhenNothingIsOpen)
    {
        ArgumentNullException.ThrowIfNull(analyzerDocument);
        configuredSampleRate = sampleRateWhenNothingIsOpen;
        if (ReferenceEquals(document, analyzerDocument))
        {
            return;
        }

        Unfollow();
        document = analyzerDocument;
        analyzerDocument.Changed += HandleDocumentChanged;
    }

    /// <summary>The open measurement changed.</summary>
    private protected virtual void OnMeasurementChanged()
    {
    }

    private protected void Present()
    {
        presenting = true;
        try
        {
            PresentControls();
        }
        finally
        {
            presenting = false;
        }

        OnPresented();
    }

    private protected virtual void PresentControls()
    {
    }

    /// <summary>After the controls show the session, outside the presenting guard.</summary>
    private protected virtual void OnPresented()
    {
    }

    private protected void Edit(Action edit)
    {
        if (presenting)
        {
            return;
        }

        edit();
        Present();
    }

    private protected void Bind(ThemedNumericUpDown field, Action<decimal> edit) =>
        field.ValueChanged += (_, _) => Edit(() => edit(field.Value));

    private protected void Bind(CheckBox box, Action<bool> edit) =>
        box.CheckedChanged += (_, _) => Edit(() => edit(box.Checked));

    private protected void Bind(RadioButton radio, Action<bool> edit) =>
        radio.CheckedChanged += (_, _) => Edit(() => edit(radio.Checked));

    /// <summary>A moved list (a pick, an arrow key, a reset) reaches the session at once.</summary>
    private protected void BindIndex(ThemedComboBox combo, Action<int> select) =>
        combo.SelectedIndexChanged += (_, _) => Edit(() => select(combo.SelectedIndex));

    private protected void BindItem<T>(ThemedComboBox combo, Action<T> select) =>
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedItem is T item)
            {
                Edit(() => select(item));
            }
        };

    /// <summary>Only a moved value: the setter rewrites the editor, which would discard text being typed.</summary>
    private protected static void Show(ThemedNumericUpDown field, decimal value)
    {
        if (field.Value != value)
        {
            field.Value = value;
        }
    }

    /// <summary>A field whose upper bound moves with the session; the value never passes a bound it is written under.</summary>
    private protected static void Show(ThemedNumericUpDown field, decimal value, decimal maximum)
    {
        if (field.Maximum < value)
        {
            field.Maximum = value;
        }

        Show(field, value);
        if (field.Maximum != maximum)
        {
            field.Maximum = maximum;
        }
    }

    private protected static void ShowIndex(ThemedComboBox combo, int index)
    {
        if (combo.SelectedIndex != index)
        {
            combo.SelectedIndex = index;
        }
    }

    /// <summary>An item the list does not hold leaves the selection where it is.</summary>
    private protected static void ShowItem(ThemedComboBox combo, object item)
    {
        int index = combo.Items.IndexOf(item);
        if (index >= 0)
        {
            ShowIndex(combo, index);
        }
    }

    private void Unfollow()
    {
        if (document != null)
        {
            document.Changed -= HandleDocumentChanged;
            document = null;
        }
    }

    private void HandleDocumentChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke((MethodInvoker)OnMeasurementChanged);
            return;
        }

        OnMeasurementChanged();
    }
}
