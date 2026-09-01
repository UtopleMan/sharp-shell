namespace Sharp.Shell.Commands;

// Reading a list of file operands with the workspace check and the missing-file report every
// reading applet shares.
internal static class FileOperands
{
    public static IEnumerable<string> Read(
        IReadOnlyList<string> operands,
        AppletContext context,
        AppletRun run,
        string appletName)
    {
        foreach (string operand in operands)
        {
            string absolute = context.State.Resolve(operand);

            if (!context.State.IsInsideRoot(absolute) || !File.Exists(absolute))
            {
                context.WriteError($"{appletName}: {operand}: No such file or directory\n");
                run.ExitCode = 1;
                continue;
            }

            foreach (string chunk in FileChunks.Read(absolute))
            {
                yield return chunk;
            }
        }
    }
}
