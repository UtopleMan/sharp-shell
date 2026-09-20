using System.Text;
using Sharp.Shell.Execution;
using Sharp.Shell.Lexing;

namespace Sharp.Shell.Expansion;

// Turns one parsed word into the fields a command actually receives. Quoting decides everything:
// only the output of an *unquoted* expansion is field-split, and only unquoted characters are
// glob-special. That is why the lexer kept the quoting structure instead of resolving it.
public sealed class WordExpander(ShellState state, Func<string, CommandSubstitution> runSubstitution)
{
    private const string DEFAULT_FIELD_SEPARATORS = " \t\n";

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

        List<Field> fields = Split(fragments, splitAndGlob, Separators);

        return ExpansionResult.Ok(splitAndGlob ? Globbed(fields) : [.. fields.Select(field => field.Text)]);
    }

    // IFS decides what a field boundary is. An unset IFS means the default; an empty IFS disables
    // splitting altogether, which is the property a script relies on when it wants one field back.
    private string Separators =>
        state.Variables.TryGetValue("IFS", out string separators) ? separators : DEFAULT_FIELD_SEPARATORS;

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
                fragments.Add(new Fragment(Home, false, false));
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

    // Without a HOME there is nowhere to go but the root, which is the one directory the shell is
    // certain of. The core never reads the operating system's idea of a home directory.
    private string Home =>
        state.Variables.TryGetValue("HOME", out string home) && home.Length > 0 ? home : state.RootPath;

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
            return ExpansionResult.Failed(resolved.ErrorMessage, resolved.IsFatal);
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

    private static List<Field> Split(List<Fragment> fragments, bool splitAndGlob, string separators)
    {
        FieldSplitter splitter = new(separators);

        foreach (Fragment fragment in fragments)
        {
            splitter.Append(fragment, splitting: splitAndGlob && fragment.Splittable && separators.Length > 0);
        }

        return splitter.Finish();
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

    // POSIX field splitting, which treats the two kinds of separator differently. A run of IFS
    // whitespace is one boundary and a run at either end is ignored; a non-whitespace separator is a
    // boundary every time it appears, so `a::b` with IFS=: has an empty field in the middle. One rule
    // for both kinds gets one of them wrong.
    private sealed class FieldSplitter(string separators)
    {
        private readonly List<Field> fields = [];

        private Field current = new();

        private bool inSeparatorRun;

        private bool runHasNonWhitespace;

        public void Append(Fragment fragment, bool splitting)
        {
            if (!splitting)
            {
                inSeparatorRun = false;
                current.Append(fragment.Text, fragment.Globbable);
                return;
            }

            foreach (char character in fragment.Text)
            {
                Take(character, fragment.Globbable);
            }
        }

        public List<Field> Finish()
        {
            if (current.HasContent)
            {
                fields.Add(current);
            }

            return fields;
        }

        private void Take(char character, bool globbable)
        {
            if (!separators.Contains(character, StringComparison.Ordinal))
            {
                inSeparatorRun = false;
                current.Append(character, globbable);
                return;
            }

            Separate(IsWhitespace(character));
        }

        private void Separate(bool isWhitespace)
        {
            if (!inSeparatorRun)
            {
                StartRun(isWhitespace);
                return;
            }

            if (isWhitespace || !runHasNonWhitespace)
            {
                runHasNonWhitespace |= !isWhitespace;
                return;
            }

            fields.Add(new Field());
        }

        private void StartRun(bool isWhitespace)
        {
            inSeparatorRun = true;
            runHasNonWhitespace = !isWhitespace;

            if (current.HasContent)
            {
                fields.Add(current);
                current = new Field();
                return;
            }

            if (!isWhitespace)
            {
                fields.Add(new Field());
            }
        }

        private static bool IsWhitespace(char separator) => separator is ' ' or '\t' or '\n';
    }

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
