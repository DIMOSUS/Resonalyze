using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.Screenshots;

/// <summary>Catalogue of automatable doc screenshots, grouped into scenes (one app launch each); names are paths under <c>assets/images</c>.</summary>
/// <remarks>Not automatable: <c>noise</c> (live signal), <c>compare</c> (composed crop), forum-article figures and context menus.</remarks>
internal static class Shots
{
    public static IReadOnlyList<Scene> All { get; } =
    [
        new("modes", ShotSession.AssetWindowSize,
            ["fr", "phase", "gd", "impulse", "waterfall", "burst", "time-alignment"],
            Modes),
        // On request only: Record Settings draws the live audio config and shoots defaults without the rig attached.
        new("record", ShotSession.AssetWindowSize, ["measurement-options"],
            RecordSettings, OnRequest: true),
        new("overlays", ShotSession.AssetWindowSize,
            ["calc_overlay", "regular_overlay", "target_overlay"], Overlays),
        new("virtual-dsp", ShotSession.AssetWindowSize, ["visual_dsp"], VirtualDspAsset),
        new("eq-wizard", ShotSession.AssetWindowSize,
            ["eq_wizard", "eq_wizard_phase"], EqWizardAssets),
        new("fir-constructor", ShotSession.AssetWindowSize, ["fir_constructor"], FirConstructorAsset),
        // Skipped in a sweep without an array measurement; fails when asked for by name (see Program).
        new("array", ShotSession.ManualWindowSize, ["manual/array-curves"],
            ArrayCurves, Unavailable: NeedsArrayMeasurement),
        new("array-dialog", ShotSession.ManualWindowSize, ["manual/array-microphones"],
            ArrayDialogFigure,
            Unavailable: config => NeedsArrayMeasurement(config)
                ?? (config.Rig == null
                    ? "no \"arrayRig\" in the config — the inputs, the loopback and the " +
                      "backend to draw the dialog for, which no measurement records"
                    : null)),
        // Drawn off the form from the real ai-proposal.json; packageId is rewritten because any other id shows the untrusted-package warning.
        new("agent", ShotSession.AssetWindowSize, ["ai_assistant"], AgentReview),
        new("manual", ShotSession.ManualWindowSize,
            ["manual/virtual-dsp", "manual/channel-card", "manual/eq-wizard-handoff",
             "manual/eq-wizard-tuned", "manual/dsp-processor",
             "manual/dsp-processor-model", "manual/eq-target", "manual/auto-crossover",
             "manual/auto-delay", "manual/tuning-sheet-q", "manual/audition-track"],
            Manual)
    ];

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
            session.Pump(tab is "Waterfall" or "Burst" ? 12_000 : 5_000);
            session.OpenModeSettings();
            PoseCurves(session, tab);
            session.CaptureScreen(name);
        }
    }

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

    // Through the real Overlay methods: ConfigureOperation takes 23 arguments the collection already knows.
    private static void Overlays(ShotSession session, Func<string, bool> wanted)
    {
        session.LoadMeasurement(session.Config.Measurement);
        object collection = Reflect.Field(session.Shell, "overlayCollection");
        object[] slots = ((System.Collections.IEnumerable)Reflect.Field(collection, "overlays"))
            .Cast<object>().ToArray();

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

    private static void FirConstructorAsset(ShotSession session, Func<string, bool> wanted)
    {
        session.SelectTab("ToolsFirConstructor");
        var panel = Reflect.Field<FirConstructorPanel>(session.Shell, "firConstructorPanel");
        Reflect.Field<ThemedComboBox>(panel, "comboBoxType").SelectedIndex = 2;
        Reflect.Field<ThemedNumericUpDown>(panel, "numericHighPassHz").Value = 250;
        Reflect.Field<ThemedNumericUpDown>(panel, "numericLowPassHz").Value = 3_000;
        Reflect.Field<ThemedNumericUpDown>(panel, "numericTaps").Value = 4_095;
        session.Pump(3_000);
        session.CaptureScreen("fir_constructor");
    }

    private static string? NeedsArrayMeasurement(ShotConfig config) =>
        string.IsNullOrWhiteSpace(config.ArrayMeasurement)
            ? "no \"arrayMeasurement\" in the config — a measurement recorded with a " +
              "microphone array"
            : null;

    private static void ArrayCurves(ShotSession session, Func<string, bool> wanted)
    {
        _ = wanted;
        session.LoadMeasurement(session.Config.ArrayMeasurement);
        session.SelectTab("Frequency");
        session.Pump(4_000);
        session.OpenModeSettings();
        Form settings = session.ModeSettingsDialog
            ?? throw new InvalidOperationException(
                "manual/array-curves: the Frequency Response settings did not open.");
        // Distortion, noise floor and coherence off: extra traces bury the point/average/spread comparison.
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

    /// <summary>Built directly: Record Settings lists only inputs of the attached interface.</summary>
    /// <remarks>Rows come from the measurement; device facts the file lacks come from the config.</remarks>
    private static void ArrayDialogFigure(ShotSession session, Func<string, bool> wanted)
    {
        _ = wanted;
        Task<ImpulseResponseFile> loading =
            ImpulseResponseFile.LoadAsync(session.Config.ArrayMeasurement);
        session.Await(loading);
        List<ImpulseResponseFile.ArrayMicrophoneFileEntry> positions =
            loading.Result.ArrayMicrophones?.Microphones
            ?? throw new InvalidOperationException(
                $"{session.Config.ArrayMeasurement} carries no microphone array.");

        session.CaptureDialog(
            ArrayDialog(positions, session.Config.Rig!), "manual/array-microphones");
    }

    private static Form ArrayDialog(
        IReadOnlyList<ImpulseResponseFile.ArrayMicrophoneFileEntry> positions,
        ArrayRig rig)
    {
        ImpulseResponseFile.ArrayMicrophoneFileEntry anchor =
            positions.FirstOrDefault(position => position.IsMeasurementMicrophone)
            ?? throw new InvalidOperationException(
                "The array has no measurement microphone in it.");
        List<ImpulseResponseFile.ArrayMicrophoneFileEntry> further =
            [.. positions.Where(position => !position.IsMeasurementMicrophone)];

        // Config may name calibrations per row: a set moved between sittings stores one name for every position.
        string?[] names = rig.Calibrations is { } authored
            ? authored.Length == further.Count
                ? [.. authored]
                : throw new InvalidOperationException(
                    $"arrayRig names {authored.Length} calibrations and the measurement " +
                    $"has {further.Count} further microphones. One per row, in order.")
            : [.. further.Select(position => position.Calibration?.Name)];

        List<MicrophoneCalibrationEntry> calibrations =
            [.. names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new MicrophoneCalibrationEntry(name!, name!, true))];

        List<ArrayMicrophoneDefinition> microphones =
            [.. further.Select((position, index) => new ArrayMicrophoneDefinition
            {
                ChannelOffset = position.ChannelOffset,
                CalibrationId = names[index],
                Note = position.Note
            })];

        // Device facts are authored in the config, not guessed; the listed rows still come from the measurement.
        int loopback = rig.LoopbackInput - 1;
        foreach (ImpulseResponseFile.ArrayMicrophoneFileEntry position in positions)
        {
            if (position.ChannelOffset >= rig.Inputs || position.ChannelOffset == loopback)
            {
                throw new InvalidOperationException(
                    $"arrayRig says {rig.Inputs} inputs with the loopback on " +
                    $"Input {rig.LoopbackInput}, and the measurement recorded a " +
                    $"position on Input {position.ChannelOffset + 1}. One of the two " +
                    "describes a different rig.");
            }
        }

        return new Options.ArrayMicrophonesDialog(
            microphones,
            calibrations,
            [.. Enumerable.Range(0, rig.Inputs)],
            anchor.ChannelOffset,
            loopback,
            Options.ArrayInputSources.Describe(rig.Backend, rig.Inputs));
    }

    private static void Manual(ShotSession session, Func<string, bool> wanted)
    {
        OpenSession(session);
        var panel = Reflect.Field<VirtualCrossoverPanel>(session.Shell, "virtualCrossoverPanel");

        if (wanted("manual/virtual-dsp"))
        {
            session.CaptureScreen("manual/virtual-dsp");
            AnnotateVirtualDsp(session.Config.Resolve("manual/virtual-dsp"));
        }

        if (wanted("manual/channel-card"))
        {
            var list = Reflect.Field<FlowLayoutPanel>(panel, "channelListPanel");
            Control card = list.Controls.Count > 1
                ? list.Controls[1]
                : throw new InvalidOperationException(
                    "manual/channel-card: the session has no second channel block.");
            session.Capture(card, "manual/channel-card");
            AnnotateChannelCard(session.Config.Resolve("manual/channel-card"));
        }

        // Wizard first: a modal loop of its own is what has disturbed its async Auto Tune.
        if (wanted("manual/eq-wizard-handoff") || wanted("manual/eq-wizard-tuned"))
        {
            EqWizardPanel wizard = HandOff(session, "C");
            // Bank emptied directly: Reset filters asks via MessageBox, which is not in OpenForms and would hang the run.
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

        // Built directly: with a catalog model Export opens a native SaveFileDialog nothing here can close.
        if (wanted("manual/tuning-sheet-q"))
        {
            session.CaptureDialog(
                QConventionDialog(Dsp.PeqQConvention.Rbj), "manual/tuning-sheet-q");
        }

        if (wanted("manual/dsp-processor"))
        {
            // Built directly: the manual captions it as a new project (Custom), while the session names a catalog model.
            session.CaptureDialog(
                DspProcessorDialog(
                    Dsp.DspProcessorProfile.Custom(96_000, Dsp.PeqQConvention.Rbj),
                    follows: true,
                    measurementRateHz: 96_000),
                "manual/dsp-processor");
        }

        if (wanted("manual/dsp-processor-model"))
        {
            // Posing the live dialog's model combo would race its own change handler.
            session.CaptureDialog(
                DspProcessorDialog(
                    Dsp.DspProcessorCatalog.Preset("amp-panacea-v1-v2")!.ToProfile(),
                    follows: false,
                    measurementRateHz: 96_000),
                "manual/dsp-processor-model");
        }

        if (wanted("manual/eq-target"))
        {
            // Opens the dialog directly; a posted drop-down menu is not a modal to wait on.
            session.CaptureModal("manual/eq-target",
                () => Reflect.Invoke(panel, "OpenTargetSettings"), 2_000);
        }

        if (wanted("manual/auto-crossover"))
        {
            // Regions are measured while the dialog is on screen: it lays itself out at runtime.
            AutoCrossoverFigure.Layout? crossover = null;
            session.CaptureModal(
                "manual/auto-crossover",
                () => Reflect.Field<Button>(panel, "buttonAutoSetup").PerformClick(),
                4_000,
                dialog => crossover = AutoCrossoverFigure.Measure(dialog));
            if (crossover != null)
            {
                AutoCrossoverFigure.Draw(
                    crossover, session.Config.Resolve("manual/auto-crossover"));
            }
        }

        if (wanted("manual/auto-delay"))
        {
            // Run is pressed inside the dialog's loop; regions are measured while it is on screen.
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

    private static void AgentReview(ShotSession session, Func<string, bool> wanted)
    {
        _ = wanted;
        OpenSession(session);
        var panel = Reflect.Field<VirtualCrossoverPanel>(session.Shell, "virtualCrossoverPanel");

        // Copy for AI through its real path: it mints the package id the review checks.
        session.Await((Task)Reflect.Invoke(panel, "CopyForAiAsync")!);
        session.Pump(3_000);
        var packageId = Reflect.Field<string>(panel, "lastAgentPackageId");

        string path = Path.Combine(AppContext.BaseDirectory, "ai-proposal.json");
        string reply = File.ReadAllText(path).Replace("PACKAGE_ID", packageId, StringComparison.Ordinal);
        AgentProposalParseResult parsed = AgentProposalParser.Parse(reply);
        if (!parsed.Succeeded)
        {
            throw new InvalidOperationException($"ai-proposal.json: {parsed.Error}");
        }

        AgentProposalReview review = AgentProposalValidator.Review(
            parsed.Proposal!, panel.BuildAgentSessionSnapshot());
        // Refuse to photograph a reply the session argues with (warnings, or a row it will not offer).
        if (review.Warnings.Count > 0)
        {
            throw new InvalidOperationException(
                "The review warns, so the figure would show a refusal: " +
                string.Join(" | ", review.Warnings));
        }
        if (review.Verdicts.FirstOrDefault(verdict => !verdict.Applicable) is { } refused)
        {
            throw new InvalidOperationException(
                $"{refused.Id} is not applicable to this session ({refused.Message}), so " +
                "the figure would show a refused row. The reply is written for a session " +
                "with a C-D junction on both sides.");
        }

        session.CaptureDialog(new AgentProposalDialog(review), "ai_assistant");
    }

    private static void OpenSession(ShotSession session)
    {
        session.SelectTab("ToolsVirtualCrossover");
        var panel = Reflect.Field<VirtualCrossoverPanel>(session.Shell, "virtualCrossoverPanel");
        VirtualCrossoverProjectFile project =
            VirtualCrossoverProjectFile.LoadFrom(session.Config.Session);
        session.Await((Task)Reflect.Invoke(panel, "ApplyProjectAsync", project, true)!);
        session.Pump(10_000);
    }

    private static EqWizardPanel HandOff(ShotSession session, string channelName)
    {
        var panel = Reflect.Field<VirtualCrossoverPanel>(session.Shell, "virtualCrossoverPanel");
        VirtualCrossoverChannel channel = panel.Session.Channels.FirstOrDefault(
            candidate => candidate.Name == channelName)
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
        Dsp.DspProcessorProfile profile,
        bool follows,
        int measurementRateHz,
        bool? phaseControl = null,
        bool? firFilters = null)
    {
        // Reflection hides constructor changes from the build: keep these arguments in sync with DspProcessorDialog by hand.
        Type type = typeof(VirtualCrossoverPanel).Assembly
            .GetType("Resonalyze.DspProcessorDialog")
            ?? throw new InvalidOperationException("No Resonalyze.DspProcessorDialog type.");
        return (Form)Activator.CreateInstance(
            type,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public,
            binder: null,
            [profile, follows, measurementRateHz, phaseControl, firFilters],
            culture: null)!;
    }

    // Coordinates are read off the rendered figure: re-read them when the window size or panel layout changes.

    private static void AnnotateVirtualDsp(string path)
    {
        using Annotate figure = Annotate.Open(path);
        figure.Region(Box(14, 56, 354, 942), "1", new Point(40, 918))
              .Region(Box(364, 54, 1468, 576), "2", new Point(398, 84))
              .Region(Box(368, 580, 1472, 638), "3", new Point(1436, 609))
              .Region(Box(368, 642, 502, 1014), "4", new Point(435, 800))
              .Region(Box(506, 642, 1472, 1014), "5", new Point(1440, 675))
              .Region(Box(1490, 145, 1716, 1014), "6", new Point(1516, 988))
              .Save(path);
    }

    private static void AnnotateChannelCard(string path)
    {
        using Annotate figure = Annotate.Open(path);
        figure.Gutter(40, onLeft: true, sample: new Point(318, 100))
              .Region(Box(3, 3, 319, 30), "1", new Point(20, 16),
                  leader: true, badgeRadius: 10)
              .Region(Box(3, 31, 319, 53), "2", new Point(20, 42),
                  leader: true, badgeRadius: 10)
              .Region(Box(3, 53, 319, 75), "3", new Point(20, 64),
                  leader: true, badgeRadius: 10)
              .Region(Box(3, 75, 319, 100), "4", new Point(20, 87),
                  leader: true, badgeRadius: 10)
              .Region(Box(3, 101, 319, 178), "5", new Point(20, 139),
                  leader: true, badgeRadius: 10)
              .Region(Box(3, 179, 319, 203), "6", new Point(20, 191),
                  leader: true, badgeRadius: 10)
              .Save(path);
    }

    private static void AnnotateEqWizard(string path)
    {
        using Annotate figure = Annotate.Open(path);
        figure.Gutter(52, onLeft: true, sample: new Point(100, 600))
              .Region(Box(14, 62, 204, 93), "1", new Point(26, 77), leader: true)
              .Region(Box(14, 120, 204, 270), "2", new Point(26, 195), leader: true)
              // Figure pixels, captured 1:1 (panel y=0 at 46).
              .Region(Box(14, 475, 204, 535), "3", new Point(26, 505), leader: true)
              .Region(Box(14, 807, 206, 982), "4", new Point(26, 894), leader: true)
              .Region(Box(18, 985, 202, 1014), "5", new Point(26, 999), leader: true)
              .Detail(Box(18, 883, 200, 935))
              .Region(Box(1494, 682, 1712, 1018), "6", new Point(1584, 962))
              .Save(path);
    }

    private static Rectangle Box(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);
}

internal sealed record Scene(
    string Name,
    Size WindowSize,
    IReadOnlyList<string> Shots,
    Action<ShotSession, Func<string, bool>> Body,
    bool OnRequest = false,
    Func<ShotConfig, string?>? Unavailable = null);
