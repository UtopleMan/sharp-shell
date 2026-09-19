using System.Diagnostics;
using Sharp.Shell.Tests.Conformance;
using Xunit;
using Sharp.Shell.Tests.Support;

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
// And only cases the classifier calls Owned, so that what is being compared cannot depend on which
// programs happen to be installed on the machine running the suite. A case naming `git` would
// otherwise measure the local `git`.
//
// --strict is still essential. Without it an unowned command would be started for real and the
// case would "pass" without this shell doing anything.
public class SharpCorpusTests(ITestOutputHelper output)
{
    [Fact]
    public void TheBinaryPassesEveryCaseTheLibraryPasses()
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");
        Assert.SkipUnless(CorpusSelector.IsAvailable, "the vendored corpus is not in the test output");

        SpecCase[] comparable = Comparable();
        output.WriteLine($"{comparable.Length} comparable cases through {SharpBinary.Path}");

        Report("pass in-process but not through the binary", comparable, Disagrees);
    }

    // The same corpus read the way a script is read, one line at a time, rather than handed over as
    // a single argument. A command is not always a line — 94% of these cases span several, and
    // 1102 of them contain a multi-line construct — so this is what stops the reader running
    // fragments: an `if` body without its condition, a here-document's text taken for commands.
    //
    // The assertion is agreement between the two forms rather than against the corpus, which
    // isolates the reader: if the line itself is wrong, the test above says so first.
    [Fact]
    public void AScriptFileReadsTheSameAsOneCommandString()
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");
        Assert.SkipUnless(CorpusSelector.IsAvailable, "the vendored corpus is not in the test output");

        SpecCase[] comparable = Comparable();
        int spanningLines = comparable.Count(specCase => specCase.Body.TrimEnd('\n').Contains('\n', StringComparison.Ordinal));
        output.WriteLine($"{comparable.Length} comparable cases, {spanningLines} of them spanning more than one line");

        Report("read differently from a file than from -c", comparable, ReadsDifferently);
    }

    private void Report(string complaint, SpecCase[] cases, Func<SpecCase, string?> check)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<string> failures = [.. cases.Select(check).OfType<string>()];

        output.WriteLine($"{cases.Length - failures.Count}/{cases.Length} agree, in {stopwatch.Elapsed.TotalSeconds:F1}s");

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} case(s) {complaint}:\n{string.Join('\n', failures.Take(20))}");
    }

    // Only cases the library already passes, and only those whose behaviour cannot depend on what
    // is installed on this machine.
    private static SpecCase[] Comparable()
    {
        IReadOnlySet<string> knownFailures = ExpectedFailures.Load();
        Sharp.Shell.CommandClassifier classifier = new(Sharp.Shell.Commands.AppletRegistry.CreateDefault());

        return
        [
            .. CorpusSelector.Cases()
                .Where(specCase => !specCase.IsUnparsed && !knownFailures.Contains(specCase.Id))
                .Where(specCase =>
                    classifier.Classify(specCase.Body, ScratchWorkspace.State).Tier == Sharp.Shell.ExecutionTier.Owned),
        ];
    }

    // Each form gets its own workspace: the first run's side effects must not be the second run's
    // starting conditions, and the script file itself must not appear in a workspace an `ls` case
    // is about to list.
    private static string? ReadsDifferently(SpecCase specCase)
    {
        string scripts = Directory.CreateTempSubdirectory("sharp-corpus-scripts").FullName;
        string inlineRoot = Directory.CreateTempSubdirectory("sharp-corpus-inline").FullName;
        string scriptRoot = Directory.CreateTempSubdirectory("sharp-corpus-script").FullName;

        try
        {
            string path = Path.Combine(scripts, "case.sh");
            File.WriteAllText(path, specCase.Body.EndsWith('\n') ? specCase.Body : $"{specCase.Body}\n");

            SharpResult inline = SharpBinary.RunCommand(specCase.Body, inlineRoot, "--strict");
            SharpResult script = SharpBinary.RunScript(path, scriptRoot, "--strict");

            string inlineOutput = WithoutWorkspace(inline.Stdout, inlineRoot);
            string scriptOutput = WithoutWorkspace(script.Stdout, scriptRoot);

            if (!string.Equals(inlineOutput, scriptOutput, StringComparison.Ordinal))
            {
                return $"  {specCase.Id} {specCase.Description}\n      -c '{Escape(inlineOutput)}' != file '{Escape(scriptOutput)}'";
            }

            return inline.ExitCode == script.ExitCode
                ? null
                : $"  {specCase.Id} {specCase.Description}\n      -c status {inline.ExitCode} != file status {script.ExitCode}";
        }
        finally
        {
            TryDelete(scripts);
            TryDelete(inlineRoot);
            TryDelete(scriptRoot);
        }
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

    // The two forms run in two workspaces, so a case that prints where it is — `pwd`, `realpath` —
    // would differ for a reason that has nothing to do with how its lines were read.
    private static string WithoutWorkspace(string output, string root) =>
        output.Replace(root, "<workspace>", StringComparison.Ordinal);

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
