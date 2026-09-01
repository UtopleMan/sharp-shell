using System.Text;
using Sharp.Shell.Text;

namespace Sharp.Shell.Commands.Awk;

// The four shapes of FS, which are four different rules rather than four regular expressions.
//
// The default `" "` splits on runs of blanks and drops the leading and trailing ones, so `"  a  b  "`
// is two fields and not four. A single character splits on exactly that character, taken literally —
// `awk -F.` splits `a.b` into `a` and `b`, it does not match every character. Anything longer is an
// extended regular expression. An empty FS leaves the record as one field, which is what both
// reference implementations do.
//
// In paragraph mode a newline separates fields whatever FS says, because a paragraph record contains
// the newlines that joined its lines.
internal static class FieldSplitter
{
    public static IReadOnlyList<string> Split(
        string record,
        string fieldSeparator,
        bool isParagraphMode,
        AwkRegexCache regexes)
    {
        if (record.Length == 0)
        {
            return [];
        }

        if (fieldSeparator == " ")
        {
            return OnBlanks(record);
        }

        if (fieldSeparator.Length == 0)
        {
            return [record];
        }

        if (fieldSeparator.Length == 1 && !isParagraphMode)
        {
            return OnCharacter(record, fieldSeparator[0]);
        }

        return OnPattern(record, PatternFor(fieldSeparator, isParagraphMode), regexes);
    }

    private static string PatternFor(string fieldSeparator, bool isParagraphMode)
    {
        if (!isParagraphMode)
        {
            return fieldSeparator;
        }

        string body = fieldSeparator.Length == 1 ? Quoted(fieldSeparator[0]) : fieldSeparator;
        return $"({body})|\n";
    }

    private static string Quoted(char character) =>
        "\\^$.[]|()*+?{}".Contains(character, StringComparison.Ordinal)
            ? $"\\{character}"
            : character.ToString();

    private static List<string> OnBlanks(string record)
    {
        List<string> fields = [];
        int cursor = 0;

        while (cursor < record.Length)
        {
            while (cursor < record.Length && IsBlank(record[cursor]))
            {
                cursor++;
            }

            int start = cursor;

            while (cursor < record.Length && !IsBlank(record[cursor]))
            {
                cursor++;
            }

            if (cursor > start)
            {
                fields.Add(record[start..cursor]);
            }
        }

        return fields;
    }

    private static bool IsBlank(char character) => character is ' ' or '\t' or '\n';

    private static List<string> OnCharacter(string record, char separator)
    {
        List<string> fields = [];
        int start = 0;

        for (int cursor = 0; cursor < record.Length; cursor++)
        {
            if (record[cursor] != separator)
            {
                continue;
            }

            fields.Add(record[start..cursor]);
            start = cursor + 1;
        }

        fields.Add(record[start..]);
        return fields;
    }

    // A separator that matches nothing at this position must still advance, or an FS that can match
    // empty would loop for ever.
    private static List<string> OnPattern(string record, string pattern, AwkRegexCache regexes)
    {
        AwkRegex separator = regexes.Compile(pattern);
        List<string> fields = [];
        int start = 0;
        int cursor = 0;

        while (cursor <= record.Length)
        {
            if (separator.Find(record, cursor) is not (int found, int length) || found >= record.Length)
            {
                break;
            }

            if (length == 0)
            {
                cursor = found + 1;
                continue;
            }

            fields.Add(record[start..found]);
            start = found + length;
            cursor = start;
        }

        fields.Add(record[start..]);
        return fields;
    }

    public static string Join(IReadOnlyList<string> fields, string outputSeparator)
    {
        StringBuilder joined = new();

        for (int index = 0; index < fields.Count; index++)
        {
            if (index > 0)
            {
                joined.Append(outputSeparator);
            }

            joined.Append(fields[index]);
        }

        return joined.ToString();
    }
}
