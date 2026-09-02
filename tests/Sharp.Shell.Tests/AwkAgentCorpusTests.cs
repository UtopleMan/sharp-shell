using Sharp.Shell;
using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Differential;
using Xunit;

namespace Sharp.Shell.Tests;

// The other half of the bar. The differential suite proves the language is right; this proves the
// *commands agents actually write* are both owned and right — a construct can be implemented
// perfectly and still escalate because classification cannot see it, and that would be worth
// nothing.
public class AwkAgentCorpusTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, string> Fixtures = new(StringComparer.Ordinal)
    {
        ["table"] = "alpha 10 150\nbeta 20 90\ngamma 30 220\n",
        ["passwd"] = "root:x:0:0:root:/root:/bin/sh\nuser:x:1000:1000::/home/user:/bin/bash\n",
        ["csv"] = "name,count,total\nalpha,2,4\nbeta,3,9\n",
        ["lines"] = "one\ntwo\nthree\nfour\nfive\n",
        ["dupes"] = "a\nb\na\nc\nb\na\n",
        ["ragged"] = "  padded   \n\tone tab\t\nplain\n",
        ["paragraphs"] = "a b\n\nc\nd e\n\n\nf\n",
    };

    public static TheoryData<int> LineNumbers => [.. Enumerable.Range(0, Corpus.Count)];

    private static IReadOnlyList<CorpusLine> Corpus { get; } = ReadCorpus();

    [Fact]
    public void TheCorpusIsBigEnoughToMeanSomething()
    {
        Assert.True(Corpus.Count >= 40, $"only {Corpus.Count} one-liners");
    }

    [Theory]
    [MemberData(nameof(LineNumbers))]
    public void EveryOneLinerIsOwned(int lineNumber)
    {
        CorpusLine line = Corpus[lineNumber];
        using ShellHarnessForCorpus harness = new(line, Fixtures);

        output.WriteLine(harness.CommandLine);
        Classification classification = harness.Classify();

        Assert.Equal(ExecutionTier.Owned, classification.Tier);
        Assert.Empty(classification.UnownedPrograms);
    }

    [Theory]
    [MemberData(nameof(LineNumbers))]
    public void EveryOneLinerMatchesBothOracles(int lineNumber)
    {
        Assert.SkipUnless(AwkOracle.IsAvailable, "one of the awk oracles is missing on this machine");

        CorpusLine line = Corpus[lineNumber];
        using ShellHarnessForCorpus harness = new(line, Fixtures);

        Assert.SkipUnless(
            AwkOracle.CanCompare(harness.Arguments, string.Empty),
            "this one-liner's own answer moved between recording runs, so no recording of it can be compared");

        output.WriteLine(harness.CommandLine);
        string actual = harness.RunOurs();

        foreach (ExternalOracle oracle in AwkOracle.All)
        {
            OracleResult expected = oracle.Run(harness.Arguments, string.Empty, harness.Root);

            Assert.Equal(expected.Stdout, actual);
        }
    }

    // A single quote in a program would need shell quoting the corpus does not do, so the format
    // simply forbids it rather than growing a quoting rule.
    [Fact]
    public void NoOneLinerNeedsShellQuoting()
    {
        Assert.All(Corpus, line => Assert.All(line.Arguments, argument =>
            Assert.DoesNotContain('\'', argument)));
    }

    private static IReadOnlyList<CorpusLine> ReadCorpus()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "corpus", "awk-agent-oneliners.txt");

        return
        [
            .. File.ReadLines(path)
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(CorpusLine.Parse),
        ];
    }
}

internal sealed record CorpusLine(string Fixture, IReadOnlyList<string> Arguments)
{
    public static CorpusLine Parse(string line)
    {
        string[] parts = line.Split('\t');

        return new CorpusLine(parts[0], [.. parts.Skip(1)]);
    }
}

// One temporary workspace holding the fixture the line reads, so both our applet and the oracles see
// the same file under the same name.
internal sealed class ShellHarnessForCorpus : IDisposable
{
    private readonly ShellExecutor executor = new(AppletRegistry.CreateDefault(), new NotSupportedCommandExecutor());

    public ShellHarnessForCorpus(CorpusLine line, IReadOnlyDictionary<string, string> fixtures)
    {
        Root = Directory.CreateTempSubdirectory("duetui-awk-corpus").FullName;
        string fixtureName = $"{line.Fixture}.txt";
        File.WriteAllText(Path.Combine(Root, fixtureName), fixtures[line.Fixture]);

        Arguments = [.. line.Arguments, fixtureName];
        CommandLine = $"awk {string.Join(' ', line.Arguments.Select(Quoted))} {fixtureName}";
    }

    public string Root { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string CommandLine { get; }

    public Classification Classify() => new CommandClassifier(AppletRegistry.CreateDefault()).Classify(CommandLine);

    public string RunOurs() => executor.Execute(CommandLine, new ShellState(Root), CancellationToken.None).Stdout;

    private static string Quoted(string argument) =>
        argument.StartsWith('-') && !argument.Contains(' ', StringComparison.Ordinal) ? argument : $"'{argument}'";

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
