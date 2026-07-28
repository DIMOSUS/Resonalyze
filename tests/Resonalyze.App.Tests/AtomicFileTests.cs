namespace Resonalyze.App.Tests;

/// <summary>
/// Exports the user shares (tuning sheets, PEQ profiles, Virtual DSP projects)
/// used to be written with <c>File.Create</c>, which truncates the destination
/// on open — so a failure mid-write replaced a good file with a broken one.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), $"resonalyze-atomic-{Guid.NewGuid():N}");

    public AtomicFileTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void WriteAllText_CreatesTheFile()
    {
        string path = Path.Combine(directory, "new.txt");

        AtomicFile.WriteAllText(path, "content");

        Assert.Equal("content", File.ReadAllText(path));
    }

    [Fact]
    public void WriteAllText_ReplacesAnExistingFile()
    {
        string path = Path.Combine(directory, "existing.txt");
        File.WriteAllText(path, "old");

        AtomicFile.WriteAllText(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
    }

    [Fact]
    public void Write_WhenTheContentWriterThrows_LeavesTheOriginalUntouched()
    {
        string path = Path.Combine(directory, "precious.json");
        File.WriteAllText(path, "the good export");

        Assert.Throws<InvalidOperationException>(() =>
            AtomicFile.Write(path, stream =>
            {
                stream.WriteByte(0x7B);
                throw new InvalidOperationException("serialization blew up halfway");
            }));

        Assert.Equal("the good export", File.ReadAllText(path));
    }

    [Fact]
    public void Write_WhenTheContentWriterThrows_LeavesNoTemporaryFileBehind()
    {
        string path = Path.Combine(directory, "debris.json");

        Assert.Throws<InvalidOperationException>(() =>
            AtomicFile.Write(path, _ => throw new InvalidOperationException("nope")));

        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void Write_CreatesMissingDirectories()
    {
        string path = Path.Combine(directory, "nested", "deeper", "file.txt");

        AtomicFile.WriteAllText(path, "content");

        Assert.Equal("content", File.ReadAllText(path));
    }
}
