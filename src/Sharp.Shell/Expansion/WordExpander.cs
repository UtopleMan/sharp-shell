using System.Text;
using Sharp.Shell.Execution;
using Sharp.Shell.Lexing;

namespace Sharp.Shell.Expansion;

// Turns one parsed word into the fields a command actually receives. Quoting decides everything:
// only the output of an *unquoted* expansion is field-split, and only unquoted characters are
// glob-special. That is why the lexer kept the quoting structure instead of resolving it.
public sealed class WordExpander(ShellState state, Func<string, CommandSubstitution> runSubstitution)
{
    private const string FieldSeparators = " \t\n";

    // Command words and arguments: expanded, split, then globbed.
    public ExpansionResult Expand(Word word) => Build(word, splitAndGlob: true);

    // Assignment values and redirection targets: expanded, but never split into several fields.
    public ExpansionResult ExpandValue(Word word) => Build(word, splitAndGlob: false);

    private ExpansionResult Build(Word word, bool splitAndGlob)
    {
        List<Fragment> fragments = [];

        foreach (WordPart part in word.Parts)
        {
            ExpansionResult? failure = AddFragments(part, quoted: false, fragments);
            if (failure is not null)
            {
                return failure;
            }
        }

        List<Field> fields = Split(fragments, splitAndGlob);

        return ExpansionResult.Ok(splitAndGlob ? Globbed(fields) : [.. fields.Select(field => field.Text)]);
    }

    private ExpansionResult? AddFragments(WordPart part, bool quoted, List<Fragment> fragments)
    {
        switch (part.Kind)
        {
            case WordPartKind.Literal:
                fragments.Add(new Fragment(part.Text, Splittable: false, Globbable: !quoted));
                return null;

            case WordPartKind.SingleQuoted:
                fragments.Add(new Fragment(part.Text, false, false));
                return null;

            case WordPartKind.DoubleQuoted:
                foreach (WordPart nested in part.Nested ?? [])
                {
                    ExpansionResult? failure = AddFragments(nested, quoted: true, fragments);
                    if (failure is not null)
                    {
                        return failure;
                    }
                }

                if (part.Nested is { Count: 0 })
                {
                    fragments.Add(new Fragment(string.Empty, false, false));
                }

                return null;

            case WordPartKind.Tilde:
                fragments.Add(new Fragment(state.RootPath + part.Text, false, false));
                return null;

            case WordPartKind.Arithmetic:
                return AddArithmetic(part, quoted, fragments);

            case WordPartKind.CommandSubstitution:
                CommandSubstitution substitution = runSubstitution(part.Text);
                fragments.Add(new Fragment(substitution.Output.TrimEnd('\n'), !quoted, !quoted));
                return null;

            default:
                return AddParameter(part, quoted, fragments);
        }
    }

    private ExpansionResult? AddArithmetic(WordPart part, bool quoted, List<Fragment> fragments)
    {
        if (!ArithmeticEvaluator.TryEvaluate(part.Text, state, out long value, out string error))
        {
            return ExpansionResult.Failed(error);
        }

        fragments.Add(new Fragment(value.ToString(), !quoted, !quoted));
        return null;
    }

    private ExpansionResult? AddParameter(WordPart part, bool quoted, List<Fragment> fragments)
    {
        ParameterResult resolved = ParameterExpander.Expand(part.Text, state, ExpandNestedText);

        if (resolved.UnsupportedReason is not null)
        {
            return ExpansionResult.Unsupported(resolved.UnsupportedReason);
        }

        if (resolved.ErrorMessage is not null)
        {
            return ExpansionResult.Failed(resolved.ErrorMessage);
        }

        fragments.Add(new Fragment(resolved.Value, !quoted, !quoted));
        return null;
    }

    // The word inside ${x:-word} is itself subject to expansion. Lexing it and expanding the
    // resulting words keeps one implementation rather than a second, weaker one.
    private string ExpandNestedText(string text)
    {
        LexResult lexed = Lexer.Tokenize(text);
        if (lexed.Error is not null)
        {
            return text;
        }

        List<string> pieces = [];
        foreach (Token token in lexed.Tokens.Where(token => token.Kind == TokenKind.Word))
        {
            ExpansionResult expanded = ExpandValue(token.Word!);
            pieces.AddRange(expanded.Fields);
        }

        return string.Join(' ', pieces);
    }

    private static List<Field> Split(List<Fragment> fragments, bool splitAndGlob)
    {
        List<Field> fields = [];
        Field current = new();

        foreach (Fragment fragment in fragments)
        {
            if (!splitAndGlob || !fragment.Splittable)
            {
                current.Append(fragment.Text, fragment.Globbable);
                continue;
            }

            AppendSplit(fragment, fields, ref current);
        }

        if (current.HasContent)
        {
            fields.Add(current);
        }

        return fields;
    }

    private static void AppendSplit(Fragment fragment, List<Field> fields, ref Field current)
    {
        foreach (char character in fragment.Text)
        {
            if (!FieldSeparators.Contains(character, StringComparison.Ordinal))
            {
                current.Append(character, fragment.Globbable);
                continue;
            }

            if (current.HasContent)
            {
                fields.Add(current);
                current = new Field();
            }
        }
    }

    private IReadOnlyList<string> Globbed(List<Field> fields)
    {
        List<string> expanded = [];

        foreach (Field field in fields)
        {
            if (!field.HasUnquotedWildcard)
            {
                expanded.Add(field.Text);
                continue;
            }

            IReadOnlyList<string> matches = Globber.Expand(field.GlobPattern, state);
            expanded.AddRange(matches.Count > 0 ? matches : [field.Text]);
        }

        return expanded;
    }

    private readonly record struct Fragment(string Text, bool Splittable, bool Globbable);

    // A field under construction, carrying which of its characters came from unquoted text. Only
    // those may act as glob wildcards; the quoted ones are escaped into the pattern.
    private sealed class Field
    {
        private readonly StringBuilder text = new();
        private readonly StringBuilder pattern = new();

        public bool HasContent { get; private set; }

        public bool HasUnquotedWildcard { get; private set; }

        public string Text => text.ToString();

        public string GlobPattern => pattern.ToString();

        public void Append(string value, bool globbable)
        {
            HasContent = true;
            foreach (char character in value)
            {
                Append(character, globbable);
            }
        }

        public void Append(char character, bool globbable)
        {
            HasContent = true;
            text.Append(character);

            if (!globbable && character is '*' or '?' or '[' or '\\')
            {
                pattern.Append('\\');
            }
            else if (globbable && character is '*' or '?' or '[')
            {
                HasUnquotedWildcard = true;
            }

            pattern.Append(character);
        }
    }
}
