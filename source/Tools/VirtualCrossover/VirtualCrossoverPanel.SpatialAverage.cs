using Resonalyze.Dsp;

namespace Resonalyze;

// Attaching a spatially averaged magnitude to a channel, and deciding whether the
// hybrid view can be shown at all.
//
// The average is an optional REFINEMENT of what the magnitude view draws, never the
// basis of a computation: delays, polarity, junctions and the summation loss keep
// reading the honest impulse responses. What it changes is that the curve stops
// carrying the dips of one microphone position, which are the dips a tune must not
// chase.
public partial class VirtualCrossoverPanel
{
    private void ShowSpatialAverageMenu(VirtualCrossoverChannel channel)
    {
        var menu = new ContextMenuStrip();

        ToolStripMenuItem chooseItem = new("Attach capture...");
        chooseItem.Click += (_, _) => ChooseSpatialAverage(channel);
        menu.Items.Add(chooseItem);

        if (channel.SpatialAverage != null)
        {
            ToolStripMenuItem detachItem = new("Detach");
            detachItem.Click += (_, _) =>
            {
                channel.SpatialAverage = null;
                OnSpatialAverageChanged(channel);
            };
            menu.Items.Add(detachItem);
        }

        menu.Show(Cursor.Position);
    }

    private void ChooseSpatialAverage(VirtualCrossoverChannel channel)
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Resonalyze moving-mic capture (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            RestoreDirectory = true,
            Title = $"Attach a spatial average to {channel.Name}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (!LiveCaptureDocument.TryLoad(dialog.FileName, out LiveCaptureDocument document))
            {
                MessageBox.Show(
                    this,
                    "That file is not a Resonalyze capture.",
                    "Attach spatial average",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            channel.SpatialAverage = document;
            OnSpatialAverageChanged(channel);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "The capture could not be loaded." +
                    Environment.NewLine + Environment.NewLine + exception.Message,
                "Attach spatial average",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void OnSpatialAverageChanged(VirtualCrossoverChannel channel)
    {
        RefreshSpatialAverageStatus(channel);
        RefreshHybridAvailability();
        OnViewChanged();
    }

    private void RefreshSpatialAverageStatus(VirtualCrossoverChannel channel)
    {
        if (!channelControls.TryGetValue(channel, out VirtualCrossoverChannelControl? control))
        {
            return;
        }

        LiveCaptureDocument? document = channel.SpatialAverage;
        control.SetSpatialAverage(
            document?.Title,
            document?.Recipe.IntegratedSeconds);
    }

    /// <summary>
    /// Whether every channel that plays has a spatial average, and therefore whether
    /// the hybrid view is offered at all.
    /// </summary>
    /// <remarks>
    /// All or nothing, deliberately. A sum mixing spatially averaged channels with
    /// point-measured ones puts two different references on one axis and looks
    /// exactly like a measurement, so a partial set disables the toggle rather than
    /// drawing something that cannot be read.
    /// </remarks>
    private bool HasSpatialAverageForEveryChannel
    {
        get
        {
            // The channels that actually play: an enabled pair with a measurement
            // behind it. A disabled or empty one contributes nothing to the sum and
            // so cannot hold the hybrid view back.
            List<VirtualCrossoverChannel> playing = channels
                .Where(channel =>
                    channel.Pair.Enabled && channel.TransferImpulseResponse != null)
                .ToList();
            return playing.Count > 0 && playing.All(channel => channel.SpatialAverage != null);
        }
    }

    private void RefreshHybridAvailability()
    {
        bool available = HasSpatialAverageForEveryChannel;
        if (!available && checkBoxHybrid.Checked)
        {
            // Losing coverage must not leave a hybrid curve on screen with nothing
            // behind it for one of the channels.
            checkBoxHybrid.Checked = false;
        }

        checkBoxHybrid.Enabled = available;
        UiStyle.SetTextEnabledLook(checkBoxHybrid, available, interactive: true);
        toolTip.SetToolTip(
            checkBoxHybrid,
            available
                ? "Draw each channel's magnitude from its spatial average with this " +
                    "channel's DSP chain on top, instead of from the impulse response " +
                    "measured at one point. Timing, polarity and the summation loss are " +
                    "unaffected: they keep reading the impulse responses."
                : "Needs a spatial average on every channel that plays. Attach one " +
                    "per channel with the MMM button.");
    }

    /// <summary>
    /// One channel's magnitude drawn from its spatial average instead of from the
    /// impulse response measured at one point: the stored curve, lifted by the set's
    /// common offset, with this channel's own DSP chain on top. Null when the channel
    /// has no average attached.
    /// </summary>
    /// <remarks>
    /// The chain is added as its ANALYTIC magnitude, not as the difference between
    /// two gated spectra. A spatial average is a steady-state curve with no window,
    /// and a gate does not commute with a filter — the two readings part by several
    /// dB wherever the bank rings longer than the window.
    /// <para>
    /// This is exact rather than a convenience. A spatial average is
    /// √⟨|H(f,r)|²⟩ over the listening volume, and a filter D(f) does not depend on
    /// position, so ⟨|D·H|²⟩ = |D|²·⟨|H|²⟩ — the filter comes straight out of the
    /// average. Delay and polarity are absent from it for the same reason: they are
    /// pure phase, so a hybrid channel curve is the tonal balance alone.
    /// </para>
    /// </remarks>
    private IReadOnlyList<SignalPoint>? BuildHybridChannelCurve(
        VirtualCrossoverChannel channel,
        IReadOnlyList<SignalPoint> reference,
        double offsetDb)
    {
        if (channel.SpatialAverage is not { } document || reference.Count == 0)
        {
            return null;
        }

        int rate = document.Recipe.SampleRateHz > 0
            ? document.Recipe.SampleRateHz
            : channel.SampleRate;
        if (rate <= 0)
        {
            return null;
        }

        var prepared = PreparedDspResponse.Create(channel.Settings.ToChain(), rate);
        var points = new List<SignalPoint>(reference.Count);
        foreach (SignalPoint point in reference)
        {
            double average = SampleSpatialAverageDb(document, point.X);
            if (double.IsNaN(average))
            {
                // The capture says it has nothing here — below a protective
                // high-pass, typically. A break is the honest answer; inventing a
                // level would put a curve where no measurement exists.
                points.Add(new SignalPoint(point.X, double.NaN));
                continue;
            }

            double chainDb = DataHelper.AmplitudeToDecibels(
                prepared.Response(point.X).Magnitude);
            points.Add(new SignalPoint(point.X, average + offsetDb + chainDb));
        }

        return points;
    }

    // The stored curve at one frequency, interpolated on its own logarithmic grid.
    // Linear in dB between neighbours, and NaN as soon as either neighbour is NaN:
    // a gap must not be bridged by the points around it.
    private static double SampleSpatialAverageDb(LiveCaptureDocument document, double hz)
    {
        double[] curve = document.CurveDb;
        int count = curve.Length;
        if (count < 2 || !(hz > 0) ||
            !(document.GridStartHz > 0) || !(document.GridStopHz > document.GridStartHz))
        {
            return double.NaN;
        }

        double position = Math.Log(hz / document.GridStartHz) /
            Math.Log(document.GridStopHz / document.GridStartHz) * (count - 1);
        if (position < 0 || position > count - 1)
        {
            return double.NaN;
        }

        int low = (int)Math.Floor(position);
        int high = Math.Min(low + 1, count - 1);
        double fraction = position - low;
        return curve[low] + (curve[high] - curve[low]) * fraction;
    }

    /// <summary>
    /// The one offset that puts the whole spatial-average set on the same axis as the
    /// impulse responses, in dB.
    /// </summary>
    /// <remarks>
    /// ONE offset for the set, never one per channel. The captures were taken in a
    /// single analyzer session at a fixed gain, so their relative levels are honest
    /// measurements — arguably better than the point responses' — and normalizing
    /// each channel separately would throw exactly that away.
    /// <para>
    /// Taken as the median across channels of the median difference within each
    /// channel's drawn range. A median rather than a mean because the two curves
    /// legitimately differ in SHAPE — that difference is the whole point of the
    /// feature — and a few large deviations must not drag the alignment.
    /// </para>
    /// </remarks>
    private double ResolveHybridOffsetDb(
        List<ProcessedChannel> processed,
        List<AnalysisCurve> references)
    {
        var perChannel = new List<double>();
        for (int i = 0; i < processed.Count && i < references.Count; i++)
        {
            VirtualCrossoverChannel channel = processed[i].Channel;
            IReadOnlyList<SignalPoint> reference = references[i].Points;
            if (BuildHybridChannelCurve(channel, reference, 0.0) is not { } hybrid)
            {
                continue;
            }

            var differences = new List<double>();
            for (int k = 0; k < hybrid.Count && k < reference.Count; k++)
            {
                double difference = reference[k].Y - hybrid[k].Y;
                if (double.IsFinite(difference))
                {
                    differences.Add(difference);
                }
            }

            if (differences.Count > 0)
            {
                differences.Sort();
                perChannel.Add(differences[differences.Count / 2]);
            }
        }

        if (perChannel.Count == 0)
        {
            return 0.0;
        }

        perChannel.Sort();
        return perChannel[perChannel.Count / 2];
    }
}
