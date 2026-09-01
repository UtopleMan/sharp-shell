using System.Text;
using System.Text.RegularExpressions;

namespace Sharp.Shell.Commands.Sed;

internal enum CycleResult
{
    Completed,
    Deleted,
    Restart,
}

internal enum SedCaseMode
{
    None,
    Upper,
    Lower,
}

// sed's cycle: read a record, run the script over it, print the pattern space unless -n, flush the
// append queue, repeat. Everything that makes sed a language rather than a filter lives here — the
// hold space, the two-line lookahead of N/D, and branching, which can jump anywhere in the script
// including backwards.
//
// Output is buffered per cycle, not per run, so a consumer that stops reading still stops the input
// from being read. A printed pattern space carries a separator only when the record it came from
// had one, which is what makes `printf 'a\nb\nc' | sed -n '$p'` end without a newline while
// `sed -n '1p'` on the same input still ends with one. Everything sed generates itself — `=`, `i`,
// `a`, `l`, `F` — always ends with a separator.
internal sealed class SedMachine(
    SedProgram program,
    SedRunOptions options,
    ISedFileSystem files,
    Action<string> writeError,
    CancellationToken cancellationToken)
{
    private readonly bool autoPrints = options.AutoPrints && !program.SuppressesAutoPrint;

    private readonly bool[] rangeActive = new bool[program.Commands.Count];

    private readonly int[] rangeEnd = new int[program.Commands.Count];

    private readonly bool[] rangeClosed = new bool[program.Commands.Count];

    private readonly bool[] rangeExhausted = new bool[program.Commands.Count];

    private readonly List<(string Text, bool IsRaw)> appendQueue = [];

    private readonly string separator = options.Separator.ToString();

    private string patternSpace = string.Empty;

    private string holdSpace = string.Empty;

    private int lineNumber;

    private bool hasSubstituted;

    private bool isLastRecord;

    private bool hasTrailingSeparator = true;

    private Regex? lastRegex;

    public int ExitCode { get; private set; }

    public bool HasQuit { get; private set; }

    public IEnumerable<string> Run(IEnumerable<string> input)
    {
        SedRecordReader reader = new(input, options.Separator);
        List<string> output = [];

        while (!HasQuit && !cancellationToken.IsCancellationRequested && reader.TryRead(out SedRecord record))
        {
            LoadRecord(record);
            output.Clear();
            RunCycle(reader, output);

            foreach (string chunk in output)
            {
                yield return chunk;
            }
        }
    }

    private void RunCycle(SedRecordReader reader, List<string> output)
    {
        while (true)
        {
            CycleResult result = ExecuteInstructions(reader, output);

            if (result == CycleResult.Restart)
            {
                continue;
            }

            if (result == CycleResult.Completed && autoPrints)
            {
                Write(output, patternSpace);
            }

            FlushAppends(output);
            return;
        }
    }

    private CycleResult ExecuteInstructions(SedRecordReader reader, List<string> output)
    {
        int index = 0;

        while (index < program.Commands.Count)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                HasQuit = true;
                return CycleResult.Deleted;
            }

            SedCommand command = program.Commands[index];

            if (command is SedBlockStart block)
            {
                index = Matches(block, index) ? index + 1 : block.EndIndex;
                continue;
            }

            if (command is SedLabel or SedNoOperation || !Matches(command, index))
            {
                index++;
                continue;
            }

            if (command is SedBranch branch)
            {
                index = ShouldBranch(branch) ? TargetOf(branch) : index + 1;
                continue;
            }

            CycleResult? result = Execute(command, index, reader, output);

            if (result is not null)
            {
                return result.Value;
            }

            index++;
        }

        return CycleResult.Completed;
    }

    private CycleResult? Execute(SedCommand command, int index, SedRecordReader reader, List<string> output) =>
        command switch
        {
            SedPrint print => PrintCommand(print, output),
            SedDelete delete => DeleteCommand(delete),
            SedSubstitute substitute => SubstituteCommand(substitute, output),
            SedTransliterate transliterate => TransliterateCommand(transliterate),
            SedHold hold => HoldCommand(hold),
            SedLineNumber => WriteLineAndContinue(output, lineNumber.ToString()),
            SedText text => TextCommand(text, index, output),
            SedNextLine next => NextLineCommand(next, reader, output),
            SedReadFile read => ReadFileCommand(read),
            SedWriteFile write => WriteFileCommand(write, output),
            SedList list => WriteLineAndContinue(output, Render(patternSpace, list.Width == 0 ? options.ListWidth : list.Width)),
            SedQuit quit => QuitCommand(quit),
            SedZap => Zap(),
            SedFileName => WriteLineAndContinue(output, options.FileName),
            _ => null,
        };

    private CycleResult? PrintCommand(SedPrint print, List<string> output)
    {
        // With no embedded newline there is no "first line" to speak of, and P is exactly p —
        // including inheriting the record's missing final newline.
        if (print.FirstLineOnly && patternSpace.Contains(options.Separator))
        {
            WriteLine(output, FirstLine(patternSpace));
            return null;
        }

        Write(output, patternSpace);
        return null;
    }

    private CycleResult DeleteCommand(SedDelete delete)
    {
        if (!delete.FirstLineOnly)
        {
            patternSpace = string.Empty;
            return CycleResult.Deleted;
        }

        int position = patternSpace.IndexOf(options.Separator);

        if (position < 0)
        {
            patternSpace = string.Empty;
            return CycleResult.Deleted;
        }

        patternSpace = patternSpace[(position + 1)..];
        return CycleResult.Restart;
    }

    private CycleResult? SubstituteCommand(SedSubstitute substitute, List<string> output)
    {
        Regex? pattern = substitute.Pattern ?? lastRegex;

        if (pattern is null)
        {
            return null;
        }

        lastRegex = pattern;
        (string replaced, bool matched) = Replace(pattern, substitute);

        if (!matched)
        {
            return null;
        }

        patternSpace = replaced;
        hasSubstituted = true;

        if (substitute.Prints)
        {
            Write(output, patternSpace);
        }

        if (substitute.WriteFile is not null)
        {
            WriteToFile(substitute.WriteFile, patternSpace, output);
        }

        return null;
    }

    private (string Text, bool Matched) Replace(Regex pattern, SedSubstitute substitute)
    {
        int firstReplaced = substitute.Occurrence > 0 ? substitute.Occurrence : 1;
        StringBuilder built = new();
        int copied = 0;
        int count = 0;
        int previousMatchEnd = -1;
        bool matched = false;
        Match match = Match(pattern, patternSpace, 0);

        while (match.Success)
        {
            // `s/a*/X/g` on "a" is one replacement, not two: the empty match sitting where the
            // previous match ended is not a separate match.
            if (match.Length == 0 && match.Index == previousMatchEnd)
            {
                if (match.Index + 1 > patternSpace.Length)
                {
                    break;
                }

                match = Match(pattern, patternSpace, match.Index + 1);
                continue;
            }

            count++;
            previousMatchEnd = match.Index + match.Length;

            if (count >= firstReplaced && (substitute.IsGlobal || count == firstReplaced))
            {
                built.Append(patternSpace, copied, match.Index - copied);
                built.Append(BuildReplacement(substitute.Replacement, match));
                copied = match.Index + match.Length;
                matched = true;

                if (!substitute.IsGlobal)
                {
                    break;
                }
            }

            int next = match.Index + Math.Max(match.Length, 1);

            if (next > patternSpace.Length)
            {
                break;
            }

            match = Match(pattern, patternSpace, next);
        }

        if (!matched)
        {
            return (patternSpace, false);
        }

        built.Append(patternSpace, copied, patternSpace.Length - copied);
        return (built.ToString(), true);
    }

    private static string BuildReplacement(IReadOnlyList<SedReplacementPart> parts, Match match)
    {
        StringBuilder built = new();
        SedCaseMode rest = SedCaseMode.None;
        SedCaseMode next = SedCaseMode.None;

        void Append(string text)
        {
            foreach (char character in text)
            {
                built.Append(Convert(character, next == SedCaseMode.None ? rest : next));
                next = SedCaseMode.None;
            }
        }

        foreach (SedReplacementPart part in parts)
        {
            switch (part.Kind)
            {
                case SedReplacementKind.Literal: Append(part.Text); break;
                case SedReplacementKind.WholeMatch: Append(match.Value); break;
                case SedReplacementKind.Group: Append(GroupValue(match, part.Group)); break;
                case SedReplacementKind.UpperCaseRest: rest = SedCaseMode.Upper; break;
                case SedReplacementKind.LowerCaseRest: rest = SedCaseMode.Lower; break;
                case SedReplacementKind.UpperCaseNext: next = SedCaseMode.Upper; break;
                case SedReplacementKind.LowerCaseNext: next = SedCaseMode.Lower; break;
                case SedReplacementKind.EndCaseConversion:
                    rest = SedCaseMode.None;
                    next = SedCaseMode.None;
                    break;
            }
        }

        return built.ToString();
    }

    private static string GroupValue(Match match, int group) =>
        group == 0 ? match.Value : group < match.Groups.Count ? match.Groups[group].Value : string.Empty;

    private static char Convert(char character, SedCaseMode mode) => mode switch
    {
        SedCaseMode.Upper => char.ToUpperInvariant(character),
        SedCaseMode.Lower => char.ToLowerInvariant(character),
        _ => character,
    };

    private CycleResult? TransliterateCommand(SedTransliterate transliterate)
    {
        StringBuilder built = new(patternSpace.Length);

        foreach (char character in patternSpace)
        {
            int position = transliterate.From.IndexOf(character, StringComparison.Ordinal);
            built.Append(position < 0 ? character : transliterate.To[position]);
        }

        patternSpace = built.ToString();
        return null;
    }

    private CycleResult? HoldCommand(SedHold hold)
    {
        switch (hold.Action)
        {
            case SedHoldAction.CopyToHold: holdSpace = patternSpace; break;
            case SedHoldAction.AppendToHold: holdSpace = $"{holdSpace}{separator}{patternSpace}"; break;
            case SedHoldAction.CopyFromHold: patternSpace = holdSpace; break;
            case SedHoldAction.AppendFromHold: patternSpace = $"{patternSpace}{separator}{holdSpace}"; break;
            case SedHoldAction.Exchange: (patternSpace, holdSpace) = (holdSpace, patternSpace); break;
        }

        return null;
    }

    private CycleResult? TextCommand(SedText text, int index, List<string> output)
    {
        if (text.Placement == SedTextPlacement.Append)
        {
            appendQueue.Add((text.Text, false));
            return null;
        }

        if (text.Placement == SedTextPlacement.Insert)
        {
            WriteLine(output, text.Text);
            return null;
        }

        // `c` on a range prints once, when the range closes; on a single address it prints per line.
        // End of input closes a range that never reached its second address, so `1,9c\text` over
        // three lines still prints the text.
        if (text.Range is not { IsRange: true } || rangeClosed[index] || isLastRecord)
        {
            WriteLine(output, text.Text);
        }

        patternSpace = string.Empty;
        return CycleResult.Deleted;
    }

    private CycleResult? NextLineCommand(SedNextLine next, SedRecordReader reader, List<string> output)
    {
        if (!next.Appends && autoPrints)
        {
            Write(output, patternSpace);
        }

        if (reader.TryRead(out SedRecord record))
        {
            AppendRecord(record, next.Appends);
            return null;
        }

        HasQuit = true;

        // GNU prints the pattern space when N runs out of input; POSIX discards it.
        return next.Appends && options.PrintsPatternSpaceOnMissingNextLine
            ? CycleResult.Completed
            : CycleResult.Deleted;
    }

    private CycleResult? ReadFileCommand(SedReadFile read)
    {
        if (!read.OneLineOnly)
        {
            string content = files.ReadAll(read.Path);

            if (content.Length > 0)
            {
                appendQueue.Add((content, true));
            }

            return null;
        }

        string? line = files.ReadLine(read.Path);

        if (line is not null)
        {
            appendQueue.Add((line, false));
        }

        return null;
    }

    private CycleResult? WriteFileCommand(SedWriteFile write, List<string> output)
    {
        WriteToFile(write.Path, write.FirstLineOnly ? FirstLine(patternSpace) : patternSpace, output);
        return null;
    }

    private void WriteToFile(string path, string text, List<string> output)
    {
        if (files.IsStandardOutput(path))
        {
            Write(output, text);
            return;
        }

        if (files.IsStandardError(path))
        {
            writeError($"{text}{separator}");
            return;
        }

        files.Append(path, $"{text}{separator}");
    }

    private CycleResult QuitCommand(SedQuit quit)
    {
        ExitCode = quit.ExitCode;
        HasQuit = true;

        if (!quit.Silent)
        {
            return CycleResult.Completed;
        }

        appendQueue.Clear();
        return CycleResult.Deleted;
    }

    private CycleResult? Zap()
    {
        patternSpace = string.Empty;
        return null;
    }

    private bool ShouldBranch(SedBranch branch) => branch.Condition switch
    {
        SedBranchCondition.Always => true,
        SedBranchCondition.IfSubstituted => TakeSubstitutedFlag(),
        _ => !TakeSubstitutedFlag(),
    };

    private bool TakeSubstitutedFlag()
    {
        bool substituted = hasSubstituted;
        hasSubstituted = false;
        return substituted;
    }

    private int TargetOf(SedBranch branch) =>
        branch.Label.Length == 0 ? program.Commands.Count : program.Labels[branch.Label];

    private bool Matches(SedCommand command, int index)
    {
        rangeClosed[index] = false;

        if (command.Range is not SedRange range)
        {
            return true;
        }

        bool matched = range.IsRange ? MatchesRange(range, index) : MatchesAddress(range.Start);
        return range.IsNegated ? !matched : matched;
    }

    private bool MatchesRange(SedRange range, int index)
    {
        if (!rangeActive[index])
        {
            if (range.Start.Kind != SedAddressKind.Zero)
            {
                return StartRange(range, index);
            }

            // `0,/re/` begins before the first line, so its end can match on line one. Line zero
            // never comes round again, which is what stops the range from restarting.
            if (rangeExhausted[index])
            {
                return false;
            }

            rangeActive[index] = true;
            rangeEnd[index] = ComputeEndLine(range);
        }

        if (IsAtRangeEnd(range, index))
        {
            rangeActive[index] = false;
            rangeClosed[index] = true;
            rangeExhausted[index] = true;
        }

        return true;
    }

    private bool StartRange(SedRange range, int index)
    {
        if (!MatchesAddress(range.Start))
        {
            return false;
        }

        rangeActive[index] = true;
        rangeEnd[index] = ComputeEndLine(range);

        // An end that is not in the future makes the range a single line, which is how sed reads
        // `4,2p` and `$,/x/p`.
        if (ClosesOnTheStartingLine(range, index))
        {
            rangeActive[index] = false;
            rangeClosed[index] = true;
        }

        return true;
    }

    private bool ClosesOnTheStartingLine(SedRange range, int index) => range.EndKind switch
    {
        SedRangeEndKind.Address => range.End!.Kind switch
        {
            SedAddressKind.Line => range.End.Line <= lineNumber,
            SedAddressKind.Last => isLastRecord,
            _ => false,
        },
        _ => rangeEnd[index] <= lineNumber,
    };

    private int ComputeEndLine(SedRange range)
    {
        if (range.EndKind == SedRangeEndKind.RelativeLines)
        {
            return lineNumber + range.EndValue;
        }

        if (range.EndKind != SedRangeEndKind.NextMultiple || range.EndValue <= 0)
        {
            return range.EndKind == SedRangeEndKind.Address && range.End?.Kind == SedAddressKind.Line
                ? range.End.Line
                : 0;
        }

        int remainder = lineNumber % range.EndValue;
        return remainder == 0 ? lineNumber : lineNumber + range.EndValue - remainder;
    }

    private bool IsAtRangeEnd(SedRange range, int index) => range.EndKind switch
    {
        SedRangeEndKind.Address => range.End!.Kind switch
        {
            SedAddressKind.Line => lineNumber >= range.End.Line,
            SedAddressKind.Last => isLastRecord,
            SedAddressKind.Regex => MatchesRegex(range.End.Pattern),
            _ => MatchesAddress(range.End),
        },
        _ => lineNumber >= rangeEnd[index],
    };

    private bool MatchesAddress(SedAddress address) => address.Kind switch
    {
        SedAddressKind.Line => lineNumber == address.Line,
        SedAddressKind.Last => isLastRecord,
        SedAddressKind.Regex => MatchesRegex(address.Pattern),
        SedAddressKind.Step => MatchesStep(address),
        _ => false,
    };

    private bool MatchesStep(SedAddress address) =>
        address.Step <= 0
            ? lineNumber == address.Line
            : lineNumber >= address.Line && (lineNumber - address.Line) % address.Step == 0;

    private bool MatchesRegex(Regex? pattern)
    {
        Regex? resolved = pattern ?? lastRegex;

        if (resolved is null)
        {
            return false;
        }

        lastRegex = resolved;
        return IsMatch(resolved, patternSpace);
    }

    private void LoadRecord(SedRecord record)
    {
        patternSpace = record.Text;
        isLastRecord = record.IsLast;
        hasTrailingSeparator = record.HasTrailingSeparator;
        lineNumber++;
        hasSubstituted = false;
    }

    private void AppendRecord(SedRecord record, bool appends)
    {
        patternSpace = appends ? $"{patternSpace}{separator}{record.Text}" : record.Text;
        isLastRecord = record.IsLast;
        hasTrailingSeparator = record.HasTrailingSeparator;
        lineNumber++;
        hasSubstituted = false;
    }

    private void FlushAppends(List<string> output)
    {
        foreach ((string text, bool isRaw) in appendQueue)
        {
            if (isRaw)
            {
                WriteVerbatim(output, text);
                continue;
            }

            WriteLine(output, text);
        }

        appendQueue.Clear();
    }

    private CycleResult? WriteLineAndContinue(List<string> output, string text)
    {
        WriteLine(output, text);
        return null;
    }

    // The pattern space keeps its record's newline, or its absence.
    private void Write(List<string> output, string text)
    {
        output.Add(text);

        if (hasTrailingSeparator)
        {
            output.Add(separator);
        }
    }

    private void WriteLine(List<string> output, string text)
    {
        output.Add(text);
        output.Add(separator);
    }

    private static void WriteVerbatim(List<string> output, string text) => output.Add(text);

    private string FirstLine(string text)
    {
        int position = text.IndexOf(options.Separator);
        return position < 0 ? text : text[..position];
    }

    private Match Match(Regex pattern, string input, int start)
    {
        try
        {
            return pattern.Match(input, start);
        }
        catch (RegexMatchTimeoutException)
        {
            ReportRunawayPattern();
            return System.Text.RegularExpressions.Match.Empty;
        }
    }

    private bool IsMatch(Regex pattern, string input)
    {
        try
        {
            return pattern.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            ReportRunawayPattern();
            return false;
        }
    }

    private void ReportRunawayPattern()
    {
        writeError("sed: regular expression took too long to match\n");
        ExitCode = 2;
        HasQuit = true;
    }

    // `l` renders unambiguously: escapes for the characters that have them, three-digit octal for
    // everything else unprintable, a `$` marking the end, and a trailing backslash where a long line
    // is folded.
    private static string Render(string text, int width)
    {
        StringBuilder rendered = new();
        int column = 0;

        foreach (char character in text)
        {
            string piece = RenderCharacter(character);

            if (width > 1 && column + piece.Length > width - 1)
            {
                rendered.Append("\\\n");
                column = 0;
            }

            rendered.Append(piece);
            column += piece.Length;
        }

        return rendered.Append('$').ToString();
    }

    private static string RenderCharacter(char character) => character switch
    {
        '\\' => @"\\",
        '\a' => @"\a",
        '\b' => @"\b",
        '\f' => @"\f",
        '\n' => @"\n",
        '\r' => @"\r",
        '\t' => @"\t",
        '\v' => @"\v",
        _ => character is >= ' ' and < (char)0x7f ? character.ToString() : $@"\{System.Convert.ToString(character, 8).PadLeft(3, '0')}",
    };
}
