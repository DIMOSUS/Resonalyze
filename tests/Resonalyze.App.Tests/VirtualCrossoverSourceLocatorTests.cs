namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSourceLocatorTests
{
    [Fact]
    public void Locate_PrefersTheStoredPathWhenItStillExists()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string stored = WriteFile(Path.Combine(root, "original"), "woofer.json");
            string session = Path.Combine(root, "session");
            WriteFile(session, "woofer.json");

            // The search is a fallback: a copy beside the session must not shadow a readable source.
            Assert.Equal(
                stored,
                VirtualCrossoverSourceLocator.Locate(stored, null, session));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_FindsTheMeasurementBesideTheSessionFile()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string expected = WriteFile(root, "woofer.json");

            Assert.Equal(
                expected,
                VirtualCrossoverSourceLocator.Locate(
                    @"D:\car\v5\left\woofer.json", null, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_FindsTheMeasurementUnderTheStoredSubfolders()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string expected = WriteFile(Path.Combine(root, "left"), "woofer.json");

            Assert.Equal(
                expected,
                VirtualCrossoverSourceLocator.Locate(
                    Path.Combine(@"D:\car\v5", "left", "woofer.json"), null, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_PrefersTheDeepestTailMatch()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            // The deeper match agrees with more of the stored path; the shallow one would silently swap measurements.
            string expected = WriteFile(Path.Combine(root, "left"), "woofer.json");
            WriteFile(root, "woofer.json");

            Assert.Equal(
                expected,
                VirtualCrossoverSourceLocator.Locate(
                    @"D:\car\v5\left\woofer.json", null, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_DoesNotSearchWithoutAProjectDirectory()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            WriteFile(root, "woofer.json");
            string missing = Path.Combine(root, "gone", "woofer.json");

            // The autosave lives in app data, where a same-named file would be a false match.
            Assert.Null(VirtualCrossoverSourceLocator.Locate(missing, null, null));
            Assert.Null(VirtualCrossoverSourceLocator.Locate(null, null, root));
            Assert.Null(VirtualCrossoverSourceLocator.Locate("   ", null, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_ReturnsNullWhenNothingBesideTheSessionMatches()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            WriteFile(Path.Combine(root, "right"), "woofer.json");

            Assert.Null(
                VirtualCrossoverSourceLocator.Locate(
                    Path.Combine(@"D:\car\v5", "left", "woofer.json"), null, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_FollowsTheRelativePathIntoASiblingFolder()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            // The sub comes from a previous round's folder that no search under the session folder reaches.
            string session = Path.Combine(root, "v5");
            Directory.CreateDirectory(session);
            string expected = WriteFile(Path.Combine(root, "v4"), "subwoofer.json");

            Assert.Equal(
                expected,
                VirtualCrossoverSourceLocator.Locate(
                    @"D:\car\v4\subwoofer.json",
                    Path.Combine("..", "v4", "subwoofer.json"),
                    session));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_PrefersTheRelativePathOverATailMatch()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string session = Path.Combine(root, "v5");
            string expected = WriteFile(Path.Combine(root, "v4"), "mid.json");
            WriteFile(session, "mid.json");

            Assert.Equal(
                expected,
                VirtualCrossoverSourceLocator.Locate(
                    @"D:\car\v4\mid.json",
                    Path.Combine("..", "v4", "mid.json"),
                    session));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_IgnoresARootedOrMissingRelativePath()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string expected = WriteFile(root, "woofer.json");

            // A rooted relative value is dropped, so hand-edits cannot smuggle a second absolute path.
            Assert.Equal(
                expected,
                VirtualCrossoverSourceLocator.Locate(
                    @"D:\car\v5\woofer.json", @"C:\elsewhere\woofer.json", root));
            Assert.Equal(
                expected,
                VirtualCrossoverSourceLocator.Locate(
                    @"D:\car\v5\woofer.json", Path.Combine("..", "gone.json"), root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Relativize_DescribesTheMeasurementFromTheExportFolder()
    {
        Assert.Equal(
            Path.Combine("..", "v4", "mid.json"),
            VirtualCrossoverSourceLocator.Relativize(
                @"D:\car\v4\mid.json", @"D:\car\v5"));
        Assert.Equal(
            "woofer.json",
            VirtualCrossoverSourceLocator.Relativize(
                @"D:\car\v5\woofer.json", @"D:\car\v5"));

        Assert.Null(
            VirtualCrossoverSourceLocator.Relativize(
                @"E:\car\v4\mid.json", @"D:\car\v5"));
        Assert.Null(
            VirtualCrossoverSourceLocator.Relativize("mid.json", @"D:\car\v5"));
        Assert.Null(VirtualCrossoverSourceLocator.Relativize(null, @"D:\car\v5"));
    }

    [Fact]
    public void SaveTo_WritesTheRelativePathsThatSaveKeepsOut()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string session = Path.Combine(root, "v5");
            Directory.CreateDirectory(session);
            var project = new VirtualCrossoverProjectFile();
            project.Pairs[0].Left.SourceFilePath =
                Path.Combine(root, "v4", "subwoofer.json");
            project.Pairs[1].Left.SourceFilePath =
                Path.Combine(session, "l bass.json");

            project.SaveTo(Path.Combine(session, "session.json"));
            VirtualCrossoverProjectFile exported = VirtualCrossoverProjectFile.LoadFrom(
                Path.Combine(session, "session.json"));
            Assert.Equal(
                Path.Combine("..", "v4", "subwoofer.json"),
                exported.Pairs[0].Left.SourceRelativePath);
            Assert.Equal("l bass.json", exported.Pairs[1].Left.SourceRelativePath);
            Assert.Null(exported.Pairs[2].Left.SourceRelativePath);

            project.Save(root);
            VirtualCrossoverProjectFile autosaved =
                VirtualCrossoverProjectFile.LoadOrDefault(root);
            Assert.Null(autosaved.Pairs[0].Left.SourceRelativePath);
            Assert.Equal(
                Path.Combine(root, "v4", "subwoofer.json"),
                autosaved.Pairs[0].Left.SourceFilePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveKeepsTheImportedRelativePathsInMemory()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string sessionFolder = Path.Combine(root, "v5");
            Directory.CreateDirectory(sessionFolder);
            var original = new VirtualCrossoverProjectFile();
            original.Pairs[0].Left.SourceFilePath =
                Path.Combine(root, "v4", "subwoofer.json");
            string sessionPath = Path.Combine(sessionFolder, "session.json");
            original.SaveTo(sessionPath);

            Assert.Null(original.Pairs[0].Left.SourceRelativePath);

            VirtualCrossoverProjectFile imported =
                VirtualCrossoverProjectFile.LoadFrom(sessionPath);
            string relative = Path.Combine("..", "v4", "subwoofer.json");
            Assert.Equal(relative, imported.Pairs[0].Left.SourceRelativePath);

            // The autosave must keep the imported relative path: the tool still resolves dead absolute paths with it.
            imported.Save(root);
            Assert.Equal(relative, imported.Pairs[0].Left.SourceRelativePath);
            Assert.Null(
                VirtualCrossoverProjectFile.LoadOrDefault(root)
                    .Pairs[0].Left.SourceRelativePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadFrom_RemembersTheFolderTheSessionCameFrom()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(root, "session.json");
            new VirtualCrossoverProjectFile().SaveTo(path);

            VirtualCrossoverProjectFile imported =
                VirtualCrossoverProjectFile.LoadFrom(path);
            Assert.Equal(root, imported.ProjectDirectory);

            new VirtualCrossoverProjectFile().Save(root);
            Assert.Null(VirtualCrossoverProjectFile.LoadOrDefault(root).ProjectDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string WriteFile(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "{}");
        return path;
    }

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
