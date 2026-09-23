using System.Numerics;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// A shown Virtual DSP panel on three measured blocks (a low-pass, a band-pass and a high-pass, LR24 at 300 Hz and
/// 3 kHz), driven through its controls: menus are taken through <see cref="VirtualCrossoverPanel.ShowMenu"/>, messages
/// answered through <see cref="VirtualCrossoverPanel.ShowMessage"/>, and a modal dialog by a timer in its loop.
/// </summary>
internal sealed class VirtualCrossoverLivePanel : IDisposable
{
    private const int SampleRate = 48_000;
    private const int PeakIndex = 480;

    private readonly Form host = new()
    {
        Width = 1_400,
        Height = 900,
        StartPosition = FormStartPosition.Manual,
        Location = new System.Drawing.Point(-5_000, -5_000)
    };

    private string metric = string.Empty;

    public VirtualCrossoverLivePanel(double rightAmplitude = 1.0)
    {
        Panel = new VirtualCrossoverPanel
        {
            Dock = DockStyle.Fill,
            MetricChanged = (compact, _) => metric = compact,
            WarningChanged = (text, _, _) => Warning = text,
            ShowMenu = (_, menu) => Menu = menu,
            ShowMessage = (text, caption, buttons, _) =>
            {
                Messages.Add(caption + ": " + text);
                return buttons == MessageBoxButtons.OK
                    ? DialogResult.OK
                    : Answers.Count > 0 ? Answers.Dequeue() : DialogResult.No;
            }
        };
        host.Controls.Add(Panel);
        List<VirtualCrossoverChannel> channels = Session.Channels;
        for (int index = 0; index < channels.Count; index++)
        {
            channels[index].Pair = Session.Project.Pairs[index];
            foreach (bool rightSide in new[] { false, true })
            {
                VirtualCrossoverChannelState state = channels[index].PhysicalSideState(rightSide);
                var impulse = new Complex[16_384];
                impulse[PeakIndex] = rightSide ? rightAmplitude : 1.0;
                state.TransferImpulseResponse = impulse;
                state.TransferPeakIndex = PeakIndex;
                state.SampleRate = SampleRate;
                VirtualCrossoverChannelSettings settings = channels[index].SideSettings(rightSide);
                settings.CrossoverKind = index switch
                {
                    0 => CrossoverKind.LowPass,
                    1 => CrossoverKind.BandPass,
                    _ => CrossoverKind.HighPass
                };
                settings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, index == 1 ? 300 : 3_000, 24);
                settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, index == 0 ? 300 : 3_000, 24);
            }
        }

        host.Show();
        // A side switch writes every block's settings into its card, as a project load does.
        Find<RadioButton>("radioSideRight").Checked = true;
        Find<RadioButton>("radioSideLeft").Checked = true;
        // Any view toggle redraws, and the metric names the junctions once the processed blocks are in.
        CheckBox sum = Find<CheckBox>("checkBoxShowSum");
        sum.Checked = !sum.Checked;
        sum.Checked = !sum.Checked;
        Wait(() => metric.Contains("A/B", StringComparison.Ordinal) && metric.Contains("B/C", StringComparison.Ordinal),
            "quote its junctions");
    }

    public VirtualCrossoverPanel Panel { get; }

    public VirtualCrossoverSession Session => Panel.Session;

    public string Metric => metric;

    public string Warning { get; private set; } = string.Empty;

    /// <summary>The last menu the panel opened.</summary>
    public ContextMenuStrip? Menu { get; private set; }

    public List<string> Messages { get; } = [];

    public Queue<DialogResult> Answers { get; } = new();

    public T Find<T>(string name) where T : Control =>
        (T)Panel.Controls.Find(name, searchAllChildren: true).Single();

    public VirtualCrossoverChannelControl Card(VirtualCrossoverChannel channel) =>
        Find<FlowLayoutPanel>("channelListPanel").Controls.OfType<VirtualCrossoverChannelControl>()
            .Single(card => card.ChannelName == channel.Name);

    /// <summary>Clicks a panel button once the panel is idle, and waits for it to be idle again.</summary>
    public void Click(string button)
    {
        Settle();
        Find<Button>(button).PerformClick();
        Settle();
    }

    public void ClickMenu(string text)
    {
        ToolStripMenuItem item = Items(Menu!.Items).First(item => item.Text!.StartsWith(text, StringComparison.Ordinal));
        item.PerformClick();
        Settle();
    }

    public bool MenuItemEnabled(string text) =>
        Items(Menu!.Items).First(item => item.Text!.StartsWith(text, StringComparison.Ordinal)).Enabled;

    /// <summary>Opens a modal dialog with <paramref name="open"/> and answers it with <paramref name="answer"/>, run by a timer
    /// in the dialog's own loop; <paramref name="answer"/> is called again while it returns false (a search running).</summary>
    public void Answer<TForm>(Action open, Func<TForm, bool> answer) where TForm : Form
    {
        Exception? failure = null;
        bool seen = false;
        DateTime deadline = DateTime.UtcNow.AddSeconds(120);
        using var pilot = new System.Windows.Forms.Timer { Interval = 20 };
        pilot.Tick += (_, _) =>
        {
            if (Application.OpenForms.OfType<TForm>().FirstOrDefault(form => form.Visible && form.Modal) is not { } dialog)
            {
                return;
            }

            seen = true;
            try
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"{typeof(TForm).Name} was never answered.");
                }

                if (answer(dialog) && dialog.Visible && dialog.DialogResult == DialogResult.None)
                {
                    dialog.DialogResult = DialogResult.Cancel;
                    dialog.Close();
                }
            }
            catch (Exception exception)
            {
                failure ??= exception;
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            }
        };
        pilot.Start();
        Settle();
        open();
        pilot.Stop();
        if (failure != null)
        {
            throw new InvalidOperationException($"Answering {typeof(TForm).Name} failed.", failure);
        }

        Assert.True(seen, $"{typeof(TForm).Name} never opened.");
        Settle();
    }

    public static T In<T>(Form dialog, string name) where T : Control =>
        (T)dialog.Controls.Find(name, searchAllChildren: true).Single();

    /// <summary>The panel is idle when its automatic commands are offered again: a redraw withholds them.</summary>
    public void Settle() => Wait(() => Find<Button>("buttonAutoDelay").Enabled && Find<Button>("buttonAi").Enabled);

    public void Dispose()
    {
        host.Close();
        host.Dispose();
    }

    // Generous: the whole suite runs in parallel, and a redraw of three blocks waits on its own tasks.
    public void Wait(Func<bool> condition, string what = "settle")
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(120);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            StaTest.Pump();
            Thread.Sleep(5);
        }

        Assert.True(condition(), $"The panel did not {what}; the metric reads '{metric}'.");
        for (int i = 0; i < 10; i++)
        {
            StaTest.Pump();
            Thread.Sleep(5);
        }
    }

    private static IEnumerable<ToolStripMenuItem> Items(ToolStripItemCollection items)
    {
        foreach (ToolStripMenuItem item in items.OfType<ToolStripMenuItem>())
        {
            yield return item;
            foreach (ToolStripMenuItem nested in Items(item.DropDownItems))
            {
                yield return nested;
            }
        }
    }
}
