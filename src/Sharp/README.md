# sharp

`Sharp.Shell` as a runnable shell. Commands the shell owns are C# function calls inside this
process — no `fork`, no `exec`, no `PATH`. Anything it does not own is started as a real program,
one command at a time. Only a line this shell cannot run at all — `&`, `trap`, a syntax error — is
handed to your real shell as the original string.

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
--explain      print each line's classification, then one line per command the shell decided about.
--strict       start no processes at all. An unowned command is refused where it would have been
               dispatched: the line exits 126 with the reason, and the owned commands ahead of the
               refusal have already run. This is the sandboxed guest's configuration, and the one
               to test against — with real programs available, they answer and the result measures
               nothing.
```

## Scripts

A command is not always a line, so `sharp script.sh` and `echo … | sharp` read lines until the
command is whole: a function body, a multi-line `if`, a here-document and a trailing `\` or `|` all
continue onto the next line. Interactively, the continuation prompt is `> `.

A **syntax error stops a non-interactive shell where it stands**, as bash does: the offending
command does not run and neither does anything after it, and the exit status is 2. A construct this
shell does not implement — `&`, `trap`, process substitution — is not a syntax error: it is valid
bash, so the line is handed over and the script runs on.

## What `--explain` tells you

The first line classifies the line; the indented ones are the decisions, one per command, in the
order the shell dispatched them.

```
$ sharp --explain -c 'ls src | head -3'
[owned]
  owned ls src
  owned head -3

$ sharp --explain --strict -c 'echo hi; git status'
[native git] — 'git' is not one of the sandboxed commands
  owned echo hi
  native git status — refused: 'git' is not one of the sandboxed commands
```

`[owned]` means every program in the line is one of ours. `[native …]` names the programs that are
not — and the line **still runs**, command by command, rather than being handed over whole.
`mutates` means the line can change files, which is what the agent's tool layer prompts on.

Note what the second example shows: `echo hi` printed before `git status` was refused. A denial
stops the line, but it cannot undo what ran ahead of it.

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

The `Owned` filter keeps the comparison to cases whose behaviour cannot depend on what is installed
on the machine running the suite. A case naming `git` would otherwise measure the local `git`.

Everything a real shell does that has no meaning without processes — `&`, job control, `trap`,
`coproc`, process substitution — is refused by name and sent to your real shell rather than
emulated. Shell functions, `select`, `[[ … ]]`, `$$` and `$!` are not in that set: they need no
process model, and they run here.
