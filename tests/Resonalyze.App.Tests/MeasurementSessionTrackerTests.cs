using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// The current-measurement identity (history entry id + loaded-IR flag) moved
/// off Form1 into <see cref="MeasurementSessionTracker"/>; these pin the
/// transitions the shell relies on: load/save wiring into the history
/// service, the persist guard, and the delete-keeps-the-loaded-IR rule.
/// </summary>
public sealed class MeasurementSessionTrackerTests : IDisposable
{
    private readonly string directory;
    private int sessionCaptures;

    public MeasurementSessionTrackerTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-session-tracker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
    }

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
    public void Initially_NothingIsCurrentAndPersistIsANoOp()
    {
        (MeasurementSessionTracker tracker, _) = CreateTracker();

        tracker.PersistCurrentSessionState();

        Assert.Null(tracker.CurrentEntryId);
        Assert.False(tracker.HasImpulseResponse);
        Assert.Equal(0, sessionCaptures);
    }

    [Fact]
    public async Task MarkLoadedFile_CreatesTheFileBackedEntryAndMakesItCurrent()
    {
        (MeasurementSessionTracker tracker, MeasurementHistoryService history) =
            CreateTracker();
        (string path, ImpulseResponseFile file) = await CreateImpulseResponseFileAsync(
            "a.json");

        tracker.MarkLoadedFile(path, file);

        Assert.True(tracker.HasImpulseResponse);
        Assert.NotNull(tracker.CurrentEntryId);
        MeasurementHistoryEntry entry = Assert.Single(history.Entries);
        Assert.Equal(entry.Id, tracker.CurrentEntryId);
        Assert.Equal(path, entry.SourceFilePath);
    }

    [Fact]
    public async Task MarkSavedFile_WithoutACurrentEntry_CreatesOneAndMakesItCurrent()
    {
        (MeasurementSessionTracker tracker, MeasurementHistoryService history) =
            CreateTracker();
        (string path, ImpulseResponseFile file) = await CreateImpulseResponseFileAsync(
            "saved.json");

        tracker.MarkSavedFile(path, file);

        MeasurementHistoryEntry entry = Assert.Single(history.Entries);
        Assert.Equal(entry.Id, tracker.CurrentEntryId);
        Assert.Equal(path, entry.SourceFilePath);
    }

    [Fact]
    public async Task MarkSavedFile_WithACurrentEntry_MarksItSavedAndKeepsItCurrent()
    {
        (MeasurementSessionTracker tracker, MeasurementHistoryService history) =
            CreateTracker();
        (string loadedPath, ImpulseResponseFile loadedFile) =
            await CreateImpulseResponseFileAsync("a.json");
        tracker.MarkLoadedFile(loadedPath, loadedFile);
        Guid? currentBeforeSave = tracker.CurrentEntryId;
        (string savedPath, ImpulseResponseFile savedFile) =
            await CreateImpulseResponseFileAsync("b.json");

        tracker.MarkSavedFile(savedPath, savedFile);

        Assert.Equal(currentBeforeSave, tracker.CurrentEntryId);
        MeasurementHistoryEntry entry = Assert.Single(history.Entries);
        Assert.Equal(savedPath, entry.SourceFilePath);
    }

    [Fact]
    public async Task ForgetEntry_DropsTheCurrentPointerButKeepsTheLoadedIr()
    {
        (MeasurementSessionTracker tracker, _) = CreateTracker();
        (string path, ImpulseResponseFile file) = await CreateImpulseResponseFileAsync(
            "a.json");
        tracker.MarkLoadedFile(path, file);
        Guid current = tracker.CurrentEntryId!.Value;

        tracker.ForgetEntry(Guid.NewGuid());
        Assert.Equal(current, tracker.CurrentEntryId);

        tracker.ForgetEntry(current);
        Assert.Null(tracker.CurrentEntryId);
        Assert.True(tracker.HasImpulseResponse);
    }

    [Fact]
    public async Task PersistCurrentSessionState_WritesTheCapturedSessionIntoTheEntry()
    {
        (MeasurementSessionTracker tracker, MeasurementHistoryService history) =
            CreateTracker();
        (string path, ImpulseResponseFile file) = await CreateImpulseResponseFileAsync(
            "a.json");
        tracker.MarkLoadedFile(path, file);
        int capturesAfterLoad = sessionCaptures;

        tracker.PersistCurrentSessionState();

        Assert.Equal(capturesAfterLoad + 1, sessionCaptures);
        Assert.NotNull(history.FindById(tracker.CurrentEntryId!.Value)!.Session);
    }

    [Fact]
    public async Task PersistCurrentSessionState_SkipsWhenNoImpulseResponseIsLoaded()
    {
        (MeasurementSessionTracker tracker, _) = CreateTracker();
        (string path, ImpulseResponseFile file) = await CreateImpulseResponseFileAsync(
            "a.json");
        tracker.MarkLoadedFile(path, file);
        tracker.SetImpulseResponseAvailable(false);
        int capturesAfterLoad = sessionCaptures;

        tracker.PersistCurrentSessionState();

        Assert.Equal(capturesAfterLoad, sessionCaptures);
    }

    [Fact]
    public void MarkRestoredAndReset_ToggleTheWholeIdentity()
    {
        (MeasurementSessionTracker tracker, _) = CreateTracker();
        Guid entryId = Guid.NewGuid();

        tracker.MarkRestored(entryId);
        Assert.Equal(entryId, tracker.CurrentEntryId);
        Assert.True(tracker.HasImpulseResponse);

        tracker.Reset();
        Assert.Null(tracker.CurrentEntryId);
        Assert.False(tracker.HasImpulseResponse);
    }

    private (MeasurementSessionTracker Tracker, MeasurementHistoryService History)
        CreateTracker()
    {
        var history = new MeasurementHistoryService(new MeasurementHistoryPersistence(
            Path.Combine(directory, "measurement-history.json")));
        var tracker = new MeasurementSessionTracker(
            history,
            () =>
            {
                sessionCaptures++;
                return new MeasurementSessionSnapshot();
            });
        return (tracker, history);
    }

    private async Task<(string Path, ImpulseResponseFile File)>
        CreateImpulseResponseFileAsync(string fileName)
    {
        string path = Path.Combine(directory, fileName);
        ImpulseResponseFile file = ImpulseResponseFileAtomicSaveTests.CreateFile(
            sampleValue: 1.0);
        await file.SaveAsync(path);
        return (path, file);
    }
}
