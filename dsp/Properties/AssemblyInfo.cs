using System.Runtime.CompilerServices;

// The DSP test project reaches a few internals directly, where testing through the
// public surface would only measure the approximation against itself. SmoothBinsHann is
// the case in point: its contract is an error BOUND against the exact convolution, so the
// test keeps that exact form as its own reference and compares the two — which needs the
// real one in hand. Nothing outside the tests is granted this.
[assembly: InternalsVisibleTo("Resonalyze.Dsp.Tests")]
