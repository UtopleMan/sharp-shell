using System.Globalization;
using System.Runtime.CompilerServices;
using Sharp.Shell.Execution;
using Sharp.Shell.Text;

namespace Sharp.Shell.Commands.Awk;

internal enum AwkSignal
{
    None,
    Break,
    Continue,
    Next,
    NextFile,
    Exit,
    Return,
}

// A tree-walking interpreter over the immutable AST: single-threaded, reflection-free, and lazy at
// record granularity, so `awk '{print}' big | head -1` stops its producer the way every other applet
// does.
//
// The main loop is driven by ARGV and ARGC rather than by a list captured at startup, because a
// BEGIN rule is allowed to rewrite them and an assignment operand between two files has to take
// effect between those two files.
internal sealed class AwkInterpreter(
    AwkProgram program,
    AwkRunOptions options,
    AwkOutput output,
    AppletContext context)
{
    private const int MaximumCallDepth = 1000;

    private readonly AwkRegexCache regexes = new();

    private readonly AwkMathBuiltins math = new();

    private readonly bool[] activeRanges = new bool[program.Rules.Count];

    private AwkEnvironment environment = null!;

    private AwkValue returned;

    private bool hasExited;

    private bool hasFailed;

    public int ExitCode { get; private set; }

    public IEnumerable<string> Run()
    {
        environment = new AwkEnvironment(regexes);
        Prepare();

        if (!Guarded(RunBegin))
        {
            yield return output.TakePending();
            yield break;
        }

        yield return output.TakePending();

        if (!hasExited && !hasFailed && AwkProgramFacts.ReadsInput(program))
        {
            foreach (string chunk in RunInput())
            {
                yield return chunk;
            }
        }

        if (!hasFailed)
        {
            hasExited = false;
            Guarded(RunEnd);
        }

        yield return output.TakePending();
    }

    private void Prepare()
    {
        AwkArray environmentVariables = environment.Array("ENVIRON");

        foreach (KeyValuePair<string, string> variable in options.EnvironmentVariables)
        {
            environmentVariables.Write(variable.Key, AwkValue.FromInput(variable.Value));
        }

        AwkArray arguments = environment.Array("ARGV");
        arguments.Write("0", AwkValue.Of("awk"));

        for (int index = 0; index < options.Operands.Count; index++)
        {
            arguments.Write(
                (index + 1).ToString(CultureInfo.InvariantCulture),
                AwkValue.FromInput(options.Operands[index]));
        }

        environment.Write("ARGC", AwkValue.Of(options.Operands.Count + 1d));

        if (options.FieldSeparator is not null)
        {
            environment.Write("FS", AwkValue.Of(options.FieldSeparator));
        }

        foreach (string assignment in options.Assignments)
        {
            ApplyAssignment(assignment);
        }
    }

    private void RunBegin()
    {
        foreach (AwkRule rule in program.Rules)
        {
            if (rule.Pattern is not AwkBeginPattern)
            {
                continue;
            }

            if (Execute(rule.Action!) == AwkSignal.Exit)
            {
                hasExited = true;
                return;
            }
        }
    }

    private void RunEnd()
    {
        foreach (AwkRule rule in program.Rules)
        {
            if (rule.Pattern is not AwkEndPattern)
            {
                continue;
            }

            if (Execute(rule.Action!) == AwkSignal.Exit)
            {
                return;
            }
        }
    }

    private IEnumerable<string> RunInput()
    {
        int index = 1;
        bool hasReadFile = false;

        while (index < ArgumentCount() && !hasExited && !hasFailed)
        {
            string argument = ArgumentAt(index);
            index++;

            if (argument.Length == 0)
            {
                continue;
            }

            if (IsAssignmentOperand(argument))
            {
                if (!Guarded(() => ApplyAssignment(argument)))
                {
                    yield break;
                }

                continue;
            }

            hasReadFile = true;

            foreach (string chunk in RunFile(argument))
            {
                yield return chunk;
            }
        }

        if (hasReadFile || hasExited || hasFailed)
        {
            yield break;
        }

        foreach (string chunk in RunStream(string.Empty, context.Input))
        {
            yield return chunk;
        }
    }

    private IEnumerable<string> RunFile(string name)
    {
        if (name == "-")
        {
            return RunStream(name, context.Input);
        }

        string absolute = context.State.Resolve(name);

        if (!context.State.IsInsideRoot(absolute) || !File.Exists(absolute))
        {
            context.WriteError($"awk: can't open file {name}\n");
            ExitCode = 2;
            return TextStream.Empty;
        }

        return RunStream(name, FileChunks.Read(absolute));
    }

    private IEnumerable<string> RunStream(string name, IEnumerable<string> chunks)
    {
        using RecordReader records = new(chunks);
        environment.Write("FILENAME", AwkValue.Of(name));
        environment.Write("FNR", AwkValue.Of(0d));

        while (TryRunRecord(records))
        {
            string pending = output.TakePending();

            if (pending.Length > 0)
            {
                yield return pending;
            }
        }

        yield return output.TakePending();
    }

    // The whole of a record's work sits inside one try, so the iterator that drives it never has to
    // yield from inside a catch — which C# would not allow anyway.
    private bool TryRunRecord(RecordReader records)
    {
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (!records.TryRead(environment.RecordSeparator, out string record))
            {
                return false;
            }

            environment.Write("NR", AwkValue.Of(environment.Read("NR").ToNumber() + 1));
            environment.Write("FNR", AwkValue.Of(environment.Read("FNR").ToNumber() + 1));
            environment.Record.Set(record, environment.FieldSeparator, environment.IsParagraphMode);

            AwkSignal signal = RunRules();

            if (signal == AwkSignal.Exit)
            {
                hasExited = true;
            }

            return signal is not (AwkSignal.Exit or AwkSignal.NextFile);
        }
        catch (AwkRuntimeException failure)
        {
            Report(failure.Message);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private AwkSignal RunRules()
    {
        for (int index = 0; index < program.Rules.Count; index++)
        {
            AwkRule rule = program.Rules[index];

            if (rule.Pattern is AwkBeginPattern or AwkEndPattern || !Matches(rule.Pattern, index))
            {
                continue;
            }

            if (rule.Action is null)
            {
                output.Write(environment.Record.Text + environment.OutputRecordSeparator);
                continue;
            }

            AwkSignal signal = Execute(rule.Action);

            if (signal != AwkSignal.None)
            {
                return signal;
            }
        }

        return AwkSignal.None;
    }

    // A range fires on the record that starts it, and its end pattern is tested on that same record,
    // which is what makes `/a/,/a/` a one-record range rather than one that never closes.
    private bool Matches(AwkPattern? pattern, int index) => pattern switch
    {
        null => true,
        AwkExpressionPattern expression => Evaluate(expression.Expression).IsTrue(),
        AwkRangePattern range => MatchesRange(range, index),
        _ => false,
    };

    private bool MatchesRange(AwkRangePattern range, int index)
    {
        if (!activeRanges[index])
        {
            if (!Evaluate(range.From).IsTrue())
            {
                return false;
            }

            activeRanges[index] = !Evaluate(range.To).IsTrue();
            return true;
        }

        activeRanges[index] = !Evaluate(range.To).IsTrue();
        return true;
    }

    private AwkSignal Execute(AwkStatement statement)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        return statement switch
        {
            AwkBlock block => ExecuteBlock(block),
            AwkExpressionStatement expression => Discard(expression.Expression),
            AwkIfStatement branch => ExecuteIf(branch),
            AwkWhileStatement loop => ExecuteWhile(loop),
            AwkDoWhileStatement loop => ExecuteDoWhile(loop),
            AwkForStatement loop => ExecuteFor(loop),
            AwkForInStatement loop => ExecuteForIn(loop),
            AwkBreakStatement => AwkSignal.Break,
            AwkContinueStatement => AwkSignal.Continue,
            AwkNextStatement => AwkSignal.Next,
            AwkNextFileStatement => AwkSignal.NextFile,
            AwkExitStatement exit => ExecuteExit(exit),
            AwkReturnStatement value => ExecuteReturn(value),
            AwkDeleteStatement delete => ExecuteDelete(delete),
            AwkPrintStatement print => ExecutePrint(print),
            AwkPrintfStatement print => ExecutePrintf(print),
            _ => AwkSignal.None,
        };
    }

    private AwkSignal ExecuteBlock(AwkBlock block)
    {
        foreach (AwkStatement statement in block.Statements)
        {
            AwkSignal signal = Execute(statement);

            if (signal != AwkSignal.None)
            {
                return signal;
            }
        }

        return AwkSignal.None;
    }

    private AwkSignal Discard(AwkExpression expression)
    {
        Evaluate(expression);
        return AwkSignal.None;
    }

    private AwkSignal ExecuteIf(AwkIfStatement branch)
    {
        if (Evaluate(branch.Condition).IsTrue())
        {
            return Execute(branch.Then);
        }

        return branch.Else is null ? AwkSignal.None : Execute(branch.Else);
    }

    private AwkSignal ExecuteWhile(AwkWhileStatement loop)
    {
        while (Evaluate(loop.Condition).IsTrue())
        {
            AwkSignal signal = Execute(loop.Body);

            if (LeavesLoop(signal, out AwkSignal escaping))
            {
                return escaping;
            }
        }

        return AwkSignal.None;
    }

    private AwkSignal ExecuteDoWhile(AwkDoWhileStatement loop)
    {
        do
        {
            AwkSignal signal = Execute(loop.Body);

            if (LeavesLoop(signal, out AwkSignal escaping))
            {
                return escaping;
            }
        }
        while (Evaluate(loop.Condition).IsTrue());

        return AwkSignal.None;
    }

    private AwkSignal ExecuteFor(AwkForStatement loop)
    {
        if (loop.Initialiser is not null)
        {
            Execute(loop.Initialiser);
        }

        while (loop.Condition is null || Evaluate(loop.Condition).IsTrue())
        {
            AwkSignal signal = Execute(loop.Body);

            if (LeavesLoop(signal, out AwkSignal escaping))
            {
                return escaping;
            }

            if (loop.Update is not null)
            {
                Execute(loop.Update);
            }
        }

        return AwkSignal.None;
    }

    private AwkSignal ExecuteForIn(AwkForInStatement loop)
    {
        foreach (string subscript in environment.Array(loop.ArrayName).Subscripts)
        {
            environment.Write(loop.VariableName, AwkValue.FromInput(subscript));
            AwkSignal signal = Execute(loop.Body);

            if (LeavesLoop(signal, out AwkSignal escaping))
            {
                return escaping;
            }
        }

        return AwkSignal.None;
    }

    // break and continue are consumed by the loop; next, nextfile, exit and return travel through it.
    private static bool LeavesLoop(AwkSignal signal, out AwkSignal escaping)
    {
        escaping = signal == AwkSignal.Break ? AwkSignal.None : signal;

        return signal is not (AwkSignal.None or AwkSignal.Continue);
    }

    private AwkSignal ExecuteExit(AwkExitStatement exit)
    {
        if (exit.Status is not null)
        {
            ExitCode = (int)Evaluate(exit.Status).ToNumber();
        }

        return AwkSignal.Exit;
    }

    private AwkSignal ExecuteReturn(AwkReturnStatement statement)
    {
        returned = statement.Value is null ? AwkValue.Uninitialized : Evaluate(statement.Value);
        return AwkSignal.Return;
    }

    private AwkSignal ExecuteDelete(AwkDeleteStatement delete)
    {
        AwkArray array = environment.Array(delete.ArrayName);

        if (delete.Subscripts.Count == 0)
        {
            array.Clear();
            return AwkSignal.None;
        }

        array.Remove(Subscript(delete.Subscripts));
        return AwkSignal.None;
    }

    private AwkSignal ExecutePrint(AwkPrintStatement print)
    {
        string text = print.Arguments.Count == 0
            ? environment.Record.Text
            : string.Join(
                environment.OutputFieldSeparator,
                print.Arguments.Select(argument => Evaluate(argument).ToStringWith(environment.OutputFormat)));

        Emit(text + environment.OutputRecordSeparator, print.Redirection);
        return AwkSignal.None;
    }

    private AwkSignal ExecutePrintf(AwkPrintfStatement print)
    {
        List<AwkValue> values = [.. print.Arguments.Select(Evaluate)];
        string format = values[0].ToStringWith(environment.ConvertFormat);

        Emit(AwkPrintf.Format(format, values.Skip(1).ToList(), environment.ConvertFormat), print.Redirection);
        return AwkSignal.None;
    }

    private void Emit(string text, AwkRedirection? redirection)
    {
        if (redirection is null)
        {
            output.Write(text);
            return;
        }

        output.WriteTo(Evaluate(redirection.Target).ToStringWith(environment.ConvertFormat), redirection.Kind, text);
    }

    private AwkValue Evaluate(AwkExpression expression) => expression switch
    {
        AwkNumberLiteral number => AwkValue.Of(number.Value),
        AwkStringLiteral text => AwkValue.Of(text.Value),
        AwkRegexLiteral pattern => AwkValue.Of(regexes.Compile(pattern.Pattern).IsMatch(environment.Record.Text)),
        AwkVariable variable => environment.Read(variable.Name),
        AwkField field => AwkValue.FromInput(environment.Record.Field(FieldIndex(field.Index))),
        AwkArrayElement element => environment.Array(element.Name).Read(Subscript(element.Subscripts)),
        AwkGrouping group => Evaluate(group.Inner),
        AwkAssignment assignment => EvaluateAssignment(assignment),
        AwkTernary ternary => Evaluate(ternary.Condition).IsTrue()
            ? Evaluate(ternary.WhenTrue)
            : Evaluate(ternary.WhenFalse),
        AwkBinary binary => EvaluateBinary(binary),
        AwkUnary unary => EvaluateUnary(unary),
        AwkIncrement increment => EvaluateIncrement(increment),
        AwkMatchExpression match => EvaluateMatch(match),
        AwkMembership membership => AwkValue.Of(
            environment.Array(membership.ArrayName).Contains(Subscript(membership.Subscripts))),
        AwkCall call => CallFunction(call),
        AwkBuiltinCall call => CallBuiltin(call),
        _ => AwkValue.Uninitialized,
    };

    private int FieldIndex(AwkExpression expression)
    {
        int index = (int)Evaluate(expression).ToNumber();

        return index >= 0 ? index : throw new AwkRuntimeException($"trying to access out of range field {index}");
    }

    private AwkValue EvaluateAssignment(AwkAssignment assignment)
    {
        if (assignment.Operator == AwkAssignOperator.Assign)
        {
            AwkValue assigned = Evaluate(assignment.Value);
            Store(assignment.Target, assigned);
            return assigned;
        }

        double left = Evaluate(assignment.Target).ToNumber();
        double right = Evaluate(assignment.Value).ToNumber();
        AwkValue combined = AwkValue.Of(Arithmetic(OperatorFor(assignment.Operator), left, right));
        Store(assignment.Target, combined);

        return combined;
    }

    private static AwkBinaryOperator OperatorFor(AwkAssignOperator assignment) => assignment switch
    {
        AwkAssignOperator.Add => AwkBinaryOperator.Add,
        AwkAssignOperator.Subtract => AwkBinaryOperator.Subtract,
        AwkAssignOperator.Multiply => AwkBinaryOperator.Multiply,
        AwkAssignOperator.Divide => AwkBinaryOperator.Divide,
        AwkAssignOperator.Modulo => AwkBinaryOperator.Modulo,
        _ => AwkBinaryOperator.Power,
    };

    private void Store(AwkExpression target, AwkValue value)
    {
        switch (target)
        {
            case AwkGrouping group:
                Store(group.Inner, value);
                return;
            case AwkVariable variable:
                environment.Write(variable.Name, value);
                return;
            case AwkField field:
                environment.Record.SetField(
                    FieldIndex(field.Index),
                    value.ToStringWith(environment.ConvertFormat),
                    environment.OutputFieldSeparator);
                return;
            case AwkArrayElement element:
                environment.Array(element.Name).Write(Subscript(element.Subscripts), value);
                return;
            default:
                throw new AwkRuntimeException("assignment to something that is not a variable");
        }
    }

    private AwkValue EvaluateBinary(AwkBinary binary) => binary.Operator switch
    {
        AwkBinaryOperator.Or => AwkValue.Of(Evaluate(binary.Left).IsTrue() || Evaluate(binary.Right).IsTrue()),
        AwkBinaryOperator.And => AwkValue.Of(Evaluate(binary.Left).IsTrue() && Evaluate(binary.Right).IsTrue()),
        AwkBinaryOperator.Concatenate => AwkValue.Of(
            Evaluate(binary.Left).ToStringWith(environment.ConvertFormat)
            + Evaluate(binary.Right).ToStringWith(environment.ConvertFormat)),
        AwkBinaryOperator.Less or AwkBinaryOperator.LessOrEqual or AwkBinaryOperator.Greater
            or AwkBinaryOperator.GreaterOrEqual or AwkBinaryOperator.Equal or AwkBinaryOperator.NotEqual =>
            AwkValue.Of(Relates(binary)),
        _ => AwkValue.Of(Arithmetic(
            binary.Operator,
            Evaluate(binary.Left).ToNumber(),
            Evaluate(binary.Right).ToNumber())),
    };

    private bool Relates(AwkBinary binary)
    {
        int order = AwkComparison.Compare(
            Evaluate(binary.Left),
            Evaluate(binary.Right),
            environment.ConvertFormat);

        return binary.Operator switch
        {
            AwkBinaryOperator.Less => order < 0,
            AwkBinaryOperator.LessOrEqual => order <= 0,
            AwkBinaryOperator.Greater => order > 0,
            AwkBinaryOperator.GreaterOrEqual => order >= 0,
            AwkBinaryOperator.Equal => order == 0,
            _ => order != 0,
        };
    }

    private static double Arithmetic(AwkBinaryOperator arithmetic, double left, double right) => arithmetic switch
    {
        AwkBinaryOperator.Add => left + right,
        AwkBinaryOperator.Subtract => left - right,
        AwkBinaryOperator.Multiply => left * right,
        AwkBinaryOperator.Divide => right == 0 ? throw new AwkRuntimeException("division by zero") : left / right,
        AwkBinaryOperator.Modulo => right == 0 ? throw new AwkRuntimeException("division by zero in %") : left % right,
        _ => Math.Pow(left, right),
    };

    private AwkValue EvaluateUnary(AwkUnary unary) => unary.Operator switch
    {
        AwkUnaryOperator.Not => AwkValue.Of(!Evaluate(unary.Operand).IsTrue()),
        AwkUnaryOperator.Negate => AwkValue.Of(-Evaluate(unary.Operand).ToNumber()),
        _ => AwkValue.Of(Evaluate(unary.Operand).ToNumber()),
    };

    private AwkValue EvaluateIncrement(AwkIncrement increment)
    {
        double before = Evaluate(increment.Target).ToNumber();
        double after = increment.IsDecrement ? before - 1 : before + 1;
        Store(increment.Target, AwkValue.Of(after));

        return AwkValue.Of(increment.IsPrefix ? after : before);
    }

    private AwkValue EvaluateMatch(AwkMatchExpression match)
    {
        bool matched = PatternFor(match.Pattern)
            .IsMatch(Evaluate(match.Subject).ToStringWith(environment.ConvertFormat));

        return AwkValue.Of(matched != match.IsNegated);
    }

    // A regular expression literal in this position is the pattern itself; anything else is a string
    // that becomes one, translated at the moment it resolves.
    private AwkRegex PatternFor(AwkExpression expression) => expression is AwkRegexLiteral literal
        ? regexes.Compile(literal.Pattern)
        : regexes.Compile(Evaluate(expression).ToStringWith(environment.ConvertFormat));

    private string Subscript(IReadOnlyList<AwkExpression> subscripts) => string.Join(
        environment.SubscriptSeparator,
        subscripts.Select(subscript => Evaluate(subscript).ToStringWith(environment.ConvertFormat)));

    private AwkValue CallFunction(AwkCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out AwkFunction? function))
        {
            throw new AwkRuntimeException($"calling undefined function {call.Name}");
        }

        // Two guards, because the declared limit is not the real one: a thousand awk frames is several
        // thousand frames of this interpreter, so the stack can run out first. Either way the answer is
        // an awk error, never a StackOverflowException, which no catch could turn into one.
        if (environment.Depth >= MaximumCallDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw new AwkRuntimeException("function call nested too deep");
        }

        Dictionary<string, AwkSlot> frame = BindParameters(call, function);
        returned = AwkValue.Uninitialized;
        environment.PushFrame(frame);

        try
        {
            Execute(function.Body);
        }
        finally
        {
            environment.PopFrame();
        }

        return returned;
    }

    // A bare variable name shares its array with the parameter; everything else is a value. The
    // sharing is lazy, so a name that is still undecided becomes an array only if the callee uses it
    // as one.
    private Dictionary<string, AwkSlot> BindParameters(AwkCall call, AwkFunction function)
    {
        Dictionary<string, AwkSlot> frame = new(StringComparer.Ordinal);

        for (int index = 0; index < function.Parameters.Count; index++)
        {
            AwkSlot slot = new();

            if (index < call.Arguments.Count)
            {
                Bind(slot, call.Arguments[index]);
            }

            frame[function.Parameters[index]] = slot;
        }

        return frame;
    }

    private void Bind(AwkSlot slot, AwkExpression argument)
    {
        if (argument is AwkVariable variable)
        {
            slot.ShareArrayWith(environment.Slot(variable.Name));
            slot.Value = environment.Read(variable.Name);
            return;
        }

        slot.Value = Evaluate(argument);
    }

    private AwkValue CallBuiltin(AwkBuiltinCall call) => call.Builtin switch
    {
        AwkBuiltin.Length => Length(call),
        AwkBuiltin.Substr => AwkValue.Of(AwkStringBuiltins.Substring(
            Text(call.Arguments[0]),
            Evaluate(call.Arguments[1]).ToNumber(),
            call.Arguments.Count > 2 ? Evaluate(call.Arguments[2]).ToNumber() : null)),
        AwkBuiltin.Index => AwkValue.Of((double)AwkStringBuiltins.IndexOf(
            Text(call.Arguments[0]),
            Text(call.Arguments[1]))),
        AwkBuiltin.Split => Split(call),
        AwkBuiltin.Sub or AwkBuiltin.Gsub => Substitute(call),
        AwkBuiltin.Match => Match(call),
        AwkBuiltin.Sprintf => AwkValue.Of(Sprintf(call.Arguments)),
        AwkBuiltin.Atan2 => AwkValue.Of(Math.Atan2(
            Evaluate(call.Arguments[0]).ToNumber(),
            Evaluate(call.Arguments[1]).ToNumber())),
        AwkBuiltin.Rand => AwkValue.Of(math.Random()),
        AwkBuiltin.Srand => AwkValue.Of(call.Arguments.Count == 0
            ? math.SeedFromClock()
            : math.Seed(Evaluate(call.Arguments[0]).ToNumber())),
        AwkBuiltin.Tolower => AwkValue.Of(Text(call.Arguments[0]).ToLowerInvariant()),
        AwkBuiltin.Toupper => AwkValue.Of(Text(call.Arguments[0]).ToUpperInvariant()),
        AwkBuiltin.Close => AwkValue.Of((double)output.Close(Text(call.Arguments[0]))),
        AwkBuiltin.Fflush => AwkValue.Of(0d),
        _ => AwkValue.Of(AwkMathBuiltins.Apply(call.Builtin, Evaluate(call.Arguments[0]).ToNumber())),
    };

    private AwkValue Length(AwkBuiltinCall call)
    {
        if (call.Arguments.Count == 0)
        {
            return AwkValue.Of((double)environment.Record.Text.Length);
        }

        if (call.Arguments[0] is AwkVariable variable && environment.Slot(variable.Name).HasArray)
        {
            return AwkValue.Of((double)environment.Slot(variable.Name).Array.Count);
        }

        return AwkValue.Of((double)Text(call.Arguments[0]).Length);
    }

    private AwkValue Split(AwkBuiltinCall call)
    {
        string subject = Text(call.Arguments[0]);
        AwkArray target = ArrayOf(call.Arguments[1]);
        bool isDefault = call.Arguments.Count < 3;

        string separator = isDefault
            ? environment.FieldSeparator
            : call.Arguments[2] is AwkRegexLiteral literal ? literal.Pattern : Text(call.Arguments[2]);

        return AwkValue.Of((double)AwkStringBuiltins.Split(subject, target, separator, isDefault, regexes));
    }

    private static readonly AwkExpression WholeRecord = new AwkField(new AwkNumberLiteral(0));

    private AwkValue Substitute(AwkBuiltinCall call)
    {
        AwkExpression target = call.Arguments.Count > 2 ? call.Arguments[2] : WholeRecord;
        string subject = Evaluate(target).ToStringWith(environment.ConvertFormat);

        (string text, int count) = AwkStringBuiltins.Substitute(
            subject,
            PatternFor(call.Arguments[0]),
            Text(call.Arguments[1]),
            call.Builtin == AwkBuiltin.Gsub);

        if (count > 0)
        {
            Store(target, AwkValue.Of(text));
        }

        return AwkValue.Of((double)count);
    }

    private AwkValue Match(AwkBuiltinCall call)
    {
        string subject = Text(call.Arguments[0]);
        (int start, int length) = PatternFor(call.Arguments[1]).Find(subject, 0) ?? (-1, -1);

        environment.Write("RSTART", AwkValue.Of(start + 1d));
        environment.Write("RLENGTH", AwkValue.Of(start < 0 ? -1d : length));

        return AwkValue.Of(start + 1d);
    }

    private string Sprintf(IReadOnlyList<AwkExpression> arguments)
    {
        List<AwkValue> values = [.. arguments.Select(Evaluate)];

        return AwkPrintf.Format(
            values[0].ToStringWith(environment.ConvertFormat),
            values.Skip(1).ToList(),
            environment.ConvertFormat);
    }

    private AwkArray ArrayOf(AwkExpression expression) => expression is AwkVariable variable
        ? environment.Array(variable.Name)
        : throw new AwkRuntimeException("an array name was expected");

    private string Text(AwkExpression expression) =>
        Evaluate(expression).ToStringWith(environment.ConvertFormat);

    private int ArgumentCount() => (int)environment.Read("ARGC").ToNumber();

    private string ArgumentAt(int index) => environment
        .Array("ARGV")
        .Read(index.ToString(CultureInfo.InvariantCulture))
        .ToStringWith(environment.ConvertFormat);

    // An operand shaped like `name=value` is an assignment applied where it stands, not a file, which
    // is what lets `awk '{...}' a.txt n=2 b.txt` see a different n over the second file.
    private static bool IsAssignmentOperand(string operand)
    {
        int equals = operand.IndexOf('=', StringComparison.Ordinal);

        if (equals <= 0 || !(char.IsAsciiLetter(operand[0]) || operand[0] == '_'))
        {
            return false;
        }

        return operand[..equals].All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    private void ApplyAssignment(string assignment)
    {
        int equals = assignment.IndexOf('=', StringComparison.Ordinal);
        environment.Write(assignment[..equals], AwkValue.FromInput(AwkEscapes.Decode(assignment[(equals + 1)..])));
    }

    private bool Guarded(Action work)
    {
        try
        {
            work();
            return true;
        }
        catch (AwkRuntimeException failure)
        {
            Report(failure.Message);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void Report(string message)
    {
        context.WriteError($"awk: {message}\n");
        ExitCode = 2;
        hasFailed = true;
    }
}
