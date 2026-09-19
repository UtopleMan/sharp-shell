using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Expansion;
using Sharp.Shell.Lexing;
using Sharp.Shell.Parsing;

namespace Sharp.Shell;

// Answers two different questions about one command line. Tier and UnownedPrograms describe it —
// whether the shell owns every program, and which ones it does not — and drive the host's prompt
// policy. UnrunnableReason decides something else entirely: whether this shell can run the line at
// all. Only that second answer stops execution.
//
// `ls | awk '…'` is Native and runnable: the shell runs it, owns `ls`, and asks ICommandExecutor
// for `awk`. `git status &` is unrunnable, because the shell has no process model to put a job in
// the background with.
public sealed class CommandClassifier(AppletRegistry applets)
{
    public Classification Classify(string commandLine, ShellState state)
    {
        ParseResult parsed = Parser.Parse(commandLine);

        if (!parsed.IsParsed)
        {
            return Classification.Unrunnable([], mutates: false, parsed.UnsupportedReason!);
        }

        Inspection inspection = new(applets, state);
        inspection.Walk(parsed.Program!);

        if (inspection.UnrunnableReason is { } unrunnable)
        {
            return Classification.Unrunnable([.. inspection.UnownedPrograms], inspection.Mutates, unrunnable);
        }

        return inspection.Reason is null
            ? Classification.Owned(
                inspection.Mutates,
                inspection.Reads,
                inspection.Writes,
                inspection.KnowsEveryRead,
                inspection.KnowsEveryWrite)
            : Classification.Native([.. inspection.UnownedPrograms], inspection.Mutates, inspection.Reason);
    }
}

internal sealed class Inspection(AppletRegistry applets, ShellState state)
{
    private readonly List<string> unownedPrograms = [];

    // Functions this line defines, plus the ones the session already has. Calling one is owned: the
    // shell runs the body itself, and every command inside it is dispatched through the same hook.
    private readonly HashSet<string> definedFunctions = new(StringComparer.Ordinal);

    private readonly OperandSink readOperands = new();

    private readonly OperandSink writeOperands = new();

    // A private copy of the shell's position, moved by every cd this pass can follow. Classification
    // has no side effects, so the caller's state must not be the one that walks.
    private readonly ShellState resolution = state.Fork();

    private bool knowsWhereItIs = true;

    private int conditionalDepth;

    public IReadOnlyList<string> UnownedPrograms => unownedPrograms;

    // False once a file operand of that kind was left out because it is only a path after the line
    // runs. Reads and writes answer separately, so a hidden write costs the line a write question
    // and nothing else.
    public bool KnowsEveryRead => readOperands.KnowsEveryFile;

    public bool KnowsEveryWrite => writeOperands.KnowsEveryFile;

    public IReadOnlyList<string> Reads => readOperands.Paths;

    public IReadOnlyList<string> Writes => writeOperands.Paths;

    public bool Mutates { get; private set; }

    public string? Reason { get; private set; }

    // An expansion form the shell does not implement cannot be worked around at run time the way an
    // unowned program can: there is no other tier to ask. The line is unrunnable, the same as one
    // that will not parse.
    public string? UnrunnableReason { get; private set; }

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
                WalkConditionally(subshell.Body);
                return;
            case BraceGroupNode group:
                Walk(group.Body);
                return;
            case IfNode conditional:
                WalkIf(conditional);
                return;
            case WhileNode loop:
                Walk(loop.Condition);
                WalkConditionally(loop.Body);
                return;
            case ForNode loop:
                WalkFor(loop);
                return;
            case CaseNode branch:
                WalkCase(branch);
                return;
            case SelectNode menu:
                WalkSelect(menu);
                return;
            case FunctionDefinition definition:
                WalkFunction(definition);
                return;
            case ConditionNode condition:
                WalkAllWords(condition.Words);
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
            WalkConditionally(branch.Body);
        }

        if (conditional.ElseBody is not null)
        {
            WalkConditionally(conditional.ElseBody);
        }
    }

    // A branch or a loop body may run any number of times, including none, and a subshell's cd never
    // reaches the parent at all. Whatever such a body does to the working directory, this pass cannot
    // say where the line stands afterwards.
    private void WalkConditionally(ShellNode body)
    {
        conditionalDepth++;
        Walk(body);
        conditionalDepth--;
    }

    private void WalkFor(ForNode loop)
    {
        WalkAllWords(loop.Items);
        WalkConditionally(loop.Body);
    }

    private void WalkSelect(SelectNode menu)
    {
        WalkAllWords(menu.Items);
        WalkConditionally(menu.Body);
    }

    // A function body is walked so its programs count towards the line's, but the body may never
    // run, so it is walked conditionally — the same treatment a loop body gets.
    private void WalkFunction(FunctionDefinition definition)
    {
        definedFunctions.Add(definition.Name);
        WalkConditionally(definition.Body);
    }

    private void WalkAllWords(IReadOnlyList<Word> words)
    {
        foreach (Word word in words)
        {
            CheckWord(word);
        }
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

            WalkConditionally(arm.Body);
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

        RecordRedirectedFile(redirection);
    }

    // `> f` and `>> f` write f, `< f` reads it. A duplication (2>&1) names a descriptor, not a file,
    // and a here-document has no file at all.
    private void RecordRedirectedFile(Redirection redirection)
    {
        if (redirection.Target is not { } target)
        {
            return;
        }

        OperandSink? destination = redirection.Kind switch
        {
            RedirectionKind.Output or RedirectionKind.Append or RedirectionKind.OutputAndError => writeOperands,
            RedirectionKind.Input => readOperands,
            _ => null,
        };

        if (destination is not null)
        {
            Record(target, destination);
        }
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

        if (definedFunctions.Contains(program) || resolution.Functions.ContainsKey(program))
        {
            return;
        }

        if (!applets.TryGet(program, out IApplet applet))
        {
            Escalate(program, $"'{program}' is not one of the sandboxed commands");
            return;
        }

        if (applet.Name == "cd")
        {
            FollowDirectoryChange(command);
        }

        CheckFlags(command, applet, program);
    }

    private void CheckFlags(SimpleCommand command, IApplet applet, string program)
    {
        List<string> words = [.. command.Words.Skip(1).Select(word => word.LiteralText)];
        IReadOnlyList<string> arguments = FlagReader.ExpandShortFlagBundles(
            words, applet.BundleableFlags, out IReadOnlyList<int> sourcePositions);

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

        RecordFileOperands(command, applet, arguments, sourcePositions);
    }

    // Which files the invocation names, resolved the way the shell will resolve them: quoting and
    // globbing applied, then made absolute against the workspace.
    //
    // An operand this pass cannot resolve is left out and its kind's flag goes false. It is not
    // escalated: the sandbox deliberately accepts `sed -n '1,5p' "$file"` as owned, and a word whose
    // value only exists once the line runs cannot be turned into a path before it does.
    private void RecordFileOperands(
        SimpleCommand command,
        IApplet applet,
        IReadOnlyList<string> arguments,
        IReadOnlyList<int> sourcePositions)
    {
        OperandPositions positions = applet.FileOperandPositions(arguments);

        RecordOperands(positions.Reads, command, sourcePositions, readOperands);
        RecordOperands(positions.Writes, command, sourcePositions, writeOperands);
    }

    private void RecordOperands(
        IReadOnlyList<int> positions,
        SimpleCommand command,
        IReadOnlyList<int> sourcePositions,
        OperandSink destination)
    {
        foreach (int position in positions)
        {
            Record(command.Words[sourcePositions[position] + 1], destination);
        }
    }

    // Every relative operand after a cd is read from where the cd landed, so this pass follows the
    // ones it can see. A cd whose target only exists at run time, or one inside a branch that may not
    // run at all, leaves the position unknown — and an unknown position makes every later operand
    // unresolvable, because resolving it against the wrong directory would name the wrong file.
    private void FollowDirectoryChange(SimpleCommand command)
    {
        if (conditionalDepth > 0)
        {
            knowsWhereItIs = false;
            return;
        }

        Word? target = command.Words.Skip(1).FirstOrDefault(word => !FlagReader.IsFlag(word.LiteralText));

        if (target is null)
        {
            resolution.TryChangeDirectory(resolution.RootPath, out _);
            return;
        }

        if (!IsStaticallyKnown(target) || Expand(target) is not [string directory])
        {
            knowsWhereItIs = false;
            return;
        }

        knowsWhereItIs &= resolution.TryChangeDirectory(directory, out _);
    }

    private IReadOnlyList<string>? Expand(Word word)
    {
        ExpansionResult expanded = new WordExpander(resolution, NoSubstitution).Expand(word);

        return expanded.IsSupported ? expanded.Fields : null;
    }

    private void Record(Word word, OperandSink destination)
    {
        if (!knowsWhereItIs || !IsStaticallyKnown(word) || Expand(word) is not { } fields)
        {
            destination.RecordUnresolvedOperand();
            return;
        }

        foreach (string field in fields)
        {
            destination.Add(resolution.Resolve(field));
        }
    }

    // Nothing runs during classification, so a substitution the expander asks for cannot be answered.
    // No word reaching the expander here contains one: IsStaticallyKnown refuses those first.
    private static CommandSubstitution NoSubstitution(string commandLine) => new(0, string.Empty);

    // Like Word.IsFullyLiteral, plus tilde: `~/notes` resolves from the shell state alone, with no
    // variable, no substitution and no arithmetic, so its file is knowable before the line runs.
    private static bool IsStaticallyKnown(Word word) => word.Parts.All(IsStaticallyKnown);

    private static bool IsStaticallyKnown(WordPart part) => part.Kind switch
    {
        WordPartKind.Literal or WordPartKind.SingleQuoted or WordPartKind.Tilde => true,
        WordPartKind.DoubleQuoted => part.Nested is not null && part.Nested.All(IsStaticallyKnown),
        _ => false,
    };

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
                Absorb(new CommandClassifier(applets).Classify(part.Text, resolution));
                return;
            case WordPartKind.Parameter:
                CheckParameterForm(part.Text);
                return;
        }
    }

    private void CheckParameterForm(string expression)
    {
        ParameterResult result = ParameterExpander.Expand(expression, resolution, static word => word);

        if (result.UnsupportedReason is not null)
        {
            UnrunnableReason ??= result.UnsupportedReason;
            Reason ??= result.UnsupportedReason;
        }
    }

    private void Absorb(Classification inner)
    {
        Mutates |= inner.Mutates;

        bool owned = inner.Tier == ExecutionTier.Owned;
        readOperands.Absorb(inner.Reads, owned && inner.KnowsEveryRead);
        writeOperands.Absorb(inner.Writes, owned && inner.KnowsEveryWrite);

        foreach (string program in inner.UnownedPrograms)
        {
            Record(program);
        }

        if (inner.Tier == ExecutionTier.Native)
        {
            Reason ??= inner.Reason;
        }

        if (!inner.IsRunnable)
        {
            UnrunnableReason ??= inner.UnrunnableReason;
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

// The file operands of one kind, and whether they are all of them. A line that hides an operand
// behind a variable hides it from one list only, so the two are counted apart.
internal sealed class OperandSink
{
    private readonly List<string> paths = [];

    public IReadOnlyList<string> Paths => paths;

    public bool KnowsEveryFile { get; private set; } = true;

    public void Add(string path)
    {
        if (!paths.Contains(path, StringComparer.Ordinal))
        {
            paths.Add(path);
        }
    }

    public void RecordUnresolvedOperand() => KnowsEveryFile = false;

    public void Absorb(IReadOnlyList<string> inner, bool knowsEveryFile)
    {
        foreach (string path in inner)
        {
            Add(path);
        }

        KnowsEveryFile &= knowsEveryFile;
    }
}
