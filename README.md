# sharp-shell

A bash implementation that keeps the *language* and discards the Unix *process model*. Every
command the shell owns is a C# function call in the calling process: a pipeline is a buffer between
two functions, `$(...)` is a captured string, and there is no `fork`, no `exec`, and no `PATH` to
borrow from. Anything it does not own — `git`, `dotnet`, `npm`, anything needing a real toolchain —
is never partly executed: the whole line classifies as native and is handed back as the original
string for the caller to decide about.

That property is the point. An agent's bash tool is the widest hole in its sandbox; this shell
closes most of it by *owning* the exploratory majority of commands instead of spawning them, and by
making the rest a visible, per-program decision the host makes rather than a side effect the shell
already caused.

## Projects

| Project | What it is |
|---|---|
| `src/Sharp.Shell` | The language core: lexer, parser, word expansion, executor, and the owned command set (`grep`, `sed`, `awk`, `cat`, `ls`, `find`, `wc`, `sort`, …). Zero package references, no `System.Diagnostics.Process`, no threads, no reflection — so it compiles into a trimmed, NativeAOT WASI-P2 guest. |
| `src/Sharp` | `sharp`, the same shell as a runnable binary. Try the language, or see exactly which commands a sandboxed tool tier would run itself. See [its README](src/Sharp/README.md). |
| `tests/Sharp.Shell.Tests` | In-process tests, black-box tests through the `sharp` binary, differential tests against real `bash`/`sed`/`awk`, and the vendored oils spec corpus with a two-way ratchet on expected failures. |

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

- **Rule 1 — the root.** With a root set, every path above it is refused. `sharp --root <dir>` is
  that rule at the command line.
- **Rule 2 — all or nothing.** One unowned program in a line sends the *whole* line native. The
  shell never runs the owned half first. `sharp --strict` refuses instead of handing over, which is
  the configuration a sandboxed guest runs under and the only honest one to measure against.

Everything that has no meaning without processes — `&`, job control, `trap`, process substitution,
`$$` — is refused by name rather than emulated.

## Used by

[duetui](https://github.com/UtopleMan/duetui) vendors this repository as a submodule at
`vendor/sharp-shell`: the host references `Sharp.Shell` directly, and its builtin-tools WASI-P2
guest compiles the same sources into the sandbox.
