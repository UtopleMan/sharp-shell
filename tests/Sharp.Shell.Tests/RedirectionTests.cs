using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class RedirectionTests
{
    [Fact]
    public void WritesStdoutToAFile()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("echo hi > f.txt");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal("hi\n", File.ReadAllText(Path.Combine(harness.Root, "f.txt")));
    }

    [Fact]
    public void TruncatesOnEachWrite()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "old content\n");

        harness.Run("echo new > f.txt");

        Assert.Equal("new\n", File.ReadAllText(Path.Combine(harness.Root, "f.txt")));
    }

    [Fact]
    public void AppendsWithDoubleAngle()
    {
        using ShellHarness harness = new();

        harness.Run("echo a > f.txt; echo b >> f.txt");

        Assert.Equal("a\nb\n", File.ReadAllText(Path.Combine(harness.Root, "f.txt")));
    }

    [Fact]
    public void ReadsStdinFromAFile()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "one\ntwo\n");

        Assert.Equal("2\n", harness.Run("wc -l < f.txt").Stdout);
    }

    [Fact]
    public void RedirectsStderrToAFile()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("cat missing.txt 2> err.txt");

        Assert.Equal(string.Empty, result.Stderr);
        Assert.Contains("missing.txt", File.ReadAllText(Path.Combine(harness.Root, "err.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public void RedirectsBothStreamsWithAmpersandAngle()
    {
        using ShellHarness harness = new();

        harness.Run("cat missing.txt &> both.txt");

        Assert.Contains("missing.txt", File.ReadAllText(Path.Combine(harness.Root, "both.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public void FoldsStderrIntoStdout()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("cat missing.txt 2>&1");

        Assert.Contains("missing.txt", result.Stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public void RedirectingOutsideTheWorkspaceIsRefusedAndWritesNothing()
    {
        using ShellHarness harness = new();
        string outside = Path.Combine(Path.GetTempPath(), $"duetui-escape-{Guid.NewGuid():N}.txt");

        ShellResult result = harness.Run($"echo pwned > {outside}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(outside), "a redirection outside the workspace created a file");
    }

    [Fact]
    public void ReadsAHereDocument()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("cat << EOF\nline one\nline two\nEOF");

        Assert.Equal("line one\nline two\n", result.Stdout);
    }

    [Fact]
    public void ExpandsAnUnquotedHereDocument()
    {
        using ShellHarness harness = new();

        Assert.Equal("value\n", harness.Run("v=value; cat << EOF\n$v\nEOF").Stdout);
    }

    [Fact]
    public void DoesNotExpandAQuotedHereDocument()
    {
        using ShellHarness harness = new();

        Assert.Equal("$v\n", harness.Run("v=value; cat << 'EOF'\n$v\nEOF").Stdout);
    }

    [Fact]
    public void StripsLeadingTabsWithDashForm()
    {
        using ShellHarness harness = new();

        Assert.Equal("body\n", harness.Run("cat <<- EOF\n\tbody\n\tEOF").Stdout);
    }

    [Fact]
    public void RedirectionTargetsAreExpanded()
    {
        using ShellHarness harness = new();

        harness.Run("name=out.txt; echo hi > $name");

        Assert.Equal("hi\n", File.ReadAllText(Path.Combine(harness.Root, "out.txt")));
    }

    [Fact]
    public void RedirectionWorksInsideAPipeline()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "a\nb\nc\n");

        harness.Run("cat f.txt | head -2 > top.txt");

        Assert.Equal("a\nb\n", File.ReadAllText(Path.Combine(harness.Root, "top.txt")));
    }
}
