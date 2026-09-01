namespace Sharp.Shell;

// Tool results are fed back into the model verbatim, so an unbounded read or command can push a
// single turn past the context window. Text beyond the budget is cut here with a notice so the
// model knows the result was clipped rather than complete. Lives in this project because both the
// host's built-in tools and the sandboxed guest cap their output, and the constant needs one home.
public static class OutputBudget
{
    public const int MaxCharacters = 100_000;

    public static string Cap(string text)
    {
        if (text.Length <= MaxCharacters)
        {
            return text;
        }

        int omittedCharacters = text.Length - MaxCharacters;

        return $"{text[..MaxCharacters]}\n(truncated {omittedCharacters} more characters — narrow the request or use offset/limit)";
    }
}
