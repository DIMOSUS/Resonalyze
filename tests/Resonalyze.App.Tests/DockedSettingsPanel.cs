using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>A mode settings panel opened in a real <see cref="DockedModeSettingsHost"/> with live apply, driven through
/// its controls as a user drives them; <see cref="TakeApplies"/> counts what the host applied.</summary>
internal sealed class DockedSettingsPanel<TPanel> : IDisposable
    where TPanel : Form
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly Form owner;
    private readonly DockedModeSettingsHost host;
    private int applies;

    public DockedSettingsPanel(Func<TPanel> create, Action<TPanel> init, Action<TPanel>? apply = null)
    {
        owner = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-10_000, -10_000),
            Size = new Size(800, 600),
            ShowInTaskbar = false
        };
        owner.Show();
        host = new DockedModeSettingsHost(owner, owner);
        host.Toggle(
            "panel",
            create,
            init,
            panel =>
            {
                applies++;
                apply?.Invoke(panel);
                return Task.CompletedTask;
            },
            applyOnChange: true);
        TPanel? shown = null;
        host.InvokeIfOpen<TPanel>(panel => shown = panel);
        Panel = shown ?? throw new InvalidOperationException("The panel did not open.");
        Settle();
    }

    public TPanel Panel { get; }

    /// <summary>The applies the host ran since the last call, once the queued ones have run.</summary>
    public int TakeApplies()
    {
        Settle();
        int count = applies;
        applies = 0;
        return count;
    }

    public T Find<T>(string name) where T : Control =>
        (T)Panel.Controls.Find(name, searchAllChildren: true).Single();

    public decimal Value(string name) => Find<ThemedNumericUpDown>(name).Value;

    public string Selected(string name)
    {
        ThemedComboBox combo = Find<ThemedComboBox>(name);
        return combo.GetItemText(combo.SelectedItem) ?? string.Empty;
    }

    /// <summary>A value entered in the field: it rounds and clamps it as it does typed text.</summary>
    public void Type(string name, decimal value) => Find<ThemedNumericUpDown>(name).Value = value;

    /// <summary>A mouse click: a box that takes clicks toggles, then the click is raised.</summary>
    public void Click(string name)
    {
        Control control = Find<Control>(name);
        if (control is Button button)
        {
            button.PerformClick();
            return;
        }

        typeof(Control).GetMethod("OnClick", Hidden)!.Invoke(control, [EventArgs.Empty]);
    }

    /// <summary>A pick in the drop-down: the list moves, then the pick is committed.</summary>
    public void Pick(string name, string label)
    {
        ThemedComboBox combo = Find<ThemedComboBox>(name);
        int index = combo.Items.Cast<object>().Select(combo.GetItemText).ToList().IndexOf(label);
        Assert.True(index >= 0, $"no {label} in {name}");
        typeof(ThemedComboBox).GetMethod("CommitPopupSelection", Hidden)!.Invoke(combo, [index]);
    }

    /// <summary>An arrow key in the closed list: it moves without a commit.</summary>
    public void Arrow(string name, int delta) =>
        typeof(ThemedComboBox).GetMethod("MoveSelection", Hidden)!.Invoke(Find<ThemedComboBox>(name), [delta, false]);

    public IReadOnlyList<string> Items(string name)
    {
        ThemedComboBox combo = Find<ThemedComboBox>(name);
        return combo.Items.Cast<object>().Select(item => combo.GetItemText(item) ?? string.Empty).ToList();
    }

    public void Settle()
    {
        for (int pass = 0; pass < 5; pass++)
        {
            StaTest.Pump();
        }
    }

    public void Dispose()
    {
        host.Dispose();
        owner.Dispose();
    }
}
