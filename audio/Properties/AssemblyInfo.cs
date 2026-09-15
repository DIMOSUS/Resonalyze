using System.Runtime.CompilerServices;

// Only the audio tests see internals: a compile error in the app means a backend detail leaked.
[assembly: InternalsVisibleTo("Resonalyze.Audio.Tests")]
