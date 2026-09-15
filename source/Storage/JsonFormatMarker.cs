using System.Text.Json;

namespace Resonalyze;

/// <summary>Reads the root <c>format</c> marker from the file head without deserializing (IRs are tens of MB).</summary>
/// <remarks>Token walk across chunks: only a root-object property counts (nested ones belong to parts). Same leniency as the
/// deserializers (comments, trailing commas; case-sensitive names) so a file they open is not turned away.</remarks>
internal static class JsonFormatMarker
{
    private const string MarkerProperty = "format";
    private const string VersionProperty = "version";

    private const int ChunkBytes = 64 * 1024;

    private static readonly JsonReaderOptions ProbeOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <remarks>Null covers every way of not knowing, deliberately not told apart: each means "not yours".</remarks>
    internal static string? Read(string path) => Read(path, wantVersion: false).Format;

    /// <summary>For preflight: a future version must be refused before deserialization reports a misleading parse error.</summary>
    internal static (string? Format, int? Version) ReadWithVersion(string path) =>
        Read(path, wantVersion: true);

    private static (string? Format, int? Version) Read(string path, bool wantVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            // Shared read: the application may be writing the file itself.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Scan(stream, wantVersion);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException
                or ArgumentException or JsonException)
        {
            return (null, null);
        }
    }

    private static (string? Format, int? Version) Scan(FileStream stream, bool wantVersion)
    {
        SkipByteOrderMark(stream);

        var buffer = new byte[ChunkBytes];
        int filled = 0;
        bool rootSeen = false;
        // Survives a property name and value falling either side of a chunk boundary.
        bool expectingFormat = false;
        bool expectingVersion = false;
        string? format = null;
        int? version = null;
        bool versionSeen = !wantVersion;
        JsonReaderState state = new(ProbeOptions);
        while (true)
        {
            int read = stream.Read(buffer, filled, buffer.Length - filled);
            filled += read;
            bool finalBlock = read == 0;
            var reader = new Utf8JsonReader(buffer.AsSpan(0, filled), finalBlock, state);
            while (reader.Read())
            {
                if (expectingFormat)
                {
                    format = reader.TokenType == JsonTokenType.String
                        ? reader.GetString()
                        : null;
                    if (format == null || versionSeen)
                    {
                        return (format, version);
                    }

                    expectingFormat = false;
                    continue;
                }

                if (expectingVersion)
                {
                    version = reader.TokenType == JsonTokenType.Number &&
                        reader.TryGetInt32(out int value)
                            ? value
                            : null;
                    versionSeen = true;
                    if (format != null)
                    {
                        return (format, version);
                    }

                    expectingVersion = false;
                    continue;
                }

                if (!rootSeen)
                {
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        return (null, null);
                    }

                    rootSeen = true;
                    continue;
                }

                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
                {
                    return (format, version);
                }

                if (reader.TokenType == JsonTokenType.PropertyName &&
                    reader.CurrentDepth == 1)
                {
                    if (reader.ValueTextEquals(MarkerProperty))
                    {
                        expectingFormat = true;
                    }
                    else if (wantVersion && reader.ValueTextEquals(VersionProperty))
                    {
                        expectingVersion = true;
                    }
                }
            }

            if (finalBlock)
            {
                return (format, version);
            }

            state = reader.CurrentState;
            int consumed = (int)reader.BytesConsumed;
            buffer.AsSpan(consumed, filled - consumed).CopyTo(buffer);
            filled -= consumed;
            if (filled == buffer.Length)
            {
                // A token longer than the buffer; no document of ours has one near its head.
                return (format, version);
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
