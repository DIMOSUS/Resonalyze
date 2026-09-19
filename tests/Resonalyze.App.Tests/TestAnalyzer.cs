namespace Resonalyze.App.Tests;

/// <summary>The engine and the open measurement a plot or panel reads, wired as the main window wires them.</summary>
internal sealed class TestAnalyzer : IDisposable
{
    public ExpSweepMeasurement Engine { get; } = new(new FakeAudioSessionFactory());

    public AnalyzerDocument Document { get; } = new();

    public MeasurementResult Result =>
        Document.Result ?? throw new InvalidOperationException("Nothing is open.");

    public void Open(MeasurementResult result, string? sourceName = null) =>
        Document.TryBegin()!.Install(result, sourceName);

    public void Dispose() => Engine.Dispose();
}
