using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// The core never reads the operating system. It is handed an environment and decides what a child
// process is offered; the host decides what that child actually gets.
public class EnvironmentTests
{
    [Fact]
    public void AnEnvironmentSuppliedAtConstructionIsExported()
    {
        ShellState state = new(
            Path.GetTempPath(),
            environment: new Dictionary<string, string> { ["HOME"] = "/home/me" });

        Assert.Equal("/home/me", state.Variables["HOME"]);
        Assert.True(state.Variables.IsExported("HOME"));
    }

    // duetui's host already passes PWD through WasiOptions.Environment and the shell used to drop
    // it, so $PWD expanded to nothing. No operating system is involved in this test.
    [Fact]
    public void TheWorkingDirectoryTheHostSuppliedExpands()
    {
        using ShellHarness harness = new();
        harness.Environment["PWD"] = "/workspace";

        Assert.Equal("/workspace\n", harness.Run("echo $PWD").Stdout);
    }

    [Fact]
    public void AnExportedVariableReachesAChild()
    {
        RecordingCommandExecutor external = new();
        using ShellHarness harness = new(external);

        harness.Run("export FOO=bar; somewhere");

        Assert.Equal("bar", Assert.Single(external.Environments)["FOO"]);
    }

    [Fact]
    public void AnUnexportedVariableDoesNotReachAChild()
    {
        RecordingCommandExecutor external = new();
        using ShellHarness harness = new(external);

        harness.Run("BAZ=local; somewhere");

        Assert.False(Assert.Single(external.Environments).ContainsKey("BAZ"));
    }

    [Fact]
    public void AnInheritedNameStaysExportedWhenItIsReassigned()
    {
        RecordingCommandExecutor external = new();
        using ShellHarness harness = new(external);
        harness.Environment["PATH"] = "/theirs";

        harness.Run("PATH=/mine; somewhere");

        Assert.Equal("/mine", Assert.Single(external.Environments)["PATH"]);
    }

    [Fact]
    public void AWholeLineHandedOverCarriesTheEnvironmentToo()
    {
        RecordingCommandExecutor external = new();
        using ShellHarness harness = new(external);
        harness.Environment["HOME"] = "/home/me";

        harness.Classified("git status &");

        Assert.Equal("/home/me", Assert.Single(external.LineEnvironments)["HOME"]);
    }
}
