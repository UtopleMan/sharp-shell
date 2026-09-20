using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// One table, three spellings. bash keeps `set -o` and `shopt` as two tables with two name sets; this
// shell keeps one table so an rc file written in either dialect switches the same flag.
public class ShellOptionsTests
{
    private static string Out(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine).Stdout;
    }

    private static ShellResult Result(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine);
    }

    [Fact]
    public void SetMinusOTurnsAnOptionOn()
    {
        Assert.Equal("0\n", Out("set -o errexit; shopt -q errexit; echo $?"));
    }

    [Fact]
    public void SetPlusOTurnsAnOptionOff()
    {
        Assert.Equal("1\n", Out("set -o errexit; set +o errexit; shopt -q errexit; echo $?"));
    }

    [Fact]
    public void ShoptSetsTheSameFlagAsSetMinusO()
    {
        Assert.Equal("0\n", Out("shopt -s errexit; shopt -q errexit; echo $?"));
    }

    [Fact]
    public void SetoptSetsTheSameFlagAsShopt()
    {
        Assert.Equal("0\n", Out("setopt errexit; shopt -q errexit; echo $?"));
    }

    [Fact]
    public void UnsetoptClearsIt()
    {
        Assert.Equal("1\n", Out("setopt errexit; unsetopt errexit; shopt -q errexit; echo $?"));
    }

    [Fact]
    public void ShoptMinusUClearsIt()
    {
        Assert.Equal("1\n", Out("setopt errexit; shopt -u errexit; shopt -q errexit; echo $?"));
    }

    [Fact]
    public void SetMinusEIsTheShortSpellingOfErrexit()
    {
        Assert.Equal("0\n", Out("set -e; shopt -q errexit; echo $?"));
    }

    [Fact]
    public void SetMinusUIsTheShortSpellingOfNounset()
    {
        Assert.Equal("0\n", Out("set -u; shopt -q nounset; echo $?"));
    }

    // bash pads the name to fifteen characters and follows it with a tab, which is what a script
    // parsing `set -o` expects to find.
    [Fact]
    public void SetMinusOWithNoNameListsEveryOption()
    {
        string listing = Out("set -o");

        Assert.Contains("errexit        \toff\n", listing, StringComparison.Ordinal);
        Assert.Contains("autocd         \toff\n", listing, StringComparison.Ordinal);
    }

    [Fact]
    public void ShoptWithNoNameListsEveryOption()
    {
        Assert.Contains("pipefail       \toff\n", Out("shopt"), StringComparison.Ordinal);
    }

    [Fact]
    public void SetoptWithNoNameListsOnlyWhatIsOn()
    {
        Assert.Equal("errexit\n", Out("setopt errexit; setopt"));
    }

    [Fact]
    public void AnUnknownNameIsAnErrorFromSet()
    {
        ShellResult result = Result("set -o nosuchthing");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("set: nosuchthing: invalid option name\n", result.Stderr);
    }

    [Fact]
    public void AnUnknownNameIsAnErrorFromShopt()
    {
        ShellResult result = Result("shopt -s nosuchthing");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("shopt: nosuchthing: invalid shell option name\n", result.Stderr);
    }

    [Fact]
    public void AnUnknownNameIsAnErrorFromSetopt()
    {
        ShellResult result = Result("setopt nosuchthing");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("setopt: nosuchthing: invalid option name\n", result.Stderr);
    }

    [Fact]
    public void OptionsSurviveAFork()
    {
        Assert.Equal("0\n", Out("set -o nounset; echo $(shopt -q nounset; echo $?)"));
    }

    // The one option a command substitution does not inherit, because bash does not pass it either:
    // `set -e; echo $(echo one; false; echo two)` prints both words and the outer shell carries on.
    [Fact]
    public void ErrexitIsNotInheritedByACommandSubstitution()
    {
        Assert.Equal("one two\n", Out("set -e; echo $(echo one; false; echo two)"));
    }

    [Fact]
    public void AnInteractiveOptionIsStoredWithoutBeingActedOn()
    {
        Assert.Equal("0\n", Out("setopt histignoredups; shopt -q histignoredups; echo $?"));
    }
}
