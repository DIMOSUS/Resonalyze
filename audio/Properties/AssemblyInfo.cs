using System.Runtime.CompilerServices;

// The audio-driver internals (device sessions, PCM decoders, accumulators,
// stream configuration) are exercised directly by the audio test project.
// The main application and its tests deliberately see only the public
// abstraction (IAudioSessionFactory, the neutral DTOs, the catalogs), so no
// InternalsVisibleTo is granted to them — a compile error there means a
// backend detail leaked past the boundary.
[assembly: InternalsVisibleTo("Resonalyze.Audio.Tests")]
