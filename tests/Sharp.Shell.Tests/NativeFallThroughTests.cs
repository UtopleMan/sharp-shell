using Sharp.Shell;
using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// Rule 2, all-or-nothing per invocation. The failure this guards against is double execution: a
// line that partly ran here and then ran again in a real shell would delete twice, move twice,
// append twice. So the assertion is not "the tier is Native" — it is "the workspace is untouched".
public class NativeFallThroughTests
{
    [Theory]
    [InlineData("rm f.txt && git status")]
    [InlineData("mkdir made | jq '.'")]
    [InlineData("touch created; curl http://example.com")]
    [InlineData("mv a.txt b.txt && npm install")]
    [InlineData("cp a.txt copy.txt; docker ps")]
    [InlineData("rm -rf dir && echo done | sed --debug 's/x/y/'")]
    [InlineData("echo replaced > f.txt && git add f.txt")]
    public void AFallThroughLinePerformsNoWritesAtAll(string commandLine)
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "original\n");
        harness.Write("a.txt", "a\n");
        harness.Write("dir/inner.txt", "inner\n");

        IReadOnlyList<FileFingerprint> before = Fingerprint(harness.Root);

        ShellRun run = harness.Classified(commandLine);

        Assert.Equal(ExecutionTier.Native, run.Classification.Tier);
        Assert.Null(run.Result);
        Assert.Equal(before, Fingerprint(harness.Root));
    }

    [Fact]
    public void AnOwnedLineStillRuns()
    {
        using ShellHarness harness = new();

        ShellRun run = harness.Classified("echo hi");

        Assert.Equal(ExecutionTier.Owned, run.Classification.Tier);
        Assert.NotNull(run.Result);
        Assert.Equal("hi\n", run.Result!.Stdout);
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

    private static IReadOnlyList<FileFingerprint> Fingerprint(string root) =>
    [
        .. Directory
            .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => new FileFingerprint(
                Path.GetRelativePath(root, path),
                File.Exists(path) ? new FileInfo(path).Length : -1,
                File.Exists(path) ? File.ReadAllText(path) : string.Empty)),
    ];

    private sealed record FileFingerprint(string Path, long Length, string Content);
}
