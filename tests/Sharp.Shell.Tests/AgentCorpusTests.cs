using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Differential;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// The committed counterpart to MinedDifferentialTests. Those commands come from whichever machine
// runs the suite, so their rate means nothing on anyone else's; these come from published
// SWE-bench trajectories and travel with the repository, which is what makes a ratchet possible.
//
// Both floors are measured, not aspirational: the first run printed the real number and it was
// written down with a margin. A change that pushes either rate below its floor is a regression in
// how much of an agent's real bash this shell can answer without a process.
//
// Safety, because these are real commands: only lines that classify Owned AND do not mutate are
// ever executed, so a mined `rm -rf build` or `sed -i` is classified and then left alone.
public class AgentCorpusTests(ITestOutputHelper output)
{
    private const double OwnedRateFloor = 0.68;

    private const double SameOutputRateFloor = 0.95;

    // Enough to be a real sample, few enough that the differential half stays a few seconds.
    private const int ComparedCommands = 100;

    [Fact]
    public void OwnedRateDoesNotRegress()
    {
        string[] commands = Corpus();
        CommandClassifier classifier = new(AppletRegistry.CreateDefault());
        int owned = commands.Count(command => classifier.Classify(command, ScratchWorkspace.State).Tier == ExecutionTier.Owned);
        double rate = (double)owned / commands.Length;

        output.WriteLine($"owned {owned}/{commands.Length} = {rate:P1}");

        Assert.True(rate >= OwnedRateFloor, $"owned rate {rate:P1} fell below the ratchet {OwnedRateFloor:P1}");
    }

    // Both sides run in an empty temp workspace, not in this repository. These commands name paths
    // inside the agent's own checkout — django/, sympy/, sphinx/ — so almost none of them find
    // their file either way, and what is compared is that both shells say the same thing about
    // that. Rooting at the repository instead would compare against build output, and the rate
    // would move with bin/ and .git rather than with the language.
    //
    // Only the output is ratcheted. The exit status is reported and deliberately not asserted on:
    // for a file that is not there BSD sed exits 1 and GNU sed exits 2, this applet follows GNU,
    // and a floor over that number would pass on Linux and fail on macOS — the same trap
    // LanguageCorpus documents for BSD and GNU column formatting.
    [Fact]
    public void AgreementWithBashDoesNotRegress()
    {
        Assert.SkipUnless(BashOracle.IsAvailable, "no system bash to compare against");

        string[] sampled = Sample(Safe(Corpus()));
        string[] compared = [.. sampled.Where(BashOracle.CanCompare)];
        using ShellHarness harness = new();

        if (compared.Length < sampled.Length)
        {
            output.WriteLine($"{sampled.Length - compared.Length} sampled command(s) answer differently on every run and cannot be replayed");
        }

        output.WriteLine($"bash {BashOracle.Version}, both sides rooted at an empty workspace");
        output.WriteLine($"comparing {compared.Length} owned, non-mutating commands");

        Comparison[] comparisons = [.. compared.Select(command => Compare(command, harness.Root))];
        int sameOutput = comparisons.Count(comparison => comparison.SameOutput);
        int sameStatus = comparisons.Count(comparison => comparison.SameStatus);
        double rate = (double)sameOutput / comparisons.Length;

        output.WriteLine($"{sameOutput}/{comparisons.Length} produce the same output = {rate:P1}");
        output.WriteLine($"{sameStatus}/{comparisons.Length} also carry the same exit status (reported, not ratcheted)");

        foreach (Comparison mismatch in comparisons.Where(comparison => !comparison.SameOutput).Take(15))
        {
            output.WriteLine($"  {mismatch.Command}");
        }

        Assert.True(rate >= SameOutputRateFloor, $"output agreement {rate:P1} fell below the ratchet {SameOutputRateFloor:P1}");
    }

    private static Comparison Compare(string command, string root)
    {
        OracleResult oracle = BashOracle.Run(command, root);
        ShellResult mine = RunOurs(command, root);

        return new Comparison(
            command,
            string.Equals(oracle.Stdout, mine.Stdout, StringComparison.Ordinal),
            oracle.ExitCode == mine.ExitCode);
    }

    private static ShellResult RunOurs(string command, string root)
    {
        ShellExecutor executor = new(AppletRegistry.CreateDefault(), new NotSupportedCommandExecutor());
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));

        return executor.Execute(command, new ShellState(root), deadline.Token);
    }

    private static string[] Safe(IEnumerable<string> commands)
    {
        CommandClassifier classifier = new(AppletRegistry.CreateDefault());

        return
        [
            .. commands
                .Select(command => (command, classification: classifier.Classify(command, ScratchWorkspace.State)))
                .Where(pair => pair.classification.Tier == ExecutionTier.Owned && !pair.classification.Mutates)
                .Select(pair => pair.command),
        ];
    }

    // A fixed stride rather than a random draw, so the same commands are compared on every run and
    // a rate change means a behaviour change.
    private static string[] Sample(string[] commands)
    {
        if (commands.Length <= ComparedCommands)
        {
            return commands;
        }

        int stride = commands.Length / ComparedCommands;
        return [.. commands.Where((_, index) => index % stride == 0).Take(ComparedCommands)];
    }

    private static string[] Corpus()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "corpus", "agent-commands.txt");

        return
        [
            .. File.ReadAllLines(path).Where(line => line.Length > 0 && !line.StartsWith('#')),
        ];
    }

    private sealed record Comparison(string Command, bool SameOutput, bool SameStatus);
}
