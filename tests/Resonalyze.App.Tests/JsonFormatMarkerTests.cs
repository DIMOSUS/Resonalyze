using System.Text;

namespace Resonalyze.App.Tests;

/// <summary>
/// Every reader of a Resonalyze JSON document asks this before it opens one, and what
/// it must be is two things at once: cheap enough to run on a file it is about to
/// decline — an impulse response is tens of megabytes, and being asked "are you a
/// capture?" used to parse all of it — and as well informed as the deserializer it
/// stands in front of, since a wrong answer routes a measurement to the wrong loader.
/// </summary>
public sealed class JsonFormatMarkerTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory(
        "resonalyze-marker-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void TheFirstPropertyIsWhereOurDocumentsCarryIt() =>
        Assert.Equal(
            "resonalyze-impulse-response",
            Marker("{\"format\": \"resonalyze-impulse-response\", \"version\": 7}"));

    [Fact]
    public void AMarkerBeyondTheFirstChunkIsStillFound()
    {
        // Our writers put it first, but a document that has been through another tool
        // — re-serialized with its keys sorted, say — can carry it past any fixed
        // window into the file. The walk therefore continues chunk by chunk rather
        // than giving up at a cut-off, which is what keeps this exactly as informed
        // as the full parse it replaces.
        var json = new StringBuilder("{\"curveDb\": [");
        json.AppendJoin(", ", Enumerable.Range(0, 40_000));
        json.Append("], \"format\": \"resonalyze-live-capture\"}");

        Assert.True(json.Length > 128 * 1024, $"only {json.Length} characters");
        Assert.Equal("resonalyze-live-capture", Marker(json.ToString()));
    }

    [Fact]
    public void AMarkerSplitAcrossAChunkBoundaryIsReadWhole()
    {
        // The property name can land at the end of one chunk and its value in the
        // next. The reader's state crosses with it; a walk that started each chunk
        // fresh would lose the value it was in the middle of.
        var json = new StringBuilder("{\"pad\": \"");
        json.Append('x', 64 * 1024 - 16);
        json.Append("\", \"format\": \"resonalyze-virtual-crossover\"}");

        Assert.Equal("resonalyze-virtual-crossover", Marker(json.ToString()));
    }

    [Fact]
    public void OnlyTheRootObjectsOwnPropertyCounts() =>
        // A nested marker names a PART of the document — a recipe, a channel — and
        // answering with it would open the file as the thing inside it.
        Assert.Equal(
            "resonalyze-overlay",
            Marker("{\"recipe\": {\"format\": \"resonalyze-live-capture\"}, " +
                "\"format\": \"resonalyze-overlay\"}"));

    [Fact]
    public void AByteOrderMarkIsNotPartOfTheDocument() =>
        Assert.Equal(
            "resonalyze-live-capture",
            Marker(
                "{\"format\": \"resonalyze-live-capture\"}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)));

    [Fact]
    public void ACommentBeforeTheMarkerIsSkippedTheWayTheReadersSkipIt()
    {
        // Every document reader is configured to skip comments and to accept a
        // trailing comma, so a hand-edited file carrying either is a file they would
        // open. Turning it away here would send a capture to the impulse-response
        // loader to be misreported as an unsupported format.
        string json = string.Join(
            Environment.NewLine,
            "{",
            "  // taken in the back seat",
            "  \"title\": \"l tw\",",
            "  \"format\": \"resonalyze-live-capture\",",
            "}");

        Assert.Equal("resonalyze-live-capture", Marker(json));
    }

    [Fact]
    public void TheNameIsMatchedAsStrictlyAsTheDeserializerMatchesIt() =>
        // None of the readers' options ask for case-insensitive property names, so a
        // file writing "Format" does not bind there — and must not read as declared
        // here either, or the probe would send a file to a loader that then refuses
        // it for carrying no format at all.
        Assert.Null(Marker("{\"Format\": \"resonalyze-impulse-response\"}"));

    [Theory]
    [InlineData("{\"version\": 7}")]
    [InlineData("{}")]
    [InlineData("[{\"format\": \"resonalyze-overlay\"}]")]
    [InlineData("\"resonalyze-overlay\"")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void ADocumentThatDeclaresNothingReadsAsNothing(string json) =>
        Assert.Null(Marker(json));

    [Fact]
    public void ATruncatedDocumentStillDeclaresWhatItGotTo() =>
        // Where the saving of a large measurement was interrupted: the head is intact
        // and says what it is, which is all this answers.
        Assert.Equal(
            "resonalyze-impulse-response",
            Marker("{\"format\": \"resonalyze-impulse-response\", \"samples\": [1, 2"));

    [Fact]
    public void AFileThatIsNotThereReadsAsNothing() =>
        Assert.Null(JsonFormatMarker.Read(Path.Combine(directory, "gone.json")));

    private string Marker(string json, Encoding? encoding = null)
    {
        string path = Path.Combine(directory, $"probe-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json, encoding ?? new UTF8Encoding(false));
        return JsonFormatMarker.Read(path)!;
    }
}
