# sharp-shell

A bash implementation that keeps the *language* and discards the Unix *process model*. Every
command the shell owns is a C# function call in the calling process: a pipeline is a buffer between
two functions, `$(...)` is a captured string, and there is no `fork`, no `exec`, and no `PATH` to
borrow from. Anything it does not own — `git`, `dotnet`, `npm`, anything needing a real toolchain —
the shell asks the host for, one command at a time, before it dispatches it.

That property is the point. An agent's bash tool is the widest hole in its sandbox; this shell
closes most of it by *owning* the exploratory majority of commands instead of spawning them, and by
making the rest a visible, per-command decision the host makes rather than a side effect the shell
already caused. The host never parses bash to know what it is consenting to: the question arrives
fully expanded, so iteration three of a loop asks about `rm build/c.cs`, not about `rm $f`.

## Projects

| Project | What it is |
|---|---|
| `src/Sharp.Shell` | The language core: lexer, parser, word expansion, executor, and the owned command set (`grep`, `sed`, `awk`, `cat`, `ls`, `find`, `wc`, `sort`, …). Zero package references, no `System.Diagnostics.Process`, no threads, no reflection — so it compiles into a trimmed, NativeAOT WASI-P2 guest. |
| `src/Sharp` | `shsh`, the same shell as a runnable binary. Try the language, or see exactly which commands a sandboxed tool tier would run itself. See [its README](src/Sharp/README.md). |
| `tests/Sharp.Shell.Tests` | In-process tests, black-box tests through the `shsh` binary, differential tests against real `bash`/`sed`/`awk`, and the vendored oils spec corpus with a two-way ratchet on expected failures. |

## Build and test

```
dotnet build Sharp.slnx
dotnet test tests/Sharp.Shell.Tests
```

The differential suites compare this shell against the real `bash`, `sed` and `awk`. Where a tool is
missing — Windows, or any machine without `gawk` — they replay
`tests/Sharp.Shell.Tests/corpus/oracle-recordings.json`, the answers those tools gave on the machine
that recorded them. A call with no recording fails rather than passing quietly.

```
SHARP_ORACLE_RECORD=1 dotnet test    # re-record, on a machine that has the tools
SHARP_ORACLE_REPLAY=1 dotnet test    # prove the recording is complete, ignoring installed tools
```

Re-record on the platform CI runs on: BSD and GNU coreutils disagree about formatting, so a
recording carries its platform with it. `MinedDifferentialTests` is deliberately not recorded — its
commands come from whichever machine runs the suite, so it still needs a real `bash`.

## Confinement

`Sharp.Shell` enforces two rules the embedding host relies on:

- **Rule 1 — the root.** With a root set, every path above it is refused. `shsh --root <dir>` is
  that rule at the command line.
- **Rule 2 — ask before every dispatch.** The shell runs the line and asks about each command
  before dispatching it, owned applet and real program alike. A line it cannot run at all is handed
  over whole, as one decision. `shsh --strict` supplies an approver that refuses every unowned
  name, which is the configuration a sandboxed guest runs under and the only honest one to measure
  against.

  **A denial mid-line leaves the commands before it already run.** That is the cost of deciding at
  run time and it is not recoverable: the shell can only ask about `rm $f` once `$f` has a value,
  and by then the commands ahead of it are done. A host that needs a line to be all-or-nothing must
  decide before calling, not during.

Everything that has no meaning without processes — `&`, job control, `trap`, `coproc`, process
substitution — is refused by name rather than emulated, and those lines keep the whole-line
hand-back. Everything that merely *looked* like it needed processes does not: shell functions,
`select`, `[[ … ]]`, `$$` and `$!` all run here.

## The two host seams

Both live in `src/Sharp.Shell`, beside each other, and both are consulted by `ShellExecutor` alone.

> **The invariant: nothing starts a process except at this shell's request.**
>
> The host does not decide to go native, does not pre-scan the line, and does not keep a `bash -c`
> path of its own for the cases it thinks the shell cannot handle. It answers the two questions
> below and otherwise waits. A host that keeps its own escalation path has not adopted this design —
> it has added a second, ungated way to start a process beside the gated one, which is the hole this
> shell exists to close.

| Seam | The shell asks |
|---|---|
| `ICommandApprover` | *May this run?* `Approve` for one command, with `program`, `arguments`, a canonical `commandText`, the working directory, and whether the shell is about to run its own applet. `ApproveLine` for a line the shell cannot run itself, which is the coarse case: nobody can say what the commands in it are, so the line is the target. |
| `ICommandExecutor` | *Run this.* `Execute` for one unowned command, with its arguments already expanded. `ExecuteLine` for a line the shell cannot run itself. |

**Be fail-closed.** `DenyingCommandApprover` and `NotSupportedCommandExecutor` are the shipped
fail-closed implementations; `AllowAllCommandApprover` is the default so that embedding the library
without plugging anything in behaves as it did before the seam existed. `ApproveLine` has no default
implementation on purpose — gating one request and forgetting the other should not compile.

A denied command exits `126`, writes `duetui-shell: <reason>` to stderr, and unwinds the run:
`denied || fallback` does **not** run `fallback`. `ShellResult.RefusalReason` reports it without
anyone having to parse stderr.

See [docs/host-integration.md](docs/host-integration.md) for what a host has to implement.

## Used by

[duetui](https://github.com/UtopleMan/duetui) vendors this repository as a submodule at
`vendor/sharp-shell`: the host references `Sharp.Shell` directly, and its builtin-tools WASI-P2
guest compiles the same sources into the sandbox.
