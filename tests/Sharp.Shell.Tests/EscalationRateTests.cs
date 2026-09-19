using Sharp.Shell;
using Sharp.Shell.Commands;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// The design doc's last risk: "if real agent commands escalate more often than expected, the
// confinement gain shrinks toward architecture, not safety", and it should be measured early
// rather than at the end. This is that measurement.
//
// Two numbers now, because the per-command approval hook split what "escalates" means. Native is
// the old one: a line the shell does not own every program in. Unrunnable is the one that costs
// something — a line the shell cannot run at all, which is handed over whole and gated as a single
// decision. Every other line runs here and is gated command by command, whatever its tier.
//
// It asserts only that the corpus was readable. The numbers are information, not a gate — a gate
// would make a quiet machine or a different working style look like a regression.
public class EscalationRateTests(ITestOutputHelper output)
{
    [Fact]
    public void MeasureTheEscalationRateOverRealAgentCommands()
    {
        IReadOnlyList<string> commands = SessionCommandCorpus.Commands();
        output.WriteLine($"sessions directory: {SessionCommandCorpus.DefaultDirectory}");
        output.WriteLine($"distinct bash commands mined: {commands.Count}");

        if (commands.Count == 0)
        {
            output.WriteLine("no stored sessions on this machine — nothing to measure");
            return;
        }

        CommandClassifier classifier = new(AppletRegistry.CreateDefault());
        List<Classification> classifications =
            [.. commands.Select(command => classifier.Classify(command, ScratchWorkspace.State))];

        Report(commands, classifications);
    }

    private void Report(IReadOnlyList<string> commands, IReadOnlyList<Classification> classifications)
    {
        int owned = classifications.Count(classification => classification.Tier == ExecutionTier.Owned);
        int unrunnable = classifications.Count(classification => !classification.IsRunnable);
        int mutating = classifications.Count(classification => classification.Mutates);

        output.WriteLine($"owned (every program ours):  {owned} ({Percent(owned, commands.Count)})");
        output.WriteLine($"native (some program not):   {commands.Count - owned} ({Percent(commands.Count - owned, commands.Count)})");
        output.WriteLine($"runnable (gated per command): {commands.Count - unrunnable} ({Percent(commands.Count - unrunnable, commands.Count)})");
        output.WriteLine($"unrunnable (gated whole):     {unrunnable} ({Percent(unrunnable, commands.Count)})");
        output.WriteLine($"of all commands, mutating: {mutating} ({Percent(mutating, commands.Count)})");

        output.WriteLine(string.Empty);
        output.WriteLine("unowned programs by frequency:");

        foreach (var program in classifications
            .SelectMany(classification => classification.UnownedPrograms)
            .GroupBy(program => program, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Take(20))
        {
            output.WriteLine($"  {program.Count(),5}  {program.Key}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("escalation reasons by frequency:");

        foreach (var reason in classifications
            .Where(classification => classification.Reason is not null)
            .GroupBy(classification => classification.Reason!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Take(25))
        {
            output.WriteLine($"  {reason.Count(),5}  {reason.Key}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("reasons a line cannot be run at all, by frequency:");

        foreach (var reason in classifications
            .Where(classification => !classification.IsRunnable)
            .GroupBy(classification => classification.UnrunnableReason!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Take(25))
        {
            output.WriteLine($"  {reason.Count(),5}  {reason.Key}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("sample lines handed over whole:");

        foreach (string command in commands
            .Where((_, index) => !classifications[index].IsRunnable)
            .Take(20))
        {
            output.WriteLine($"  {Trim(command)}");
        }
    }

    private static string Percent(int part, int total) => $"{100.0 * part / total:F1}%";

    private static string Trim(string command)
    {
        string single = command.Replace('\n', ' ');
        return single.Length <= 110 ? single : $"{single[..110]}…";
    }
}
