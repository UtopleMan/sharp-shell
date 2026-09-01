using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Expansion;
using Sharp.Shell.Lexing;
using Sharp.Shell.Parsing;

namespace Sharp.Shell;

// Rule 2, all-or-nothing per invocation: resolve every command name and flag in the line first;
// if all are owned, execute in the sandbox, and if even one is not, hand the *original* command
// string to the native tier untouched.
//
// No partial execution, ever. `ls | awk '…'` goes entirely native rather than running `ls` here
// and again there. This matters most for the mutating applets, where a fall-through after a
// partly executed `rm` would delete twice.
public sealed class CommandClassifier(AppletRegistry applets)
{
    public Classification Classify(string commandLine)
    {
        ParseResult parsed = Parser.Parse(commandLine);

        if (!parsed.IsParsed)
        {
            return Classification.Native([], mutates: false, parsed.UnsupportedReason!);
        }

        Inspection inspection = new(applets);
        inspection.Walk(parsed.Program!);

        return inspection.Reason is null
            ? Classification.Owned(inspection.Mutates)
            : Classification.Native([.. inspection.UnownedPrograms], inspection.Mutates, inspection.Reason);
    }
}

internal sealed class Inspection(AppletRegistry applets)
{
    // Form checks need somewhere to look variables up. Nothing is read from or written to disk
    // here, and no command substitution is run — the inner source is classified, never executed.
    private static readonly ShellState FormCheckState = new(Path.GetTempPath());

    private readonly List<string> unownedPrograms = [];

    public IReadOnlyList<string> UnownedPrograms => unownedPrograms;

    public bool Mutates { get; private set; }

    public string? Reason { get; private set; }

    public void Walk(ShellNode node)
    {
        switch (node)
        {
            case SequenceNode sequence:
                WalkAll(sequence.Items);
                return;
            case PipelineNode pipeline:
                WalkAll(pipeline.Stages);
                return;
            case AndOrNode andOr:
                Walk(andOr.Left);
                Walk(andOr.Right);
                return;
            case SubshellNode subshell:
                Walk(subshell.Body);
                return;
            case BraceGroupNode group:
                Walk(group.Body);
                return;
            case IfNode conditional:
                WalkIf(conditional);
                return;
            case WhileNode loop:
                Walk(loop.Condition);
                Walk(loop.Body);
                return;
            case ForNode loop:
                WalkFor(loop);
                return;
            case CaseNode branch:
                WalkCase(branch);
                return;
            case SimpleCommand command:
                WalkCommand(command);
                return;
        }
    }

    private void WalkAll(IReadOnlyList<ShellNode> nodes)
    {
        foreach (ShellNode node in nodes)
        {
            Walk(node);
        }
    }

    private void WalkIf(IfNode conditional)
    {
        foreach (ConditionalBranch branch in conditional.Branches)
        {
            Walk(branch.Condition);
            Walk(branch.Body);
        }

        if (conditional.ElseBody is not null)
        {
            Walk(conditional.ElseBody);
        }
    }

    private void WalkFor(ForNode loop)
    {
        foreach (Word item in loop.Items)
        {
            CheckWord(item);
        }

        Walk(loop.Body);
    }

    private void WalkCase(CaseNode branch)
    {
        CheckWord(branch.Subject);

        foreach (CaseArm arm in branch.Arms)
        {
            foreach (Word pattern in arm.Patterns)
            {
                CheckWord(pattern);
            }

            Walk(arm.Body);
        }
    }

    private void WalkCommand(SimpleCommand command)
    {
        foreach (Assignment assignment in command.Assignments)
        {
            CheckWord(assignment.Value);
        }

        foreach (Word word in command.Words)
        {
            CheckWord(word);
        }

        foreach (Redirection redirection in command.Redirections)
        {
            CheckRedirection(redirection);
        }

        if (command.Words.Count == 0)
        {
            return;
        }

        CheckCommandName(command);
    }

    private void CheckRedirection(Redirection redirection)
    {
        if (redirection.Target is not null)
        {
            CheckWord(redirection.Target);
        }

        Mutates |= redirection.Kind
            is RedirectionKind.Output
            or RedirectionKind.Append
            or RedirectionKind.OutputAndError;
    }

    private void CheckCommandName(SimpleCommand command)
    {
        Word name = command.Words[0];

        if (!name.IsFullyLiteral)
        {
            Escalate(null, "the command name needs expansion before it is known");
            return;
        }

        string program = name.LiteralText;

        if (program.Contains('/', StringComparison.Ordinal) || program.Contains('\\', StringComparison.Ordinal))
        {
            Escalate(program, $"'{program}' is an explicit path, so it is not an owned command");
            return;
        }

        if (!applets.TryGet(program, out IApplet applet))
        {
            Escalate(program, $"'{program}' is not one of the sandboxed commands");
            return;
        }

        CheckFlags(command, applet, program);
    }

    private void CheckFlags(SimpleCommand command, IApplet applet, string program)
    {
        List<string> words = [.. command.Words.Skip(1).Select(word => word.LiteralText)];
        IReadOnlyList<string> arguments = FlagReader.ExpandShortFlagBundles(words, applet.BundleableFlags);

        // A word that is program text cannot be checked while it still needs expanding: `sed
        // "s/$x/y/"` reaches here as `s//y/`, which parses cleanly and is not the script that would
        // run. Only those words, though — the data operands beside them may expand freely.
        if (applet.ProgramTextArguments(words).Any(position => !command.Words[position + 1].IsFullyLiteral))
        {
            Escalate(program, $"'{program}' needs its program before expansion to be checkable");
            return;
        }

        FlagSupport support = applet.CheckFlags(arguments);

        if (!support.IsSupported)
        {
            Escalate(program, $"'{program}' does not implement {support.UnsupportedFlag}");
            return;
        }

        Mutates |= applet.MutatesWith(arguments);
    }

    // A command substitution is classified, never run: its inner program set counts towards this
    // line's, because `echo $(git rev-parse HEAD)` needs git just as much as `git rev-parse` does.
    private void CheckWord(Word word)
    {
        foreach (WordPart part in word.Parts)
        {
            CheckPart(part);
        }
    }

    private void CheckPart(WordPart part)
    {
        if (part.Nested is not null)
        {
            foreach (WordPart nested in part.Nested)
            {
                CheckPart(nested);
            }
        }

        switch (part.Kind)
        {
            case WordPartKind.CommandSubstitution:
                Absorb(new CommandClassifier(applets).Classify(part.Text));
                return;
            case WordPartKind.Parameter:
                CheckParameterForm(part.Text);
                return;
        }
    }

    private void CheckParameterForm(string expression)
    {
        ParameterResult result = ParameterExpander.Expand(expression, FormCheckState, static word => word);

        if (result.UnsupportedReason is not null)
        {
            Escalate(null, result.UnsupportedReason);
        }
    }

    private void Absorb(Classification inner)
    {
        Mutates |= inner.Mutates;

        foreach (string program in inner.UnownedPrograms)
        {
            Record(program);
        }

        if (inner.Tier == ExecutionTier.Native)
        {
            Reason ??= inner.Reason;
        }
    }

    private void Escalate(string? program, string reason)
    {
        Record(program);
        Reason ??= reason;
    }

    private void Record(string? program)
    {
        if (program is not null && !unownedPrograms.Contains(program, StringComparer.Ordinal))
        {
            unownedPrograms.Add(program);
        }
    }
}
