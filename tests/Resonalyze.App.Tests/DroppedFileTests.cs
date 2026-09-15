using System.Drawing;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Pinned against the app's own writers, so a moved marker cannot silently route a file to the wrong loader.</summary>
public sealed class DroppedFileTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory(
        "resonalyze-dropped-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public async Task AnImpulseResponseIsReadFromWhatItsOwnWriterWrote()
    {
        string path = Path.Combine(directory, "measurement.json");
        var file = new ImpulseResponseFile
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            SampleRate = 48_000,
            Bits = 24,
            Octaves = 10,
            SweepDurationSeconds = 1.5,
            SweepDeconvolutionPeakIndex = 1,
            SweepDeconvolutionRealSamples = [0.125, 1.0, -0.25, 0.0]
        };
        await file.SaveAsync(path);

        Assert.Equal(DroppedFileKind.ImpulseResponse, DroppedFile.Classify(path));
    }

    [Fact]
    public void ASpatialAverageCaptureIsReadFromWhatItsOwnWriterWrote()
    {
        string path = Path.Combine(directory, "capture.json");
        BuildCapture().Save(path);

        Assert.Equal(DroppedFileKind.SpatialAverageCapture, DroppedFile.Classify(path));
    }

    [Fact]
    public void AVirtualDspSessionIsReadFromWhatItsOwnWriterWrote()
    {
        string path = Path.Combine(directory, "session.json");
        new VirtualCrossoverProjectFile().SaveTo(path);

        Assert.Equal(DroppedFileKind.VirtualDspSession, DroppedFile.Classify(path));
    }

    [Fact]
    public void AnOverlaySlotIsRecognizedSoItCanBeRefusedByName()
    {
        new OverlayFile
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Mode = Mode.FrequencyResponse,
            Slot = 2,
            Title = "left tweeter",
            ColorArgb = Color.Orange.ToArgb(),
            Points = [new OverlayPoint(20, 80), new OverlayPoint(20_000, 70)]
        }.Save(directory);

        Assert.Equal(
            DroppedFileKind.OverlaySlot,
            DroppedFile.Classify(OverlayFile.GetPath(Mode.FrequencyResponse, 2, directory)));
    }

    [Fact]
    public void TheMarkerIsSniffedRatherThanTheDocumentParsed()
    {
        // Only the head is read: a truncated IR is still an IR.
        string path = Path.Combine(directory, "truncated.json");
        File.WriteAllText(
            path,
            "{\r\n  \"format\": \"resonalyze-impulse-response\",\r\n  \"version\": 7,\r\n" +
            "  \"sweepDeconvolutionRealSamples\": [0.1, 0.2, 0.3");

        Assert.Equal(DroppedFileKind.ImpulseResponse, DroppedFile.Classify(path));
    }

    [Fact]
    public void AByteOrderMarkDoesNotHideTheFormat()
    {
        string path = Path.Combine(directory, "bom.json");
        File.WriteAllText(
            path,
            "{\"format\": \"resonalyze-virtual-crossover\", \"version\": 8}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(DroppedFileKind.VirtualDspSession, DroppedFile.Classify(path));
    }

    [Fact]
    public void AFormatNestedInsideTheDocumentDoesNotNameIt()
    {
        string path = Path.Combine(directory, "nested.json");
        File.WriteAllText(
            path,
            "{\"recipe\": {\"format\": \"resonalyze-live-capture\"}, " +
            "\"format\": \"resonalyze-impulse-response\"}");

        Assert.Equal(DroppedFileKind.ImpulseResponse, DroppedFile.Classify(path));
    }

    [Theory]
    [InlineData("declared", "{\"format\": \"something-else\"}")]
    [InlineData("undeclared", "{\"version\": 7}")]
    [InlineData("array", "[1, 2, 3]")]
    [InlineData("prose", "not json at all")]
    [InlineData("empty", "")]
    public void JsonThatDeclaresNothingWeWriteIsNotOpened(string name, string content)
    {
        string path = Path.Combine(directory, $"foreign-{name}.json");
        File.WriteAllText(path, content);

        Assert.Equal(DroppedFileKind.Unknown, DroppedFile.Classify(path));
    }

    [Fact]
    public void AMissingFileIsSimplyNotOurs()
    {
        Assert.Equal(
            DroppedFileKind.Unknown,
            DroppedFile.Classify(Path.Combine(directory, "gone.json")));
    }

    [Theory]
    [InlineData("sweep.wav")]
    [InlineData("SWEEP.WAV")]
    public void ARecordedSweepIsTakenAtItsExtension(string name)
    {
        // The sweep and REW importers diagnose the file themselves; the file need not exist for this answer.
        Assert.Equal(
            DroppedFileKind.RecordedSweep,
            DroppedFile.Classify(Path.Combine(directory, name)));
    }

    [Fact]
    public void ATextFileIsTakenAsTheRewExportTheLoadDialogTakesItFor() =>
        Assert.Equal(
            DroppedFileKind.RewImpulseResponseExport,
            DroppedFile.Classify(Path.Combine(directory, "rew-export.txt")));

    [Theory]
    [InlineData("notes.pdf")]
    [InlineData("no-extension")]
    public void AnExtensionTheShellNeverOpensIsNotOurs(string name) =>
        Assert.Equal(
            DroppedFileKind.Unknown,
            DroppedFile.Classify(Path.Combine(directory, name)));

    [Theory]
    [InlineData("measurement.json", true)]
    [InlineData("sweep.wav", true)]
    [InlineData("export.TXT", true)]
    [InlineData("photo.png", false)]
    public void TheHoverTestStopsAtTheExtension(string name, bool openable)
    {
        // Asked on every mouse move during a drag, so it must not touch the disk.
        Assert.Equal(openable, DroppedFile.HasOpenableExtension(name));
    }

    private static LiveCaptureDocument BuildCapture()
    {
        const int sampleRate = 48_000;
        const int sequenceLength = 32_768;
        var spectrum = new double[sequenceLength / 2 + 1];
        Array.Fill(spectrum, -60.0);
        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "left tweeter",
            SpectrumDb = spectrum,
            CurveDb = new double[LiveCaptureDocument.CurvePointCount],
            GridStartHz = 20,
            GridStopHz = 20_000,
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = LiveAnalysisMode.Mmm,
                SampleRateHz = sampleRate,
                SequenceLength = sequenceLength,
                FrameMilliseconds = 1000.0 * sequenceLength / sampleRate,
                WindowType = WindowType.Rectangular,
                WindowEnbwBins = Windowing.EquivalentNoiseBandwidthBins(
                    WindowType.Rectangular, sequenceLength),
                WindowMainLobeBins = Windowing.MainLobeWidthBins(WindowType.Rectangular),
                AveragedFrameCount = 130,
                IntegratedSeconds = 130.0 * sequenceLength / sampleRate,
                SlopeCompensation = true,
                MagnitudeScale = MagnitudeScale.SoundPressureLevel
            }
        };
    }
}
