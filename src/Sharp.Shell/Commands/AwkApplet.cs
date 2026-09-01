using Sharp.Shell.Commands.Awk;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// POSIX awk, with the parts that cannot exist inside the sandbox refused rather than faked. `system()`
// and both pipe directions run a shell; every `getline` form reads from somewhere the sandbox does not
// bound; the gawk-only builtins would answer differently from the awk the author meant. Each of those
// escalates the whole command line to the native tier.
//
// `gawk` is deliberately not one of the names claimed: someone who types it may want exactly the
// extensions this refuses.
public sealed class AwkApplet(string name) : IApplet
{
    public string Name => name;

    public bool Mutates => false;

    public IReadOnlyList<string> BundleableFlags => [];

    // The program is a program, not data: `awk "$prog" f` reaches classification as an empty script,
    // which parses perfectly and is not what will run.
    public IReadOnlyList<int> ProgramTextArguments(IReadOnlyList<string> arguments)
    {
        return AwkOptions.Parse(arguments).HasInlineProgramAt(out int position) ? [position] : [];
    }

    public bool MutatesWith(IReadOnlyList<string> arguments)
    {
        AwkOptions options = AwkOptions.Parse(arguments);

        if (options.HasProgramFile)
        {
            return true;
        }

        if (options.InlineProgram is null)
        {
            return false;
        }

        AwkProgram? program = AwkParser.Parse(options.InlineProgram).Program;

        return program is null || AwkProgramFacts.WritesOutsideStandardStreams(program);
    }

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments)
    {
        AwkOptions options = AwkOptions.Parse(arguments);

        if (options.UnsupportedFlag is not null)
        {
            return FlagSupport.Reject(options.UnsupportedFlag);
        }

        // A program read from a file cannot be checked here: classification does no I/O, so the
        // check happens again when it runs.
        if (options.Error is not null || options.HasProgramFile || options.InlineProgram is null)
        {
            return FlagSupport.Supported;
        }

        string? unsupported = AwkParser.Parse(options.InlineProgram).UnsupportedConstruct;

        return unsupported is null ? FlagSupport.Supported : FlagSupport.Reject(unsupported);
    }

    public AppletRun Run(AppletContext context)
    {
        AwkOptions options = AwkOptions.Parse(context.Arguments);

        if (options.UnsupportedFlag is not null)
        {
            return Report(context, $"awk: unsupported: {options.UnsupportedFlag}\n");
        }

        if (options.Error is not null)
        {
            return Report(context, $"awk: {options.Error}\n");
        }

        (string? source, string? sourceError) = ReadProgram(options, context);

        if (source is null)
        {
            return Report(context, $"awk: {sourceError ?? "usage: awk [-F fs][-v var=value][prog | -f progfile][file ...]"}\n");
        }

        AwkParseResult parsed = AwkParser.Parse(source);

        if (parsed.UnsupportedConstruct is not null)
        {
            return Report(context, $"awk: unsupported construct: {parsed.UnsupportedConstruct}\n");
        }

        return parsed.Program is null
            ? Report(context, $"awk: {parsed.Error}\n")
            : Start(parsed.Program, options, context);
    }

    private static AppletRun Start(AwkProgram program, AwkOptions options, AppletContext context)
    {
        AwkOutput output = new(context);
        AwkInterpreter interpreter = new(program, RunOptionsFor(options, context), output, context);
        AppletRun run = new();
        run.Output = Stream(interpreter, output, run);

        return run;
    }

    private static IEnumerable<string> Stream(AwkInterpreter interpreter, AwkOutput output, AppletRun run)
    {
        using (output)
        {
            foreach (string chunk in interpreter.Run())
            {
                yield return chunk;
            }

            run.ExitCode = interpreter.ExitCode != 0 ? interpreter.ExitCode : output.ErrorExitCode;
        }
    }

    // There is no separate environment inside the sandbox — export assigns a variable and nothing
    // else — so ENVIRON is the shell's variables.
    private static AwkRunOptions RunOptionsFor(AwkOptions options, AppletContext context) => new(
        options.FieldSeparator,
        options.Assignments,
        options.InputOperands,
        context.State.Variables);

    private static (string? Source, string? Error) ReadProgram(AwkOptions options, AppletContext context)
    {
        if (!options.HasProgramFile)
        {
            return (options.InlineProgram, null);
        }

        List<string> parts = [];

        foreach (string file in options.ProgramFiles)
        {
            string absolute = context.State.Resolve(file);

            if (!context.State.IsInsideRoot(absolute) || !File.Exists(absolute))
            {
                return (null, $"can't open file {file}");
            }

            parts.Add(File.ReadAllText(absolute));
        }

        return (string.Join('\n', parts), null);
    }

    private static AppletRun Report(AppletContext context, string message)
    {
        context.WriteError(message);
        return AppletRun.Failed(2);
    }
}
