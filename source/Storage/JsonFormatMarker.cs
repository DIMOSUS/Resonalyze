using System.Text.Json;

namespace Resonalyze;

/// <summary>
/// Reads the <c>format</c> marker a Resonalyze JSON document declares, out of the head
/// of the file, without deserializing the rest of it.
/// </summary>
/// <remarks>
/// Which document a file holds has to be answered before anything can open it, and the
/// documents are nothing like the same size: a spatial-average capture is a few
/// hundred kilobytes, an impulse response tens of megabytes. Deserializing one to read
/// a string its first line already carries means reading and allocating all of it to
/// answer "not yours" — which is what every load of an impulse response did on its way
/// past the capture reader.
/// <para>
/// The scan is a token walk with the reader's state carried from chunk to chunk, so it
/// knows as much as a full parse would about WHERE the marker is: only a property of
/// the root object counts — a nested <c>format</c> belongs to a recipe or a channel,
/// and naming a file after one of its parts is how a document gets opened as the thing
/// inside it — and the walk ends with the root object. It also reads a document on
/// exactly the terms the deserializers behind it do, in both directions: their options
/// accept comments and trailing commas, so the probe does too — a file they would open
/// must not be turned away at the door — and none of them ask for case-insensitive
/// property names, so <c>format</c> matches and <c>Format</c> does not, here as there.
/// </para>
/// </remarks>
internal static class JsonFormatMarker
{
    private const string MarkerProperty = "format";

    /// <summary>
    /// How much of the file is held at a time. Our own documents declare the marker
    /// first, so the first read answers; a file that declares one later is walked
    /// without ever holding more than this.
    /// </summary>
    private const int ChunkBytes = 64 * 1024;

    /// <summary>
    /// The leniency every one of the document readers is configured with. A capture
    /// carrying a comment before its marker is a capture they would open, so it must
    /// not be declined here and sent on to another loader to be misreported.
    /// </summary>
    private static readonly JsonReaderOptions ProbeOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// The <c>format</c> of the root object in the file at <paramref name="path"/>, or
    /// null when there is none to read.
    /// </summary>
    /// <remarks>
    /// Null covers every way of not knowing — an unreadable file, one that is not
    /// JSON, one whose root is not an object, one that declares no format — and they
    /// are deliberately not told apart. A caller asks this to decide whether a file is
    /// its own, and every one of those answers that question with "no"; the loader
    /// that can say more never gets the file.
    /// </remarks>
    internal static string? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            // Shared read: the file may be one the application is itself writing, and
            // asking what it is must not fail for holding a lock nobody asked for.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Scan(stream);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException
                or ArgumentException or JsonException)
        {
            return null;
        }
    }

    private static string? Scan(FileStream stream)
    {
        SkipByteOrderMark(stream);

        var buffer = new byte[ChunkBytes];
        int filled = 0;
        bool rootSeen = false;
        // Set once the marker's property name has been read, so the value can still be
        // picked up when the two fall either side of a chunk boundary.
        bool expectingValue = false;
        JsonReaderState state = new(ProbeOptions);
        while (true)
        {
            int read = stream.Read(buffer, filled, buffer.Length - filled);
            filled += read;
            bool finalBlock = read == 0;
            var reader = new Utf8JsonReader(buffer.AsSpan(0, filled), finalBlock, state);
            while (reader.Read())
            {
                if (expectingValue)
                {
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }

                if (!rootSeen)
                {
                    // A document whose root is not an object declares nothing.
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        return null;
                    }

                    rootSeen = true;
                    continue;
                }

                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
                {
                    return null;
                }

                if (reader.TokenType == JsonTokenType.PropertyName &&
                    reader.CurrentDepth == 1 &&
                    reader.ValueTextEquals(MarkerProperty))
                {
                    expectingValue = true;
                }
            }

            if (finalBlock)
            {
                return null;
            }

            state = reader.CurrentState;
            int consumed = (int)reader.BytesConsumed;
            buffer.AsSpan(consumed, filled - consumed).CopyTo(buffer);
            filled -= consumed;
            if (filled == buffer.Length)
            {
                // One token longer than the whole buffer, which no document of ours
                // carries near its head. Nothing left to do but decline it.
                return null;
            }
        }
    }

    private static void SkipByteOrderMark(FileStream stream)
    {
        Span<byte> head = stackalloc byte[3];
        int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        if (read != head.Length || !head.SequenceEqual(Utf8ByteOrderMark))
        {
            stream.Position = 0;
        }
    }
}
