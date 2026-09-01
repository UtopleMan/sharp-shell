using System.Text;

namespace Sharp.Shell.Text;

// Writes the parsed pattern back out as .NET syntax. Every construct whose POSIX meaning was
// decided during parsing arrives here already resolved, so this stage only escapes and groups.
internal static class RegexEmitter
{
    public static string Emit(RegexNode node) => node switch
    {
        RegexLiteral literal => EscapeLiteral(literal.Character),
        RegexAtom atom => atom.Emission,
        RegexBackreference backreference => $@"\{backreference.Group}",
        RegexGroup group => $"({Emit(group.Body)})",
        RegexRepeat repeat => Repeat(repeat),
        RegexSequence sequence => Concatenate(sequence),
        RegexAlternation alternation => string.Join('|', alternation.Branches.Select(Emit)),
        _ => string.Empty,
    };

    private static string Concatenate(RegexSequence sequence)
    {
        StringBuilder emitted = new();

        foreach (RegexNode item in sequence.Items)
        {
            emitted.Append(Emit(item));
        }

        return emitted.ToString();
    }

    private static string Repeat(RegexRepeat repeat) => $"{Group(repeat.Body)}{Quantifier(repeat)}";

    // A quantifier binds to one atom, so anything that is not already one gets a non-capturing
    // group. Capturing here would renumber the backreferences the pattern may use.
    private static string Group(RegexNode body) =>
        body is RegexLiteral or RegexGroup or RegexBackreference || body is RegexAtom { IsZeroWidth: false }
            ? Emit(body)
            : $"(?:{Emit(body)})";

    private static string Quantifier(RegexRepeat repeat) => (repeat.Minimum, repeat.Maximum) switch
    {
        (0, RegexRepeat.Unbounded) => "*",
        (1, RegexRepeat.Unbounded) => "+",
        (0, 1) => "?",
        (int minimum, RegexRepeat.Unbounded) => $"{{{minimum},}}",
        (int minimum, int maximum) when minimum == maximum => $"{{{minimum}}}",
        (int minimum, int maximum) => $"{{{minimum},{maximum}}}",
    };

    private static string EscapeLiteral(char character) => character switch
    {
        '\\' or '.' or '$' or '^' or '{' or '[' or '(' or '|' or ')' or '*' or '+' or '?' or '#' or ' ' or '}' or ']' => $@"\{character}",
        '\n' => @"\n",
        '\r' => @"\r",
        '\t' => @"\t",
        '\f' => @"\f",
        '\v' => @"\v",
        _ => character < ' ' || character == (char)0x7f ? $@"\x{(int)character:x2}" : character.ToString(),
    };
}
