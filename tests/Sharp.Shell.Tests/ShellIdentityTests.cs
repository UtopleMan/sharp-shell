using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// The shell's own name is the host's to supply: the binary is shsh, duetui's guest is something
// else, and neither is a name the core may hardcode.
public class ShellIdentityTests
{
    [Fact]
    public void DollarZeroAnswersTheNameTheHostSupplied()
    {
        using ShellHarness harness = new();

        Assert.Equal("test-shell\n", harness.Run("echo $0").Stdout);
    }

    [Fact]
    public void AHostThatSuppliesNothingGetsTheDocumentedDefault()
    {
        ShellState state = new(Path.GetTempPath());

        Assert.Equal("sharp-shell", state.ShellName);
    }

    [Fact]
    public void AForkKeepsTheName()
    {
        using ShellHarness harness = new();

        Assert.Equal("test-shell\n", harness.Run("echo $(echo $0)").Stdout);
    }

    [Fact]
    public void AMessageTheShellWritesAboutItselfCarriesTheName()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("nosuchcommand");

        Assert.Equal("test-shell: nosuchcommand: command not found\n", result.Stderr);
    }
}
