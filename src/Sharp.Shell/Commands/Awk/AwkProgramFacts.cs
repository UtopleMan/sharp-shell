namespace Sharp.Shell.Commands.Awk;

// Questions the classifier and the interpreter both ask of a parsed program without running it:
// where does it write, and does it read input at all.
internal static class AwkProgramFacts
{
    public static bool WritesOutsideStandardStreams(AwkProgram program) => Redirections(program)
        .Any(redirection => redirection.Target is not AwkStringLiteral { Value: "/dev/stdout" or "/dev/stderr" });

    // A program of nothing but BEGIN rules never reads: `awk 'BEGIN{print 1}'` must not sit waiting
    // on a terminal.
    public static bool ReadsInput(AwkProgram program) =>
        program.Rules.Any(rule => rule.Pattern is not AwkBeginPattern);

    private static IEnumerable<AwkRedirection> Redirections(AwkProgram program) =>
        Statements(program).Select(RedirectionOf).Where(redirection => redirection is not null)!;

    private static AwkRedirection? RedirectionOf(AwkStatement statement) => statement switch
    {
        AwkPrintStatement print => print.Redirection,
        AwkPrintfStatement print => print.Redirection,
        _ => null,
    };

    private static IEnumerable<AwkStatement> Statements(AwkProgram program)
    {
        foreach (AwkRule rule in program.Rules)
        {
            if (rule.Action is not null)
            {
                foreach (AwkStatement statement in Nested(rule.Action))
                {
                    yield return statement;
                }
            }
        }

        foreach (AwkFunction function in program.Functions.Values)
        {
            foreach (AwkStatement statement in Nested(function.Body))
            {
                yield return statement;
            }
        }
    }

    private static IEnumerable<AwkStatement> Nested(AwkStatement statement)
    {
        yield return statement;

        foreach (AwkStatement child in Children(statement))
        {
            foreach (AwkStatement descendant in Nested(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<AwkStatement> Children(AwkStatement statement) => statement switch
    {
        AwkBlock block => block.Statements,
        AwkIfStatement branch => branch.Else is null ? [branch.Then] : [branch.Then, branch.Else],
        AwkWhileStatement loop => [loop.Body],
        AwkDoWhileStatement loop => [loop.Body],
        AwkForStatement loop => Parts(loop),
        AwkForInStatement loop => [loop.Body],
        _ => [],
    };

    private static IEnumerable<AwkStatement> Parts(AwkForStatement loop)
    {
        if (loop.Initialiser is not null)
        {
            yield return loop.Initialiser;
        }

        if (loop.Update is not null)
        {
            yield return loop.Update;
        }

        yield return loop.Body;
    }
}
