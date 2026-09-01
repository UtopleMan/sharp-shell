using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests.Differential;

// The other half of Layer 2: commands this project's own agent really ran, compared against real
// bash. No ratchet and no assertion on the rate — the corpus is mined from the local machine, so a
// committed expectation would mean nothing on anyone else's. The number is the deliverable.
//
// Safety, because these are real commands: only lines that classify Owned AND do not mutate are
// run. A mined `rm` with an absolute path would otherwise be handed to a real shell.
//
// Both sides run rooted at this repository rather than an empty temp directory. Mined commands
// routinely open with `cd <repo> && …`, and in a temp workspace every one of them fails here and
// succeeds in bash — which measures the sandbox boundary, not language agreement. Rooting both
// sides at the repo compares what this test is actually for. Everything run is read-only.
public class MinedDifferentialTests(ITestOutputHelper output)
{
    [Fact]
    public void MeasureAgreementWithBashOverRealAgentCommands()
    {
        Assert.SkipUnless(BashOracle.IsAvailable, "no system bash to compare against");

        CommandClassifier classifier = new(AppletRegistry.CreateDefault());
        string[] safe =
        [
            .. SessionCommandCorpus.Commands()
                .Select(command => (command, classification: classifier.Classify(command)))
                .Where(pair => pair.classification.Tier == ExecutionTier.Owned && !pair.classification.Mutates)
                .Select(pair => pair.command),
        ];

        output.WriteLine($"bash {BashOracle.Version}");
        output.WriteLine($"mined commands that are owned and non-mutating: {safe.Length}");

        if (safe.Length == 0)
        {
            output.WriteLine("nothing to compare on this machine");
            return;
        }

        Report(safe);
    }

    private static ShellResult RunOurs(string command, string root)
    {
        ShellExecutor executor = new(AppletRegistry.CreateDefault(), new NotSupportedCommandExecutor());
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));

        return executor.Execute(command, new ShellState(root), deadline.Token);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sharp.slnx")))
            {
                return directory.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private void Report(IReadOnlyList<string> commands)
    {
        List<string> mismatched = [];

        string root = RepositoryRoot();
        output.WriteLine($"both sides rooted at {root}");

        foreach (string command in commands)
        {
            OracleResult oracle = BashOracle.Run(command, root);
            ShellResult mine = RunOurs(command, root);

            if (string.Equals(oracle.Stdout, mine.Stdout, StringComparison.Ordinal) && oracle.ExitCode == mine.ExitCode)
            {
                continue;
            }

            mismatched.Add($"{command}\n      bash exit {oracle.ExitCode}, ours {mine.ExitCode}");
        }

        output.WriteLine($"{commands.Count - mismatched.Count}/{commands.Count} agree with bash");

        foreach (string mismatch in mismatched.Take(15))
        {
            output.WriteLine($"  {mismatch}");
        }
    }
}
