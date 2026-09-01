using System.Text;
using Sharp.Shell.Text;

namespace Sharp.Shell.Commands.Awk;

// The string builtins, as pure functions over what they are given. Every rule below was checked
// against `/usr/bin/awk` and `gawk --posix`, which agree on all of them.
internal static class AwkStringBuiltins
{
    // Positions and lengths are truncated towards zero, not rounded: `substr("hello", 1.5, 2.5)` is
    // "he", not "el". A start before the string is pulled up to 1 *without* shortening the count, so
    // `substr("hello", -1, 3)` is "hel".
    public static string Substring(string subject, double from, double? count)
    {
        int start = (int)Math.Truncate(from);
        start = Math.Max(start, 1);

        if (start > subject.Length)
        {
            return string.Empty;
        }

        int available = subject.Length - start + 1;
        int wanted = count is null ? available : (int)Math.Truncate(count.Value);

        return subject.Substring(start - 1, Math.Clamp(wanted, 0, available));
    }

    public static int IndexOf(string subject, string sought) =>
        subject.IndexOf(sought, StringComparison.Ordinal) + 1;

    // An explicit empty separator splits into characters, which is not what an empty FS does to a
    // record — both oracles draw that distinction and so does this.
    public static int Split(
        string subject,
        AwkArray target,
        string separator,
        bool isDefaultSeparator,
        AwkRegexCache regexes)
    {
        target.Clear();

        if (subject.Length == 0)
        {
            return 0;
        }

        IReadOnlyList<string> parts = separator.Length == 0 && !isDefaultSeparator
            ? [.. subject.Select(character => character.ToString())]
            : FieldSplitter.Split(subject, separator, isParagraphMode: false, regexes);

        for (int index = 0; index < parts.Count; index++)
        {
            target.Write((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), AwkValue.FromInput(parts[index]));
        }

        return parts.Count;
    }

    // gsub's awkward case: an empty match sitting exactly where the previous match ended is not a
    // second substitution, which is why `gsub(/a*/, "-")` turns "ab" into "-b-" and not "--b-".
    public static (string Text, int Count) Substitute(
        string subject,
        AwkRegex pattern,
        string replacement,
        bool isGlobal)
    {
        StringBuilder result = new();
        int position = 0;
        int previousEnd = -1;
        int count = 0;

        while (position <= subject.Length)
        {
            if (pattern.Find(subject, position) is not (int start, int length))
            {
                break;
            }

            result.Append(subject, position, start - position);

            if (length == 0 && start == previousEnd)
            {
                if (start >= subject.Length)
                {
                    position = start;
                    break;
                }

                result.Append(subject[start]);
                position = start + 1;
                continue;
            }

            count++;
            result.Append(Expanded(replacement, subject.Substring(start, length)));
            previousEnd = start + length;
            position = start + length;

            if (!isGlobal)
            {
                break;
            }

            if (length != 0)
            {
                continue;
            }

            if (start >= subject.Length)
            {
                break;
            }

            result.Append(subject[start]);
            position = start + 1;
        }

        result.Append(subject, position, subject.Length - position);
        return (result.ToString(), count);
    }

    // `&` is the matched text, `\&` is a literal ampersand, `\\` is a literal backslash.
    private static string Expanded(string replacement, string matched)
    {
        StringBuilder expanded = new();

        for (int cursor = 0; cursor < replacement.Length; cursor++)
        {
            if (replacement[cursor] == '&')
            {
                expanded.Append(matched);
                continue;
            }

            if (replacement[cursor] != '\\' || cursor + 1 >= replacement.Length)
            {
                expanded.Append(replacement[cursor]);
                continue;
            }

            char escaped = replacement[cursor + 1];

            if (escaped is '&' or '\\')
            {
                expanded.Append(escaped);
                cursor++;
                continue;
            }

            expanded.Append('\\');
        }

        return expanded.ToString();
    }
}
