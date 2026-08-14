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

            // A copy sitting beside the session must not shadow the real, still
            // readable source: the search is a fallback, not a preference.
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
            // The flat case: a session exported together with its measurements
            // into one folder, opened on a machine where the original tree
            // (D:\car\v5) does not exist at all.
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
            // A whole measurement tree copied across: the stored tail (left\...)
            // is preserved under the session's folder, so the deeper candidate is
            // the one that resolves.
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
            // Both exist under the session's folder: a bare woofer.json that is some
            // OTHER measurement, and the one whose folder still matches the stored
            // path. The deeper match agrees with more of the stored path, so it wins
            // — taking the shallow one would swap two same-rate measurements with
            // nothing on screen to say so.
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

            // The internal autosave passes no directory: it lives in the
            // application data folder, where a same-named file would be a false
            // match rather than the user's measurement.
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
            // Only paths the stored one actually names are probed, so an
            // unrelated sibling — even one holding a measurement of the same
            // name — is never picked up.
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
            // The real shape of a tuning session: the sub still comes from the
            // previous round's folder, which no search UNDER the session's folder
            // can ever reach — only the relative path recorded at export does.
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
            // Two files of the same name: the one the session's own layout points
            // at, and one that merely happens to sit beside the session. The
            // recorded arrangement is the stronger evidence, so it wins.
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

            // A hand-edited session cannot smuggle a second absolute reference in
            // through the relative field: a rooted value is dropped, and the tail
            // search answers instead.
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

        // A relative path cannot cross a volume, and there is nothing to be
        // relative to when the stored path is not fully qualified.
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

            // The autosave carries none: it has no folder of its own that the
            // measurements could be relative to, so a value left over from the
            // export above would be a confident wrong answer.
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

            // Exporting states the arrangement on the wire without adopting it: the
            // live project still describes the session it was loaded from, whose
            // folder is what the tool resolves against.
            Assert.Null(original.Pairs[0].Left.SourceRelativePath);

            VirtualCrossoverProjectFile imported =
                VirtualCrossoverProjectFile.LoadFrom(sessionPath);
            string relative = Path.Combine("..", "v4", "subwoofer.json");
            Assert.Equal(relative, imported.Pairs[0].Left.SourceRelativePath);

            // The autosave writes no relative path, but must not take the imported
            // one away either: the tool is still using it to find measurements whose
            // absolute paths are dead — including while the relink prompt is open,
            // which a debounced autosave can fire behind.
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

            // The autosave has no companion folder: nothing to search there.
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
