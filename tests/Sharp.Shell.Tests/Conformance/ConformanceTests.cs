using Xunit;

namespace Sharp.Shell.Tests.Conformance;

// Layer 1 of the testing plan: the vendored oils corpus, run in-process against Sharp.Shell.
// Seconds rather than minutes, because there is no process per case.
public class ConformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void TheCorpusIsSelectedByItsOwnHeaders()
    {
        Assert.SkipUnless(CorpusSelector.IsAvailable, "the vendored corpus is not in the test output");

        IReadOnlyList<SpecCase> cases = CorpusSelector.Cases();
        int unparsed = cases.Count(specCase => specCase.IsUnparsed);

        output.WriteLine($"files declaring bash: {cases.Select(specCase => specCase.File).Distinct().Count()}");
        output.WriteLine($"cases: {cases.Count}");
        output.WriteLine($"unparsed directives: {unparsed}");

        Assert.NotEmpty(cases);
        Assert.Equal(0, unparsed);
    }

    [Fact]
    public void ConformanceMatchesTheRatchet()
    {
        Assert.SkipUnless(CorpusSelector.IsAvailable, "the vendored corpus is not in the test output");

        IReadOnlyList<SpecCase> cases = CorpusSelector.Cases();
        List<CaseOutcome> outcomes = [.. cases.Select(CorpusRunner.Run)];
        List<CaseOutcome> failures = [.. outcomes.Where(outcome => !outcome.Passed)];

        ReportPassRates(outcomes);

        if (ExpectedFailures.IsRegenerating)
        {
            RegenerateBaseline(failures, outcomes.Count);
            return;
        }

        IReadOnlySet<string> expected = ExpectedFailures.Load();
        AssertRatchet(outcomes, failures, expected);
    }

    // The design doc's v1 bar is stated against the subset of cases that need no external helper,
    // so both numbers are reported: the whole bash-comparable set, and that subset.
    private void ReportPassRates(IReadOnlyList<CaseOutcome> outcomes)
    {
        int passed = outcomes.Count(outcome => outcome.Passed);
        output.WriteLine($"{passed}/{outcomes.Count} bash-comparable cases pass");

        CaseOutcome[] helperFree = [.. outcomes.Where(outcome => !UsesExternalHelper(outcome.Case))];
        int helperFreePassed = helperFree.Count(outcome => outcome.Passed);
        output.WriteLine($"{helperFreePassed}/{helperFree.Length} helper-free cases pass");
    }

    private static bool UsesExternalHelper(SpecCase specCase) =>
        specCase.Body.Contains(".py", StringComparison.Ordinal)
        || specCase.Body.Contains("-helper.sh", StringComparison.Ordinal);

    private void RegenerateBaseline(List<CaseOutcome> failures, int total)
    {
        string? path = ExpectedFailures.SourcePath();
        Assert.NotNull(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path!)!);
        ExpectedFailures.Write(path!, failures, total);
        output.WriteLine($"wrote {failures.Count} expected failures to {path}");
    }

    private void AssertRatchet(
        IReadOnlyList<CaseOutcome> outcomes,
        IReadOnlyList<CaseOutcome> failures,
        IReadOnlySet<string> expected)
    {
        string[] unlisted =
        [
            .. failures
                .Where(failure => !expected.Contains(failure.Case.Id))
                .Take(20)
                .Select(failure => $"  {failure.Case.Id} {failure.Case.Description}\n      {failure.Detail}"),
        ];

        string[] unexpectedPasses =
        [
            .. outcomes
                .Where(outcome => outcome.Passed && expected.Contains(outcome.Case.Id))
                .Take(20)
                .Select(outcome => $"  {outcome.Case.Id} {outcome.Case.Description}"),
        ];

        Assert.True(
            unlisted.Length == 0,
            $"{unlisted.Length} corpus case(s) fail without being listed in expected-failures.txt:\n{string.Join('\n', unlisted)}");

        Assert.True(
            unexpectedPasses.Length == 0,
            $"{unexpectedPasses.Length} listed case(s) now pass — delete their lines from expected-failures.txt:\n{string.Join('\n', unexpectedPasses)}");
    }
}
