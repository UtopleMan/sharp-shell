namespace Sharp.Shell.Commands;

// alias and unalias over the state's alias table. The expansion itself is the executor's, because an
// alias stands in for a command at the dispatch point and nowhere else.
public sealed class AliasApplet(string name, bool isDefining) : IApplet
{
    public string Name => name;

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);

        if (operands.Count == 0)
        {
            return isDefining ? Listing(context) : Missing(context);
        }

        return isDefining ? Define(context, operands) : Remove(context, operands);
    }

    private static AppletRun Listing(AppletContext context) =>
        new()
        {
            Output = context.State.Aliases
                .OrderBy(alias => alias.Key, StringComparer.Ordinal)
                .Select(alias => $"alias {alias.Key}='{alias.Value}'\n"),
        };

    private static AppletRun Define(AppletContext context, IReadOnlyList<string> operands)
    {
        foreach (string operand in operands)
        {
            int equals = operand.IndexOf('=', StringComparison.Ordinal);

            if (equals <= 0)
            {
                return Report(context, "alias", operand, "not found");
            }

            context.State.Aliases[operand[..equals]] = operand[(equals + 1)..];
        }

        return new AppletRun();
    }

    private AppletRun Remove(AppletContext context, IReadOnlyList<string> operands)
    {
        foreach (string operand in operands)
        {
            if (!context.State.Aliases.Remove(operand))
            {
                return Report(context, name, operand, "not found");
            }
        }

        return new AppletRun();
    }

    private AppletRun Missing(AppletContext context)
    {
        context.WriteError($"{name}: usage: {name} name [name ...]\n");
        return AppletRun.Failed(2);
    }

    private static AppletRun Report(AppletContext context, string applet, string operand, string reason)
    {
        context.WriteError($"{applet}: {operand}: {reason}\n");
        return AppletRun.Failed(1);
    }
}
