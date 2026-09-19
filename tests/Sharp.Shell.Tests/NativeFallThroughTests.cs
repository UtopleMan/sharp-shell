using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// There is no fall-through left. This suite used to guard all-or-nothing per invocation: a line
// containing one unowned program ran nowhere, because running its owned half here and the whole
// line again in a real shell would delete twice, move twice, append twice.
//
// The double-execution hazard is gone with the second execution. The shell runs the line end to
// end and asks ICommandExecutor for each unowned command, so `rm f.txt && git status` removes
// f.txt exactly once and asks for `git status` exactly once. What replaces the old assertion is
// the invariant: a process is started only because the shell asked, and never before the approver
// was asked first.
public class NativeFallThroughTests
{
    [Theory]
    [InlineData("rm f.txt && git status")]
    [InlineData("mkdir made | jq '.'")]
    [InlineData("touch created; curl http://example.com")]
    [InlineData("mv a.txt b.txt && npm install")]
    [InlineData("cp a.txt copy.txt; docker ps")]
    [InlineData("rm -rf dir && echo x | sed --debug 's/x/y/'")]
    [InlineData("echo replaced > f.txt && git add f.txt")]
    public void AMixedLineRunsAndAsksForOnlyTheUnownedCommand(string commandLine)
    {
        RecordingCommandExecutor external = new();
        using ShellHarness harness = new(external);
        Populate(harness);

        ShellRun run = harness.Classified(commandLine);

        Assert.True(run.Classification.IsRunnable);
        Assert.Equal(ExecutionTier.Native, run.Classification.Tier);
        Assert.Single(external.Commands);
        Assert.Empty(external.Lines);
    }

    [Fact]
    public void TheUnownedCommandArrivesExpandedAndOwnedWorkAlreadyHappened()
    {
        RecordingCommandExecutor external = new();
        using ShellHarness harness = new(external);
        Populate(harness);

        harness.Classified("rm f.txt && git add f.txt");

        Assert.False(File.Exists(Path.Combine(harness.Root, "f.txt")));
        Assert.Equal(["git add f.txt"], external.Commands);
    }

    // The invariant, asserted rather than described: every process request is preceded by an
    // approval for the same target. Nothing reaches the executor on anyone else's initiative.
    [Fact]
    public void EveryProcessRequestIsPrecededByAnApprovalForTheSameTarget()
    {
        RecordingCommandExecutor external = new();
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(external, approver);
        Populate(harness);

        harness.Classified("echo one; git status; npm install");

        Assert.Equal(["git status", "npm install"], external.Commands);
        Assert.Equal(["echo one", "git status", "npm install"], approver.Texts);
    }

    [Fact]
    public void ARefusedNativeCommandStopsTheLineAndLeavesEarlierWorkDone()
    {
        RecordingCommandExecutor external = new();
        RecordingCommandApprover approver = new(request =>
            request.Program == "git" ? CommandApproval.Deny("git: refused by the host") : CommandApproval.Allowed);
        using ShellHarness harness = new(external, approver);
        Populate(harness);

        ShellResult result = harness.Classified("rm f.txt; git status; echo after").Result;

        Assert.False(File.Exists(Path.Combine(harness.Root, "f.txt")));
        Assert.Empty(external.Commands);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(126, result.ExitCode);
    }

    // A line the shell cannot run at all is the one case that still goes out whole — and it goes
    // out through the same seam, as one approved target, not around it.
    [Theory]
    [InlineData("git status &")]
    [InlineData("trap 'echo bye' EXIT")]
    [InlineData("echo unterminated '")]
    public void AnUnrunnableLineIsAskedForWhole(string commandLine)
    {
        RecordingCommandExecutor external = new();
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(external, approver);

        ShellRun run = harness.Classified(commandLine);

        Assert.False(run.Classification.IsRunnable);
        Assert.Equal([commandLine], external.Lines);
        Assert.Empty(external.Commands);
        Assert.Equal([commandLine], approver.Lines.Select(request => request.CommandText));
    }

    [Fact]
    public void ARefusedWholeLineNeverReachesTheExecutor()
    {
        RecordingCommandExecutor external = new();
        RecordingCommandApprover approver = new(_ => CommandApproval.Deny("no whole lines here"));
        using ShellHarness harness = new(external, approver);

        ShellResult result = harness.Classified("git status &").Result;

        Assert.Empty(external.Lines);
        Assert.Equal(126, result.ExitCode);
        Assert.Equal("no whole lines here", result.RefusalReason);
    }

    // With nothing able to start a process — the guest's configuration — an unrunnable line reports
    // the reason it could not be run rather than failing silently.
    [Fact]
    public void AnUnrunnableLineNoExecutorCanTakeReportsItsReason()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Classified("git status &").Result;

        Assert.Equal(127, result.ExitCode);
        Assert.StartsWith("duetui-shell: ", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOwnedLineStillRuns()
    {
        using ShellHarness harness = new();

        ShellRun run = harness.Classified("echo hi");

        Assert.Equal(ExecutionTier.Owned, run.Classification.Tier);
        Assert.Equal("hi\n", run.Result.Stdout);
    }

    [Fact]
    public void AnOwnedMutationStillRuns()
    {
        using ShellHarness harness = new();

        ShellRun run = harness.Classified("touch made.txt");

        Assert.Equal(ExecutionTier.Owned, run.Classification.Tier);
        Assert.True(run.Classification.Mutates);
        Assert.True(File.Exists(Path.Combine(harness.Root, "made.txt")));
    }

    private static void Populate(ShellHarness harness)
    {
        harness.Write("f.txt", "original\n");
        harness.Write("a.txt", "a\n");
        harness.Write("dir/inner.txt", "inner\n");
    }
}
