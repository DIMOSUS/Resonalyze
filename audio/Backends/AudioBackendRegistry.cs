namespace Resonalyze.Audio;

/// <summary>
/// The one place a persisted <see cref="AudioBackend"/> is resolved to a concrete
/// backend implementation. Not a plugin framework — the backends are a fixed,
/// compile-time set constructed once in the application's composition root.
/// </summary>
public sealed class AudioBackendRegistry : IAudioBackendRegistry
{
    private readonly IReadOnlyDictionary<AudioBackend, IAudioBackend> byId;

    public AudioBackendRegistry(IEnumerable<IAudioBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);
        var map = new Dictionary<AudioBackend, IAudioBackend>();
        foreach (IAudioBackend backend in backends)
        {
            map[backend.Descriptor.Id] = backend;
        }
        byId = map;
        Backends = map.Values.Select(backend => backend.Descriptor).ToArray();
    }

    /// <summary>The default set of backends shipped with the application.</summary>
    public static AudioBackendRegistry CreateDefault() =>
        new(
        [
            new MmeBackend(),
            WasapiBackend.CreateShared(),
            WasapiBackend.CreateExclusive(),
            new AsioBackend()
        ]);

    public IReadOnlyList<AudioBackendDescriptor> Backends { get; }

    public IAudioBackend GetBackend(AudioBackend id) =>
        byId.TryGetValue(id, out IAudioBackend? backend)
            ? backend
            : throw new InvalidOperationException($"No audio backend is registered for '{id}'.");
}
