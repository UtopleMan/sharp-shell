namespace Sharp.Shell.Tests.Differential;

// Two references rather than one. `/usr/bin/awk` is one-true-awk, the dialect this applet targets;
// `gawk --posix` is a second, independent implementation of the same standard. A case only counts as
// agreed when both say the same thing, which is what turns "our answer matches an awk" into "our
// answer matches awk".
//
// Where the two disagree with each other the case belongs in corpus/awk-dialect-buckets.md, not in
// the comparison list.
public static class AwkOracle
{
    public static ExternalOracle OneTrueAwk { get; } = ExternalOracle.Locate("awk", ["/usr/bin/awk", "/bin/awk"]);

    public static ExternalOracle Gawk { get; } = ExternalOracle.Locate(
        "gawk --posix",
        ["/opt/homebrew/bin/gawk", "/usr/local/bin/gawk", "/usr/bin/gawk"],
        "--posix");

    public static IReadOnlyList<ExternalOracle> All { get; } = [OneTrueAwk, Gawk];

    public static bool IsAvailable => All.All(oracle => oracle.IsAvailable);
}
