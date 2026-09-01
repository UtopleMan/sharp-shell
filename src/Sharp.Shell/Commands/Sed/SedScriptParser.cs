using System.Text;
using System.Text.RegularExpressions;
using Sharp.Shell.Text;

namespace Sharp.Shell.Commands.Sed;

// Reads a sed script into a flat instruction list. Parsing is separated from execution because
// classification needs the parse without the run: the applet has to answer "do we own this script?"
// before anything happens, and the answer is this parse succeeding.
//
// Failures split two ways. An Error is invalid sed and becomes exit 2 with a message, the way real
// sed reports it. An UnsupportedConstruct is valid sed this shell does not own — a shell-executing
// command, a regular expression the translator will not vouch for — and escalates the whole command
// line to the native tier rather than answering differently.
internal sealed class SedScriptParser
{
    private const decimal SupportedVersion = 4.9m;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private readonly string script;

    private readonly SedParseOptions options;

    private readonly List<SedCommand> commands = [];

    private readonly Dictionary<string, int> labels = new(StringComparer.Ordinal);

    private readonly List<int> openBlocks = [];

    private readonly List<string> branchTargets = [];

    private int index;

    private string? error;

    private string? unsupported;

    private bool suppressesAutoPrint;

    private SedScriptParser(string script, SedParseOptions options)
    {
        this.script = script;
        this.options = options;
    }

    public static SedParseResult Parse(string script, SedParseOptions? options = null) =>
        new SedScriptParser(script, options ?? new SedParseOptions()).Run();

    private bool HasFailed => error is not null || unsupported is not null;

    private SedParseResult Run()
    {
        ReadAutoPrintSuppression();

        while (!HasFailed)
        {
            SkipSeparators();

            if (index >= script.Length)
            {
                break;
            }

            ParseStatement();
        }

        if (!HasFailed && openBlocks.Count > 0)
        {
            Fail("unmatched `{'");
        }

        if (!HasFailed)
        {
            VerifyBranchTargets();
        }

        if (unsupported is not null)
        {
            return SedParseResult.Unsupported(unsupported);
        }

        return error is not null
            ? SedParseResult.Invalid(error)
            : SedParseResult.Parsed(new SedProgram(commands, labels, suppressesAutoPrint));
    }

    // `#n` on the very first line is sed's in-script spelling of -n.
    private void ReadAutoPrintSuppression()
    {
        if (!script.StartsWith("#n", StringComparison.Ordinal))
        {
            return;
        }

        if (script.Length > 2 && script[2] != '\n')
        {
            return;
        }

        suppressesAutoPrint = true;
        index = 2;
    }

    private void ParseStatement()
    {
        if (script[index] == '#')
        {
            SkipComment();
            return;
        }

        if (script[index] == '}')
        {
            CloseBlock();
            return;
        }

        SedRange? range = ParseRange();

        if (HasFailed)
        {
            return;
        }

        SkipBlanks();

        if (index >= script.Length)
        {
            Fail("missing command");
            return;
        }

        char code = script[index];
        index++;
        ParseCommand(code, range);
    }

    private void ParseCommand(char code, SedRange? range)
    {
        switch (code)
        {
            case '{': OpenBlock(range); return;
            case 's': ParseSubstitute(range); return;
            case 'y': ParseTransliterate(range); return;
            case 'p': Add(new SedPrint(range, FirstLineOnly: false)); return;
            case 'P': Add(new SedPrint(range, FirstLineOnly: true)); return;
            case 'd': Add(new SedDelete(range, FirstLineOnly: false)); return;
            case 'D': Add(new SedDelete(range, FirstLineOnly: true)); return;
            case 'n': Add(new SedNextLine(range, Appends: false)); return;
            case 'N': Add(new SedNextLine(range, Appends: true)); return;
            case 'h': Add(new SedHold(range, SedHoldAction.CopyToHold)); return;
            case 'H': Add(new SedHold(range, SedHoldAction.AppendToHold)); return;
            case 'g': Add(new SedHold(range, SedHoldAction.CopyFromHold)); return;
            case 'G': Add(new SedHold(range, SedHoldAction.AppendFromHold)); return;
            case 'x': Add(new SedHold(range, SedHoldAction.Exchange)); return;
            case 'b': ParseBranch(range, SedBranchCondition.Always); return;
            case 't': ParseBranch(range, SedBranchCondition.IfSubstituted); return;
            case 'T': ParseBranch(range, SedBranchCondition.IfNotSubstituted); return;
            case ':': ParseLabelDefinition(range); return;
            case '=': Add(new SedLineNumber(range)); return;
            case 'a': ParseTextCommand(range, SedTextPlacement.Append); return;
            case 'i': ParseTextCommand(range, SedTextPlacement.Insert); return;
            case 'c': ParseTextCommand(range, SedTextPlacement.Change); return;
            case 'r': ParseReadFile(range, oneLineOnly: false); return;
            case 'R': ParseReadFile(range, oneLineOnly: true); return;
            case 'w': ParseWriteFile(range, firstLineOnly: false); return;
            case 'W': ParseWriteFile(range, firstLineOnly: true); return;
            case 'l': ParseList(range); return;
            case 'q': ParseQuit(range, silent: false); return;
            case 'Q': ParseQuit(range, silent: true); return;
            case 'z': Add(new SedZap(range)); return;
            case 'F': Add(new SedFileName(range)); return;
            case 'v': ParseVersion(range); return;
            case 'e': Unsupport("the `e' command runs a shell command"); return;
            case '#': SkipComment(); return;
            default: Fail($"unknown command: `{code}'"); return;
        }
    }

    private SedRange? ParseRange()
    {
        SedAddress? start = ParseAddress();

        if (start is null || HasFailed)
        {
            return null;
        }

        (SedRangeEndKind endKind, SedAddress? end, int endValue) = ParseRangeEnd();

        if (HasFailed)
        {
            return null;
        }

        return new SedRange(start, endKind, end, endValue, ParseNegation());
    }

    private (SedRangeEndKind Kind, SedAddress? End, int Value) ParseRangeEnd()
    {
        SkipBlanks();

        if (index >= script.Length || script[index] != ',')
        {
            return (SedRangeEndKind.None, null, 0);
        }

        index++;
        SkipBlanks();

        if (index < script.Length && script[index] is '+' or '~')
        {
            bool isRelative = script[index] == '+';
            index++;
            return (isRelative ? SedRangeEndKind.RelativeLines : SedRangeEndKind.NextMultiple, null, ReadNumber() ?? 0);
        }

        SedAddress? end = ParseAddress();

        if (end is null && !HasFailed)
        {
            Fail("expected an address after `,'");
        }

        return (SedRangeEndKind.Address, end, 0);
    }

    private bool ParseNegation()
    {
        bool isNegated = false;
        SkipBlanks();

        while (index < script.Length && script[index] == '!')
        {
            index++;
            isNegated = !isNegated;
            SkipBlanks();
        }

        return isNegated;
    }

    private SedAddress? ParseAddress()
    {
        SkipBlanks();

        if (index >= script.Length)
        {
            return null;
        }

        char current = script[index];

        if (char.IsAsciiDigit(current))
        {
            return ParseNumericAddress();
        }

        if (current == '$')
        {
            index++;
            return new SedAddress(SedAddressKind.Last);
        }

        if (current == '/')
        {
            return ParseRegexAddress('/');
        }

        if (current != '\\' || index + 1 >= script.Length)
        {
            return null;
        }

        index++;
        return ParseRegexAddress(script[index]);
    }

    private SedAddress ParseNumericAddress()
    {
        int line = ReadNumber() ?? 0;

        if (index < script.Length && script[index] == '~')
        {
            index++;
            return new SedAddress(SedAddressKind.Step, line, ReadNumber() ?? 0);
        }

        return line == 0 ? new SedAddress(SedAddressKind.Zero) : new SedAddress(SedAddressKind.Line, line);
    }

    private SedAddress? ParseRegexAddress(char delimiter)
    {
        index++;
        string? pattern = ReadPattern(delimiter);

        if (pattern is null)
        {
            Fail("unterminated address regular expression");
            return null;
        }

        (bool ignoresCase, bool isMultiline) = ReadAddressFlags();

        if (pattern.Length == 0)
        {
            return new SedAddress(SedAddressKind.Regex);
        }

        Regex? compiled = Compile(pattern, ignoresCase, isMultiline);
        return compiled is null ? null : new SedAddress(SedAddressKind.Regex, Pattern: compiled);
    }

    private (bool IgnoresCase, bool IsMultiline) ReadAddressFlags()
    {
        bool ignoresCase = false;
        bool isMultiline = false;

        while (index < script.Length && script[index] is 'I' or 'M')
        {
            ignoresCase |= script[index] == 'I';
            isMultiline |= script[index] == 'M';
            index++;
        }

        return (ignoresCase, isMultiline);
    }

    private void ParseSubstitute(SedRange? range)
    {
        if (index >= script.Length || script[index] is '\n' or '\\')
        {
            Fail("unterminated `s' command");
            return;
        }

        char delimiter = script[index];
        index++;

        string? pattern = ReadPattern(delimiter);
        string? replacement = pattern is null ? null : ReadRaw(delimiter);

        if (pattern is null || replacement is null)
        {
            Fail("unterminated `s' command");
            return;
        }

        SubstituteFlags flags = ReadSubstituteFlags();

        if (HasFailed)
        {
            return;
        }

        Regex? compiled = pattern.Length == 0
            ? null
            : Compile(pattern, flags.IgnoresCase, flags.IsMultiline, usesMatchExtent: true);

        if (pattern.Length > 0 && compiled is null)
        {
            return;
        }

        Add(new SedSubstitute(
            range,
            compiled,
            ParseReplacement(replacement, delimiter),
            flags.IsGlobal,
            flags.Occurrence,
            flags.Prints,
            flags.WriteFile));
    }

    private sealed record SubstituteFlags(
        bool IsGlobal,
        bool Prints,
        bool IgnoresCase,
        bool IsMultiline,
        int Occurrence,
        string? WriteFile);

    private SubstituteFlags ReadSubstituteFlags()
    {
        bool isGlobal = false;
        bool prints = false;
        bool ignoresCase = false;
        bool isMultiline = false;
        int occurrence = 0;
        string? writeFile = null;

        while (index < script.Length)
        {
            char flag = script[index];

            if (char.IsAsciiDigit(flag))
            {
                occurrence = ReadNumber() ?? 0;
                continue;
            }

            if (flag == 'e')
            {
                Unsupport("the `s///e' flag runs a shell command");
                return new SubstituteFlags(isGlobal, prints, ignoresCase, isMultiline, occurrence, writeFile);
            }

            if (flag == 'w')
            {
                index++;
                writeFile = ReadFileName();
                break;
            }

            if (flag is not ('g' or 'p' or 'i' or 'I' or 'm' or 'M'))
            {
                break;
            }

            isGlobal |= flag == 'g';
            prints |= flag == 'p';
            ignoresCase |= flag is 'i' or 'I';
            isMultiline |= flag is 'm' or 'M';
            index++;
        }

        return new SubstituteFlags(isGlobal, prints, ignoresCase, isMultiline, occurrence, writeFile);
    }

    private void ParseTransliterate(SedRange? range)
    {
        if (index >= script.Length || script[index] is '\n' or '\\')
        {
            Fail("unterminated `y' command");
            return;
        }

        char delimiter = script[index];
        index++;

        string? from = ReadRaw(delimiter);
        string? to = from is null ? null : ReadRaw(delimiter);

        if (from is null || to is null)
        {
            Fail("unterminated `y' command");
            return;
        }

        string source = UnescapeTransliteration(from, delimiter);
        string target = UnescapeTransliteration(to, delimiter);

        if (source.Length != target.Length)
        {
            Fail("strings for `y' command are different lengths");
            return;
        }

        Add(new SedTransliterate(range, source, target));
    }

    private void ParseBranch(SedRange? range, SedBranchCondition condition)
    {
        string label = ReadLabel();
        branchTargets.Add(label);
        Add(new SedBranch(range, label, condition));
    }

    private void ParseLabelDefinition(SedRange? range)
    {
        if (range is not null)
        {
            Fail(": doesn't want any addresses");
            return;
        }

        string name = ReadLabel();

        if (name.Length == 0)
        {
            Fail("\": \" lacks a label");
            return;
        }

        if (!labels.TryAdd(name, commands.Count))
        {
            Fail($"duplicate label `{name}'");
            return;
        }

        Add(new SedLabel(name));
    }

    private void ParseTextCommand(SedRange? range, SedTextPlacement placement)
    {
        Add(new SedText(range, placement, ReadText()));
    }

    private void ParseReadFile(SedRange? range, bool oneLineOnly)
    {
        string path = ReadFileName();

        if (path.Length == 0)
        {
            Fail("missing filename in r/R/w/W commands");
            return;
        }

        Add(new SedReadFile(range, path, oneLineOnly));
    }

    private void ParseWriteFile(SedRange? range, bool firstLineOnly)
    {
        string path = ReadFileName();

        if (path.Length == 0)
        {
            Fail("missing filename in r/R/w/W commands");
            return;
        }

        Add(new SedWriteFile(range, path, firstLineOnly));
    }

    // A width of zero means "whatever -l said", resolved when the script runs.
    private void ParseList(SedRange? range)
    {
        SkipBlanks();
        Add(new SedList(range, ReadNumber() ?? 0));
    }

    private void ParseQuit(SedRange? range, bool silent)
    {
        if (range is { IsRange: true })
        {
            Fail("command only uses one address");
            return;
        }

        SkipBlanks();
        Add(new SedQuit(range, ReadNumber() ?? 0, silent));
    }

    private void ParseVersion(SedRange? range)
    {
        SkipBlanks();
        int start = index;

        while (index < script.Length && (char.IsAsciiDigit(script[index]) || script[index] == '.'))
        {
            index++;
        }

        string requested = script[start..index];

        if (requested.Length > 0 && (!decimal.TryParse(requested, out decimal version) || version > SupportedVersion))
        {
            Unsupport($"`v {requested}' requires a newer sed");
            return;
        }

        Add(new SedNoOperation(range));
    }

    private void OpenBlock(SedRange? range)
    {
        openBlocks.Add(commands.Count);
        Add(new SedBlockStart(range, 0));
    }

    private void CloseBlock()
    {
        index++;

        if (openBlocks.Count == 0)
        {
            Fail("unexpected `}'");
            return;
        }

        int start = openBlocks[^1];
        openBlocks.RemoveAt(openBlocks.Count - 1);
        commands[start] = ((SedBlockStart)commands[start]) with { EndIndex = commands.Count };
    }

    private void VerifyBranchTargets()
    {
        foreach (string target in branchTargets)
        {
            if (target.Length > 0 && !labels.ContainsKey(target))
            {
                Fail($"can't find label for jump to `{target}'");
                return;
            }
        }
    }

    // An address only asks whether the pattern matches; `s` replaces what it matched, so only `s`
    // needs the translator to vouch for where the match ends.
    private Regex? Compile(string pattern, bool ignoresCase, bool isMultiline, bool usesMatchExtent = false)
    {
        RegexTranslation translation = PosixRegexTranslator.Translate(
            pattern,
            options.IsExtendedRegex ? RegexDialect.ExtendedPosix : RegexDialect.BasicPosix,
            options.AllowsGnuExtensions,
            isMultiline,
            usesMatchExtent);

        if (!translation.IsTranslated)
        {
            Unsupport($"regular expression `{pattern}': {translation.RefusalReason}");
            return null;
        }

        RegexOptions regexOptions = RegexOptions.CultureInvariant;

        if (ignoresCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        if (isMultiline)
        {
            regexOptions |= RegexOptions.Multiline;
        }

        return new Regex(translation.Pattern!, regexOptions, MatchTimeout);
    }

    // Reads a regular expression up to its closing delimiter. Bracket expressions are tracked
    // because the delimiter is an ordinary character inside them: `s/[/]/x/` is one valid command.
    private string? ReadPattern(char delimiter)
    {
        StringBuilder pattern = new();
        bool isInBracket = false;
        int bracketMembers = 0;

        while (index < script.Length)
        {
            char current = script[index];

            if (current == '\\' && index + 1 < script.Length)
            {
                AppendEscapedPatternCharacter(pattern, script[index + 1], delimiter, isInBracket);
                index += 2;
                continue;
            }

            if (isInBracket)
            {
                if (current == '[' && index + 1 < script.Length && script[index + 1] is ':' or '.' or '=')
                {
                    AppendBracketConstruct(pattern);
                    bracketMembers++;
                    continue;
                }

                if (current == ']' && bracketMembers > 0)
                {
                    isInBracket = false;
                }
                else if (current != '^' || bracketMembers > 0)
                {
                    bracketMembers++;
                }

                pattern.Append(current);
                index++;
                continue;
            }

            if (current == delimiter)
            {
                index++;
                return pattern.ToString();
            }

            if (current == '[')
            {
                isInBracket = true;
                bracketMembers = 0;
            }

            pattern.Append(current);
            index++;
        }

        return null;
    }

    // A backslash before the delimiter removes the delimiter's special meaning. Whether the
    // backslash survives depends on what the delimiter is: POSIX reads `\.` as a literal dot, so the
    // escape must stay for punctuation, while an alphanumeric delimiter would become an undefined
    // escape and must lose it.
    private static void AppendEscapedPatternCharacter(
        StringBuilder pattern,
        char escaped,
        char delimiter,
        bool isInBracket)
    {
        if (escaped == delimiter && !isInBracket)
        {
            pattern.Append(char.IsAsciiLetterOrDigit(delimiter) ? string.Empty : "\\").Append(delimiter);
            return;
        }

        pattern.Append('\\').Append(escaped);
    }

    private void AppendBracketConstruct(StringBuilder pattern)
    {
        char kind = script[index + 1];
        string closing = $"{kind}]";
        int close = script.IndexOf(closing, index + 2, StringComparison.Ordinal);

        if (close < 0)
        {
            pattern.Append(script[index]);
            index++;
            return;
        }

        pattern.Append(script[index..(close + 2)]);
        index = close + 2;
    }

    private string? ReadRaw(char delimiter)
    {
        StringBuilder text = new();

        while (index < script.Length)
        {
            char current = script[index];

            if (current == '\\' && index + 1 < script.Length)
            {
                text.Append(current).Append(script[index + 1]);
                index += 2;
                continue;
            }

            if (current == delimiter)
            {
                index++;
                return text.ToString();
            }

            text.Append(current);
            index++;
        }

        return null;
    }

    private static IReadOnlyList<SedReplacementPart> ParseReplacement(string replacement, char delimiter)
    {
        List<SedReplacementPart> parts = [];
        StringBuilder literal = new();

        void FlushLiteral()
        {
            if (literal.Length == 0)
            {
                return;
            }

            parts.Add(new SedReplacementPart(SedReplacementKind.Literal, literal.ToString()));
            literal.Clear();
        }

        for (int position = 0; position < replacement.Length; position++)
        {
            char current = replacement[position];

            if (current == '&')
            {
                FlushLiteral();
                parts.Add(new SedReplacementPart(SedReplacementKind.WholeMatch));
                continue;
            }

            if (current != '\\' || position + 1 >= replacement.Length)
            {
                literal.Append(current);
                continue;
            }

            char escaped = replacement[++position];
            SedReplacementKind? kind = escaped switch
            {
                >= '0' and <= '9' => SedReplacementKind.Group,
                'U' => SedReplacementKind.UpperCaseRest,
                'L' => SedReplacementKind.LowerCaseRest,
                'u' => SedReplacementKind.UpperCaseNext,
                'l' => SedReplacementKind.LowerCaseNext,
                'E' => SedReplacementKind.EndCaseConversion,
                _ => null,
            };

            if (kind is null)
            {
                literal.Append(UnescapeCharacter(escaped, delimiter));
                continue;
            }

            FlushLiteral();
            parts.Add(kind == SedReplacementKind.Group
                ? new SedReplacementPart(SedReplacementKind.Group, Group: escaped - '0')
                : new SedReplacementPart(kind.Value));
        }

        FlushLiteral();
        return parts;
    }

    private static char UnescapeCharacter(char escaped, char delimiter) => escaped switch
    {
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        'f' => '\f',
        'v' => '\v',
        'a' => '\a',
        _ => escaped == delimiter ? delimiter : escaped,
    };

    private static string UnescapeTransliteration(string text, char delimiter)
    {
        StringBuilder unescaped = new();

        for (int position = 0; position < text.Length; position++)
        {
            if (text[position] != '\\' || position + 1 >= text.Length)
            {
                unescaped.Append(text[position]);
                continue;
            }

            unescaped.Append(UnescapeCharacter(text[++position], delimiter));
        }

        return unescaped.ToString();
    }

    // Both spellings: the POSIX `a\` with the text on following lines, and the GNU one-liner
    // `a text`. A backslash removes the special meaning of what follows it, and a backslash before a
    // newline continues the text onto the next line.
    private string ReadText()
    {
        SkipBlanks();

        if (index < script.Length && script[index] == '\\')
        {
            index++;

            if (index < script.Length && script[index] == '\n')
            {
                index++;
            }

            SkipBlanks();
        }

        StringBuilder text = new();

        while (index < script.Length)
        {
            char current = script[index];

            if (current == '\\' && index + 1 < script.Length)
            {
                text.Append(script[index + 1]);
                index += 2;
                continue;
            }

            index++;

            if (current == '\n')
            {
                break;
            }

            text.Append(current);
        }

        return text.ToString();
    }

    // A filename runs to the end of the line: a semicolon in `w out;txt` is part of the name, not a
    // command separator.
    private string ReadFileName()
    {
        SkipBlanks();
        int start = index;

        while (index < script.Length && script[index] != '\n')
        {
            index++;
        }

        return script[start..index].TrimEnd();
    }

    private string ReadLabel()
    {
        SkipBlanks();
        int start = index;

        while (index < script.Length && script[index] is not (';' or '\n' or '}' or ' ' or '\t'))
        {
            index++;
        }

        return script[start..index];
    }

    private int? ReadNumber()
    {
        int start = index;

        while (index < script.Length && char.IsAsciiDigit(script[index]))
        {
            index++;
        }

        return index == start ? null : int.Parse(script[start..index]);
    }

    private void SkipSeparators()
    {
        while (index < script.Length && script[index] is ' ' or '\t' or '\n' or '\r' or ';')
        {
            index++;
        }
    }

    private void SkipBlanks()
    {
        while (index < script.Length && script[index] is ' ' or '\t')
        {
            index++;
        }
    }

    private void SkipComment()
    {
        while (index < script.Length && script[index] != '\n')
        {
            index++;
        }
    }

    private void Add(SedCommand command) => commands.Add(command);

    private void Fail(string reason) => error ??= reason;

    private void Unsupport(string construct) => unsupported ??= construct;
}
