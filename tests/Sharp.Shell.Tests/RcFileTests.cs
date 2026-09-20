using Sharp;
using Xunit;

namespace Sharp.Shell.Tests;

// The rc files are the binary's, not the core's: finding them needs a home directory and a start-up
// order. Every test here points HOME at its own temp directory, so the suite never reads the real one.
public class RcFileTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("sharp-rc-root").FullName;

    private readonly string home = Directory.CreateTempSubdirectory("sharp-rc-home").FullName;

    private readonly StringWriter output = new();

    private readonly StringWriter errors = new();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Directory.Delete(root, recursive: true);
        Directory.Delete(home, recursive: true);
    }

    private Session Started(bool interactive, bool readsRcFiles = true)
    {
        Session session = new(new SessionSettings(root, root, false, true, readsRcFiles, home), output, errors);
        session.LoadStartupFiles(interactive);
        return session;
    }

    private void WriteRcFile(string name, string content) =>
        File.WriteAllText(Path.Combine(home, name), content);

    [Fact]
    public void TheEnvironmentFileIsReadEvenWhenTheShellIsNotInteractive()
    {
        WriteRcFile(".shshenv", "MARKER=from-env\n");

        Started(interactive: false).Run("echo $MARKER", CancellationToken.None);

        Assert.Equal("from-env\n", output.ToString());
    }

    [Fact]
    public void TheInteractiveFileIsNotReadForACommand()
    {
        WriteRcFile(".shshrc", "MARKER=from-rc\n");

        Started(interactive: false).Run("echo \"[$MARKER]\"", CancellationToken.None);

        Assert.Equal("[]\n", output.ToString());
    }

    [Fact]
    public void TheInteractiveFileIsReadWhenTheShellIsInteractive()
    {
        WriteRcFile(".shshrc", "MARKER=from-rc\n");

        Started(interactive: true).Run("echo $MARKER", CancellationToken.None);

        Assert.Equal("from-rc\n", output.ToString());
    }

    [Fact]
    public void TheEnvironmentFileIsReadBeforeTheInteractiveOne()
    {
        WriteRcFile(".shshenv", "ORDER=env\n");
        WriteRcFile(".shshrc", "ORDER=$ORDER-then-rc\n");

        Started(interactive: true).Run("echo $ORDER", CancellationToken.None);

        Assert.Equal("env-then-rc\n", output.ToString());
    }

    [Fact]
    public void AnAliasFromTheInteractiveFileIsUsable()
    {
        WriteRcFile(".shshrc", "alias greet='echo hello'\n");

        Started(interactive: true).Run("greet", CancellationToken.None);

        Assert.Equal("hello\n", output.ToString());
    }

    [Fact]
    public void NoRcFilesIsSilence()
    {
        Started(interactive: true).Run("true", CancellationToken.None);

        Assert.Equal(string.Empty, errors.ToString());
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void NorcSkipsBothFiles()
    {
        WriteRcFile(".shshenv", "MARKER=from-env\n");
        WriteRcFile(".shshrc", "MARKER=from-rc\n");

        Started(interactive: true, readsRcFiles: false).Run("echo \"[$MARKER]\"", CancellationToken.None);

        Assert.Equal("[]\n", output.ToString());
    }

    // An unusable shell is worse than a broken alias, so a bad rc file is reported and the shell starts.
    [Fact]
    public void ABrokenRcFileIsReportedWithItsNameAndTheShellStillStarts()
    {
        WriteRcFile(".shshenv", "echo unterminated '\n");

        Session session = Started(interactive: false);

        Assert.Contains(".shshenv", errors.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, session.Run("true", CancellationToken.None));
    }

    [Fact]
    public void TheEnvironmentFileCanChangeTheWorkingDirectory()
    {
        Directory.CreateDirectory(Path.Combine(root, "inner"));
        WriteRcFile(".shshenv", "cd inner\n");

        Started(interactive: false).Run("pwd", CancellationToken.None);

        Assert.Equal($"{Path.Combine(root, "inner")}\n", output.ToString());
    }
}
