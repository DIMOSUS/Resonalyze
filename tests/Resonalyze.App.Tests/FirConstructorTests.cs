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

        // An import on the shown side replaces the kernel AND drops the design, and
        // the hidden side follows both.
        pair.Left.Fir = new FirFilter([1.0]);
        pair.Left.FirSourceName = "room.wav";
        pair.Left.FirDesign = null;

        Assert.True(sideLock.Follow([pair], shownRight: false));
        Assert.Same(pair.Left.Fir, pair.Right.Fir);
        Assert.Equal("room.wav", pair.Right.FirSourceName);
        Assert.Null(pair.Right.FirDesign);
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

        // The processor moved: the kernel waits for a rebuild.
        control.ProcessorSampleRateHz = 96_000;
        Assert.Contains("rebuild", control.FirConflict);
        Assert.Equal(Resonalyze.Ui.UiPalette.WarningRed, control.FirButton.ForeColor);

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

            Assert.NotNull(panel.CurrentDesign);
            Assert.Equal(4_095, panel.CurrentKernel!.Length);
            Assert.False(panel.InVirtualDspHandoff);

            Field<DarkNumericUpDown>(panel, "numericTaps").Value = 2_000;

            Assert.Equal(2_001, panel.CurrentDesign!.TapCount);
            Assert.Equal(2_001, panel.CurrentKernel!.Length);

            // A band-pass with its corners crossed cannot be built, and says why.
            Select(Field<DarkComboBox>(panel, "comboBoxType"), 2);
            Field<DarkNumericUpDown>(panel, "numericHighPassHz").Value = 3_000;
            Assert.Null(panel.CurrentKernel);
            Assert.Contains("band-pass", Field<Label>(panel, "labelProblem").Text);
        });
    }

    [Fact]
    public void AHandoff_RebuildsADesignAtTheProcessorsRate_AndReturnsIt()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            var channel = new VirtualCrossoverChannel("B");
            WithDesignedKernel(channel.Pair.Left, HighPassDesign(rate: 96_000, taps: 2_047));
            FirConstructorHandoffRequest request = FirConstructorHandoff.Build(channel, false, 1, 48_000);
            FirCrossoverDesign? returned = null;
            panel.ReturnFirRequested = (_, _, design) => returned = design;

            panel.BeginVirtualDspHandoff(request);

            Assert.True(panel.InVirtualDspHandoff);
            Assert.Equal(HighPassDesign(rate: 48_000, taps: 2_047), panel.CurrentDesign);
            Assert.False(Field<DarkComboBox>(panel, "comboBoxSampleRate").Enabled);
            Assert.Contains("rebuilt", Field<Label>(panel, "labelSession").Text);

            Field<Button>(panel, "buttonReturnToDsp").PerformClick();
            Assert.Equal(panel.CurrentDesign, returned);

            panel.EndVirtualDspHandoff();
            Assert.False(panel.InVirtualDspHandoff);
            Assert.True(Field<DarkComboBox>(panel, "comboBoxSampleRate").Enabled);
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

            Assert.Same(channel.Pair.Left.Fir, panel.CurrentKernel);
            Assert.Null(panel.CurrentDesign);
            // A bare kernel is not a design, so there is nothing to return.
            Button returnButton = Field<Button>(panel, "buttonReturnToDsp");
            Assert.True(returnButton.Visible || !panel.Visible);
            Assert.False(returnButton.Enabled);

            Field<DarkNumericUpDown>(panel, "numericTaps").Value = 511;

            Assert.NotNull(panel.CurrentDesign);
            Assert.NotSame(channel.Pair.Left.Fir, panel.CurrentKernel);
            Assert.True(returnButton.Enabled);
        });
    }

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
