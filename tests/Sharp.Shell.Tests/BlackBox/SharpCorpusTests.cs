using System.Diagnostics;
using Sharp.Shell.Tests.Conformance;
using Xunit;

namespace Sharp.Shell.Tests.BlackBox;

// The oils corpus is designed for exactly this: upstream runs bash, dash, mksh and zsh as
// subprocesses against it. Layer 1 runs it in-process against the library for speed; this runs the
// same cases through the sharp *binary*, so anything the process boundary can break — argument
// passing, stdout encoding, exit status, the REPL's own plumbing — is covered too.
//
// Two filters, and the second one is the subtle half.
//
// Only cases the library already passes are run: a case the library fails tells us nothing new
// here, while one it passes and the binary fails is a bug in the binary.
//
// And only cases the classifier calls Owned. Layer 1 drives the executor directly, so an unowned
// command inside a case fails on its own and the rest of the line still runs — that is the right
// way to measure the *language*. The binary honours Rule 2 instead: one unowned name sends the
// whole line to the native tier, so under --strict nothing runs at all. Comparing across that
// difference measures the rule, not the binary, and 191 cases "regressed" for exactly this reason
// on the first run of this test.
//
// --strict is still essential. With the fall-through on, an unowned command would be answered by
// the real bash on the machine and the case would "pass" without this shell doing anything.
public class SharpCorpusTests(ITestOutputHelper output)
{
    [Fact]
    public void TheBinaryPassesEveryCaseTheLibraryPasses()
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");
        Assert.SkipUnless(CorpusSelector.IsAvailable, "the vendored corpus is not in the test output");

        IReadOnlySet<string> knownFailures = ExpectedFailures.Load();
        Sharp.Shell.CommandClassifier classifier = new(Sharp.Shell.Commands.AppletRegistry.CreateDefault());

        SpecCase[] passing =
        [
            .. CorpusSelector.Cases().Where(specCase => !specCase.IsUnparsed && !knownFailures.Contains(specCase.Id)),
        ];

        SpecCase[] expectedToPass =
        [
            .. passing.Where(specCase =>
                classifier.Classify(specCase.Body).Tier == Sharp.Shell.ExecutionTier.Owned),
        ];

        output.WriteLine(
            $"{passing.Length} cases pass in-process, of which {expectedToPass.Length} classify Owned " +
            $"and are therefore comparable; running each through {SharpBinary.Path}");

        Stopwatch stopwatch = Stopwatch.StartNew();
        List<string> regressions = [];

        foreach (SpecCase specCase in expectedToPass)
        {
            if (Disagrees(specCase) is { } detail)
            {
                regressions.Add(detail);
            }
        }

        output.WriteLine($"{expectedToPass.Length - regressions.Count}/{expectedToPass.Length} agree, in {stopwatch.Elapsed.TotalSeconds:F1}s");

        Assert.True(
            regressions.Count == 0,
            $"{regressions.Count} case(s) pass in-process but not through the binary:\n{string.Join('\n', regressions.Take(20))}");
    }

    private static string? Disagrees(SpecCase specCase)
    {
        string root = Directory.CreateTempSubdirectory("sharp-corpus").FullName;

        try
        {
            SharpResult result = SharpBinary.RunCommand(specCase.Body, root, "--strict");

            if (specCase.AcceptableStdout.Count > 0 && !specCase.AcceptableStdout.Contains(result.Stdout, StringComparer.Ordinal))
            {
                return $"  {specCase.Id} {specCase.Description}\n      stdout '{Escape(specCase.AcceptableStdout[0])}' != '{Escape(result.Stdout)}'";
            }

            return specCase.AcceptableStatus.Count > 0 && !specCase.AcceptableStatus.Contains(result.ExitCode)
                ? $"  {specCase.Id} {specCase.Description}\n      status {specCase.AcceptableStatus[0]} != {result.ExitCode}"
                : null;
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string Escape(string text)
    {
        string escaped = text.Replace("\n", "\\n", StringComparison.Ordinal);
        return escaped.Length <= 80 ? escaped : $"{escaped[..80]}…";
    }

    private static void TryDelete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
