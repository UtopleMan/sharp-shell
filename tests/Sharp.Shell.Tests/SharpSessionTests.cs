using Sharp;
using Xunit;

namespace Sharp.Shell.Tests;

// The binary's session loop, driven in-process. The black-box tests in BlackBox/ still drive the
// built executable through a real process — they are what proves argv, stdio and exit status are
// wired up — but a spawned process is invisible to a coverage collector, so the logic those tests
// cover is also exercised here where it can be measured.
//
// Every session is confined with --root at its own temp workspace and runs --strict, so nothing
// here can start a process or touch a path outside the workspace.
public class SharpSessionTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("sharp-session").FullName;

    private readonly StringWriter output = new();

    private readonly StringWriter errors = new();

    private Session Confined(bool explains = false, bool strict = true) =>
        new(new SessionSettings(root, root, explains, strict), output, errors);

    [Fact]
    public void AnOwnedLineRunsAndItsOutputReachesTheWriter()
    {
        int status = Confined().Run("echo hello", CancellationToken.None);

        Assert.Equal(0, status);
        Assert.Equal("hello\n", output.ToString());
        Assert.Equal(string.Empty, errors.ToString());
    }

    [Fact]
    public void AnEmptyLineKeepsTheLastStatusAndRunsNothing()
    {
        Session session = Confined();
        session.Run("false", CancellationToken.None);

        Assert.Equal(1, session.Run("   ", CancellationToken.None));
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void StateSurvivesBetweenLines()
    {
        Session session = Confined();
        Directory.CreateDirectory(Path.Combine(root, "inner"));

        session.Run("cd inner", CancellationToken.None);
        session.Run("pwd", CancellationToken.None);

        Assert.EndsWith("inner\n", output.ToString(), StringComparison.Ordinal);
        Assert.EndsWith("inner", session.WorkingDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void AVariableSetOnOneLineIsReadableOnTheNext()
    {
        Session session = Confined();

        session.Run("greeting=hi", CancellationToken.None);
        session.Run("echo $greeting", CancellationToken.None);

        Assert.Equal("hi\n", output.ToString());
    }

    // The strings --explain prints are the contract src/Sharp/README.md documents.
    [Fact]
    public void ExplainNamesTheTierForAnOwnedLine()
    {
        Confined(explains: true).Run("echo hi", CancellationToken.None);

        Assert.StartsWith("[owned]", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainNamesTheProgramThatForcedTheLineOut()
    {
        Confined(explains: true).Run("git status", CancellationToken.None);

        Assert.StartsWith("[native git] — ", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainReportsThatAnOwnedLineMutates()
    {
        Confined(explains: true).Run("rm -rf build", CancellationToken.None);

        Assert.StartsWith("[owned mutates]", errors.ToString(), StringComparison.Ordinal);
    }

    // Strict no longer refuses to execute the line; it refuses the unowned command at the dispatch
    // point, which is a refusal — 126 — rather than a command that could not be found.
    [Fact]
    public void StrictRefusesAnUnownedCommandWithItsReason()
    {
        int status = Confined().Run("git status", CancellationToken.None);

        Assert.Equal(126, status);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal("duetui-shell: 'git' is not one of the sandboxed commands\n", errors.ToString());
    }

    // The cost of deciding at run time: the commands ahead of the refusal have already run.
    [Fact]
    public void StrictRunsTheOwnedCommandsAheadOfTheRefusal()
    {
        int status = Confined().Run("echo first; git status; echo never", CancellationToken.None);

        Assert.Equal(126, status);
        Assert.Equal("first\n", output.ToString());
    }

    [Fact]
    public void ExplainNamesEachCommandItDecidedAbout()
    {
        Confined(explains: true).Run("echo hi; git status", CancellationToken.None);

        Assert.Contains("  owned echo hi", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("  native git status — refused: ", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRootIsRefusedRatherThanEscaped()
    {
        int status = Confined().Run("cat ../outside.txt", CancellationToken.None);

        Assert.NotEqual(0, status);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void ExitIsRequestedWithItsStatus()
    {
        Session session = Confined();

        session.Run("exit 3", CancellationToken.None);

        Assert.True(session.WantsExit);
        Assert.Equal(3, session.ExitStatus);
    }

    [Fact]
    public void AStartDirectoryOutsideTheRootIsReportedNotObeyed()
    {
        Session session = new(
            new SessionSettings(root, Path.GetTempPath(), Explains: false, Strict: true),
            output,
            errors);

        Assert.StartsWith("shsh: ", errors.ToString(), StringComparison.Ordinal);
        Assert.Equal(root, session.WorkingDirectory);
    }

    public void Dispose()
    {
        output.Dispose();
        errors.Dispose();
        Directory.Delete(root, recursive: true);
    }
}
