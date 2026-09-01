using System.Text;
using Sharp.Shell.Commands;
using Sharp.Shell.Expansion;
using Sharp.Shell.Lexing;
using Sharp.Shell.Parsing;

namespace Sharp.Shell.Execution;

// Runs a parsed program by calling functions, never by starting processes. A pipeline is the
// producer's lazy output handed to the consumer as its input; the last stage is the only one
// drained, so a consumer that stops early stops everything upstream of it.
public sealed class ShellExecutor(AppletRegistry applets, ICommandExecutor external)
{
    private readonly CommandClassifier classifier = new(applets);

    // The only public way in. Classification happens after parsing and flag-checking but before any
    // side effect: a Native verdict returns with Result null and nothing touched, so the caller
    // hands the original command string to the native tier. Execute is internal precisely so no
    // other assembly can reach execution without passing through here.
    public ShellRun Run(string commandLine, ShellState state, CancellationToken cancellationToken)
    {
        Classification classification = classifier.Classify(commandLine);

        return classification.Tier == ExecutionTier.Native
            ? new ShellRun(classification, null)
            : new ShellRun(classification, Execute(commandLine, state, cancellationToken));
    }

    internal ShellResult Execute(string commandLine, ShellState state, CancellationToken cancellationToken)
    {
        ParseResult parsed = Parser.Parse(commandLine);
        StringBuilder standardOutput = new();
        StringBuilder standardError = new();

        if (!parsed.IsParsed)
        {
            standardError.Append($"duetui-shell: {parsed.UnsupportedReason}\n");
            return new ShellResult(2, string.Empty, standardError.ToString());
        }

        int exitCode = Run(
            parsed.Program!,
            state,
            TextStream.Empty,
            chunk => standardOutput.Append(chunk),
            text => standardError.Append(text),
            cancellationToken);

        return new ShellResult(exitCode, standardOutput.ToString(), standardError.ToString());
    }

    private int Run(
        ShellNode node,
        ShellState state,
        IEnumerable<string> input,
        Action<string> writeOutput,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        int exitCode = node switch
        {
            SequenceNode sequence => RunSequence(sequence, state, writeOutput, writeError, cancellationToken),
            AndOrNode andOr => RunAndOr(andOr, state, writeOutput, writeError, cancellationToken),
            PipelineNode pipeline => RunPipeline(pipeline, state, input, writeOutput, writeError, cancellationToken),
            IfNode conditional => RunIf(conditional, state, writeOutput, writeError, cancellationToken),
            WhileNode loop => RunWhile(loop, state, writeOutput, writeError, cancellationToken),
            ForNode loop => RunFor(loop, state, writeOutput, writeError, cancellationToken),
            CaseNode branch => RunCase(branch, state, writeOutput, writeError, cancellationToken),
            SubshellNode subshell => Run(subshell.Body, state.Fork(), input, writeOutput, writeError, cancellationToken),
            BraceGroupNode group => Run(group.Body, state, input, writeOutput, writeError, cancellationToken),
            SimpleCommand command => Drain(Invoke(command, state, input, writeError, cancellationToken), writeOutput),
            _ => Unsupported(node, writeError),
        };

        state.LastExitCode = exitCode;
        return exitCode;
    }

    private static int Unsupported(ShellNode node, Action<string> writeError)
    {
        writeError($"duetui-shell: {node.GetType().Name} is not supported yet\n");
        return 2;
    }

    private int RunSequence(
        SequenceNode sequence,
        ShellState state,
        Action<string> writeOutput,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        int exitCode = 0;
        foreach (ShellNode item in sequence.Items)
        {
            exitCode = Run(item, state, TextStream.Empty, writeOutput, writeError, cancellationToken);

            if (state.ExitRequested)
            {
                return state.ExitStatus;
            }
        }

        return exitCode;
    }

    private int RunAndOr(
        AndOrNode andOr,
        ShellState state,
        Action<string> writeOutput,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        int left = Run(andOr.Left, state, TextStream.Empty, writeOutput, writeError, cancellationToken);

        if (state.ExitRequested)
        {
            return state.ExitStatus;
        }

        bool runsRight = andOr.Kind == AndOrKind.And ? left == 0 : left != 0;

        return runsRight
            ? Run(andOr.Right, state, TextStream.Empty, writeOutput, writeError, cancellationToken)
            : left;
    }

    private int RunPipeline(
        PipelineNode pipeline,
        ShellState state,
        IEnumerable<string> input,
        Action<string> writeOutput,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> flowing = input;
        for (int stage = 0; stage < pipeline.Stages.Count - 1; stage++)
        {
            flowing = StageOutput(pipeline.Stages[stage], state, flowing, writeError, cancellationToken);
        }

        int exitCode = Run(pipeline.Stages[^1], state, flowing, writeOutput, writeError, cancellationToken);
        return pipeline.Negated ? (exitCode == 0 ? 1 : 0) : exitCode;
    }


    private int RunIf(
        IfNode conditional,
        ShellState state,
        Action<string> writeOutput,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        foreach (ConditionalBranch branch in conditional.Branches)
        {
            if (Run(branch.Condition, state, TextStream.Empty, writeOutput, writeError, cancellationToken) != 0)
            {
                continue;
            }

            return Run(branch.Body, state, TextStream.Empty, writeOutput, writeError, cancellationToken);
        }

        return conditional.ElseBody is null
            ? 0
            : Run(conditional.ElseBody, state, TextStream.Empty, writeOutput, writeError, cancellationToken);
    }

    // Cancellation is checked every iteration: an unbounded loop is stopped by the caller's token
    // here and by the wasm epoch deadline in the guest, never by a hidden iteration cap.
    private int RunWhile(
        WhileNode loop,
        ShellState state,
        Action<string> writeOutput,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        int exitCode = 0;

        while (!cancellationToken.IsCancellationRequested && !state.ExitRequested)
        {
            int condition = Run(loop.Condition, state, TextStream.Empty, writeOutput, writeError, cancellationToken);
            bool keepGoing = loop.UntilForm ? condition != 0 : condition == 0;

            if (!keepGoing)
            {
                return exitCode;
            }

            exitCode = Run(loop.Body, state, TextStream.Empty, writeOutput, writeError, cancellationToken);
        }

        return state.ExitRequested ? state.ExitStatus : exitCode;
    }

    private int RunFor(
        ForNode loop,
        ShellState state,
        Action<string> writeOutput,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        WordExpander expander = new(state, source => Substitute(source, state, writeError, cancellationToken));
        List<string> items = [];

        foreach (Word item in loop.Items)
        {
            ExpansionResult expanded = expander.Expand(item);
            if (!Report(expanded, writeError, out int failureCode))
            {
                return failureCode;
            }

            items.AddRange(expanded.Fields);
        }

        int exitCode = 0;
        foreach (string item in items)
        {
            if (cancellationToken.IsCancellationRequested || state.ExitRequested)
            {
                break;
            }

            state.Variables[loop.Variable] = item;
            exitCode = Run(loop.Body, state, TextStream.Empty, writeOutput, writeError, cancellationToken);
        }

        return state.ExitRequested ? state.ExitStatus : exitCode;
    }

    private int RunCase(
        CaseNode branch,
        ShellState state,
        Action<string> writeOutput,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        WordExpander expander = new(state, source => Substitute(source, state, writeError, cancellationToken));
        ExpansionResult subject = expander.ExpandValue(branch.Subject);

        if (!Report(subject, writeError, out int failureCode))
        {
            return failureCode;
        }

        string text = string.Concat(subject.Fields);

        foreach (CaseArm arm in branch.Arms)
        {
            if (!MatchesAnyPattern(text, arm, expander, writeError, out int patternFailure))
            {
                if (patternFailure != 0)
                {
                    return patternFailure;
                }

                continue;
            }

            return Run(arm.Body, state, TextStream.Empty, writeOutput, writeError, cancellationToken);
        }

        return 0;
    }

    private static bool MatchesAnyPattern(
        string text,
        CaseArm arm,
        WordExpander expander,
        Action<string> writeError,
        out int failureCode)
    {
        failureCode = 0;

        foreach (Word pattern in arm.Patterns)
        {
            ExpansionResult expanded = expander.ExpandValue(pattern);
            if (!Report(expanded, writeError, out failureCode))
            {
                return false;
            }

            if (PatternMatcher.Matches(text, string.Concat(expanded.Fields)))
            {
                return true;
            }
        }

        return false;
    }

    // A simple command streams: its output is the next stage's lazy input, which is what lets a
    // consumer stop its producer. A compound stage — `for …; done | head -2` — cannot, because
    // interleaving two running constructs in one thread would need coroutines, so it is drained
    // into a buffer first. Rare enough to be worth the simplicity, and correct either way.
    private IEnumerable<string> StageOutput(
        ShellNode stage,
        ShellState state,
        IEnumerable<string> input,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        if (stage is SimpleCommand command)
        {
            return Invoke(command, state, input, writeError, cancellationToken).Output;
        }

        List<string> buffered = [];
        Run(stage, state, input, buffered.Add, writeError, cancellationToken);

        return buffered;
    }

    private static int Drain(AppletRun run, Action<string> writeOutput)
    {
        foreach (string chunk in run.Output)
        {
            writeOutput(chunk);
        }

        return run.ExitCode;
    }

    private AppletRun Invoke(
        SimpleCommand command,
        ShellState state,
        IEnumerable<string> input,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        WordExpander expander = new(state, source => Substitute(source, state, writeError, cancellationToken));
        RedirectionPlan plan = ResolveRedirections(command, expander, state, writeError);

        if (plan.Failure is not null)
        {
            writeError($"duetui-shell: {plan.Failure}\n");
            return AppletRun.Failed(1);
        }

        if (!TryExpandWords(command, expander, writeError, out IReadOnlyList<string> words, out int failure))
        {
            return AppletRun.Failed(failure);
        }

        if (words.Count == 0)
        {
            return ApplyAssignments(command, expander, state, writeError);
        }

        string name = words[0];
        IReadOnlyList<string> arguments = [.. words.Skip(1)];
        IEnumerable<string> effectiveInput = plan.Input ?? input;

        List<string> capturedErrors = [];
        Action<string> errorSink = ErrorSinkFor(plan, capturedErrors, writeError);

        AppletRun run = RunOwnedOrExternal(
            name, arguments, effectiveInput, state, errorSink, cancellationToken);

        return ApplyOutputRedirection(run, plan, capturedErrors, state, writeError);
    }

    // Bundled short flags are expanded for an owned applet, never for the native tier: a real
    // program parses its own arguments and must receive them untouched.
    private AppletRun RunOwnedOrExternal(
        string name,
        IReadOnlyList<string> arguments,
        IEnumerable<string> input,
        ShellState state,
        Action<string> errorSink,
        CancellationToken cancellationToken)
    {
        if (applets.TryGet(name, out IApplet applet))
        {
            IReadOnlyList<string> expanded = FlagReader.ExpandShortFlagBundles(arguments, applet.BundleableFlags);

            if (applet.CheckFlags(expanded).IsSupported)
            {
                return applet.Run(new AppletContext(expanded, input, state, errorSink, cancellationToken));
            }
        }

        return RunExternal(name, arguments, state, input, errorSink, cancellationToken);
    }

    // 2>&1 folds the error text in after the command's own output rather than interleaving it. The
    // ordering differs from bash, which has two real file descriptors; the content does not.
    private static Action<string> ErrorSinkFor(RedirectionPlan plan, List<string> captured, Action<string> writeError) =>
        plan.Error switch
        {
            StreamTarget.File or StreamTarget.OtherStream => captured.Add,
            _ => writeError,
        };

    private static AppletRun ApplyOutputRedirection(
        AppletRun run,
        RedirectionPlan plan,
        List<string> capturedErrors,
        ShellState state,
        Action<string> writeError)
    {
        if (plan.Error == StreamTarget.OtherStream)
        {
            run.Output = run.Output.Concat(Deferred(capturedErrors));
        }

        if (plan.Error == StreamTarget.File)
        {
            run.Output = ThenWriteErrors(run.Output, plan.ErrorPath!, plan.ErrorAppends, capturedErrors);
        }

        if (plan.Output == StreamTarget.OtherStream)
        {
            string collected = TextStream.Collect(run.Output);
            run.Output = TextStream.Empty;
            writeError(collected);
        }

        if (plan.Output == StreamTarget.File)
        {
            WriteToFile(plan.OutputPath!, plan.OutputAppends, run.Output);
            run.Output = TextStream.Empty;
        }

        return run;
    }

    // The error file is written once the output has been drained, because an applet reports its
    // per-operand failures while its output is being enumerated, not before.
    private static IEnumerable<string> ThenWriteErrors(
        IEnumerable<string> output,
        string absolutePath,
        bool appends,
        List<string> captured)
    {
        foreach (string chunk in output)
        {
            yield return chunk;
        }

        WriteToFile(absolutePath, appends, captured);
    }

    // Reads the list at enumeration time, not at construction time, so text the applet writes to
    // stderr while its output is being drained is still picked up.
    private static IEnumerable<string> Deferred(List<string> captured)
    {
        foreach (string text in captured)
        {
            yield return text;
        }
    }

    private static void WriteToFile(string absolutePath, bool appends, IEnumerable<string> chunks)
    {
        using StreamWriter writer = new(absolutePath, append: appends);
        foreach (string chunk in chunks)
        {
            writer.Write(chunk);
        }
    }

    private RedirectionPlan ResolveRedirections(
        SimpleCommand command,
        WordExpander expander,
        ShellState state,
        Action<string> writeError)
    {
        RedirectionPlan plan = new();

        foreach (Redirection redirection in command.Redirections)
        {
            plan = Apply(redirection, plan, expander, state, writeError);
            if (plan.Failure is not null)
            {
                return plan;
            }
        }

        return plan;
    }

    private RedirectionPlan Apply(
        Redirection redirection,
        RedirectionPlan plan,
        WordExpander expander,
        ShellState state,
        Action<string> writeError)
    {
        if (redirection.Kind == RedirectionKind.HereDocument)
        {
            return plan with { Input = TextStream.FromText(HereDocumentText(redirection, expander)) };
        }

        if (!TryResolveTarget(redirection, expander, state, out string target, out string? failure))
        {
            return RedirectionPlan.Failed(failure!);
        }

        return redirection.Kind switch
        {
            RedirectionKind.Input => OpenInput(target, state, plan),
            RedirectionKind.Output => ToFile(target, state, plan, redirection.FileDescriptor, appends: false),
            RedirectionKind.Append => ToFile(target, state, plan, redirection.FileDescriptor, appends: true),
            RedirectionKind.OutputAndError => BothToFile(target, state, plan),
            RedirectionKind.DuplicateOutput => Duplicate(redirection.FileDescriptor, target, plan),
            RedirectionKind.DuplicateInput => plan,
            _ => RedirectionPlan.Failed("unsupported redirection"),
        };
    }

    private string HereDocumentText(Redirection redirection, WordExpander expander)
    {
        string body = redirection.HereDocumentBody ?? string.Empty;

        if (!redirection.Expands)
        {
            return body;
        }

        ExpansionResult expanded = expander.ExpandValue(new Word(Lexer.ExpandableParts(body)));
        return expanded.IsSupported && !expanded.HasError ? string.Concat(expanded.Fields) : body;
    }

    private static bool TryResolveTarget(
        Redirection redirection,
        WordExpander expander,
        ShellState state,
        out string target,
        out string? failure)
    {
        target = string.Empty;
        failure = null;

        ExpansionResult expanded = expander.ExpandValue(redirection.Target!);
        if (!expanded.IsSupported)
        {
            failure = expanded.UnsupportedReason;
            return false;
        }

        if (expanded.HasError)
        {
            failure = expanded.ErrorMessage;
            return false;
        }

        target = string.Concat(expanded.Fields);
        return true;
    }

    private static RedirectionPlan OpenInput(string target, ShellState state, RedirectionPlan plan)
    {
        string absolute = state.Resolve(target);

        if (!state.IsInsideRoot(absolute))
        {
            return RedirectionPlan.Failed($"{target}: outside the workspace");
        }

        if (!File.Exists(absolute))
        {
            return RedirectionPlan.Failed($"{target}: No such file or directory");
        }

        return plan with { Input = FileChunks.Read(absolute) };
    }

    private static RedirectionPlan ToFile(string target, ShellState state, RedirectionPlan plan, int fileDescriptor, bool appends)
    {
        if (!TryOpenForWriting(target, state, out string absolute, out string? failure))
        {
            return RedirectionPlan.Failed(failure!);
        }

        return fileDescriptor == 2
            ? plan with { Error = StreamTarget.File, ErrorPath = absolute, ErrorAppends = appends }
            : plan with { Output = StreamTarget.File, OutputPath = absolute, OutputAppends = appends };
    }

    private static RedirectionPlan BothToFile(string target, ShellState state, RedirectionPlan plan)
    {
        if (!TryOpenForWriting(target, state, out string absolute, out string? failure))
        {
            return RedirectionPlan.Failed(failure!);
        }

        return plan with
        {
            Output = StreamTarget.File,
            OutputPath = absolute,
            OutputAppends = false,
            Error = StreamTarget.OtherStream,
        };
    }

    private static RedirectionPlan Duplicate(int fileDescriptor, string target, RedirectionPlan plan) =>
        (fileDescriptor, target) switch
        {
            (2, "1") => plan with { Error = StreamTarget.OtherStream },
            (1, "2") => plan with { Output = StreamTarget.OtherStream },
            _ => RedirectionPlan.Failed($"{fileDescriptor}>&{target} is not supported"),
        };

    private static bool TryOpenForWriting(string target, ShellState state, out string absolute, out string? failure)
    {
        absolute = state.Resolve(target);
        failure = null;

        if (!state.IsInsideRoot(absolute))
        {
            failure = $"{target}: outside the workspace";
            return false;
        }

        if (!Directory.Exists(Path.GetDirectoryName(absolute)))
        {
            failure = $"{target}: No such file or directory";
            return false;
        }

        return true;
    }

    private AppletRun RunExternal(
        string name,
        IReadOnlyList<string> arguments,
        ShellState state,
        IEnumerable<string> input,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        CommandExecution execution = external.Execute(name, arguments, state.WorkingDirectory, input, cancellationToken);

        if (!execution.IsSupported)
        {
            writeError($"duetui-shell: {name}: command not found\n");
            return AppletRun.Failed(127);
        }

        if (execution.Error.Length > 0)
        {
            writeError(execution.Error);
        }

        return new AppletRun { Output = execution.Output, ExitCode = execution.ExitCode };
    }

    // An unsupported expansion exits 2, the same code an unsupported construct uses, because both
    // mean "this line belongs to a real shell". An expansion *error* is an ordinary failure.
    private static bool TryExpandWords(
        SimpleCommand command,
        WordExpander expander,
        Action<string> writeError,
        out IReadOnlyList<string> words,
        out int failureCode)
    {
        List<string> resolved = [];
        foreach (Word word in command.Words)
        {
            ExpansionResult expanded = expander.Expand(word);
            if (!Report(expanded, writeError, out failureCode))
            {
                words = [];
                return false;
            }

            resolved.AddRange(expanded.Fields);
        }

        words = resolved;
        failureCode = 0;
        return true;
    }

    private static bool Report(ExpansionResult expanded, Action<string> writeError, out int failureCode)
    {
        if (!expanded.IsSupported)
        {
            writeError($"duetui-shell: {expanded.UnsupportedReason}\n");
            failureCode = 2;
            return false;
        }

        if (expanded.HasError)
        {
            writeError($"duetui-shell: {expanded.ErrorMessage}\n");
            failureCode = 1;
            return false;
        }

        failureCode = 0;
        return true;
    }

    private static AppletRun ApplyAssignments(
        SimpleCommand command,
        WordExpander expander,
        ShellState state,
        Action<string> writeError)
    {
        foreach (Assignment assignment in command.Assignments)
        {
            ExpansionResult expanded = expander.ExpandValue(assignment.Value);
            if (!Report(expanded, writeError, out int failureCode))
            {
                return AppletRun.Failed(failureCode);
            }

            state.Variables[assignment.Name] = string.Concat(expanded.Fields);
        }

        return new AppletRun();
    }

    // $(...) runs against a fork of the state so a cd or assignment inside it cannot leak out, and
    // its status becomes $? exactly as bash's does.
    private CommandSubstitution Substitute(
        string source,
        ShellState state,
        Action<string> writeError,
        CancellationToken cancellationToken)
    {
        ShellState nested = state.Fork();
        ShellResult result = Execute(source, nested, cancellationToken);

        if (result.Stderr.Length > 0)
        {
            writeError(result.Stderr);
        }

        state.LastExitCode = result.ExitCode;
        return new CommandSubstitution(result.ExitCode, result.Stdout);
    }
}
