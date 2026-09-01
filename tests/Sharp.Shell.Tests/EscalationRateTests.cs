using Sharp.Shell;
using Sharp.Shell.Commands;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// The design doc's last risk: "if real agent commands escalate more often than expected, the
// confinement gain shrinks toward architecture, not safety", and it should be measured early
// rather than at the end. This is that measurement.
//
// It asserts only that the corpus was readable. The number is information, not a gate — a gate
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
        List<Classification> classifications = [.. commands.Select(classifier.Classify)];

        Report(commands, classifications);
    }

    private void Report(IReadOnlyList<string> commands, IReadOnlyList<Classification> classifications)
    {
        int owned = classifications.Count(classification => classification.Tier == ExecutionTier.Owned);
        int mutating = classifications.Count(classification => classification.Mutates);

        output.WriteLine($"owned (runs confined):  {owned} ({Percent(owned, commands.Count)})");
        output.WriteLine($"native (escalates):     {commands.Count - owned} ({Percent(commands.Count - owned, commands.Count)})");
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
        output.WriteLine("sample escalating lines:");

        foreach (string command in commands
            .Where((_, index) => classifications[index].Tier == ExecutionTier.Native)
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
