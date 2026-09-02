using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Xunit;

namespace Sharp.Shell.Tests;

// What the host gates on. Classification reports the concrete files an owned line will open, so the
// paths here are the ones a rule is matched against: absolute, and resolved after quoting, globbing
// and any cd this pass could follow.
public class FileOperandTests : IDisposable
{
    private readonly string workspace = Directory.CreateTempSubdirectory("sharp-operands").FullName;

    public FileOperandTests()
    {
        Directory.CreateDirectory(Path.Combine(workspace, "non-ai"));
        Directory.CreateDirectory(Path.Combine(workspace, "sub"));
        File.WriteAllText(Path.Combine(workspace, "notes.txt"), "one\ntwo\n");
        File.WriteAllText(Path.Combine(workspace, "other.txt"), "three\n");
        File.WriteAllText(Path.Combine(workspace, "non-ai", "secret.md"), "private\n");
        File.WriteAllText(Path.Combine(workspace, "sub", "inner.txt"), "inner\n");
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("cat notes.txt", "notes.txt")]
    [InlineData("head -n 3 notes.txt", "notes.txt")]
    [InlineData("tail -5 notes.txt", "notes.txt")]
    [InlineData("wc -l notes.txt", "notes.txt")]
    [InlineData("sed -n '1,5p' notes.txt", "notes.txt")]
    [InlineData("awk '{print $1}' notes.txt", "notes.txt")]
    [InlineData("grep needle notes.txt", "notes.txt")]
    [InlineData("cut -d : -f 1 notes.txt", "notes.txt")]
    [InlineData("sort -u notes.txt", "notes.txt")]
    [InlineData("ls sub", "sub")]
    [InlineData("find sub -name '*.txt'", "sub")]
    [InlineData("read line < notes.txt", "notes.txt")]
    [InlineData("cat < notes.txt", "notes.txt")]
    public void AReadingCommandNamesTheFileItOpens(string commandLine, string expected)
    {
        Classification classification = Classify(commandLine);

        Assert.Equal([Absolute(expected)], classification.Reads);
        Assert.Empty(classification.Writes);
        Assert.True(classification.KnowsEveryFile);
    }

    [Theory]
    [InlineData("echo hi > out.txt", "out.txt")]
    [InlineData("echo hi >> out.txt", "out.txt")]
    [InlineData("printf x > sub/out.txt", "sub/out.txt")]
    [InlineData("rm -f out.txt", "out.txt")]
    [InlineData("touch out.txt", "out.txt")]
    [InlineData("mkdir -p sub/deeper", "sub/deeper")]
    public void AWritingCommandNamesTheFileItChanges(string commandLine, string expected)
    {
        Classification classification = Classify(commandLine);

        Assert.Equal([Absolute(expected)], classification.Writes);
        Assert.True(classification.KnowsEveryFile);
    }

    [Fact]
    public void SedInPlaceBothReadsAndWritesItsOperands()
    {
        Classification classification = Classify("sed -i 's/one/1/' notes.txt");

        Assert.Equal([Absolute("notes.txt")], classification.Reads);
        Assert.Equal([Absolute("notes.txt")], classification.Writes);
    }

    [Fact]
    public void TheGrepPatternIsNotAFile()
    {
        Assert.Equal([Absolute("notes.txt")], Classify("grep -n secret notes.txt").Reads);
    }

    [Fact]
    public void TheAwkProgramIsNotAFile()
    {
        Assert.Equal([Absolute("notes.txt")], Classify("awk '{print}' notes.txt").Reads);
    }

    [Fact]
    public void AnAwkAssignmentOperandIsNotAFile()
    {
        Assert.Equal([Absolute("notes.txt")], Classify("awk '{print}' count=1 notes.txt").Reads);
    }

    [Fact]
    public void CopyingReadsItsSourcesAndWritesItsDestination()
    {
        Classification classification = Classify("cp notes.txt other.txt sub");

        Assert.Equal([Absolute("notes.txt"), Absolute("other.txt")], classification.Reads);
        Assert.Equal([Absolute("sub")], classification.Writes);
    }

    [Fact]
    public void MovingReadsItsSourceAndWritesItsDestination()
    {
        Classification classification = Classify("mv notes.txt sub/moved.txt");

        Assert.Equal([Absolute("notes.txt")], classification.Reads);
        Assert.Equal([Absolute("sub/moved.txt")], classification.Writes);
    }

    // The two evasions a substring rule on the command line cannot survive, and the reason path
    // matching is done on the resolved operand instead.
    [Theory]
    [InlineData("cat non-ai/secret.md")]
    [InlineData("cat \"no\"\"n-ai\"/secret.md")]
    [InlineData("cat n*/secret.md")]
    [InlineData("cat ./non-ai/secret.md")]
    public void QuotingAndGlobbingResolveToTheSameFile(string commandLine)
    {
        Assert.Equal([Absolute("non-ai/secret.md")], Classify(commandLine).Reads);
    }

    [Fact]
    public void AQuotedWildcardIsNotAGlob()
    {
        Assert.Equal([Absolute("n*/secret.md")], Classify("cat \"n*\"/secret.md").Reads);
    }

    [Fact]
    public void AGlobNamesEveryFileItMatches()
    {
        Classification classification = Classify("cat *.txt");

        Assert.Equal([Absolute("notes.txt"), Absolute("other.txt")], classification.Reads);
    }

    [Fact]
    public void OperandsAfterACdResolveWhereTheCdLanded()
    {
        Classification classification = Classify("cd sub && cat inner.txt");

        Assert.Equal([Absolute("sub/inner.txt")], classification.Reads);
        Assert.True(classification.KnowsEveryFile);
    }

    [Theory]
    [InlineData("cat \"$file\"")]
    [InlineData("echo hi > $out")]
    [InlineData("cat $(basename /a/notes.txt)")]
    [InlineData("if true; then cd sub; fi; cat inner.txt")]
    [InlineData("cd $target && cat inner.txt")]
    public void AFileOnlyKnownAtRunTimeIsReportedAsUnknownRatherThanGuessed(string commandLine)
    {
        Classification classification = Classify(commandLine);

        Assert.False(classification.KnowsEveryFile);
        Assert.DoesNotContain(classification.Reads, path => path.Contains("inner.txt", StringComparison.Ordinal));
    }

    // A native line runs as a real process with no preopen. Naming a subset of its files would read
    // as a guarantee the host cannot make, so it names none.
    [Fact]
    public void ANativeLineNamesNoFiles()
    {
        Classification classification = Classify("curl https://example.com > out.txt");

        Assert.Equal(ExecutionTier.Native, classification.Tier);
        Assert.Empty(classification.Reads);
        Assert.Empty(classification.Writes);
        Assert.False(classification.KnowsEveryFile);
    }

    [Fact]
    public void APipelineNamesTheFilesOfEveryStage()
    {
        Classification classification = Classify("cat notes.txt | grep one > out.txt");

        Assert.Equal([Absolute("notes.txt")], classification.Reads);
        Assert.Equal([Absolute("out.txt")], classification.Writes);
    }

    [Fact]
    public void ACommandWithoutOperandsNamesNothing()
    {
        Classification classification = Classify("echo hello | wc -l");

        Assert.Empty(classification.Reads);
        Assert.Empty(classification.Writes);
        Assert.True(classification.KnowsEveryFile);
    }

    private Classification Classify(string commandLine) =>
        new CommandClassifier(AppletRegistry.CreateDefault()).Classify(commandLine, new ShellState(workspace));

    private string Absolute(string relative) =>
        Path.GetFullPath(Path.Combine(workspace, relative.Replace('/', Path.DirectorySeparatorChar)));
}
