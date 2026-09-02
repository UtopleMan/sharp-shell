namespace Sharp.Shell.Tests.Differential;

// Runs a script through the system sed so its answer can be compared with ours, the way BashOracle
// does for the language. A thin wrapper over ExternalOracle since awk needed the same machinery.
//
// The comparison list is restricted to constructs BSD and GNU sed agree on, because the sed on a
// developer machine here is BSD and the applet targets GNU 4.x. GNU-only behaviour — \+, -r, -s, -z,
// 0~step, the one-liner `a text` — is covered by the golden tests in SedAppletTests instead.
// Installing GNU sed (brew install gnu-sed) would let this list grow; leaving it out is a scope
// choice, not an oversight.
public static class SedOracle
{
    private static readonly ExternalOracle Sed = ExternalOracle.Locate("sed", ["/usr/bin/sed", "/bin/sed"]);

    public static string? Path => Sed.Path;

    public static bool IsAvailable => Sed.IsAvailable;

    public static string Version => Sed.Version;

    public static OracleResult Run(IReadOnlyList<string> arguments, string input, string workingDirectory) =>
        Sed.Run(arguments, input, workingDirectory);

    public static bool CanCompare(IReadOnlyList<string> arguments, string input) =>
        Sed.CanCompare(arguments, input);
}
