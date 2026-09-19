using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// The hook fires at the dispatch point, which is what makes its answers worth asking about: every
// command arrives fully expanded, once per actual dispatch, loop iterations and substitutions
// included. A host that saw only the command line would have to parse bash to learn any of this.
public class ApproverTests
{
    [Fact]
    public void AsksOncePerSimpleCommand()
    {
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(approver: approver);

        harness.Run("echo hi");

        Assert.Equal(["echo hi"], approver.Texts);
    }

    [Fact]
    public void AsksPerLoopIterationWithTheVariableExpanded()
    {
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(approver: approver);
        harness.Write("a", string.Empty);
        harness.Write("b", string.Empty);
        harness.Write("c", string.Empty);

        harness.Run("for f in a b c; do rm $f; done");

        Assert.Equal(["rm a", "rm b", "rm c"], approver.Texts);
    }

    [Fact]
    public void AsksAboutSubstitutedCommandsToo()
    {
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(new StubCommandExecutor("cafe\n", 0), approver);

        harness.Run("echo $(git rev-parse HEAD)");

        Assert.Equal(["git rev-parse HEAD", "echo cafe"], approver.Texts);
    }

    [Fact]
    public void AsksAboutTheTakenBranchOnly()
    {
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(approver: approver);

        harness.Run("if true; then echo taken; else echo skipped; fi");

        Assert.Equal(["true", "echo taken"], approver.Texts);
    }

    [Fact]
    public void ReportsWhetherTheCommandIsOwned()
    {
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(new StubCommandExecutor(string.Empty, 0), approver);

        harness.Run("echo hi; git status");

        Assert.Equal([true, false], approver.Requests.Select(request => request.IsOwned));
    }

    // An applet that refuses a flag is not the one about to run, so the host is asked about the
    // native program it will actually get instead.
    [Fact]
    public void AnUnsupportedFlagIsNotAnOwnedCommand()
    {
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(new StubCommandExecutor(string.Empty, 0), approver);

        harness.Run("ls --sort=size");

        Assert.False(Assert.Single(approver.Requests).IsOwned);
    }

    [Fact]
    public void ReportsTheWorkingDirectoryTheCommandWillRunIn()
    {
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(approver: approver);
        Directory.CreateDirectory(Path.Combine(harness.Root, "nested"));

        harness.Run("cd nested; pwd");

        Assert.Equal(Path.Combine(harness.Root, "nested"), approver.Requests[^1].WorkingDirectory);
    }

    [Fact]
    public void AnAllowedDecisionChangesNothing()
    {
        using ShellHarness hooked = new(approver: new RecordingCommandApprover());
        using ShellHarness plain = new();

        ShellResult withHook = hooked.Run("echo one; echo two | wc -l");
        ShellResult withoutHook = plain.Run("echo one; echo two | wc -l");

        Assert.Equal(withoutHook, withHook);
    }

    [Fact]
    public void ADeniedOwnedCommandNeverRuns()
    {
        RecordingCommandApprover approver = new(_ => CommandApproval.Deny("rm: refused by the host"));
        using ShellHarness harness = new(approver: approver);
        string target = harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("rm keep.txt");

        Assert.True(File.Exists(target));
        Assert.Equal(126, result.ExitCode);
        Assert.Equal("duetui-shell: rm: refused by the host\n", result.Stderr);
    }

    [Fact]
    public void ADeniedNativeCommandNeverReachesTheExecutor()
    {
        RecordingCommandExecutor external = new();
        RecordingCommandApprover approver = new(_ => CommandApproval.Deny("git: refused by the host"));
        using ShellHarness harness = new(external, approver);

        ShellResult result = harness.Run("git status");

        Assert.Empty(external.Commands);
        Assert.Equal(126, result.ExitCode);
    }

    [Fact]
    public void TheDenyingApproverRefusesEverything()
    {
        using ShellHarness harness = new(approver: new DenyingCommandApprover());
        string target = harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("rm keep.txt");

        Assert.True(File.Exists(target));
        Assert.Equal(126, result.ExitCode);
        Assert.Equal("duetui-shell: rm: not approved\n", result.Stderr);
    }
}
