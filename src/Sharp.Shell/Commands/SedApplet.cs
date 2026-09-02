using Sharp.Shell.Commands.Sed;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// GNU sed 4.x, with the parts that cannot exist inside the sandbox refused rather than faked: `e`
// and `s///e` run a shell command, `--follow-symlinks` walks out of the preopen, and a regular
// expression the POSIX translator will not vouch for would answer differently from the sed the
// author meant. Each of those escalates the whole command line to the native tier.
//
// Both in-place spellings are accepted. GNU attaches the suffix (-i.bak); the BSD form agents write
// on macOS passes it as a separate empty argument (-i '').
public sealed class SedApplet : IApplet
{
    public string Name => "sed";

    public bool Mutates => false;

    // Only the script has to be literal before expansion. Its file operands may expand freely, which
    // is what makes `sed -n '1,5p' "$file"` an owned command.
    public IReadOnlyList<int> ProgramTextArguments(IReadOnlyList<string> arguments) =>
        SedOptions.Parse(arguments).ScriptIndices;

    public IReadOnlyList<string> BundleableFlags => [];

    // -i rewrites the files it reads, so an in-place run reports both directions for the same
    // operands. A script named with -f is read as program text and is not reported.
    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments)
    {
        SedOptions options = SedOptions.Parse(arguments);
        IReadOnlyList<int> inputs = options.InputFileIndices;

        return options.IsInPlace ? new OperandPositions(inputs, inputs) : OperandPositions.Reading(inputs);
    }

    public bool MutatesWith(IReadOnlyList<string> arguments)
    {
        SedOptions options = SedOptions.Parse(arguments);

        if (options.IsInPlace || options.HasScriptFile)
        {
            return true;
        }

        string? script = options.InlineScript;

        if (script is null)
        {
            return false;
        }

        SedProgram? program = SedScriptParser.Parse(script, ParseOptionsFor(options)).Program;
        return program is null || program.Commands.Any(WritesAFile);
    }

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments)
    {
        SedOptions options = SedOptions.Parse(arguments);

        if (options.UnsupportedFlag is not null)
        {
            return FlagSupport.Reject(options.UnsupportedFlag);
        }

        // A script read from a file cannot be checked here: classification does no I/O, so the
        // check happens again when it runs.
        if (options.Error is not null || options.HasScriptFile || options.InlineScript is null)
        {
            return FlagSupport.Supported;
        }

        string? unsupported = SedScriptParser
            .Parse(options.InlineScript, ParseOptionsFor(options))
            .UnsupportedConstruct;

        return unsupported is null ? FlagSupport.Supported : FlagSupport.Reject(unsupported);
    }

    public AppletRun Run(AppletContext context)
    {
        SedOptions options = SedOptions.Parse(context.Arguments);

        if (options.UnsupportedFlag is not null)
        {
            return Report(context, $"sed: unsupported: {options.UnsupportedFlag}\n", 2);
        }

        if (options.Error is not null)
        {
            return Report(context, $"sed: {options.Error}\n", 2);
        }

        (string? script, string? scriptError) = ReadScript(options, context);

        if (script is null)
        {
            return Report(context, $"sed: {scriptError ?? "usage: sed [options] script [file...]"}\n", 2);
        }

        SedParseResult parsed = SedScriptParser.Parse(script, ParseOptionsFor(options));

        if (parsed.UnsupportedConstruct is not null)
        {
            return Report(context, $"sed: unsupported construct: {parsed.UnsupportedConstruct}\n", 2);
        }

        if (parsed.Program is null)
        {
            return Report(context, $"sed: -e expression #1: {parsed.Error}\n", 2);
        }

        return options.IsInPlace
            ? RunInPlace(parsed.Program, options, context)
            : RunStreaming(parsed.Program, options, context);
    }

    private static bool WritesAFile(SedCommand command) =>
        command is SedWriteFile or SedSubstitute { WriteFile: not null };

    private static IEnumerable<string> WriteTargetsOf(SedProgram program) => program.Commands
        .Select(command => command switch
        {
            SedWriteFile write => write.Path,
            SedSubstitute { WriteFile: not null } substitute => substitute.WriteFile,
            _ => null,
        })
        .Where(path => path is not null)
        .Distinct(StringComparer.Ordinal)!;

    private static SedParseOptions ParseOptionsFor(SedOptions options) =>
        new(options.IsExtendedRegex, !options.IsPosix);

    private static SedRunOptions RunOptionsFor(SedOptions options, string fileName) => new(
        AutoPrints: !options.SuppressesAutoPrint,
        Separator: options.IsNullSeparated ? '\0' : '\n',
        FileName: fileName,
        ListWidth: options.LineWidth,
        PrintsPatternSpaceOnMissingNextLine: !options.IsPosix);

    // -e and -f are read in the order the command line gave them: `sed -f a.sed -e 's/1/Z/'` runs
    // the file's commands first, and swapping the two arguments swaps the answer.
    private static (string? Script, string? Error) ReadScript(SedOptions options, AppletContext context)
    {
        if (!options.HasScriptFile)
        {
            return (options.InlineScript, null);
        }

        List<string> parts = [];

        foreach (SedScriptSource source in options.ScriptSources)
        {
            if (!source.IsFile)
            {
                parts.Add(source.Value);
                continue;
            }

            string absolute = context.State.Resolve(source.Value);

            if (!context.State.IsInsideRoot(absolute) || !File.Exists(absolute))
            {
                return (null, $"couldn't open file {source.Value}: No such file or directory");
            }

            parts.Add(File.ReadAllText(absolute));
        }

        return (string.Join('\n', parts), null);
    }

    private static AppletRun RunStreaming(SedProgram program, SedOptions options, AppletContext context)
    {
        AppletRun run = new();
        run.Output = Stream(program, options, context, run);
        return run;
    }

    private static IEnumerable<string> Stream(
        SedProgram program,
        SedOptions options,
        AppletContext context,
        AppletRun run)
    {
        using SedFileSystem files = new(context);
        files.Create(WriteTargetsOf(program));

        foreach ((string name, IEnumerable<string> input) in InputGroups(options, context, run))
        {
            SedMachine machine = new(
                program,
                RunOptionsFor(options, name),
                files,
                context.WriteError,
                context.CancellationToken);

            foreach (string chunk in machine.Run(input))
            {
                yield return chunk;
            }

            if (machine.ExitCode != 0)
            {
                run.ExitCode = machine.ExitCode;
            }

            if (machine.HasQuit)
            {
                break;
            }
        }

        if (files.ErrorExitCode != 0)
        {
            run.ExitCode = files.ErrorExitCode;
        }
    }

    // -s and -i give each file its own line numbering and its own `$`; without them the files are
    // one stream, which is what makes `sed -n '$p' a b` print the last line of b alone.
    private static IEnumerable<(string Name, IEnumerable<string> Input)> InputGroups(
        SedOptions options,
        AppletContext context,
        AppletRun run)
    {
        IReadOnlyList<string> files = options.InputFiles;

        if (files.Count == 0)
        {
            yield return ("-", context.Input);
            yield break;
        }

        char separator = options.IsNullSeparated ? '\0' : '\n';

        if (!options.IsSeparate)
        {
            yield return (files[0], ReadFiles(files, separator, context, run));
            yield break;
        }

        foreach (string file in files)
        {
            yield return (file, ReadFiles([file], separator, context, run));
        }
    }

    // sed reads records per file: a file whose last line has no newline still ends that line, so
    // concatenating one that lacks it with a following file must not glue the two lines into one.
    // Only the last file's missing newline is observable in the output.
    private static IEnumerable<string> ReadFiles(
        IReadOnlyList<string> files,
        char separator,
        AppletContext context,
        AppletRun run)
    {
        for (int index = 0; index < files.Count; index++)
        {
            char? lastCharacter = null;

            foreach (string chunk in ReadOne(files[index], context, run))
            {
                if (chunk.Length > 0)
                {
                    lastCharacter = chunk[^1];
                }

                yield return chunk;
            }

            bool isLastFile = index == files.Count - 1;

            if (!isLastFile && lastCharacter is not null && lastCharacter != separator)
            {
                yield return separator.ToString();
            }
        }
    }

    private static IEnumerable<string> ReadOne(string file, AppletContext context, AppletRun run)
    {
        if (file == "-")
        {
            return context.Input;
        }

        string absolute = context.State.Resolve(file);

        if (context.State.IsInsideRoot(absolute) && File.Exists(absolute))
        {
            return FileChunks.Read(absolute);
        }

        context.WriteError($"sed: can't read {file}: No such file or directory\n");
        run.ExitCode = 2;
        return TextStream.Empty;
    }

    private static AppletRun RunInPlace(SedProgram program, SedOptions options, AppletContext context)
    {
        if (options.InputFiles.Count == 0)
        {
            return Report(context, "sed: no input files while in place editing\n", 2);
        }

        AppletRun run = new();
        using SedFileSystem files = new(context);
        files.Create(WriteTargetsOf(program));

        foreach (string file in options.InputFiles)
        {
            EditInPlace(program, options, context, run, files, file);
        }

        if (files.ErrorExitCode != 0)
        {
            run.ExitCode = files.ErrorExitCode;
        }

        return run;
    }

    // The transformed text lands in a temporary file first, so a failure part way through cannot
    // leave a half-written source file behind.
    private static void EditInPlace(
        SedProgram program,
        SedOptions options,
        AppletContext context,
        AppletRun run,
        SedFileSystem files,
        string file)
    {
        if (!MutationGuard.TryResolve(file, context, "sed", out string absolute))
        {
            run.ExitCode = 4;
            return;
        }

        if (!File.Exists(absolute))
        {
            context.WriteError($"sed: can't read {file}: No such file or directory\n");
            run.ExitCode = 2;
            return;
        }

        SedMachine machine = new(
            program,
            RunOptionsFor(options, file),
            files,
            context.WriteError,
            context.CancellationToken);

        string transformed = TextStream.Collect(machine.Run(FileChunks.Read(absolute)));

        if (machine.ExitCode != 0)
        {
            run.ExitCode = machine.ExitCode;
        }

        string temporary = $"{absolute}.sed-tmp";
        File.WriteAllText(temporary, transformed);

        if (options.InPlaceSuffix.Length > 0)
        {
            File.Copy(absolute, BackupPathFor(absolute, options.InPlaceSuffix), overwrite: true);
        }

        File.Move(temporary, absolute, overwrite: true);
    }

    // GNU reads a `*` in the suffix as "the file name here", which is how a backup lands in another
    // directory or gains a prefix instead of an extension.
    private static string BackupPathFor(string absolute, string suffix)
    {
        if (!suffix.Contains('*', StringComparison.Ordinal))
        {
            return absolute + suffix;
        }

        string directory = Path.GetDirectoryName(absolute) ?? string.Empty;
        string name = Path.GetFileName(absolute);
        string replaced = suffix.Replace("*", name, StringComparison.Ordinal);

        return replaced.Contains('/', StringComparison.Ordinal)
            ? replaced
            : Path.Combine(directory, replaced);
    }

    private static AppletRun Report(AppletContext context, string message, int exitCode)
    {
        context.WriteError(message);
        return AppletRun.Failed(exitCode);
    }
}
