using Sharp.Shell.Commands;
using Xunit;

namespace Sharp.Shell.Tests;

public class AppletRegistryTests
{
    // The design doc's command table plus sed (plans/sed-support.md) and awk (plans/awk-support.md),
    // asserted as a set rather than a count: a count still passes when one name is swapped for
    // another.
    private static readonly string[] DesignedCommandSet =
    [
        "ls", "cat", "head", "tail", "wc",
        "grep", "find", "sed", "awk", "nawk",
        "sort", "uniq", "cut", "tr",
        "basename", "dirname", "realpath", "pwd",
        "mkdir", "rm", "mv", "cp", "touch",
        "echo", "printf", "test", "[", "cd", "export", "read", "true", ":", "false", "exit",
        "command",
    ];

    [Fact]
    public void RegistryCoversTheDesignedCommandSet()
    {
        HashSet<string> actual = [.. AppletRegistry.CreateDefault().Names];

        Assert.Equal([.. DesignedCommandSet.Order(StringComparer.Ordinal)], [.. actual.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void EveryAppletIsReachableByItsOwnName()
    {
        AppletRegistry registry = AppletRegistry.CreateDefault();

        Assert.All(DesignedCommandSet, name => Assert.True(registry.TryGet(name, out _), $"{name} is not registered"));
    }
}
