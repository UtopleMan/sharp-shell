using Sharp.Shell.Commands;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

// For the handful of tests that invoke an applet directly rather than through the executor. Only
// `source` runs shell text, so a double that throws is the honest stand-in: a test that trips it has
// found an applet reaching for the executor behind the registry's back.
internal static class AppletContexts
{
    public static AppletContext For(
        IReadOnlyList<string> arguments,
        IEnumerable<string> input,
        ShellState state,
        Action<string> writeError) =>
        new(arguments, input, state, writeError, CancellationToken.None, RefuseShellText);

    private static ShellResult RefuseShellText(string text) =>
        throw new InvalidOperationException("this applet was not expected to run shell text");
}
