using Sharp.Shell.Commands;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Conformance;

public sealed record CaseOutcome(SpecCase Case, bool Passed, string Detail);

// Runs one spec case in its own temp workspace. Each case gets a hard deadline: the corpus
// contains unbounded loops, and the executor honours the token in exactly the places that matter.
public static class CorpusRunner
{
    private static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(5);

    public static CaseOutcome Run(SpecCase specCase)
    {
        if (specCase.IsUnparsed)
        {
            return new CaseOutcome(specCase, false, $"unparsed directive: {specCase.UnparsedDirective}");
        }

        string root = Directory.CreateTempSubdirectory("duetui-corpus").FullName;

        try
        {
            return Compare(specCase, Execute(specCase, root));
        }
        catch (Exception failure)
        {
            return new CaseOutcome(specCase, false, $"threw {failure.GetType().Name}: {failure.Message}");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static ShellResult Execute(SpecCase specCase, string root)
    {
        using CancellationTokenSource deadline = new(CaseTimeout);
        ShellExecutor executor = new(AppletRegistry.CreateDefault(), new CorpusCommandExecutor());

        return executor.Execute(specCase.Body, new ShellState(root), deadline.Token);
    }

    // A case passes when the actual output matches any acceptable alternative; an empty list of
    // alternatives means the case asserts nothing about that stream.
    private static CaseOutcome Compare(SpecCase specCase, ShellResult result)
    {
        if (Rejects(specCase.AcceptableStdout, result.Stdout))
        {
            return new CaseOutcome(specCase, false, $"stdout {Show(specCase.AcceptableStdout[0])} != {Show(result.Stdout)}");
        }

        if (Rejects(specCase.AcceptableStderr, result.Stderr))
        {
            return new CaseOutcome(specCase, false, $"stderr {Show(specCase.AcceptableStderr[0])} != {Show(result.Stderr)}");
        }

        if (specCase.AcceptableStatus.Count > 0 && !specCase.AcceptableStatus.Contains(result.ExitCode))
        {
            return new CaseOutcome(specCase, false, $"status {specCase.AcceptableStatus[0]} != {result.ExitCode}");
        }

        return new CaseOutcome(specCase, true, string.Empty);
    }

    private static bool Rejects(IReadOnlyList<string> acceptable, string actual) =>
        acceptable.Count > 0 && !acceptable.Contains(actual, StringComparer.Ordinal);

    private static string Show(string text)
    {
        string escaped = text.Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
        return escaped.Length <= 90 ? $"'{escaped}'" : $"'{escaped[..90]}…'";
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
