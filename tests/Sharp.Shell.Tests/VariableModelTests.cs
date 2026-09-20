using Sharp.Shell.Execution;
using Xunit;

namespace Sharp.Shell.Tests;

public class VariableModelTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("sharp-shell-variables").FullName;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void APlainAssignmentIsNotExported()
    {
        ShellState state = new(root);

        state.Variables["FOO"] = "bar";

        Assert.False(state.Variables.IsExported("FOO"));
        Assert.Empty(state.ExportedVariables);
    }

    [Fact]
    public void ExportingMarksTheVariable()
    {
        ShellState state = new(root);
        state.Variables["FOO"] = "bar";

        state.Variables.Export("FOO");

        Assert.True(state.Variables.IsExported("FOO"));
        Assert.Equal("bar", state.ExportedVariables["FOO"]);
    }

    [Fact]
    public void ReassigningAnExportedNameKeepsTheMark()
    {
        ShellState state = new(root);
        state.Variables["FOO"] = "bar";
        state.Variables.Export("FOO");

        state.Variables["FOO"] = "later";

        Assert.True(state.Variables.IsExported("FOO"));
        Assert.Equal("later", state.ExportedVariables["FOO"]);
    }

    [Fact]
    public void UnexportingKeepsTheValue()
    {
        ShellState state = new(root);
        state.Variables["FOO"] = "bar";
        state.Variables.Export("FOO");

        state.Variables.Unexport("FOO");

        Assert.Equal("bar", state.Variables["FOO"]);
        Assert.Empty(state.ExportedVariables);
    }

    [Fact]
    public void RemovingDropsTheValueAndTheMark()
    {
        ShellState state = new(root);
        state.Variables["FOO"] = "bar";
        state.Variables.Export("FOO");

        state.Variables.Remove("FOO");

        Assert.False(state.Variables.ContainsKey("FOO"));
        Assert.False(state.Variables.IsExported("FOO"));
        Assert.Empty(state.ExportedVariables);
    }

    [Fact]
    public void AForkSeesTheSameMarks()
    {
        ShellState state = new(root);
        state.Variables["EXPORTED"] = "yes";
        state.Variables.Export("EXPORTED");
        state.Variables["LOCAL"] = "no";

        ShellState fork = state.Fork();

        Assert.True(fork.Variables.IsExported("EXPORTED"));
        Assert.False(fork.Variables.IsExported("LOCAL"));
        Assert.Equal("no", fork.Variables["LOCAL"]);
    }

    [Fact]
    public void ExportingAnUnknownNameCreatesItEmptyAndMarked()
    {
        ShellState state = new(root);

        state.Variables.Export("FOO");

        Assert.Equal(string.Empty, state.Variables["FOO"]);
        Assert.True(state.Variables.IsExported("FOO"));
    }
}
