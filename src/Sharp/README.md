# sharp

`Sharp.Shell` as a runnable shell. Commands the shell owns are C# function calls inside this
process — no `fork`, no `exec`, no `PATH`. Anything it does not own is handed to your real shell as
the original string.

```
dotnet build src/Sharp/Sharp.csproj
src/Sharp/bin/Debug/net10.0/sharp
```

## Usage

```
sharp                       interactive
sharp -c "echo hi"          one command
sharp script.sh             run a file
echo "echo hi" | sharp      read from a pipe

--root <dir>   confine the shell to <dir>: every path above it is refused, which is the rule the
               sandboxed tool tier runs under. Without it the shell is unconfined, like any shell.
--explain      print each line's classification before running it.
--strict       never fall through to a real shell; an unowned line exits 127 with the reason.
               This is the sandboxed guest's configuration, and the one to test against — with the
               fall-through on, your real shell answers and the result measures nothing.
```

## What `--explain` tells you

```
$ sharp --explain -c 'ls src | head -3'
[owned]

$ sharp --explain -c 'git status'
[native git] — 'git' is not one of the sandboxed commands

$ sharp --explain -c 'rm -rf build'
[owned mutates]
```

`[owned]` means the whole line ran here. `[native …]` names every program that forced it out, and
the line was handed over **whole** — never partly executed here first. `mutates` means the line can
change files, which is what the agent's tool layer prompts on.

## Two differences from the sandboxed tool

- **State persists between lines.** A `cd` sticks, because a shell is a conversation. The tool tier
  gets a fresh state per call, so a stray `cd` cannot silently relocate a later one.
- **Unconfined by default.** `--root` opts into the confinement the tool tier always has.

## Tests

`tests/Sharp.Shell.Tests/BlackBox` drives this binary as a black box, through a real process:

- `SharpContractTests` — argv, which stream each thing lands on, exit status, script files,
  piped input, persistence between lines, `--root` confinement, `--strict` refusal.
- `SharpCorpusTests` — the vendored oils spec corpus, which upstream runs against `bash`, `dash`
  and `mksh` the same way. Every case that passes in-process **and** classifies `Owned` must also
  pass through the binary.

The `Owned` filter is the subtle part. Layer 1 drives the executor directly, so an unowned command
inside a case fails on its own and the rest of the line still runs — right for measuring the
language. The binary honours Rule 2, where one unowned name sends the whole line elsewhere. Compare
across that and you are measuring the rule, not the binary.

Everything a real shell does that has no meaning without processes — `&`, job control, `trap`,
process substitution, `$$` — is refused by name and sent to your real shell rather than emulated.
