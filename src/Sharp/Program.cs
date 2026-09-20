using Sharp;

// shsh — the Sharp.Shell language as a runnable shell.
//
//   shsh                        interactive
//   shsh -c "echo hi"           one command
//   shsh script.sh              run a file
//   echo "echo hi" | shsh       read from a pipe
//
//   --root <dir>   confine the shell to <dir>, refusing every path above it. This is the sandboxed
//                  tool tier's own rule, made available here so it can be tried directly.
//   --explain      print each line's classification before running it: whether the shell owns the
//                  line, which programs forced it out to a real shell, and whether it mutates.
//   --strict       never fall through to a real shell — an unowned command is refused with its
//                  reason.
//                  This is the sandboxed guest's configuration, and the one to test against: with
//                  the fall-through on, your real shell answers and the result measures nothing.
internal static class Program
{
    private static int Main(string[] arguments)
    {
        Options options = Options.Parse(arguments);

        if (options.Error is { } error)
        {
            Console.Error.WriteLine($"shsh: {error}");
            return 2;
        }

        Session session = new(options.Settings, Console.Out, Console.Error);
        using Interrupts interrupts = new();

        // Ctrl+C stops the running command and abandons the line being typed, as a shell does,
        // rather than killing the shell.
        Console.CancelKeyPress += (_, cancel) =>
        {
            cancel.Cancel = true;
            interrupts.Raise();
            Console.Error.WriteLine("^C");
        };

        return options.Command is { } command
            ? RunOneCommand(session, command, interrupts)
            : RunLines(session, options, interrupts);
    }

    private static int RunOneCommand(Session session, string command, Interrupts interrupts) =>
        CommandReader.SyntaxErrorIn(command) is { } reason
            ? ReportSyntaxError(reason)
            : RunOne(session, command, interrupts);

    // bash stops a non-interactive shell at its first syntax error: the offending command does not
    // run and neither does anything after it. An interactive shell reports and carries on, because
    // the next thing typed is a fresh start.
    private static int ReportSyntaxError(string reason)
    {
        Console.Error.WriteLine($"shsh: {reason}");
        return 2;
    }

    private static int RunLines(Session session, Options options, Interrupts interrupts)
    {
        using TextReader reader = options.ScriptPath is { } path ? new StreamReader(path) : Console.In;
        bool interactive = options.ScriptPath is null && !Console.IsInputRedirected;

        if (interactive)
        {
            Console.Error.WriteLine($"shsh — {options.Root}");
            Console.Error.WriteLine("owned commands run in-process; anything else is handed to your real shell.");
        }

        CommandReader commands = new();
        int status = 0;

        while (true)
        {
            if (interactive)
            {
                Console.Write(commands.IsContinuing ? "> " : $"{Prompt(session, options.Root)}$ ");
            }

            if (reader.ReadLine() is not { } line)
            {
                // The input ended in the middle of a command. It runs anyway, so that it reports
                // its own unterminated-construct error rather than vanishing.
                return commands.IsContinuing ? RunOneCommand(session, commands.Pending, interrupts) : status;
            }

            // Ctrl+C arrived while this line was being typed. The line goes, and so does anything
            // pending with it: an unterminated quote must not be able to swallow the shell.
            if (interactive && interrupts.WasRaised)
            {
                interrupts.Reset();
                commands.Abandon();
                continue;
            }

            if (!commands.TryComplete(line, out string command))
            {
                continue;
            }

            if (CommandReader.SyntaxErrorIn(command) is { } reason)
            {
                if (!interactive)
                {
                    return ReportSyntaxError(reason);
                }

                ReportSyntaxError(reason);
                continue;
            }

            interrupts.Reset();
            status = RunOne(session, command, interrupts);

            if (session.WantsExit)
            {
                return session.ExitStatus;
            }
        }
    }

    // A cancelled command must not take the shell down with it. The handler has already said ^C.
    private static int RunOne(Session session, string line, Interrupts interrupts)
    {
        try
        {
            return session.Run(line, interrupts.Token);
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
    }

    private static string Prompt(Session session, string root)
    {
        string relative = Path.GetRelativePath(root, session.WorkingDirectory);
        return relative == "." ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) : relative;
    }
}
