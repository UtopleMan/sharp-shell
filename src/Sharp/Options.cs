namespace Sharp;

// The command line, read once. Error is set rather than thrown so Main can print it and exit 2 the
// way a shell does, and so the parse itself can be tested without a process.
internal sealed record Options(
    string Root,
    string StartDirectory,
    string? Command,
    string? ScriptPath,
    bool Explains,
    bool Strict,
    bool NoRc,
    string? Error)
{
    public const string Usage =
        "usage: shsh [--root <dir>] [--explain] [--strict] [--norc] [-c <command> | <script>]";

    public SessionSettings Settings => new(Root, StartDirectory, Explains, Strict, !NoRc);

    public static Options Parse(string[] arguments)
    {
        // "/" means unconfined, which is what a shell normally is. --root opts into the sandboxed
        // tool tier's confinement instead, and then the session opens there.
        string root = "/";
        string? start = null;
        string? command = null;
        string? scriptPath = null;
        bool explains = false;
        bool strict = false;
        bool noRc = false;

        for (int index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "-c" when index + 1 < arguments.Length:
                    command = arguments[++index];
                    continue;
                case "--root" when index + 1 < arguments.Length:
                    root = Path.GetFullPath(arguments[++index]);
                    start = root;
                    continue;
                case "--explain":
                    explains = true;
                    continue;
                case "--strict":
                    strict = true;
                    continue;
                case "--norc":
                    noRc = true;
                    continue;
                case "-h" or "--help":
                    return Failed(root, Usage);
                default:
                    if (arguments[index].StartsWith('-'))
                    {
                        return Failed(root, $"unknown option '{arguments[index]}'\n{Usage}");
                    }

                    scriptPath = arguments[index];
                    continue;
            }
        }

        if (scriptPath is not null && !File.Exists(scriptPath))
        {
            return Failed(root, $"{scriptPath}: no such file");
        }

        return new Options(
            root,
            start ?? Directory.GetCurrentDirectory(),
            command,
            scriptPath,
            explains,
            strict,
            noRc,
            null);
    }

    private static Options Failed(string root, string error) =>
        new(root, root, null, null, false, false, false, error);
}
