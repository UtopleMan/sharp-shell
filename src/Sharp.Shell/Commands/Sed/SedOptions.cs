namespace Sharp.Shell.Commands.Sed;

// One piece of the script, in the position the command line gave it. Order is load-bearing:
// `sed -f a.sed -e 's/1/Z/'` and `sed -e 's/1/Z/' -f a.sed` run their commands in opposite orders.
internal sealed record SedScriptSource(bool IsFile, string Value);

// sed's command line, read once and used twice: by CheckFlags before anything runs, and by the
// applet when it does. Flags this sandbox cannot honour are reported as UnsupportedFlag so the whole
// command line escalates; malformed usage is reported as Error so sed reports it the way sed does.
internal sealed record SedOptions(
    bool SuppressesAutoPrint,
    IReadOnlyList<SedScriptSource> ScriptSources,
    IReadOnlyList<string> Operands,
    IReadOnlyList<int> ScriptIndices,
    bool IsExtendedRegex,
    bool IsSeparate,
    bool IsInPlace,
    string InPlaceSuffix,
    bool IsNullSeparated,
    bool IsPosix,
    int LineWidth,
    string? Error,
    string? UnsupportedFlag)
{
    private const int DefaultLineWidth = 70;

    public bool HasScriptSource => ScriptSources.Count > 0;

    public bool HasScriptFile => ScriptSources.Any(source => source.IsFile);

    // The script as far as it can be known without reading anything: null when a -f file is
    // involved, because classification does no I/O.
    public string? InlineScript =>
        HasScriptFile ? null
        : HasScriptSource ? string.Join('\n', ScriptSources.Select(source => source.Value))
        : Operands.Count > 0 ? Operands[0]
        : null;

    public IReadOnlyList<string> InputFiles => HasScriptSource ? Operands : [.. Operands.Skip(1)];

    public static SedOptions Parse(IReadOnlyList<string> arguments) => new SedOptionReader(arguments).Read();

    public static SedOptions Empty { get; } =
        new(false, [], [], [], false, false, false, string.Empty, false, false, DefaultLineWidth, null, null);
}

internal sealed class SedOptionReader(IReadOnlyList<string> arguments)
{
    private const int DefaultLineWidth = 70;

    private const string ImpossibleFlagSuffix = " cannot run inside the sandbox";

    private readonly List<SedScriptSource> scriptSources = [];

    private readonly List<int> scriptIndices = [];

    private readonly List<string> operands = [];

    private bool suppressesAutoPrint;

    private bool isExtendedRegex;

    private bool isSeparate;

    private bool isInPlace;

    private string inPlaceSuffix = string.Empty;

    private bool isNullSeparated;

    private bool isPosix;

    private int lineWidth = DefaultLineWidth;

    private bool isPastOptions;

    private string? error;

    private string? unsupportedFlag;

    private int index;

    public SedOptions Read()
    {
        for (index = 0; index < arguments.Count && unsupportedFlag is null; index++)
        {
            string argument = arguments[index];

            if (isPastOptions || !FlagReader.IsFlag(argument))
            {
                if (operands.Count == 0 && scriptSources.Count == 0)
                {
                    scriptIndices.Add(index);
                }

                operands.Add(argument);
                continue;
            }

            if (argument == "--")
            {
                isPastOptions = true;
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                ReadLongOption(argument);
                continue;
            }

            ReadShortOptions(argument);
        }

        return new SedOptions(
            suppressesAutoPrint,
            scriptSources,
            operands,
            scriptIndices,
            isExtendedRegex,
            isSeparate,
            isInPlace,
            inPlaceSuffix,
            isNullSeparated,
            isPosix,
            lineWidth,
            error,
            unsupportedFlag);
    }

    private void ReadLongOption(string argument)
    {
        string name = argument;
        string? value = null;
        int equals = argument.IndexOf('=', StringComparison.Ordinal);

        if (equals > 0)
        {
            name = argument[..equals];
            value = argument[(equals + 1)..];
        }

        switch (name)
        {
            case "--quiet" or "--silent": suppressesAutoPrint = true; return;
            case "--expression": AddScript(value ?? TakeValue(name)); return;
            case "--file": scriptSources.Add(new SedScriptSource(true, value ?? TakeValue(name))); return;
            case "--regexp-extended": isExtendedRegex = true; return;
            case "--separate": isSeparate = true; return;
            case "--null-data" or "--zero-terminated": isNullSeparated = true; return;
            case "--posix": isPosix = true; return;
            case "--unbuffered": return;
            case "--in-place":
                isInPlace = true;
                inPlaceSuffix = value ?? string.Empty;
                return;
            case "--line-length":
                lineWidth = ReadWidth(value ?? TakeValue(name));
                return;
            default:
                unsupportedFlag = $"{name}{ImpossibleFlagSuffix}";
                return;
        }
    }

    private void ReadShortOptions(string argument)
    {
        for (int position = 1; position < argument.Length && unsupportedFlag is null; position++)
        {
            char letter = argument[position];

            switch (letter)
            {
                case 'n': suppressesAutoPrint = true; break;
                case 'E' or 'r': isExtendedRegex = true; break;
                case 's': isSeparate = true; break;
                case 'z': isNullSeparated = true; break;
                case 'u': break;
                case 'e': AddScript(TakeAttachedOrNext(argument, ref position, "-e")); break;
                case 'f': scriptSources.Add(new SedScriptSource(true, TakeAttachedOrNext(argument, ref position, "-f"))); break;
                case 'l': lineWidth = ReadWidth(TakeAttachedOrNext(argument, ref position, "-l")); break;
                case 'i': ReadInPlace(argument, ref position); break;
                default: unsupportedFlag = $"-{letter}"; break;
            }
        }
    }

    // GNU attaches the suffix (-i.bak); macOS agents write the BSD spelling (-i ''). An empty next
    // argument is unambiguously the BSD form, so it is accepted; a non-empty one would be
    // indistinguishable from the script and is left to be read as one.
    private void ReadInPlace(string argument, ref int position)
    {
        isInPlace = true;
        inPlaceSuffix = argument[(position + 1)..];
        position = argument.Length;

        if (inPlaceSuffix.Length == 0 && index + 1 < arguments.Count && arguments[index + 1].Length == 0)
        {
            index++;
        }
    }

    // The index is read after the value has been taken, so an attached `-e's/x/y/'` records its own
    // word and a separate `-e 's/x/y/'` records the one after it. A `-f` path is data, not program
    // text, so it is deliberately not recorded.
    private void AddScript(string script)
    {
        scriptSources.Add(new SedScriptSource(false, script));
        scriptIndices.Add(index);
    }

    private string TakeAttachedOrNext(string argument, ref int position, string flag)
    {
        string attached = argument[(position + 1)..];
        position = argument.Length;

        if (attached.Length > 0)
        {
            return attached;
        }

        return TakeValue(flag);
    }

    private string TakeValue(string flag)
    {
        if (index + 1 >= arguments.Count)
        {
            error ??= $"option '{flag}' requires an argument";
            return string.Empty;
        }

        index++;
        return arguments[index];
    }

    private int ReadWidth(string value)
    {
        if (int.TryParse(value, out int width))
        {
            return width;
        }

        error ??= $"invalid line length: {value}";
        return DefaultLineWidth;
    }
}
