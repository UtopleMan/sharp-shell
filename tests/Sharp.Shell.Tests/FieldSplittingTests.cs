using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// IFS is a variable, so field splitting is a decision the running shell makes rather than a constant
// the expander was compiled with.
public class FieldSplittingTests
{
    private static string Out(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine).Stdout;
    }

    [Fact]
    public void TheDefaultSeparatorsAreWhitespace()
    {
        Assert.Equal("[a]\n[b]\n", Out("v='a  b'; for i in $v; do echo \"[$i]\"; done"));
    }

    [Fact]
    public void AColonSeparatorSplitsOnColons()
    {
        Assert.Equal("a\nb\nc\n", Out("IFS=:; x=a:b:c; for i in $x; do echo $i; done"));
    }

    [Fact]
    public void AColonSeparatorNoLongerSplitsOnSpaces()
    {
        Assert.Equal("[a b]\n", Out("IFS=:; x='a b'; for i in $x; do echo \"[$i]\"; done"));
    }

    // A non-whitespace separator delimits one field each time it appears, so a run of them yields
    // empty fields. Whitespace separators collapse instead, which is why the two kinds cannot share
    // one code path.
    [Fact]
    public void RepeatedNonWhitespaceSeparatorsYieldEmptyFields()
    {
        Assert.Equal("[a]\n[]\n[b]\n", Out("IFS=:; x=a::b; for i in $x; do echo \"[$i]\"; done"));
    }

    [Fact]
    public void AnEmptySeparatorDisablesSplitting()
    {
        Assert.Equal("[a b]\n", Out("IFS=; v='a b'; for i in $v; do echo \"[$i]\"; done"));
    }

    [Fact]
    public void ALeadingNonWhitespaceSeparatorYieldsAnEmptyFirstField()
    {
        Assert.Equal("[]\n[a]\n", Out("IFS=:; x=:a; for i in $x; do echo \"[$i]\"; done"));
    }

    [Fact]
    public void ATrailingSeparatorDoesNotYieldAnExtraField()
    {
        Assert.Equal("[a]\n", Out("IFS=:; x=a:; for i in $x; do echo \"[$i]\"; done"));
    }
}
