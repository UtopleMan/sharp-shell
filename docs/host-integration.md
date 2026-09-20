# Host integration

What an embedding host has to implement, and the three things that will bite it if it does not read
this first.

`Sharp.Shell` runs a bash line by calling C# functions. Where it needs something outside itself — a
program it does not own, permission to act — it asks the host through one of two interfaces. There
is no third path: **a process starts because the shell asked for one.** The host does not decide to
go native, does not pre-scan the line, and never needs to parse bash to know what it is consenting
to.

## The two seams

Both live in `src/Sharp.Shell`. `ShellExecutor` is the only caller of either, and it always asks
`ICommandApprover` before it asks `ICommandExecutor`.

```csharp
ShellExecutor executor = new(AppletRegistry.CreateDefault(), myExecutor, myApprover);
ShellRun run = executor.Run(commandLine, new ShellState(root), cancellationToken);

Console.Write(run.Result.Stdout);
Console.Error.Write(run.Result.Stderr);
```

### `ICommandApprover` — may this run?

```csharp
CommandApproval Approve(
    string program,
    IReadOnlyList<string> arguments,
    string commandText,
    string workingDirectory,
    bool isOwned,
    CancellationToken cancellationToken);

CommandApproval ApproveLine(
    string commandLine,
    string workingDirectory,
    CancellationToken cancellationToken);
```

`Approve` fires once per command dispatch — every command, owned applet and real program alike,
inside loops, branches, pipelines and `$( )`. `ApproveLine` fires for a line the shell cannot run
itself and is about to hand over whole.

`isOwned` says whether the shell is about to run its own sandboxed applet or reach for a real
program. It is information, not policy: a host may consent to a sandboxed `rm` confined to the
workspace and refuse the same name as a process, and only the shell knows which it is about to do.

Return `CommandApproval.Allowed` or `CommandApproval.Deny(reason)`. The reason is printed, so write
it for the person reading the terminal.

### `ICommandExecutor` — run this

```csharp
CommandExecution Execute(
    string program,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    IReadOnlyDictionary<string, string> environment,
    IEnumerable<string> input,
    CancellationToken cancellationToken);

CommandExecution ExecuteLine(
    string commandLine,
    string workingDirectory,
    IReadOnlyDictionary<string, string> environment,
    CancellationToken cancellationToken);
```

`Execute` runs one unowned command with its arguments already expanded. `ExecuteLine` runs a line
the shell cannot run at all. `ExecuteLine` is defaulted to `CommandExecution.NotSupported`, so an
executor that can only run programs — a wasm guest, a test double — inherits the right answer
without writing it.

**`environment` is part of the request, and filtering it is your job.** It is the shell's exported
variables — what `export` marked, plus whatever environment you handed `ShellState` — and nothing
else: a plain `x=1` assignment is not in it. The shell decides *what* is offered to a child; you
decide what the child actually receives, which is the natural place for the decision because you are
already the one starting it. Pass it straight through, filter it to an allowlist, or ignore it — but
ignoring it means `export FOO=bar; yourtool` cannot see `FOO`.

> **Contract change in v0.3.0.** Both methods gained the parameter, with no compatibility overload.
> An executor written against v0.2 will not compile until it takes the environment.

`Output` is an `IEnumerable<string>` and the shell never materializes it: `cat huge.log | yourtool`
and `yourtool | head -2` both stream, and a consumer that stops enumerating stops the producer.
That is this shell's SIGPIPE, and an executor that reads its child to the end before returning
throws it away.

## Be fail-closed

Ship `DenyingCommandApprover` and `NotSupportedCommandExecutor` until the real ones work, and make
the real ones deny on any path you have not thought about — an exception, a timeout, a decision the
user closed without answering.

`ApproveLine` deliberately has **no default implementation**. Gating one request and silently
missing the other should not compile.

`AllowAllCommandApprover` is the library default, so that constructing a `ShellExecutor` without an
approver behaves exactly as it did before the seam existed. It is a compatibility shim. Do not ship
it.

## What a denial does

A denied command exits **126** — "found but not executable", distinct from the 127 an unknown
command returns — writes `<shell name>: <reason>` to stderr, and **unwinds the run**. `denied ||
fallback` does not run `fallback`; a refusal in a loop body stops the loop; a refusal inside `$( )`
refuses the outer line. `ShellResult.RefusalReason` is non-null exactly when this happened, so a
host tells consent from failure without parsing stderr.

> **A denial mid-line leaves the commands before it already run.**
>
> This is not a defect and it cannot be fixed. The shell can only ask about `rm $f` once `$f` has a
> value, and by the time it can ask, the commands ahead of it are done. `echo built > log; rm -rf
> dist; git push` refused at `git push` still wrote `log` and still deleted `dist`.
>
> If a line must be all-or-nothing, decide before calling `Run`, not during.

## The shell's name is yours to supply

`ShellState` takes the name this shell answers to:

```csharp
ShellState state = new(root, workingDirectory: null, shellName: "my-shell");
```

It is what `$0` expands to, and it is the prefix on **every message the shell writes about itself** —
a refusal, a command not found, an unsupported construct, a redirection that could not be opened. A
host that supplies nothing gets `sharp-shell`, this library's own name.

> **Contract change in v0.3.0.** Messages used to be prefixed with the literal `duetui-shell:`
> regardless of the host. If you match on stderr — in tests, in a log parser — match on the name you
> supplied. `ShellResult.RefusalReason` carries a refusal's reason without the prefix, and is the
> thing to read rather than stderr.

## The target is post-expansion

`commandText` is `CommandText.Of(program, arguments)`: one canonical spelling, so every host names
the same target the same way instead of joining arguments its own way. Arguments containing
whitespace, quotes or shell metacharacters are POSIX single-quoted; everything else is bare.
`dotnet build "my proj"` and `dotnet build my\ proj` both arrive as `dotnet build 'my proj'`.

It is **post-expansion**, which is the point and also the caveat:

| the user typed | the host is asked about |
|---|---|
| `dotnet $VERB` | `dotnet build` |
| `rm $f` (iteration 3) | `rm build/c.cs` |
| `git $(cat cmd.txt)` | `git push` |

That is what actually runs. It is not what the user typed, and a rule written against the typed
text will not match. Match against `commandText`.

## Prompt volume

**A 10,000-iteration loop fires 10,000 approvals.** A host whose "allow" answer means *allow this
one dispatch* makes itself unusable on the first real loop.

Cache by target and scope the cache to the run. `CommandApproval` carries a reason rather than being
a bare `bool` precisely so it has room to grow; until it does, the host owns the caching:

- Key on `commandText`, or on a coarser rule the user actually agreed to (`dotnet build *`).
- Scope an "always" answer to the `Run` call, or to the session, but say which in the dialog.
- Answer from the cache synchronously. `Approve` is on the shell's only thread; every millisecond
  spent there is a millisecond the loop is not running.

## Seeding the environment

`ShellState` takes the environment it starts with. The core reads nothing from the operating system
— that is what keeps it compiling into a trimmed NativeAOT WASI-P2 guest — so every value it holds
arrived from a host:

```csharp
ShellState state = new(root, workingDirectory, shellName, environment);
```

Every entry arrives **exported**, because that is what an inherited variable is, and re-assigning one
keeps it exported the way bash does. `shsh` seeds this from `Environment.GetEnvironmentVariables()`.
A guest should seed it from whatever the host passed it — under duetui, the `wasi:cli/environment`
values that `WasiOptions.Environment` supplied. Nothing in this shell ever makes a guest inherit the
host's own environment.

`$PWD` is the case worth naming: a host that supplies it gets an expanding `$PWD`, and one that does
not gets an empty one, because the shell does not invent it.

## If the shell is inside a wasm guest

`Approve` is synchronous from the guest's side and may block for as long as a person takes to answer
a modal. Two things must be proven before relying on it:

- The guest invocation must not be on the UI thread, or the dialog can never render.
- The wasm **epoch deadline** kills a long-running guest call. A blocked approval has to pause or
  extend it, or every prompt someone thinks about for too long becomes a killed shell.

`CommandExecution.Output` streaming needs the handle-based process imports
(`host-proc-spawn` / `host-proc-write` / `host-proc-read` / `host-proc-status` / `host-proc-close`).
A materialized `proc-output` import cannot carry it.
