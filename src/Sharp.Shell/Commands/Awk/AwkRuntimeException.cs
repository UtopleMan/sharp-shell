namespace Sharp.Shell.Commands.Awk;

// A failure awk itself reports and stops on: a division by zero, a negative field index, a
// runaway recursion, a dynamic pattern the translator refuses. The applet turns it into an `awk: `
// line on standard error and exit 2.
internal sealed class AwkRuntimeException(string message) : Exception(message);
