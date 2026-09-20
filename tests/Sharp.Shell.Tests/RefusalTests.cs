using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// A refusal unwinds the run. Returning non-zero and carrying on would be worse than not asking:
// `denied || fallback` would route around the host's answer into a branch nobody approved, and the
// host would have consented to nothing while the line found another way.
//
// The trade this makes is explicit and permanent: commands *before* the refused one have already
// run. A line stops being atomic the moment the decision moves to run time, because the shell can
// only ask about `rm $f` once `$f` has a value, and by then the commands ahead of it are done.
public class RefusalTests
{
    [Fact]
    public void AndStopsAtTheRefusalInsteadOfContinuing()
    {
        RecordingCommandApprover approver = Denying("rm");
        using ShellHarness harness = new(approver: approver);
        harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("echo one && rm keep.txt && echo three");

        Assert.Equal(["echo one", "rm keep.txt"], approver.Texts);
        Assert.Equal("one\n", result.Stdout);
        Assert.Equal(126, result.ExitCode);
    }

    [Fact]
    public void OrDoesNotRunTheFallback()
    {
        RecordingCommandApprover approver = Denying("rm");
        using ShellHarness harness = new(approver: approver);
        harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("rm keep.txt || echo fallback");

        Assert.Equal(["rm keep.txt"], approver.Texts);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(126, result.ExitCode);
    }

    [Fact]
    public void ARefusalMidLoopStopsTheRemainingIterations()
    {
        RecordingCommandApprover approver = new(request =>
            request.CommandText == "rm b" ? CommandApproval.Deny("rm b: refused") : CommandApproval.Allowed);
        using ShellHarness harness = new(approver: approver);
        harness.Write("a", string.Empty);
        harness.Write("b", string.Empty);
        harness.Write("c", string.Empty);

        ShellResult result = harness.Run("for f in a b c; do rm $f; done");

        Assert.Equal(["rm a", "rm b"], approver.Texts);
        Assert.False(File.Exists(Path.Combine(harness.Root, "a")));
        Assert.True(File.Exists(Path.Combine(harness.Root, "b")));
        Assert.True(File.Exists(Path.Combine(harness.Root, "c")));
        Assert.Equal(126, result.ExitCode);
    }

    [Fact]
    public void ARefusalStopsAnOtherwiseUnboundedLoop()
    {
        RecordingCommandApprover approver = Denying("rm");
        using ShellHarness harness = new(approver: approver);
        harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("while true; do rm keep.txt; done");

        Assert.Equal(["true", "rm keep.txt"], approver.Texts);
        Assert.Equal(126, result.ExitCode);
    }

    [Fact]
    public void ARefusalInsideASubstitutionRefusesTheOuterCommand()
    {
        RecordingCommandApprover approver = Denying("rm");
        using ShellHarness harness = new(approver: approver);
        harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("echo $(rm keep.txt)");

        Assert.Equal(["rm keep.txt"], approver.Texts);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(126, result.ExitCode);
    }

    [Fact]
    public void ARefusalInsideASubshellRefusesTheOuterLine()
    {
        RecordingCommandApprover approver = Denying("rm");
        using ShellHarness harness = new(approver: approver);
        harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("(rm keep.txt) && echo after");

        Assert.Equal(["rm keep.txt"], approver.Texts);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(126, result.ExitCode);
    }

    [Fact]
    public void ARefusedConditionIsNotAFalseCondition()
    {
        RecordingCommandApprover approver = Denying("rm");
        using ShellHarness harness = new(approver: approver);
        harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("if rm keep.txt; then echo yes; else echo no; fi");

        Assert.Equal(["rm keep.txt"], approver.Texts);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(126, result.ExitCode);
    }

    [Fact]
    public void ARefusedPipelineStageStopsTheNextOne()
    {
        RecordingCommandApprover approver = Denying("cat");
        using ShellHarness harness = new(approver: approver);
        harness.Write("keep.txt", "content\n");

        ShellResult result = harness.Run("cat keep.txt | wc -l");

        Assert.Equal(["cat keep.txt"], approver.Texts);
        Assert.Equal(126, result.ExitCode);
    }

    [Fact]
    public void TheResultNamesTheRefusalDistinctlyFromAnOrdinaryFailure()
    {
        using ShellHarness refusing = new(approver: Denying("rm"));
        refusing.Write("keep.txt", "content");
        using ShellHarness failing = new();

        ShellResult refused = refusing.Run("rm keep.txt");
        ShellResult failed = failing.Run("rm missing.txt");

        Assert.True(refused.WasRefused);
        Assert.Equal("rm: refused by the host", refused.RefusalReason);
        Assert.False(failed.WasRefused);
        Assert.Null(failed.RefusalReason);
    }

    [Fact]
    public void TheReasonIsWrittenToStandardError()
    {
        using ShellHarness harness = new(approver: Denying("rm"));
        harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("rm keep.txt");

        Assert.Equal("test-shell: rm: refused by the host\n", result.Stderr);
    }

    // The contract change, asserted rather than described: what ran before the refusal stays run.
    [Fact]
    public void WorkDoneBeforeTheRefusalStands()
    {
        using ShellHarness harness = new(approver: Denying("rm"));
        harness.Write("keep.txt", "content");

        ShellResult result = harness.Run("echo written > made.txt; rm keep.txt; echo never > after.txt");

        Assert.Equal("written\n", File.ReadAllText(Path.Combine(harness.Root, "made.txt")));
        Assert.True(File.Exists(Path.Combine(harness.Root, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(harness.Root, "after.txt")));
        Assert.Equal(126, result.ExitCode);
    }

    // The redirection is resolved before the approver is asked, so a refused command must not be
    // allowed to truncate its own output target on the way out.
    [Fact]
    public void ARefusedCommandDoesNotTouchItsRedirectionTarget()
    {
        using ShellHarness harness = new(approver: Denying("cat"));
        harness.Write("keep.txt", "content\n");
        harness.Write("out.txt", "previous\n");

        harness.Run("cat keep.txt > out.txt");

        Assert.Equal("previous\n", File.ReadAllText(Path.Combine(harness.Root, "out.txt")));
    }

    private static RecordingCommandApprover Denying(string program) =>
        new(request => request.Program == program
            ? CommandApproval.Deny($"{program}: refused by the host")
            : CommandApproval.Allowed);
}
