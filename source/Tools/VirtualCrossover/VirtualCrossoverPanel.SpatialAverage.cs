using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One redraw's hybrid magnitudes: every processed channel's spatially averaged
/// curve with that channel's own DSP chain on top, plus the single offset that
/// puts the whole set on the impulse responses' axis.
/// </summary>
/// <remarks>
/// The channel curves are held WITHOUT the offset. It is one scalar for the set,
/// so adding it at the end keeps the drawing and the summation reading the very
/// same arrays — and the sum shifts by exactly that scalar too, since a common
/// gain factors straight out of a magnitude sum.
/// </remarks>
internal sealed record HybridMagnitudes(
    IReadOnlyList<IReadOnlyList<SignalPoint>> Channels,
    double OffsetDb);

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

        // Offered for a capture the session refers to but could not read, too: that
        // is the state a user most wants to be rid of.
        if (channel.SpatialAverage != null ||
            !string.IsNullOrWhiteSpace(channel.Settings.SpatialAveragePath))
        {
            ToolStripMenuItem detachItem = new("Detach");
            detachItem.Click += (_, _) =>
            {
                channel.SpatialAverage = null;
                channel.Settings.SpatialAveragePath = null;
                channel.Settings.SpatialAverageRelativePath = null;
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
            channel.Settings.SpatialAveragePath = dialog.FileName;
            // The relative path names the capture the session was IMPORTED with, and
            // this is a different file: left standing it would send the next search
            // after the one just replaced.
            channel.Settings.SpatialAverageRelativePath = null;
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
        // The attachment is session state, so it travels with the session — the whole
        // point of storing the reference rather than re-picking it every time.
        ScheduleSave();
        OnViewChanged();
    }

    private void RefreshSpatialAverageStatus(VirtualCrossoverChannel channel)
    {
        if (!channelControls.TryGetValue(channel, out VirtualCrossoverChannelControl? control))
        {
            return;
        }

        LiveCaptureDocument? document = channel.SpatialAverage;
        // A stored path with no document behind it is the missing case: name it from
        // the path, since the file that carried the title is the file that is gone.
        string? path = channel.Settings.SpatialAveragePath;
        control.SetSpatialAverage(
            document?.Title
                ?? (string.IsNullOrWhiteSpace(path)
                    ? null
                    : Path.GetFileNameWithoutExtension(path)),
            document?.Recipe.IntegratedSeconds,
            resolved: document != null);
    }

    /// <summary>
    /// Re-attaches one side's persisted capture: the stored path, then the same file
    /// beside the session it was imported from, then beside the folder the user
    /// pointed at when relinking — the very ladder the measurements climb.
    /// </summary>
    /// <remarks>
    /// A capture that no longer resolves degrades to an unattached side rather than
    /// failing the project load, and the stored path is LEFT standing: it is the only
    /// hint a later relink has, and it is what tells the button to warn instead of
    /// showing a channel that never had an average.
    /// </remarks>
    private void ResolveSpatialAverage(
        VirtualCrossoverChannelSettings settings,
        VirtualCrossoverChannelState state)
    {
        state.SpatialAverage = null;
        if (string.IsNullOrWhiteSpace(settings.SpatialAveragePath))
        {
            return;
        }

        string? path =
            VirtualCrossoverSourceLocator.Locate(
                settings.SpatialAveragePath,
                settings.SpatialAverageRelativePath,
                project.ProjectDirectory)
            ?? VirtualCrossoverSourceLocator.Locate(
                settings.SpatialAveragePath,
                settings.SpatialAverageRelativePath,
                relinkDirectory);
        if (path == null)
        {
            return;
        }

        try
        {
            if (LiveCaptureDocument.TryLoad(path, out LiveCaptureDocument document))
            {
                state.SpatialAverage = document;
                // Pin where it was actually read from, the same rule the source path
                // follows: this project becomes the internal autosave right after the
                // import, and that copy has no session file beside it to search from.
                settings.SpatialAveragePath = path;
            }
        }
        catch (Exception exception)
        {
            // An unreadable or foreign file is an unattached side, not a failed load.
            _ = exception;
        }
    }

    /// <summary>
    /// Whether every channel that plays has a spatial average, and therefore whether
    /// the hybrid view is offered at all.
    /// </summary>
    /// <remarks>
    /// All or nothing, deliberately. A sum mixing spatially averaged channels with
    /// point-measured ones puts two different references on one axis and looks
    /// exactly like a measurement, so a partial set mutes the toggle rather than
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

    // Whether the set could produce a hybrid at all, as of the last refresh. Cached
    // rather than recomputed per redraw, and it is the same answer the toggle's own
    // look is built from — the toggle stays ENABLED and is muted by hand (see below),
    // so its enabled state cannot stand in for this.
    private bool hybridAvailable;

    // Whether this redraw should draw the hybrid. Both halves matter: the tick
    // survives a view switch, and a ticked toggle with no coverage behind it must not
    // put a half-built hybrid on the plot.
    private bool HybridRequested => checkBoxHybrid.Checked && hybridAvailable;

    private void RefreshHybridAvailability()
    {
        // The tick is INTENT and outlives the coverage: HybridRequested needs both,
        // so a set that is short of a capture draws honest curves whether or not the
        // box is ticked, and clearing the tick would only mean the user has to find
        // it again after re-attaching. Same reason a pinned gate outlives its
        // sources.
        hybridAvailable = HasSpatialAverageForEveryChannel;

        // Magnitude-only, muted the way the Sum and Sum loss toggles are (see
        // UpdateViewDependentControls): a spatial average carries no phase, so there
        // is no hybrid phase or impulse view to offer. MUTED and not unticked — a
        // look at another view must not cost the tick. Through the shared helper
        // rather than Enabled, because a CheckBox WinForms disables paints its text
        // in a system grey that reads as near-black on this theme.
        bool live = hybridAvailable && radioViewMagnitude.Checked;
        UiStyle.SetTextEnabledLook(checkBoxHybrid, live, interactive: true);
        toolTip.SetToolTip(
            checkBoxHybrid,
            !hybridAvailable
                ? "Needs a spatial average on every channel that plays. Attach one " +
                    "per channel with the MMM button."
                : !radioViewMagnitude.Checked
                ? "The hybrid is a magnitude view: a spatial average carries no " +
                    "phase, so the phase and impulse views keep reading the impulse " +
                    "responses."
                : "Draw each channel's magnitude from its spatial average with this " +
                    "channel's DSP chain on top, instead of from the impulse response " +
                    "measured at one point. The sum follows, carrying the summation " +
                    "loss the impulse responses measure. Timing, polarity and that " +
                    "loss are unaffected: they keep reading the impulse responses, " +
                    "and the other side's sum is hidden while this is on.");
    }

    /// <summary>
    /// This redraw's hybrid magnitudes, or null when the set cannot produce one.
    /// </summary>
    /// <remarks>
    /// Built once per redraw and shared by the drawing and the summation. Every
    /// channel or none: a set where one channel failed to yield a curve would
    /// otherwise sum a spatial average against a point measurement, the mix the whole
    /// feature exists to avoid.
    /// </remarks>
    private HybridMagnitudes? BuildHybridMagnitudes(
        List<ProcessedChannel> processed,
        List<AnalysisCurve> references)
    {
        if (processed.Count == 0 || references.Count < processed.Count)
        {
            return null;
        }

        var hybrids = new List<IReadOnlyList<SignalPoint>>(processed.Count);
        for (int i = 0; i < processed.Count; i++)
        {
            if (BuildHybridChannelCurve(processed[i].Channel, references[i].Points)
                is not { } hybrid)
            {
                return null;
            }

            hybrids.Add(hybrid);
        }

        return new HybridMagnitudes(hybrids, ResolveHybridOffsetDb(hybrids, references));
    }

    /// <summary>
    /// One channel's magnitude drawn from its spatial average instead of from the
    /// impulse response measured at one point: the stored curve with this channel's
    /// own DSP chain on top, on the reference curve's frequency grid and at the
    /// capture's own level. Null when the channel has no average attached.
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
        IReadOnlyList<SignalPoint> reference)
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

        // The SAME chain the processed response was built through, Bypass included:
        // a bypassed channel contributes its raw measured signal, so putting the
        // chain on its average would make the hybrid the one curve on the plot that
        // ignores the switch.
        var prepared = PreparedDspResponse.Create(
            channel.Pair.Bypass ? DspChannelChain.Identity : channel.Settings.ToChain(),
            rate);
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
            points.Add(new SignalPoint(point.X, average + chainDb));
        }

        return points;
    }

    // The set's common offset, applied on the way to the plot.
    private static IReadOnlyList<SignalPoint> ShiftedBy(
        IReadOnlyList<SignalPoint> points,
        double offsetDb)
    {
        if (offsetDb == 0)
        {
            return points;
        }

        var shifted = new List<SignalPoint>(points.Count);
        foreach (SignalPoint point in points)
        {
            shifted.Add(new SignalPoint(point.X, point.Y + offsetDb));
        }

        return shifted;
    }

    /// <summary>
    /// The hybrid channels summed the way the impulse responses sum: their magnitudes
    /// added as amplitudes, then the summation loss the impulse responses measure
    /// laid on top. Null when the set has no usable grid.
    /// </summary>
    /// <remarks>
    /// A spatial average carries no phase, so the channels cannot be summed as
    /// vectors — the arithmetic sum alone would draw a system that cancels nowhere,
    /// which is exactly the picture a tune must not be built on. The loss curve
    /// supplies what the average threw away: it is the signed dB gap between the
    /// complex sum and the phase-blind one, measured on the honest impulse responses,
    /// and it carries over because a per-channel filter changes both halves of that
    /// ratio by the same factor.
    /// <para>
    /// Where the loss curve breaks — its level gate finds every channel filtered far
    /// under the local level, so the gap it would report is two noise floors doing
    /// arithmetic — the hybrid sum breaks with it rather than falling back to a
    /// lossless sum, which would draw its most confident fiction exactly where the
    /// measurement is weakest. A channel whose own curve is NaN at a point (below its
    /// protective high-pass, or past the end of its capture's grid) simply drops out
    /// of that point's sum: its output there is far under the others and its own
    /// crossover removes it anyway, so carrying the whole sum away with it would cost
    /// more than it protects.
    /// </para>
    /// </remarks>
    private static List<SignalPoint>? BuildHybridSumCurve(
        HybridMagnitudes hybrid,
        IReadOnlyList<SignalPoint> reference,
        IReadOnlyList<SignalPoint> loss)
    {
        int count = Math.Min(reference.Count, loss.Count);
        foreach (IReadOnlyList<SignalPoint> channel in hybrid.Channels)
        {
            count = Math.Min(count, channel.Count);
        }

        if (count <= 0)
        {
            return null;
        }

        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            double amplitude = 0;
            foreach (IReadOnlyList<SignalPoint> channel in hybrid.Channels)
            {
                double level = channel[i].Y;
                if (double.IsFinite(level))
                {
                    amplitude += DataHelper.DecibelsToAmplitude(level);
                }
            }

            double lossDb = loss[i].Y;
            points.Add(new SignalPoint(
                reference[i].X,
                amplitude > 0 && double.IsFinite(lossDb)
                    ? DataHelper.AmplitudeToDecibels(amplitude) + hybrid.OffsetDb + lossDb
                    : double.NaN));
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
    private static double ResolveHybridOffsetDb(
        IReadOnlyList<IReadOnlyList<SignalPoint>> hybrids,
        IReadOnlyList<AnalysisCurve> references)
    {
        var perChannel = new List<double>();
        for (int i = 0; i < hybrids.Count && i < references.Count; i++)
        {
            IReadOnlyList<SignalPoint> hybrid = hybrids[i];
            IReadOnlyList<SignalPoint> reference = references[i].Points;
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
