using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Every mutating applet resolves its operands through here. The wasm preopen also bounds them, but
// this library runs natively too, and a mutation is the one class of mistake that cannot be undone
// by trying again.
internal static class MutationGuard
{
    public static bool TryResolve(string operand, AppletContext context, string appletName, out string absolute)
    {
        absolute = context.State.Resolve(operand);

        if (context.State.IsInsideRoot(absolute))
        {
            return true;
        }

        context.WriteError($"{appletName}: {operand}: outside the workspace\n");
        return false;
    }
}
