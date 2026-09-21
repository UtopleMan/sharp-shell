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
        Assert.Equal("test-shell: rm: refused by the host\n", result.Stderr);
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

    // The narrower claim the group grant is keyed on. Every case is answered by an approver that
    // refuses, so the flag is recorded without the destructive commands under test ever running.
    [Theory]
    [InlineData("cat f", true)]
    [InlineData("rm -rf .", false)]
    [InlineData("sed -n 1,5p f", true)]
    [InlineData("sed -i s/a/b/ f", false)]
    [InlineData("sed -ni s/a/b/ f", false)]
    [InlineData("echo hi > out", false)]
    public void ReportsWhetherTheInvocationIsNonDestructive(string commandLine, bool expected)
    {
        RecordingCommandApprover approver = new(_ => CommandApproval.Deny("refused"));
        using ShellHarness harness = new(approver: approver);
        harness.Write("f", "content\n");

        harness.Run(commandLine);

        Assert.Equal(expected, Assert.Single(approver.Requests).IsNonDestructive);
    }

    // A function reports owned with no applet behind it, and its body can contain anything, so the
    // name it borrows never earns the claim.
    [Fact]
    public void AShellFunctionIsNeverNonDestructive()
    {
        RecordingCommandApprover approver = new(request =>
            request.Program == "cat" ? CommandApproval.Deny("refused") : CommandApproval.Allowed);
        using ShellHarness harness = new(approver: approver);

        harness.Run("cat() { rm -rf .; }; cat f");

        ApprovalRequest request = Assert.Single(approver.Requests);
        Assert.True(request.IsOwned);
        Assert.False(request.IsNonDestructive);
    }

    // The native tier is opaque: the shell cannot see what a real program does, so an escalation
    // never carries the claim either.
    [Fact]
    public void AnEscalationToTheNativeTierIsNeverNonDestructive()
    {
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(new StubCommandExecutor(string.Empty, 0), approver);

        harness.Run("ls -la");

        Assert.False(Assert.Single(approver.Requests).IsNonDestructive);
    }

    [Fact]
    public void TheDenyingApproverRefusesEverything()
    {
        using ShellHarness harness = new(approver: new DenyingCommandApprover());
        string target = harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("rm keep.txt");

        Assert.True(File.Exists(target));
        Assert.Equal(126, result.ExitCode);
        Assert.Equal("test-shell: rm: not approved\n", result.Stderr);
    }
}
