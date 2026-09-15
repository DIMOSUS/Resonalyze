namespace Resonalyze.Audio;

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
