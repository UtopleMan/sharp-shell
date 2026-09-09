using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class GlobberTests
{
    [Fact]
    public void ExpandsAPatternInSortedOrder()
    {
        using ShellHarness harness = new();
        harness.Write("b.txt", "");
        harness.Write("a.txt", "");
        harness.Write("c.md", "");

        Assert.Equal("a.txt b.txt\n", harness.Run("echo *.txt").Stdout);
    }

    [Fact]
    public void APatternThatMatchesNothingExpandsToItself()
    {
        using ShellHarness harness = new();

        Assert.Equal("*.none\n", harness.Run("echo *.none").Stdout);
    }

    [Fact]
    public void MatchesAcrossDirectorySegments()
    {
        using ShellHarness harness = new();
        harness.Write("src/a.cs", "");
        harness.Write("src/b.cs", "");

        Assert.Equal("src/a.cs src/b.cs\n", harness.Run("echo src/*.cs").Stdout);
    }

    [Fact]
    public void DoubleStarCrossesDirectories()
    {
        using ShellHarness harness = new();
        harness.Write("a.cs", "");
        harness.Write("src/deep/b.cs", "");

        Assert.Equal("a.cs src/deep/b.cs\n", harness.Run("echo **/*.cs").Stdout);
    }

    [Fact]
    public void QuestionMarkMatchesOneCharacter()
    {
        using ShellHarness harness = new();
        harness.Write("ab", "");
        harness.Write("abc", "");

        Assert.Equal("ab\n", harness.Run("echo a?").Stdout);
    }

    [Fact]
    public void BracketsMatchACharacterClass()
    {
        using ShellHarness harness = new();
        harness.Write("a1", "");
        harness.Write("a2", "");
        harness.Write("ax", "");

        Assert.Equal("a1 a2\n", harness.Run("echo a[0-9]").Stdout);
    }

    [Fact]
    public void AStarDoesNotMatchALeadingDot()
    {
        using ShellHarness harness = new();
        harness.Write(".hidden", "");
        harness.Write("visible", "");

        Assert.Equal("visible\n", harness.Run("echo *").Stdout);
    }

    [Fact]
    public void QuotedPatternsAreNotGlobbed()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "");

        Assert.Equal("*.txt\n", harness.Run("echo '*.txt'").Stdout);
    }

    [Fact]
    public void GlobbingNeverLeavesTheWorkspace()
    {
        using ShellHarness harness = new();

        Assert.Equal("../*\n", harness.Run("echo ../*").Stdout);
    }

    [Fact]
    public void ARootedPatternExpandsOutsideTheWorkspaceWhenTheRootIsTheFilesystem()
    {
        using ShellHarness harness = new();
        using ScratchDirectory outside = new();
        outside.Write("a.txt");
        outside.Write("b.txt");

        string expanded = harness.RunFromTheFilesystemRoot($"echo {outside.Path}/*.txt").Stdout;

        Assert.Equal($"{outside.Path}/a.txt {outside.Path}/b.txt\n", expanded);
    }

    [Fact]
    public void ListingARootedPatternOutsideTheWorkspaceSucceeds()
    {
        using ShellHarness harness = new();
        using ScratchDirectory outside = new();
        outside.Write("a.txt");

        ShellResult result = harness.RunFromTheFilesystemRoot($"ls {outside.Path}/*.txt");

        Assert.Equal(string.Empty, result.Stderr);
        Assert.Contains("a.txt", result.Stdout, StringComparison.Ordinal);
    }
}
