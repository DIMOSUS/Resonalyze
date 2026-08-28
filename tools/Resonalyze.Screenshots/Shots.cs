namespace Resonalyze.Screenshots;

/// <summary>
/// The catalogue: every screenshot the documentation uses that this tool can take.
/// </summary>
/// <remarks>
/// Shots are grouped into SCENES because each scene opens the application once and
/// arranges it a particular way; asking for a single shot starts only the scene that
/// owns it. Names are the file the shot writes, relative to <c>assets/images</c>.
///
/// Not here, and not automatable:
/// <list type="bullet">
/// <item><c>noise</c> — Live Spectrum needs a LIVE signal through real hardware.</item>
/// <item><c>compare</c> — a composed before/after crop of two different tunes.</item>
/// <item>The manual's figures taken from the forum article: the microphone photo,
/// the MMM captures, the hybrid pair and the open PEQ menu (a context menu is a
/// separate window that neither capture path reaches).</item>
/// </list>
/// </remarks>
internal static class Shots
{
    public static IReadOnlyList<Scene> All { get; } =
    [
        new("modes", ShotSession.AssetWindowSize,
            ["fr", "phase", "gd", "impulse", "waterfall", "burst", "time-alignment"],
            Modes),
        // Left out of a no-argument sweep: Record Settings draws the LIVE audio
        // configuration, so on a machine without the interface attached it shoots a
        // panel full of defaults and a "loopback is REQUIRED" warning — a worse
        // figure than the one already committed. Ask for it by name, rig plugged in.
        new("record", ShotSession.AssetWindowSize, ["measurement-options"],
            RecordSettings, OnRequest: true),
        new("overlays", ShotSession.AssetWindowSize,
            ["calc_overlay", "regular_overlay", "target_overlay"], Overlays),
        new("virtual-dsp", ShotSession.AssetWindowSize, ["visual_dsp"], VirtualDspAsset),
        new("eq-wizard", ShotSession.AssetWindowSize,
            ["eq_wizard", "eq_wizard_phase"], EqWizardAssets),
        // Skipped rather than failed without a measurement recorded through an
        // array: the figures need one and not every rig has one, and a config that
        // cannot take them must not report the sweep as broken.
        new("array", ShotSession.ManualWindowSize,
            ["manual/array-curves", "manual/array-microphones"],
            ArrayFigures,
            Unavailable: config => string.IsNullOrWhiteSpace(config.ArrayMeasurement)
                ? "no \"arrayMeasurement\" in the config — a measurement recorded " +
                  "with a microphone array"
                : null),
        new("manual", ShotSession.ManualWindowSize,
            ["manual/virtual-dsp", "manual/eq-wizard-handoff", "manual/eq-wizard-tuned",
             "manual/dsp-processor", "manual/dsp-processor-model", "manual/eq-target",
             "manual/auto-crossover", "manual/auto-delay", "manual/tuning-sheet-q",
             "manual/audition-track"],
            Manual)
    ];

    // ---------------------------------------------------------------- the modes

    // The mode settings panel is where every per-mode control lives, so it is opened
    // for each shot and the capture comes off the screen — see ShotSession.
    private static void Modes(ShotSession session, Func<string, bool> wanted)
    {
        session.LoadMeasurement(session.Config.Measurement);

        foreach ((string tab, string name) in new[]
        {
            ("Frequency", "fr"), ("Phase", "phase"), ("GroupDelay", "gd"),
            ("Impulse", "impulse"), ("Waterfall", "waterfall"), ("Burst", "burst"),
            ("TimeAlignment", "time-alignment")
        })
        {
            if (!wanted(name))
            {
                continue;
            }

            session.SelectTab(tab);
            // A waterfall or a burst decay computes a whole surface before it draws.
            session.Pump(tab is "Waterfall" or "Burst" ? 12_000 : 5_000);
            session.OpenModeSettings();
            PoseCurves(session, tab);
            session.CaptureScreen(name);
        }
    }

    // A shot of a curve nobody enabled teaches nothing: these are the curves the
    // committed figures show, and the reason each mode exists.
    private static void PoseCurves(ShotSession session, string tab)
    {
        Form? dialog = session.ModeSettingsDialog;
        if (dialog == null)
        {
            return;
        }

        string[] boxes = tab switch
        {
            "GroupDelay" =>
                ["checkBoxShowGroupDelay", "checkBoxShowMinimumPhaseGroupDelay",
                 "checkBoxShowExcessGroupDelay", "checkBoxShowCoherence"],
            "Impulse" => ["checkBoxShowImpulse", "checkBoxShowEnvelope"],
            _ => []
        };
        foreach (string name in boxes)
        {
            Reflect.Field<CheckBox>(dialog, name).Checked = true;
        }

        if (boxes.Length > 0)
        {
            session.Pump(2_500);
        }
    }

    private static void RecordSettings(ShotSession session, Func<string, bool> wanted)
    {
        _ = wanted;
        session.LoadMeasurement(session.Config.Measurement);
        session.SelectTab("Frequency");
        Reflect.Field<Button>(session.Shell, "buttonRecordOpt").PerformClick();
        session.Pump(2_000);

        object host = Reflect.Field(session.Shell, "dockedMeasurementSettingsHost");
        var dialog = (Form)Reflect.Field(host, "activeDialog");
        session.Capture(dialog, "measurement-options");
    }

    // -------------------------------------------------------------- the overlays

    // Opened through the real Overlay methods rather than by constructing the
    // dialogs: ConfigureOperation takes 23 arguments the collection already knows.
    private static void Overlays(ShotSession session, Func<string, bool> wanted)
    {
        session.LoadMeasurement(session.Config.Measurement);
        object collection = Reflect.Field(session.Shell, "overlayCollection");
        object[] slots = ((System.Collections.IEnumerable)Reflect.Field(collection, "overlays"))
            .Cast<object>().ToArray();

        // A slot each, so no dialog opens on another's leftovers.
        if (wanted("regular_overlay"))
        {
            session.CaptureModal("regular_overlay",
                () => Reflect.Invoke(slots[1], "ConfigureCaptured"), 2_000);
        }

        if (wanted("calc_overlay"))
        {
            session.CaptureModal("calc_overlay",
                () => Reflect.Invoke(slots[2], "ConfigureOperation"), 2_000);
        }

        if (wanted("target_overlay"))
        {
            session.CaptureModal("target_overlay",
                () => Reflect.Invoke(slots[3], "ConfigureTarget"), 2_000);
        }
    }

    // ------------------------------------------------------------- Virtual DSP

    private static void VirtualDspAsset(ShotSession session, Func<string, bool> wanted)
    {
        _ = wanted;
        OpenSession(session);
        session.CaptureScreen("visual_dsp");
    }

    private static void EqWizardAssets(ShotSession session, Func<string, bool> wanted)
    {
        OpenSession(session);
        EqWizardPanel wizard = HandOff(session, "C");

        if (wanted("eq_wizard"))
        {
            session.Pump(4_000);
            session.CaptureScreen("eq_wizard");
        }

        if (wanted("eq_wizard_phase"))
        {
            Reflect.Field<CheckBox>(wizard, "checkBoxEqPhase").Checked = true;
            session.Pump(6_000);
            session.CaptureScreen("eq_wizard_phase");
        }
    }

    // -------------------------------------------------------- the microphone array

    /// <summary>
    /// The two array figures, both from ONE measurement: the curves as the mode
    /// settings draw them, and the dialog that configured the set.
    /// </summary>
    /// <remarks>
    /// The dialog's rows are read back out of the measurement rather than invented,
    /// so the figure shows the positions whose curves the other shot draws — a made-up
    /// set would drift from the guide's text the first time the real one changed.
    /// The dialog is constructed rather than reached through Record Settings, which
    /// offers only inputs the ATTACHED interface has: without the rig this shot would
    /// otherwise be a dialog with nothing in it.
    /// </remarks>
    private static void ArrayFigures(ShotSession session, Func<string, bool> wanted)
    {
        Task<ImpulseResponseFile> loading =
            ImpulseResponseFile.LoadAsync(session.Config.ArrayMeasurement);
        session.Await(loading);
        List<ImpulseResponseFile.ArrayMicrophoneFileEntry> positions =
            loading.Result.ArrayMicrophones?.Microphones
            ?? throw new InvalidOperationException(
                $"{session.Config.ArrayMeasurement} carries no microphone array.");

        if (wanted("manual/array-curves"))
        {
            session.LoadMeasurement(session.Config.ArrayMeasurement);
            session.SelectTab("Frequency");
            session.Pump(4_000);
            session.OpenModeSettings();
            Form settings = session.ModeSettingsDialog
                ?? throw new InvalidOperationException(
                    "manual/array-curves: the Frequency Response settings did not open.");
            // The measurement carries its own curve selection, and the figure is about
            // ONE comparison: the point response against the positions, their average
            // and their spread. Distortion, noise floor and coherence are switched off
            // rather than left to whatever the file was last read with — six more
            // traces over the same decade is what buries the four that are the point.
            foreach ((string box, bool on) in new[]
            {
                ("checkBoxShowPrimary", true),
                ("checkBoxShowArrayAverage", true),
                ("checkBoxShowArrayMicrophones", true),
                ("checkBoxShowArraySpread", true),
                ("checkBoxShowHd2", false),
                ("checkBoxShowHd3", false),
                ("checkBoxShowHd4", false),
                ("checkBoxShowThdPlusNoise", false),
                ("checkBoxShowNoiseFloor", false),
                ("checkBoxShowCoherence", false)
            })
            {
                Reflect.Field<CheckBox>(settings, box).Checked = on;
            }

            session.Pump(3_000);
            session.CaptureScreen("manual/array-curves");
        }

        if (wanted("manual/array-microphones"))
        {
            session.CaptureDialog(ArrayDialog(positions), "manual/array-microphones");
        }
    }

    private static Form ArrayDialog(
        IReadOnlyList<ImpulseResponseFile.ArrayMicrophoneFileEntry> positions)
    {
        ImpulseResponseFile.ArrayMicrophoneFileEntry anchor =
            positions.FirstOrDefault(position => position.IsMeasurementMicrophone)
            ?? throw new InvalidOperationException(
                "The array has no measurement microphone in it.");
        List<ImpulseResponseFile.ArrayMicrophoneFileEntry> further =
            [.. positions.Where(position => !position.IsMeasurementMicrophone)];

        // The dialog names calibrations by id out of Record Settings' own list, and
        // the file kept only the curve's name — enough to show the row as it was
        // configured, which is all a figure has to be right about.
        List<MicrophoneCalibrationEntry> calibrations =
            [.. further
                .Select(position => position.Calibration?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new MicrophoneCalibrationEntry(name!, name!, true))];

        List<ArrayMicrophoneDefinition> microphones =
            [.. further.Select(position => new ArrayMicrophoneDefinition
            {
                ChannelOffset = position.ChannelOffset,
                CalibrationId = position.Calibration?.Name,
                Note = position.Note
            })];

        // The loopback's input is not in the file — no array microphone may sit on it,
        // so it is the lowest input none of them took — and the interface is given one
        // input more than the set used, which is what leaves the editor in its ordinary
        // "ready to add another" state instead of with an empty input list.
        int inputs = positions.Max(position => position.ChannelOffset) + 2;
        int loopback = Enumerable.Range(0, inputs).First(
            channel => positions.All(position => position.ChannelOffset != channel));

        return new Options.ArrayMicrophonesDialog(
            microphones,
            calibrations,
            [.. Enumerable.Range(0, inputs)],
            anchor.ChannelOffset,
            loopback,
            "ASIO driver inputs");
    }

    // ------------------------------------------------------------- the manual

    private static void Manual(ShotSession session, Func<string, bool> wanted)
    {
        OpenSession(session);
        var panel = Reflect.Field<VirtualCrossoverPanel>(session.Shell, "virtualCrossoverPanel");

        if (wanted("manual/virtual-dsp"))
        {
            session.CaptureScreen("manual/virtual-dsp");
            AnnotateVirtualDsp(session.Config.Resolve("manual/virtual-dsp"));
        }

        // The wizard first, and every modal after it: a modal loop of its own is the
        // one thing that has ever disturbed the wizard's async Auto Tune here.
        if (wanted("manual/eq-wizard-handoff") || wanted("manual/eq-wizard-tuned"))
        {
            EqWizardPanel wizard = HandOff(session, "C");
            // Cleared, so the first still shows what the guide describes: the chain's
            // curve against the target with nothing fitted yet. The bank is emptied
            // directly rather than by pressing Reset filters, which asks first — and a
            // MessageBox is not in Application.OpenForms, so nothing here can answer
            // it and the run would hang on the click forever.
            Reflect.Invoke(wizard, "ApplyEqualizationCurve",
                new Dsp.EqualizationCurve([], 0));
            session.Pump(4_000);
            if (wanted("manual/eq-wizard-handoff"))
            {
                session.CaptureScreen("manual/eq-wizard-handoff");
                AnnotateEqWizard(session.Config.Resolve("manual/eq-wizard-handoff"));
            }

            if (wanted("manual/eq-wizard-tuned"))
            {
                Reflect.Field<Button>(wizard, "buttonAutoTune").PerformClick();
                session.Pump(15_000);
                session.CaptureScreen("manual/eq-wizard-tuned");
            }

            session.SelectTab("ToolsVirtualCrossover");
            session.Pump(3_000);
        }

        // Built directly rather than by pressing Export. Export only shows this
        // chooser while the project names NO model; a session naming a catalog
        // processor skips it and opens a native SaveFileDialog instead, which is not
        // a Form in Application.OpenForms and which nothing here could close — the
        // run would hang on the click. The figure is of the dialog either way.
        if (wanted("manual/tuning-sheet-q"))
        {
            session.CaptureDialog(
                QConventionDialog(Dsp.PeqQConvention.Rbj), "manual/tuning-sheet-q");
        }

        if (wanted("manual/dsp-processor"))
        {
            session.CaptureModal("manual/dsp-processor",
                () => Reflect.Field<Button>(panel, "buttonDspProcessor").PerformClick());
        }

        if (wanted("manual/dsp-processor-model"))
        {
            // Built directly with a catalog model: posing the live dialog's model
            // combo would race its own change handler.
            session.CaptureDialog(
                DspProcessorDialog(
                    Dsp.DspProcessorCatalog.Preset("amp-panacea-v1-v2")!.ToProfile(),
                    follows: false,
                    measurementRateHz: 96_000),
                "manual/dsp-processor-model");
        }

        if (wanted("manual/eq-target"))
        {
            session.CaptureModal("manual/eq-target",
                () => Reflect.Field<Button>(panel, "buttonTargetSettings").PerformClick(), 2_000);
        }

        if (wanted("manual/auto-crossover"))
        {
            session.CaptureModal("manual/auto-crossover",
                () => Reflect.Field<Button>(panel, "buttonAutoSetup").PerformClick(), 4_000);
        }

        if (wanted("manual/auto-delay"))
        {
            // The empty dialog says nothing; the figure needs the proposal, so Run is
            // pressed inside the dialog's own loop. The regions are measured while the
            // dialog is still on screen and drawn once the file exists.
            AutoDelayFigure.Layout? layout = null;
            session.CaptureModal(
                "manual/auto-delay",
                () => Reflect.Field<Button>(panel, "buttonAutoDelay").PerformClick(),
                2_500,
                dialog => layout = AutoDelayFigure.PoseAndMeasure(session, dialog));
            if (layout != null)
            {
                AutoDelayFigure.Draw(layout, session.Config.Resolve("manual/auto-delay"));
            }
        }

        if (wanted("manual/audition-track"))
        {
            session.CaptureModal("manual/audition-track",
                () => Reflect.Field<Button>(panel, "buttonAudition").PerformClick(), 3_000);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static void OpenSession(ShotSession session)
    {
        session.SelectTab("ToolsVirtualCrossover");
        var panel = Reflect.Field<VirtualCrossoverPanel>(session.Shell, "virtualCrossoverPanel");
        VirtualCrossoverProjectFile project =
            VirtualCrossoverProjectFile.LoadFrom(session.Config.Session);
        session.Await((Task)Reflect.Invoke(panel, "ApplyProjectAsync", project, true)!);
        session.Pump(10_000);
    }

    /// <summary>Hands a channel to the EQ Wizard through the chain, as the menu does.</summary>
    private static EqWizardPanel HandOff(ShotSession session, string channelName)
    {
        var panel = Reflect.Field<VirtualCrossoverPanel>(session.Shell, "virtualCrossoverPanel");
        object[] channels = ((System.Collections.IEnumerable)Reflect.Field(panel, "channels"))
            .Cast<object>().ToArray();
        object channel = channels.FirstOrDefault(
            candidate => (string)Reflect.Property(candidate, "Name") == channelName)
            ?? throw new InvalidOperationException(
                $"The session has no channel {channelName}.");

        Reflect.Invoke(panel, "RequestPeqHandoff", channel, true);
        session.Pump(2_000);
        session.SelectTab("ToolsEqWizard");
        session.Pump(6_000);
        return Reflect.Field<EqWizardPanel>(session.Shell, "eqWizardPanel");
    }

    private static Form QConventionDialog(Dsp.PeqQConvention selected)
    {
        Type type = typeof(VirtualCrossoverPanel).Assembly
            .GetType("Resonalyze.TuningSheetQConventionDialog")
            ?? throw new InvalidOperationException(
                "No Resonalyze.TuningSheetQConventionDialog type.");
        return (Form)Activator.CreateInstance(
            type,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public,
            binder: null,
            [selected],
            culture: null)!;
    }

    private static Form DspProcessorDialog(
        Dsp.DspProcessorProfile profile, bool follows, int measurementRateHz)
    {
        Type type = typeof(VirtualCrossoverPanel).Assembly
            .GetType("Resonalyze.DspProcessorDialog")
            ?? throw new InvalidOperationException("No Resonalyze.DspProcessorDialog type.");
        return (Form)Activator.CreateInstance(
            type,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public,
            binder: null,
            [profile, follows, measurementRateHz],
            culture: null)!;
    }

    // ------------------------------------------------------------- annotations
    //
    // These coordinates are read off the rendered figure, so they belong to a window
    // size and a panel layout. When a panel moves, the boxes move with it and the
    // numbers here have to be re-read — there is no way around that short of asking
    // the controls where they are, which would tie the figures to control names as
    // brittle as the coordinates.

    private static void AnnotateVirtualDsp(string path)
    {
        using Annotate figure = Annotate.Open(path);
        figure.Region(Box(14, 56, 354, 942), "1", new Point(40, 918))
              .Region(Box(364, 54, 1468, 576), "2", new Point(398, 84))
              .Region(Box(368, 580, 1472, 638), "3", new Point(1436, 609))
              .Region(Box(368, 642, 502, 1014), "4", new Point(435, 800))
              .Region(Box(506, 642, 1472, 1014), "5", new Point(1440, 675))
              .Region(Box(1490, 384, 1716, 1014), "6", new Point(1516, 988))
              .Save(path);
    }

    private static void AnnotateEqWizard(string path)
    {
        using Annotate figure = Annotate.Open(path);
        figure.Gutter(52, onLeft: true, sample: new Point(100, 600))
              .Region(Box(14, 62, 204, 93), "1", new Point(26, 77), leader: true)
              .Region(Box(14, 120, 204, 270), "2", new Point(26, 195), leader: true)
              // The two DSP-handoff buttons, 25 px lower than they were: the panel
              // gained an EQ-curve checkbox under Bypass/Phase and the column below
              // it moved down by one row. These boxes are figure pixels and the shot
              // is captured 1:1 (window chrome puts the panel's y=0 at 46), so the
              // designer's 25 is 25 here too.
              .Region(Box(14, 475, 204, 535), "3", new Point(26, 505), leader: true)
              .Region(Box(14, 832, 206, 982), "4", new Point(26, 907), leader: true)
              .Region(Box(18, 985, 202, 1014), "5", new Point(26, 999), leader: true)
              .Detail(Box(18, 883, 200, 935))
              .Region(Box(1494, 682, 1712, 1018), "6", new Point(1584, 962))
              .Save(path);
    }

    private static Rectangle Box(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);
}

/// <summary>One arrangement of the application, producing a set of shots.</summary>
internal sealed record Scene(
    string Name,
    Size WindowSize,
    IReadOnlyList<string> Shots,
    Action<ShotSession, Func<string, bool>> Body,
    bool OnRequest = false,
    Func<ShotConfig, string?>? Unavailable = null);
