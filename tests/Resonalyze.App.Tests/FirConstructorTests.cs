using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The FIR Constructor outside the design arithmetic: the design kept beside its
/// kernel in a session, the corners a FIR crossover lends a side with no IIR one, the
/// handoff and every line its return refuses on, the Lock carrying the FIR stage, the
/// block's red button, and the panel itself.
/// </summary>
public sealed class FirConstructorTests
{
    private static readonly CrossoverEdge Lr24At80 = new(CrossoverFilterFamily.LinkwitzRiley, 80, 24);
    private static readonly CrossoverEdge Lr24At2500 = new(CrossoverFilterFamily.LinkwitzRiley, 2_500, 24);

    private static FirCrossoverDesign HighPassDesign(int rate = 48_000, int taps = 1_023) =>
        new(
            CrossoverKind.HighPass,
            Lr24At2500,
            Lr24At80,
            FirCrossoverMethod.IirMagnitude,
            FirWindow.Kaiser,
            8,
            taps,
            rate);

    private static VirtualCrossoverChannelSettings WithDesignedKernel(
        VirtualCrossoverChannelSettings settings, FirCrossoverDesign design)
    {
        settings.Fir = design.Build();
        settings.FirDesign = design;
        return settings;
    }

    // ----------------------------------------------------------- project file

    [Fact]
    public void TheDesign_TravelsBesideItsKernel_AndIsAbsentWithoutOne()
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            // A slope no hardware crossover carries: the session checks the design's
            // slopes against the constructor's list, not the IIR row's.
            FirCrossoverDesign design = HighPassDesign() with
            {
                Kind = CrossoverKind.BandPass,
                LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_500, 96),
                Window = FirWindow.Blackman
            };
            var original = new VirtualCrossoverProjectFile { DspProcessorFirFilters = true };
            WithDesignedKernel(original.Pairs[0].Left, design);
            original.Pairs[1].Left.Fir = new FirFilter([0.0, 1.0]);

            original.SaveTo(path);
            JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.Equal(design, loaded.Pairs[0].Left.FirDesign);
            Assert.True(loaded.Pairs[0].Left.HasFirCrossover);
            Assert.Equal(original.Pairs[0].Left.Fir!.Taps.ToArray(), loaded.Pairs[0].Left.Fir!.Taps.ToArray());
            Assert.Equal("BandPass", (string?)json["pairs"]![0]!["left"]!["firDesign"]!["kind"]);
            // An imported kernel has no design, and the file says nothing about one.
            Assert.Null(loaded.Pairs[1].Left.FirDesign);
            Assert.False(loaded.Pairs[1].Left.HasFirCrossover);
            Assert.Null(json["pairs"]![1]!["left"]!["firDesign"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("no kernel")]
    [InlineData("another length")]
    [InlineData("even taps")]
    public void ADesignThatDoesNotDescribeItsKernel_IsRefused(string defect)
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            var original = new VirtualCrossoverProjectFile { DspProcessorFirFilters = true };
            VirtualCrossoverChannelSettings side = WithDesignedKernel(original.Pairs[0].Left, HighPassDesign());
            original.SaveTo(path);
            JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
            JsonNode left = json["pairs"]![0]!["left"]!;
            switch (defect)
            {
                case "no kernel":
                    left.AsObject().Remove("fir");
                    break;
                case "another length":
                    left["firDesign"]!["tapCount"] = 1_021;
                    break;
                default:
                    left["firDesign"]!["tapCount"] = 1_024;
                    break;
            }

            File.WriteAllText(path, json.ToJsonString());

            Assert.Throws<InvalidDataException>(() => VirtualCrossoverProjectFile.LoadFrom(path));
            Assert.NotNull(side.FirDesign);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AWindowedSincDesign_LoadsWhateverFamilyAndSlopeItsUnusedBoxesHeld()
    {
        // Problem() does not read a sinc's family or slope, so the session may not
        // either: the constructor keeps whatever the greyed-out boxes held.
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            FirCrossoverDesign design = HighPassDesign() with
            {
                Method = FirCrossoverMethod.WindowedSinc,
                HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Chebyshev, 80, 18)
            };
            Assert.Null(design.Problem());
            var original = new VirtualCrossoverProjectFile { DspProcessorFirFilters = true };
            WithDesignedKernel(original.Pairs[0].Left, design);
            original.SaveTo(path);

            Assert.Equal(design, VirtualCrossoverProjectFile.LoadFrom(path).Pairs[0].Left.FirDesign);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ADeviceWithoutTheStage_TakesTheDesignWithTheKernel()
    {
        var project = new VirtualCrossoverProjectFile { DspProcessorFirFilters = false };
        WithDesignedKernel(project.Pairs[0].Left, HighPassDesign());

        Assert.Equal(1, project.ClearUnavailableFirFilters());
        Assert.Null(project.Pairs[0].Left.Fir);
        Assert.Null(project.Pairs[0].Left.FirDesign);
    }

    // ------------------------------------------------------ effective corners

    [Fact]
    public void TheIirCrossover_SpeaksFirst_AndAFirCrossoverWhereItIsOff()
    {
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 5_000, 12)
        };
        WithDesignedKernel(settings, HighPassDesign());

        // Both on: the IIR crossover is what the corners are read from.
        Assert.Equal(CrossoverKind.LowPass, settings.EffectiveCrossover.Kind);
        Assert.Equal(5_000, settings.EffectiveLowPassHz);
        Assert.Null(settings.EffectiveHighPassHz);

        // The IIR crossover off: the kernel's corners stand in, and the passband and
        // the junction frequency follow them.
        settings.CrossoverKind = CrossoverKind.Off;
        Assert.Equal(CrossoverKind.HighPass, settings.EffectiveCrossover.Kind);
        Assert.Equal(80, settings.EffectiveHighPassHz);
        Assert.Equal((80.0, 20_000.0), VirtualDspEqHandoff.PassbandFor(settings));
        var lower = new VirtualCrossoverChannelSettings();
        Assert.Equal(80, VirtualCrossoverJunctions.GetPairCrossoverHz(lower, settings));
        Assert.Equal((80.0, 20_000.0), VirtualCrossoverJunctions.GetChannelBand(settings));

        // The chain is never touched by it: the IIR stage stays off, the kernel is the FIR stage.
        DspChannelChain chain = settings.ToChain(VirtualCrossoverZone.Front);
        Assert.Equal(CrossoverKind.Off, chain.Crossover?.Kind ?? CrossoverKind.Off);
        Assert.Same(settings.Fir, chain.Fir);

        // An imported kernel is not a crossover, and lends no corners.
        settings.FirDesign = null;
        Assert.Equal(CrossoverKind.Off, settings.EffectiveCrossover.Kind);
        Assert.Null(VirtualDspEqHandoff.PassbandFor(settings));
    }

    [Fact]
    public void AFirCrossoverRunAtAnotherRate_CutsWhereTheRateMovesIt()
    {
        // Designed at 48 kHz, run by a 96 kHz processor: the same taps cut an octave
        // higher until the kernel is rebuilt, and the corners read say so.
        var settings = WithDesignedKernel(new VirtualCrossoverChannelSettings(), HighPassDesign());
        Assert.Equal(80, settings.EffectiveHighPassHz);

        settings.FirRunSampleRateHz = 96_000;
        Assert.Equal(160, settings.EffectiveHighPassHz);
        Assert.Equal((160.0, 20_000.0), VirtualDspEqHandoff.PassbandFor(settings));

        settings.FirRunSampleRateHz = 48_000;
        Assert.Equal(80, settings.EffectiveHighPassHz);
    }

    // ----------------------------------------------------------------- handoff

    [Fact]
    public void TheHandoff_CarriesTheSide_ItsDesign_AndTheCornersToStartFrom()
    {
        var channel = new VirtualCrossoverChannel("B");
        channel.Pair.Right.DisplayName = "Midbass";
        channel.Pair.Right.CrossoverKind = CrossoverKind.BandPass;
        channel.Pair.Right.HighPassEdge = Lr24At80;
        channel.Pair.Right.LowPassEdge = Lr24At2500;
        FirCrossoverDesign design = HighPassDesign(rate: 96_000);
        WithDesignedKernel(channel.Pair.Right, design);

        FirConstructorHandoffRequest request = FirConstructorHandoff.Build(channel, rightSide: true, 7, 48_000);

        Assert.Equal("Channel B — Midbass, right side", request.ChannelLabel);
        Assert.Same(channel.Pair.Right.Fir, request.Kernel);
        Assert.Same(channel.Pair.Right.Fir, request.Token.Kernel);
        Assert.Equal(design, request.Design);
        Assert.Equal(CrossoverKind.BandPass, request.SeedCrossover!.Kind);
        Assert.Equal(48_000, request.ProcessorSampleRateHz);
        Assert.Equal(7, request.Token.ProjectGeneration);
    }

    [Fact]
    public void AReturn_LandsTheKernelAndItsDesign_OnTheSideItWasTakenFrom()
    {
        var channel = new VirtualCrossoverChannel("B");
        channel.Pair.Left.Fir = new FirFilter([1.0]);
        channel.Pair.Left.FirSourceName = "old.wav";
        FirConstructorReturnToken token = FirConstructorHandoff.Build(channel, false, 3, 48_000).Token;
        FirCrossoverDesign design = HighPassDesign();
        FirFilter kernel = design.Build();

        Assert.True(FirConstructorHandoff.TryApplyReturn([channel], token, kernel, design, 3, 48_000, true));

        Assert.Same(kernel, channel.Pair.Left.Fir);
        Assert.Equal(design, channel.Pair.Left.FirDesign);
        Assert.Null(channel.Pair.Left.FirSourceName);
        Assert.Null(channel.Pair.Right.Fir);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("removed")]
    [InlineData("mono")]
    [InlineData("processor rate")]
    [InlineData("design rate")]
    [InlineData("kernel replaced")]
    [InlineData("stage gone")]
    public void AReturn_IsRefused_WhenTheSideIsNoLongerTheOneItWasTakenFrom(string change)
    {
        var channel = new VirtualCrossoverChannel("B");
        FirConstructorReturnToken token = FirConstructorHandoff.Build(channel, true, 3, 48_000).Token;
        FirCrossoverDesign design = HighPassDesign();
        long generation = 3;
        int rate = 48_000;
        bool stage = true;
        List<VirtualCrossoverChannel> channels = [channel];
        switch (change)
        {
            case "generation":
                generation = 4;
                break;
            case "removed":
                channels = [new VirtualCrossoverChannel("B")];
                break;
            case "mono":
                channel.Pair.Mono = true;
                break;
            case "processor rate":
                rate = 96_000;
                break;
            case "design rate":
                design = HighPassDesign(rate: 96_000);
                break;
            case "kernel replaced":
                channel.Pair.Right.Fir = new FirFilter([1.0]);
                break;
            default:
                stage = false;
                break;
        }

        FirFilter? before = channel.Pair.Right.Fir;
        Assert.False(FirConstructorHandoff.TryApplyReturn(
            channels, token, design.Build(), design, generation, rate, stage));
        Assert.Same(before, channel.Pair.Right.Fir);
        Assert.Null(channel.Pair.Right.FirDesign);
    }

    // -------------------------------------------------------------------- lock

    [Fact]
    public void TheLock_CarriesTheFirStage_AsOneUnit()
    {
        var pair = new VirtualCrossoverChannelPairSettings();
        pair.Right.InvertPolarity = true;
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        FirCrossoverDesign design = HighPassDesign();
        WithDesignedKernel(pair.Left, design);

        Assert.True(sideLock.Follow([pair], shownRight: false));
        Assert.Same(pair.Left.Fir, pair.Right.Fir);
        Assert.Equal(design, pair.Right.FirDesign);
        // Polarity is its own unit and was not touched.
        Assert.True(pair.Right.InvertPolarity);

        // A Clear on the shown side removes the crossover from both.
        pair.Left.Fir = null;
        pair.Left.FirDesign = null;

        Assert.True(sideLock.Follow([pair], shownRight: false));
        Assert.Null(pair.Right.Fir);
        Assert.Null(pair.Right.FirDesign);
    }

    [Fact]
    public void TheLock_NeverCarriesAnImportedKernel_NorWritesOverOne()
    {
        // Room or driver corrections differ between the sides of a car: the lock keeps
        // crossovers in step and leaves corrections exactly where they were imported.
        var pair = new VirtualCrossoverChannelPairSettings();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        // An import on the shown side stays there.
        var leftCorrection = new FirFilter([0.2, 1.0, 0.2]);
        pair.Left.Fir = leftCorrection;
        pair.Left.FirSourceName = "left-correction.wav";
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Null(pair.Right.Fir);

        // The hidden side has its own correction: a new import on the shown side, a
        // designed crossover there, and a Clear there all leave it alone.
        var rightCorrection = new FirFilter([0.3, 1.0, 0.3]);
        pair.Right.Fir = rightCorrection;
        pair.Right.FirSourceName = "right-correction.wav";
        sideLock.Remember([pair]);

        pair.Left.Fir = new FirFilter([0.1, 1.0, 0.1]);
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Same(rightCorrection, pair.Right.Fir);

        WithDesignedKernel(pair.Left, HighPassDesign());
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Same(rightCorrection, pair.Right.Fir);
        Assert.Null(pair.Right.FirDesign);

        pair.Left.Fir = null;
        pair.Left.FirDesign = null;
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Same(rightCorrection, pair.Right.Fir);
        Assert.Equal("right-correction.wav", pair.Right.FirSourceName);
    }

    [Fact]
    public void TheLock_DoesNotCarryAClear_OntoASideWithoutACrossover()
    {
        // The shown side's imported correction is cleared; the hidden side never had a
        // kernel, and there is no crossover to remove — nothing is written.
        var pair = new VirtualCrossoverChannelPairSettings();
        pair.Left.Fir = new FirFilter([0.2, 1.0, 0.2]);
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.Fir = null;

        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Null(pair.Right.Fir);
    }

    // ------------------------------------------------------------------- block

    [Fact]
    public void TheFirButton_IsRed_ForADesignAtAnotherRate_OrBesideAnIirCrossover_Only()
    {
        using var control = new VirtualCrossoverChannelControl { FirControlShown = true };
        control.ProcessorSampleRateHz = 48_000;
        FirCrossoverDesign design = HighPassDesign();

        control.SetFir(design.Build(), null, design);
        Assert.StartsWith("HP 80 Hz: ", control.FirInfoLabel.Text);
        Assert.Null(control.FirConflict);
        Assert.NotEqual(Resonalyze.Ui.UiPalette.WarningRed, control.FirButton.ForeColor);

        Assert.Contains($"{511 * 1_000.0 / 48_000:0.0} ms", control.FirInfoLabel.Text);

        // The processor moved: the kernel waits for a rebuild, and meanwhile the latency
        // read out is the one the same taps have at the new rate, not the design's.
        control.ProcessorSampleRateHz = 96_000;
        Assert.Contains("rebuild", control.FirConflict);
        Assert.Equal(Resonalyze.Ui.UiPalette.WarningRed, control.FirButton.ForeColor);
        Assert.Contains($"{511 * 1_000.0 / 96_000:0.0} ms", control.FirInfoLabel.Text);

        // Back at its rate, but beside an IIR crossover: cut twice.
        control.ProcessorSampleRateHz = 48_000;
        control.CrossoverKindComboBox.SelectedItem = CrossoverKind.LowPass;
        Assert.Contains("IIR crossover", control.FirConflict);
        Assert.Equal(Resonalyze.Ui.UiPalette.WarningRed, control.FirButton.ForeColor);

        // An imported kernel beside the same IIR crossover is an ordinary chain.
        control.SetFir(new FirFilter([1.0], 48_000), "room.wav");
        Assert.Null(control.FirConflict);
        Assert.NotEqual(Resonalyze.Ui.UiPalette.WarningRed, control.FirButton.ForeColor);
    }

    [Fact]
    public void TheSheet_DescribesADesignedKernel_AsItsCrossover()
    {
        var settings = WithDesignedKernel(new VirtualCrossoverChannelSettings(), HighPassDesign());

        string text = VirtualCrossoverSheet.DescribeFir(settings);

        Assert.StartsWith("Linear-phase high-pass 80 Hz Linkwitz-Riley 24 dB/oct", text);
        Assert.Contains("1023 taps at 48 kHz", text);
    }

    // ------------------------------------------------------------------- panel

    [Fact]
    public void Standalone_ThePanelDesignsFromItsControls_AndKeepsTheLengthOdd()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            Settle(panel);

            Assert.NotNull(panel.CurrentDesign);
            Assert.Equal(4_095, panel.CurrentKernel!.Length);
            Assert.False(panel.InVirtualDspHandoff);

            Field<DarkNumericUpDown>(panel, "numericTaps").Value = 2_000;
            Settle(panel);

            Assert.Equal(2_001, panel.CurrentDesign!.TapCount);
            Assert.Equal(2_001, panel.CurrentKernel!.Length);

            // A band-pass with its corners crossed cannot be built, and says why.
            Select(Field<DarkComboBox>(panel, "comboBoxType"), 2);
            Field<DarkNumericUpDown>(panel, "numericHighPassHz").Value = 3_000;
            Settle(panel);
            Assert.Null(panel.CurrentKernel);
            Assert.Contains("band-pass", Field<Label>(panel, "labelProblem").Text);
        });
    }

    [Fact]
    public void ABurstOfEdits_RebuildsInTheBackground_AndOnlyTheLastLands()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            Settle(panel);
            Button export = Field<Button>(panel, "buttonExport");
            DarkNumericUpDown taps = Field<DarkNumericUpDown>(panel, "numericTaps");

            // Three wheel steps inside one settle: the edit event returns at once, the
            // previous kernel stays on screen, and nothing may leave meanwhile.
            taps.Value = 8_191;
            taps.Value = 16_383;
            taps.Value = 1_023;
            Assert.True(panel.RebuildPending);
            Assert.Equal(4_095, panel.CurrentKernel!.Length);
            Assert.False(export.Enabled);

            Settle(panel);

            Assert.Equal(1_023, panel.CurrentKernel!.Length);
            Assert.Equal(1_023, panel.CurrentDesign!.TapCount);
            Assert.True(export.Enabled);
        });
    }

    [Fact]
    public void AHandoff_RebuildsADesignAtTheProcessorsRate_AndReturnsIt()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            Settle(panel);
            var channel = new VirtualCrossoverChannel("B");
            WithDesignedKernel(channel.Pair.Left, HighPassDesign(rate: 96_000, taps: 2_047));
            FirConstructorHandoffRequest request = FirConstructorHandoff.Build(channel, false, 1, 48_000);
            FirCrossoverDesign? returned = null;
            panel.ReturnFirRequested = (_, _, design) => returned = design;

            panel.BeginVirtualDspHandoff(request);
            Settle(panel);

            Assert.True(panel.InVirtualDspHandoff);
            Assert.Equal(HighPassDesign(rate: 48_000, taps: 2_047), panel.CurrentDesign);
            Assert.False(Field<DarkComboBox>(panel, "comboBoxSampleRate").Enabled);
            Assert.Contains("rebuilt", Field<Label>(panel, "labelSession").Text);

            Field<Button>(panel, "buttonReturnToDsp").PerformClick();
            Assert.Equal(panel.CurrentDesign, returned);

            panel.EndVirtualDspHandoff();
            Settle(panel);
            Assert.False(panel.InVirtualDspHandoff);
            Assert.True(Field<DarkComboBox>(panel, "comboBoxSampleRate").Enabled);
        });
    }

    [Fact]
    public void AHandoff_KeepsTheStandaloneWorkAside_AndPutsItBackWhenTheSessionEnds()
    {
        StaTest.Run(() =>
        {
            // A band-pass designed on its own at 96 kHz, never exported: opening a
            // channel, twice, the second replacing the first, must not cost it.
            using var panel = new FirConstructorPanel();
            Select(Field<DarkComboBox>(panel, "comboBoxType"), 2);
            Field<DarkNumericUpDown>(panel, "numericHighPassHz").Value = 250;
            Field<DarkNumericUpDown>(panel, "numericLowPassHz").Value = 3_000;
            Field<DarkNumericUpDown>(panel, "numericTaps").Value = 2_047;
            Select(Field<DarkComboBox>(panel, "comboBoxSampleRate"), 3);
            Settle(panel);
            FirCrossoverDesign standalone = panel.CurrentDesign!;
            Assert.Equal(96_000, standalone.SampleRateHz);

            var first = new VirtualCrossoverChannel("B");
            WithDesignedKernel(first.Pair.Left, HighPassDesign());
            panel.BeginVirtualDspHandoff(FirConstructorHandoff.Build(first, false, 1, 48_000));
            Settle(panel);
            var second = new VirtualCrossoverChannel("C");
            panel.BeginVirtualDspHandoff(FirConstructorHandoff.Build(second, true, 1, 48_000));
            Settle(panel);
            Assert.NotEqual(standalone, panel.CurrentDesign);

            panel.EndVirtualDspHandoff();
            Settle(panel);

            Assert.Equal(standalone, panel.CurrentDesign);
            Assert.Contains("Standalone", Field<Label>(panel, "labelSession").Text);

            // And a bare kernel the constructor was showing comes back as it was.
            using var bare = new FirConstructorPanel();
            Settle(bare);
            var file = new FirFilter([0.25, 0.5, 0.25], 48_000);
            Invoke(bare, "ShowBareKernel", file, "room.wav");
            bare.BeginVirtualDspHandoff(FirConstructorHandoff.Build(first, false, 1, 48_000));
            Settle(bare);
            bare.EndVirtualDspHandoff();
            Settle(bare);
            Assert.Same(file, bare.CurrentKernel);
            Assert.Null(bare.CurrentDesign);
        });
    }

    [Fact]
    public void AHandoffOfAnImportedKernel_ShowsItAsItIs_UntilAControlIsTouched()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            var channel = new VirtualCrossoverChannel("B");
            channel.Pair.Left.Fir = new FirFilter([0.25, 0.5, 0.25], 48_000);
            channel.Pair.Left.FirSourceName = "room.wav";
            panel.BeginVirtualDspHandoff(FirConstructorHandoff.Build(channel, false, 1, 48_000));
            Settle(panel);

            Assert.Same(channel.Pair.Left.Fir, panel.CurrentKernel);
            Assert.Null(panel.CurrentDesign);
            // A bare kernel is not a design, so there is nothing to return.
            Button returnButton = Field<Button>(panel, "buttonReturnToDsp");
            Assert.False(returnButton.Enabled);

            Field<DarkNumericUpDown>(panel, "numericTaps").Value = 511;
            Settle(panel);

            Assert.NotNull(panel.CurrentDesign);
            Assert.NotSame(channel.Pair.Left.Fir, panel.CurrentKernel);
            Assert.True(returnButton.Enabled);
        });
    }

    // Pumps the STA thread until the panel's background rebuild has landed: its
    // continuations are posted to this thread's WinForms synchronization context.
    private static void Settle(FirConstructorPanel panel)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (panel.RebuildPending)
        {
            Assert.True(DateTime.UtcNow < deadline, "The constructor's rebuild never landed.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static void Invoke(object owner, string method, params object?[] arguments) =>
        owner.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(owner, arguments);

    // ---------------------------------------------------------------- helpers

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner)!;

    private static void Select(DarkComboBox combo, int index) => combo.SelectedIndex = index;

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "resonalyze-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
