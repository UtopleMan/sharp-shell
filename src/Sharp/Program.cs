using Sharp;

// sharp — the Sharp.Shell language as a runnable shell.
//
//   sharp                       interactive
//   sharp -c "echo hi"          one command
//   sharp script.sh             run a file
//   echo "echo hi" | sharp      read from a pipe
//
//   --root <dir>   confine the shell to <dir>, refusing every path above it. This is the sandboxed
//                  tool tier's own rule, made available here so it can be tried directly.
//   --explain      print each line's classification before running it: whether the shell owns the
//                  line, which programs forced it out to a real shell, and whether it mutates.
//   --strict       never fall through to a real shell — an unowned line exits 127 with the reason.
//                  This is the sandboxed guest's configuration, and the one to test against: with
//                  the fall-through on, your real shell answers and the result measures nothing.
internal static class Program
{
    private static int Main(string[] arguments)
    {
        Options options = Options.Parse(arguments);

        if (options.Error is { } error)
        {
            Console.Error.WriteLine($"sharp: {error}");
            return 2;
        }

        Session session = new(options.Settings, Console.Out, Console.Error);
        using CancellationTokenSource interrupt = new();

        // Ctrl+C cancels the running command, as a shell does, rather than killing the shell.
        Console.CancelKeyPress += (_, cancel) =>
        {
            cancel.Cancel = true;
            interrupt.Cancel();
        };

        return options.Command is { } command
            ? session.Run(command, interrupt.Token)
            : RunLines(session, options, interrupt);
    }

    private static int RunLines(Session session, Options options, CancellationTokenSource interrupt)
    {
        using TextReader reader = options.ScriptPath is { } path ? new StreamReader(path) : Console.In;
        bool interactive = options.ScriptPath is null && !Console.IsInputRedirected;

        if (interactive)
        {
            Console.Error.WriteLine($"sharp — {options.Root}");
            Console.Error.WriteLine("owned commands run in-process; anything else is handed to your real shell.");
        }

        int status = 0;

        while (true)
        {
            if (interactive)
            {
                Console.Write($"{Prompt(session, options.Root)}$ ");
            }

            if (reader.ReadLine() is not { } line)
            {
                return status;
            }

            status = RunOne(session, line, interrupt);

            if (session.WantsExit)
            {
                return session.ExitStatus;
            }
        }
    }

    // A cancelled command must not take the shell down with it.
    private static int RunOne(Session session, string line, CancellationTokenSource interrupt)
    {
        try
        {
            return session.Run(line, interrupt.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("^C");
            return 130;
        }
    }

    private static string Prompt(Session session, string root)
    {
        string relative = Path.GetRelativePath(root, session.WorkingDirectory);
        return relative == "." ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) : relative;
    }
}
