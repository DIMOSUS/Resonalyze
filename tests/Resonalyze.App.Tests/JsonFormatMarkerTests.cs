using System.Text;

namespace Resonalyze.App.Tests;

/// <summary>Must be cheap on files it declines (IRs are tens of MB) yet as informed as the deserializer behind it.</summary>
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
        // A key-sorted document can carry the marker past any fixed window, so the walk continues chunk by chunk.
        var json = new StringBuilder("{\"curveDb\": [");
        json.AppendJoin(", ", Enumerable.Range(0, 40_000));
        json.Append("], \"format\": \"resonalyze-live-capture\"}");

        Assert.True(json.Length > 128 * 1024, $"only {json.Length} characters");
        Assert.Equal("resonalyze-live-capture", Marker(json.ToString()));
    }

    [Fact]
    public void AMarkerSplitAcrossAChunkBoundaryIsReadWhole()
    {
        // Reader state crosses chunk boundaries with a value split between them.
        var json = new StringBuilder("{\"pad\": \"");
        json.Append('x', 64 * 1024 - 16);
        json.Append("\", \"format\": \"resonalyze-virtual-crossover\"}");

        Assert.Equal("resonalyze-virtual-crossover", Marker(json.ToString()));
    }

    [Fact]
    public void OnlyTheRootObjectsOwnPropertyCounts() =>
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
        // Readers skip comments and accept trailing commas, so the probe must too.
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
        // Readers are case-sensitive, so "Format" must not read as declared.
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
        Assert.Equal(
            "resonalyze-impulse-response",
            Marker("{\"format\": \"resonalyze-impulse-response\", \"samples\": [1, 2"));

    [Fact]
    public void AFileThatIsNotThereReadsAsNothing() =>
        Assert.Null(JsonFormatMarker.Read(Path.Combine(directory, "gone.json")));

    // A future format version must be refused before deserialization.

    [Fact]
    public void TheVersionTravelsWithTheFormat() =>
        Assert.Equal(
            ("resonalyze-impulse-response", 9),
            WithVersion("{\"format\": \"resonalyze-impulse-response\", \"version\": 9}"));

    [Fact]
    public void AVersionDeclaredBeforeTheFormatStillBinds() =>
        Assert.Equal(
            ("resonalyze-impulse-response", 9),
            WithVersion("{\"version\": 9, \"format\": \"resonalyze-impulse-response\"}"));

    [Fact]
    public void OnlyTheRootObjectsOwnVersionCounts() =>
        Assert.Equal(
            ("resonalyze-overlay", 3),
            WithVersion("{\"recipe\": {\"version\": 12}, " +
                "\"format\": \"resonalyze-overlay\", \"version\": 3}"));

    [Theory]
    [InlineData("{\"format\": \"resonalyze-overlay\"}")]
    [InlineData("{\"format\": \"resonalyze-overlay\", \"version\": \"nine\"}")]
    public void AMissingOrNonNumericVersionReadsAsNoVersion(string json) =>
        Assert.Equal(("resonalyze-overlay", null), WithVersion(json));

    private string Marker(string json, Encoding? encoding = null)
    {
        string path = Path.Combine(directory, $"probe-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json, encoding ?? new UTF8Encoding(false));
        return JsonFormatMarker.Read(path)!;
    }

    private (string? Format, int? Version) WithVersion(string json)
    {
        string path = Path.Combine(directory, $"probe-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return JsonFormatMarker.ReadWithVersion(path);
    }
}
