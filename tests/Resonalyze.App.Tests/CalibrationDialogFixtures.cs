using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>Runs beside no other collection: mouse messages and a label edit need the window input state, which a
/// dialog shown by a test running alongside can take.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowInput
{
    internal const string Name = "Window input";
}

/// <summary>The calibration dialogs shown off-screen and driven as a user would: controls found by name, clicks and
/// rows through window messages, a nested modal answered from a timer in its own loop.</summary>
internal static class CalibrationDialogFixtures
{
    private const int MouseDown = 0x0201;
    private const int MouseUp = 0x0202;
    private const int MouseDoubleClick = 0x0203;

    /// <summary>On a UI thread, with texts read with a decimal point whatever the machine's locale.</summary>
    public static void Run(Action body) => StaTest.Run(() =>
    {
        using var culture = new InvariantCultureScope();
        body();
    });

    public static TForm Shown<TForm>(TForm form) where TForm : Form
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-6000, -6000);
        form.ShowInTaskbar = false;
        form.Show();
        StaTest.Pump();
        return form;
    }

    public static T In<T>(Control root, string name) where T : Control =>
        (T)root.Controls.Find(name, searchAllChildren: true).Single();

    public static void Click(Form dialog, string name)
    {
        In<Button>(dialog, name).PerformClick();
        StaTest.Pump();
    }

    public static void SelectRow(ListView list, int? index)
    {
        list.SelectedIndices.Clear();
        if (index is int row)
        {
            list.Items[row].Selected = true;
        }

        StaTest.Pump();
    }

    public static void ClickCell(DataGridView grid, int row, int column, bool twice = false)
    {
        Rectangle cell = grid.GetCellDisplayRectangle(column, row, cutOverflow: false);
        var point = (IntPtr)(((cell.Top + cell.Height / 2) << 16) | ((cell.Left + cell.Width / 2) & 0xFFFF));
        SendMessage(grid.Handle, MouseDown, (IntPtr)1, point);
        SendMessage(grid.Handle, MouseUp, IntPtr.Zero, point);
        if (twice)
        {
            SendMessage(grid.Handle, MouseDoubleClick, (IntPtr)1, point);
            SendMessage(grid.Handle, MouseUp, IntPtr.Zero, point);
        }

        StaTest.Pump();
    }

    /// <summary>Runs <paramref name="open"/>, which shows <typeparamref name="TForm"/> modally, and answers it once.</summary>
    public static void Answer<TForm>(Action open, Action<TForm> answer) where TForm : Form
    {
        Exception? failure = null;
        bool seen = false;
        using var pilot = new System.Windows.Forms.Timer { Interval = 20 };
        pilot.Tick += (_, _) =>
        {
            if (seen || Application.OpenForms.OfType<TForm>().FirstOrDefault(form => form.Visible && form.Modal) is not { } dialog)
            {
                return;
            }

            seen = true;
            try
            {
                answer(dialog);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (dialog.Visible && dialog.DialogResult == DialogResult.None)
            {
                dialog.DialogResult = DialogResult.Cancel;
            }
        };
        pilot.Start();
        open();
        pilot.Stop();
        if (failure != null)
        {
            throw new InvalidOperationException($"Answering {typeof(TForm).Name} failed.", failure);
        }

        Assert.True(seen, $"{typeof(TForm).Name} never opened.");
    }

    public static void Wait(Func<bool> condition, int timeoutMilliseconds = 30_000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            StaTest.Pump();
            Thread.Sleep(5);
        }

        Assert.True(condition(), "The dialog never reached the expected state.");
    }

    public static AudioSessionRequest SplRequest(AudioBackend backend = AudioBackend.Wave) =>
        new(backend, 48_000, 24, PlaybackChannel.Mono, new AudioCaptureRouting(0, null))
        {
            WaveInputDeviceNumber = 1,
            WasapiCaptureEndpointId = backend == AudioBackend.Wave ? null : "{capture}",
            AsioDriverName = backend == AudioBackend.Asio ? "Driver" : null
        };

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}

/// <summary>A calibrator heard by the listener: one tone per frame at the given amplitudes, then silence until the
/// listen ends; frames stay within the listener's queue so none is dropped.</summary>
internal sealed class ToneStream(double frequencyHz, params double[] amplitudes) : IAudioStreamingSession
{
    public event Action<AudioCaptureFrame>? FrameAvailable;
    public event Action<AudioInputLevels>? InputLevelsAvailable { add { } remove { } }
    public event Action? CaptureDiscontinuity { add { } remove { } }

    public TaskCompletionSource Emitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TimeSpan FrameGap { get; init; }

    public async Task RunAsync(AudioPlaybackSignal loopingSignal, int sequenceLength, CancellationToken cancellationToken)
    {
        double phase = 0.0;
        double delta = 2.0 * Math.PI * frequencyHz / loopingSignal.SampleRate;
        foreach (double amplitude in amplitudes)
        {
            var block = new float[sequenceLength];
            for (int i = 0; i < block.Length; i++)
            {
                block[i] = (float)(amplitude * Math.Sin(phase));
                phase += delta;
            }

            FrameAvailable?.Invoke(new AudioCaptureFrame([block], 0, null));
            if (FrameGap > TimeSpan.Zero)
            {
                await Task.Delay(FrameGap, cancellationToken);
            }
            else
            {
                await Task.Yield();
            }
        }

        Emitted.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Texts read with a decimal point whatever the machine's locale, restored afterwards.</summary>
internal sealed class InvariantCultureScope : IDisposable
{
    private readonly CultureInfo previous = CultureInfo.CurrentCulture;

    public InvariantCultureScope() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentCulture = previous;
}
