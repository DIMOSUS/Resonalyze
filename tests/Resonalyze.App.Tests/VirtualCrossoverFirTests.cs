using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The FIR stage outside the DSP library: how a session carries a kernel INSIDE
/// itself and drops it for a device without the stage, how the render cache tells
/// kernels apart, how the files are imported and exported, what the sheet prints,
/// and the channel block's FIR row.
/// </summary>
public sealed class VirtualCrossoverFirTests
{
    // ----------------------------------------------------------- project file

    [Fact]
    public void SaveToAndLoadFrom_CarryTheKernelItself_ToTheBit()
    {
        // The kernel is the tune's, like the PEQ bands: the session file holds the
        // taps, not a path, so it travels whole and nothing has to be found again.
        // Float64 in the file — a designed number comes back exactly.
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            var random = new Random(42);
            double[] taps = Enumerable.Range(0, 4_096).Select(_ => random.NextDouble() * 2 - 1).ToArray();
            taps[7] = 1e-17;
            var original = new VirtualCrossoverProjectFile { DspProcessorFirFilters = true };
            original.Pairs[0].Left.Fir = new FirFilter(taps, 48_000);
            original.Pairs[0].Left.FirSourceName = "left mid.wav";
            original.Pairs[2].Right.Fir = new FirFilter([0.0, 1.0]);

            original.SaveTo(path);
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            FirFilter left = loaded.Pairs[0].Left.Fir!;
            Assert.Equal(taps, left.Taps.ToArray());
            Assert.Equal(48_000, left.DeclaredSampleRateHz);
            Assert.Equal("left mid.wav", loaded.Pairs[0].Left.FirSourceName);
            // A kernel without a declared rate and without a name is still a kernel.
            Assert.Equal(new[] { 0.0, 1.0 }, loaded.Pairs[2].Right.Fir!.Taps.ToArray());
            Assert.Null(loaded.Pairs[2].Right.Fir!.DeclaredSampleRateHz);
            Assert.Null(loaded.Pairs[2].Right.FirSourceName);
            Assert.True(loaded.DspProcessorFirFilters);
            // Additive: a side without a kernel carries none.
            Assert.Null(loaded.Pairs[1].Left.Fir);
            Assert.False(loaded.Pairs[1].Left.HasFir);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AProjectWithoutFir_SerializesNoFirFields()
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            new VirtualCrossoverProjectFile().SaveTo(path);

            string json = File.ReadAllText(path);
            Assert.DoesNotContain("\"fir\"", json);
            Assert.DoesNotContain("firSourceName", json);
            Assert.DoesNotContain("dspProcessorFirFilters", json);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheKernelInTheFile_IsAnObjectOfDeclaredRateAndBase64Taps()
    {
        // The wire shape, pinned: a hand-edited or foreign writer has to know it,
        // and a garbled block is refused rather than read as some other kernel.
        var settings = new VirtualCrossoverChannelSettings
        {
            Fir = new FirFilter([0.5, -0.25], 96_000)
        };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        JsonNode node = JsonNode.Parse(JsonSerializer.Serialize(settings, options))!;
        JsonNode fir = node["fir"]!;

        Assert.Equal(96_000, (int)fir["sampleRateHz"]!);
        // Two float64 little-endian: 0.5 = 00 00 00 00 00 00 E0 3F, −0.25 = ... D0 BF.
        byte[] bytes = Convert.FromBase64String((string)fir["taps"]!);
        Assert.Equal(16, bytes.Length);
        Assert.Equal(0.5, BitConverter.ToDouble(bytes, 0));
        Assert.Equal(-0.25, BitConverter.ToDouble(bytes, 8));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<VirtualCrossoverChannelSettings>(
            """{"fir":{"taps":"AAAA"}}""", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<VirtualCrossoverChannelSettings>(
            """{"fir":{"sampleRateHz":48000}}""", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<VirtualCrossoverChannelSettings>(
            """{"fir":{"taps":""}}""", options));
    }

    [Fact]
    public void LoadFrom_AProcessorWithoutAFirStage_RemovesTheKernels_AndSaysSo()
    {
        // The invariant: a kernel in the file means a device that convolves. A hand-
        // edited session carrying one without the switch loses it where the user can
        // see, not quietly on the way past.
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            var original = new VirtualCrossoverProjectFile();
            original.Pairs[0].Left.Fir = new FirFilter([1.0]);
            original.Pairs[0].Left.FirSourceName = "left.fir";
            original.Pairs[2].Right.Fir = new FirFilter([0.0, 1.0]);
            original.SaveTo(path);

            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.Null(loaded.Pairs[0].Left.Fir);
            Assert.Null(loaded.Pairs[0].Left.FirSourceName);
            Assert.Null(loaded.Pairs[2].Right.Fir);
            Assert.Contains("2 channel sides carried a FIR filter", loaded.MigrationNoticeText);

            // With the switch on, the same file keeps them and says nothing.
            original.DspProcessorFirFilters = true;
            original.SaveTo(path);
            VirtualCrossoverProjectFile kept = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.Equal(new[] { 1.0 }, kept.Pairs[0].Left.Fir!.Taps.ToArray());
            Assert.Equal("left.fir", kept.Pairs[0].Left.FirSourceName);
            Assert.Null(kept.MigrationNoticeText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_AV10Project_OpensAsTheCurrentVersion_WithNoFir()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            new VirtualCrossoverProjectFile().Save(root);
            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 10;
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(11, VirtualCrossoverProjectFile.CurrentVersion);
            Assert.Equal(VirtualCrossoverProjectFile.CurrentVersion, loaded.Version);
            Assert.False(loaded.ResolveDspFirFilters());
            Assert.All(loaded.Pairs, pair => Assert.False(pair.Left.HasFir));
            loaded.Validate();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToChain_CarriesTheKernelByReference()
    {
        var kernel = new FirFilter([0.0, 1.0]);
        var settings = new VirtualCrossoverChannelSettings { Fir = kernel, FirSourceName = "a.fir" };

        Assert.Same(kernel, settings.ToChain(VirtualCrossoverZone.Front).Fir);
        Assert.Null(new VirtualCrossoverChannelSettings().ToChain(VirtualCrossoverZone.Front).Fir);
    }

    [Fact]
    public void ClearUnavailableFirFilters_DoesNothingWhileTheDeviceConvolves()
    {
        var project = new VirtualCrossoverProjectFile { DspProcessorFirFilters = true };
        project.Pairs[0].Left.Fir = new FirFilter([1.0]);
        project.Pairs[0].Left.FirSourceName = "a.fir";

        Assert.Equal(0, project.ClearUnavailableFirFilters());
        Assert.NotNull(project.Pairs[0].Left.Fir);

        project.DspProcessorFirFilters = false;

        Assert.Equal(1, project.ClearUnavailableFirFilters());
        Assert.Null(project.Pairs[0].Left.Fir);
        Assert.Null(project.Pairs[0].Left.FirSourceName);
    }

    // -------------------------------------------------------------- the cache

    [Fact]
    public void ChainCacheKey_TellsKernelsApartByInstance()
    {
        // The kernel's equality is reference equality (see FirFilter): the same loaded
        // instance is the same render, a re-imported file is a new one — and a chain
        // without a kernel is not a chain with one.
        var kernel = new FirFilter([0.0, 1.0]);
        var sameTaps = new FirFilter([0.0, 1.0]);
        var key = new DspChannelChainCacheKey(new DspChannelChain(Fir: kernel));

        Assert.Equal(key, new DspChannelChainCacheKey(new DspChannelChain(Fir: kernel)));
        Assert.Equal(
            key.GetHashCode(),
            new DspChannelChainCacheKey(new DspChannelChain(Fir: kernel)).GetHashCode());
        Assert.NotEqual(key, new DspChannelChainCacheKey(new DspChannelChain(Fir: sameTaps)));
        Assert.NotEqual(key, new DspChannelChainCacheKey(DspChannelChain.Identity));
    }

    // -------------------------------------------------------------- the files

    [Fact]
    public void Load_ReadsAWavsFirstChannel_AndKeepsItsRateAsTheDeclaredOne()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(root, "kernel.wav");
            float[] left = [0f, 0.5f, -0.25f, 0.125f];
            float[] right = [0.9f, 0.9f, 0.9f, 0.9f];
            AudioFileCodec.WriteWav(path, new AudioFileContent([left, right], 96_000));

            FirFilter fir = FirFilterFiles.Load(path);

            Assert.Equal(4, fir.Length);
            Assert.Equal(96_000, fir.DeclaredSampleRateHz);
            // 24-bit PCM: exact to well under a millionth.
            for (int index = 0; index < left.Length; index++)
            {
                Assert.Equal(left[index], fir.Taps[index], 5);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsTextUnderEitherExtension()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string firPath = Path.Combine(root, "kernel.fir");
            string txtPath = Path.Combine(root, "kernel.txt");
            File.WriteAllText(firPath, "0\r\n1\r\n0.5\r\n");
            File.WriteAllText(txtPath, "// rePhase\n1\n");

            Assert.Equal(new[] { 0.0, 1.0, 0.5 }, FirFilterFiles.Load(firPath).Taps.ToArray());
            Assert.Equal(new[] { 1.0 }, FirFilterFiles.Load(txtPath).Taps.ToArray());
            Assert.Null(FirFilterFiles.Load(firPath).DeclaredSampleRateHz);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_WritesAFloatWav_ThatImportsBackWithTheProcessorsRate_GainAndAll()
    {
        // Float, not 24-bit PCM: a kernel with gain has taps past ±1, and integer
        // PCM would clip them into another filter. The rate written is the one the
        // taps mean here — the processor's — whatever the import file declared.
        string root = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(root, "out.wav");
            double[] taps = [0.0, 1.5, -2.25, 0.125, 1e-7];
            var kernel = new FirFilter(taps, 44_100);

            FirFilterFiles.Save(path, kernel, 96_000, "designed.wav");
            FirFilter back = FirFilterFiles.Load(path);

            Assert.Equal(96_000, back.DeclaredSampleRateHz);
            Assert.Equal(taps.Length, back.Length);
            for (int index = 0; index < taps.Length; index++)
            {
                Assert.Equal((float)taps[index], back.Taps[index]);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("out.txt")]
    [InlineData("out.fir")]
    public void Save_WritesText_ThatImportsBackToTheBit_WithTheRateInItsHeader(string name)
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(root, name);
            var random = new Random(9);
            double[] taps = Enumerable.Range(0, 300).Select(_ => random.NextDouble() * 4 - 2).ToArray();
            taps[3] = 1e-300;
            var kernel = new FirFilter(taps);

            FirFilterFiles.Save(path, kernel, 48_000, "left mid.wav");
            FirFilter back = FirFilterFiles.Load(path);

            Assert.Equal(taps, back.Taps.ToArray());
            Assert.Equal(48_000, back.DeclaredSampleRateHz);
            string text = File.ReadAllText(path);
            Assert.StartsWith("* FIR filter exported by Resonalyze (imported from left mid.wav)", text);
            Assert.Contains("* Sample rate: 48000", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // -------------------------------------------------------------- the sheet

    [Fact]
    public void FormatText_PrintsTheKernel_OnlyWhereOneIsCarried()
    {
        var project = new VirtualCrossoverProjectFile { DspProcessorFirFilters = true };
        project.Pairs[0].Mono = true;
        project.Pairs[0].Left.DisplayName = "woofer.json";
        project.Pairs[0].Left.SourceFilePath = @"C:\m\woofer.json";
        project.Pairs[1].Left.DisplayName = "mid.json";
        project.Pairs[1].Left.SourceFilePath = @"C:\m\mid.json";
        project.Pairs[1].Right.DisplayName = "mid R.json";
        project.Pairs[1].Right.SourceFilePath = @"C:\m\mid R.json";

        Assert.DoesNotContain("FIR", VirtualCrossoverSheet.FormatText(project, null));

        project.Pairs[1].Left.Fir = new FirFilter(new double[4_096]);
        project.Pairs[1].Left.FirSourceName = "left mid.wav";
        // A kernel that arrived without a name (a hand-edited session) is still
        // printed: it is in the tune.
        project.Pairs[1].Right.Fir = new FirFilter(new double[2_048]);

        string sheet = VirtualCrossoverSheet.FormatText(project, null);

        Assert.Contains("FIR        left mid.wav (4096 taps)", sheet);
        Assert.Contains("FIR        FIR (2048 taps)", sheet);
    }

    // -------------------------------------------------------------- the block

    [Fact]
    public void WithoutTheStage_TheFirRowIsHidden_AndShowingItAddsOneRow()
    {
        using var control = new VirtualCrossoverChannelControl();
        int without = control.Height;
        int rowPitch = control.PhaseInput.Top - control.PeqMenuButton.Top;

        Assert.False(control.FirControlShown);
        Assert.False(control.FirButton.Visible);
        Assert.False(control.FirLabel.Visible);
        Assert.False(control.FirInfoLabel.Visible);

        control.FirControlShown = true;

        Assert.Equal(without + rowPitch, control.Height);
        Assert.True(control.FirButton.Visible);
        Assert.True(control.FirButton.Bottom <= control.ClientSize.Height);
        Assert.Equal(control.Height, control.MinimumSize.Height);
        Assert.Equal(control.Height, control.MaximumSize.Height);

        control.FirControlShown = false;

        Assert.Equal(without, control.Height);
    }

    [Fact]
    public void TheFirRow_TakesThePhaseRowsPlaceWhenThatRowIsHidden()
    {
        // A block with FIR and no phase control must not carry an empty row between
        // the PEQ and the FIR; with both, the FIR row sits under the phase row.
        using var control = new VirtualCrossoverChannelControl { FirControlShown = true };

        Assert.Equal(control.PhaseInput.Top, control.FirButton.Top);
        Assert.Equal(control.PhaseLabel.Top, control.FirLabel.Top);

        control.PhaseControlShown = true;
        int rowPitch = control.PhaseInput.Top - control.PeqMenuButton.Top;

        Assert.Equal(control.PhaseInput.Top + rowPitch, control.FirButton.Top);
        Assert.True(control.FirButton.Top >= control.PhaseInput.Bottom);
        Assert.True(control.FirButton.Bottom <= control.ClientSize.Height);

        control.PhaseControlShown = false;

        Assert.Equal(control.PhaseInput.Top, control.FirButton.Top);
        Assert.True(control.FirButton.Bottom <= control.ClientSize.Height);
    }

    [Fact]
    public void BothRows_AddTwoRows_AndFoldToTheSameBlockAsNone()
    {
        using var none = new VirtualCrossoverChannelControl();
        using var both = new VirtualCrossoverChannelControl
        {
            PhaseControlShown = true,
            FirControlShown = true
        };
        int rowPitch = none.PhaseInput.Top - none.PeqMenuButton.Top;

        Assert.Equal(none.Height + 2 * rowPitch, both.Height);
        Assert.True(both.FirButton.Bottom <= both.ClientSize.Height);

        none.Collapsed = true;
        both.Collapsed = true;

        Assert.Equal(none.Height, both.Height);
        Assert.False(both.FirButton.Visible);
    }

    [Fact]
    public void ScalingWithBothRows_KeepsTheFirRowInsideTheBlock()
    {
        using var control = new VirtualCrossoverChannelControl
        {
            PhaseControlShown = true,
            FirControlShown = true
        };

        control.Scale(new SizeF(1.5f, 1.5f));

        Assert.True(control.FirButton.Bottom <= control.ClientSize.Height);
        Assert.True(control.FirButton.Top >= control.PhaseInput.Bottom);
        Assert.Equal(control.Height, control.MaximumSize.Height);

        control.PhaseControlShown = false;

        Assert.Equal(control.PhaseInput.Top, control.FirButton.Top);
        Assert.True(control.FirButton.Bottom <= control.ClientSize.Height);
    }

    [Fact]
    public void TheReadout_NamesTheSource_TheKernel_AndARateThatIsNotTheProcessors()
    {
        using var control = new VirtualCrossoverChannelControl { FirControlShown = true };
        control.ProcessorSampleRateHz = 96_000;

        Assert.Equal("Import…", control.FirButton.Text);
        Assert.Equal("off", control.FirInfoLabel.Text);

        // 4096 taps peaking at tap 2048: 21.3 ms at 96 kHz.
        var taps = new double[4_096];
        taps[2_048] = 1.0;
        control.SetFir(new FirFilter(taps, 96_000), "left mid.wav");

        Assert.Equal("left mid.wav", control.FirButton.Text);
        Assert.Contains("4096 taps", control.FirInfoLabel.Text);
        Assert.Contains("21", control.FirInfoLabel.Text);
        Assert.NotEqual(Resonalyze.Ui.UiPalette.WarningAmber, control.FirInfoLabel.ForeColor);

        // The file stated a rate the processor does not run at: the taps are used as
        // they are, and the readout says so in amber.
        control.SetFir(new FirFilter(taps, 48_000), "left mid.wav");

        Assert.Contains("48 kHz", control.FirInfoLabel.Text);
        Assert.Contains("96 kHz", control.FirInfoLabel.Text);
        Assert.Equal(Resonalyze.Ui.UiPalette.WarningAmber, control.FirInfoLabel.ForeColor);

        // A kernel without a source name is still shown as a kernel.
        control.SetFir(new FirFilter(taps), null);

        Assert.Equal("FIR", control.FirButton.Text);
        Assert.Contains("4096 taps", control.FirInfoLabel.Text);

        control.SetFir(null, "stale name");

        Assert.Equal("Import…", control.FirButton.Text);
        Assert.Equal("off", control.FirInfoLabel.Text);
    }

    [Fact]
    public void TheFirButton_RaisesItsOwnEvent_NotTheSettingsOne()
    {
        using var control = new VirtualCrossoverChannelControl { FirControlShown = true };
        int fir = 0;
        int settings = 0;
        control.FirClicked += (_, _) => fir++;
        control.SettingsChanged += (_, _) => settings++;

        control.FirButton.PerformClick();

        Assert.Equal(1, fir);
        Assert.Equal(0, settings);
    }

    // ---------------------------------------------------------------- helpers

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "resonalyze-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
