using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Resonalyze.App.Tests")]

// The screenshot tool (tools/Resonalyze.Screenshots) drives the real shell to
// re-take the documentation's figures, so it reaches panels the app keeps internal.
[assembly: InternalsVisibleTo("Resonalyze.Screenshots")]
