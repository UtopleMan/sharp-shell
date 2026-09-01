#!/usr/bin/env python3
"""Mine a corpus of agent-written bash commands from public SWE-bench trajectories.

The SWE-bench experiments repository holds submission *entries*; the artifacts live in the
public `swe-bench-submissions` S3 bucket named by each entry's metadata.yaml. The
mini-SWE-agent submissions under `bash-only/` are the useful ones here: that agent's whole
action space is a single bash command per turn, so every assistant message contains exactly
one fenced bash block and nothing has to be reverse-engineered from a tool-call schema.

Run it from the repository root:

    python3 build/mine-agent-corpus.py

It rewrites tests/Sharp.Shell.Tests/corpus/agent-commands.txt in place. The output is sorted
and deduplicated, so re-running it against the same submissions produces the same file.
"""

import datetime
import json
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ElementTree
from pathlib import Path

BUCKET_URL = "https://swe-bench-submissions.s3.amazonaws.com"

S3_NAMESPACE = "{http://s3.amazonaws.com/doc/2006-03-01/}"

CORPUS_PATH = Path("tests/Sharp.Shell.Tests/corpus/agent-commands.txt")

# One submission per model family, so the corpus carries several agents' habits rather than one
# model's. Each name is a directory under evaluation/verified in SWE-bench/experiments and a
# prefix under bash-only/ in the bucket.
SUBMISSIONS = [
    "20250726_mini-v1.0.0_claude-sonnet-4-20250514",
    "20250720_mini-v0.0.0-gpt-4o-2024-11-20",
    "20250720_mini-v0.0.0-Llama-4-Maverick-17B-Instruct",
    "20250720_mini-v0.0.0_gpt-4.1-mini-2025-04-14",
]

# Sampled with a fixed stride rather than a random draw, so the corpus is reproducible.
TRAJECTORIES_PER_SUBMISSION = 120

BASH_BLOCK = re.compile(r"```bash\n(.*?)\n```", re.DOTALL)

# A command carrying any of these is either machine-specific or a credential someone pasted.
PRIVATE = re.compile(r"token|secret|passwd|password|api[_-]?key|Authorization|/Users/|/home/", re.IGNORECASE)

# Anything that reaches the network, installs software, or writes to a remote. The corpus is run
# against real bash in the differential test, so a line that is not hermetic is not usable.
NON_HERMETIC = re.compile(
    r"^\s*(sudo|apt|apt-get|yum|brew|docker|curl|wget|ssh|scp|systemctl|service)\b"
    r"|^\s*(pip|pip3|npm|yarn|pnpm|cargo|go|gem)\s+(install|add|get)\b"
    r"|^\s*git\s+(push|pull|fetch|clone|remote)\b"
)

MAXIMUM_COMMAND_LENGTH = 400


def list_keys(prefix):
    token, keys = None, []

    while True:
        url = f"{BUCKET_URL}/?list-type=2&max-keys=1000&prefix={urllib.parse.quote(prefix)}"
        if token:
            url += "&continuation-token=" + urllib.parse.quote(token)

        root = ElementTree.fromstring(urllib.request.urlopen(url, timeout=60).read())
        keys.extend(entry.find(S3_NAMESPACE + "Key").text for entry in root.findall(S3_NAMESPACE + "Contents"))

        continuation = root.find(S3_NAMESPACE + "NextContinuationToken")
        if continuation is None:
            return keys

        token = continuation.text


def read_trajectory(key):
    url = f"{BUCKET_URL}/{urllib.parse.quote(key)}"
    return json.loads(urllib.request.urlopen(url, timeout=60).read())


# Two shapes are in the bucket. mini-SWE-agent 1.x writes `.traj.json`, whose assistant messages
# carry the command inside a fenced bash block; 0.x writes `.traj`, which records the action
# already extracted. Both are read, because the older runs are the ones with the weaker models and
# so the messier commands.
def commands_in(trajectory):
    for step in trajectory.get("trajectory", []):
        action = step.get("action")
        if action:
            yield action.strip()

    for message in trajectory.get("messages", []):
        if message.get("role") != "assistant":
            continue

        for block in BASH_BLOCK.findall(str(message.get("content", ""))):
            yield block.strip()


def is_usable(command):
    return (
        command
        and not command.startswith("#")
        and "\n" not in command
        and len(command) <= MAXIMUM_COMMAND_LENGTH
        and not PRIVATE.search(command)
        and not NON_HERMETIC.search(command)
    )


def sample(keys, wanted):
    trajectories = sorted(key for key in keys if key.endswith((".traj", ".traj.json")))
    stride = max(1, len(trajectories) // wanted)
    return trajectories[::stride][:wanted]


def mine(submission):
    keys = sample(list_keys(f"bash-only/{submission}/trajs/"), TRAJECTORIES_PER_SUBMISSION)
    print(f"{submission}: {len(keys)} trajectories", file=sys.stderr)

    for index, key in enumerate(keys, start=1):
        if index % 20 == 0:
            print(f"  {index}/{len(keys)}", file=sys.stderr)

        try:
            trajectory = read_trajectory(key)
        except (urllib.error.URLError, json.JSONDecodeError, TimeoutError) as failure:
            print(f"  skipped {key}: {failure}", file=sys.stderr)
            continue

        yield from (command for command in commands_in(trajectory) if is_usable(command))


def header(count):
    mined_on = datetime.date.today().isoformat()
    return [
        "# Bash commands written by coding agents, mined from public SWE-bench trajectories.",
        "#",
        "# Source: the mini-SWE-agent submissions to the SWE-bench Verified leaderboard. The entries",
        "# live in github.com/SWE-bench/experiments under evaluation/verified; the trajectories they",
        "# name live in the public s3://swe-bench-submissions bucket under bash-only/. mini-SWE-agent",
        "# (github.com/SWE-agent/mini-swe-agent) is MIT-licensed and its whole action space is one",
        "# bash command per turn, which is why these trajectories and not another harness's.",
        "#",
        "# Submissions mined:",
        *[f"#   {submission}" for submission in SUBMISSIONS],
        "#",
        f"# Mined {mined_on} by build/mine-agent-corpus.py: {TRAJECTORIES_PER_SUBMISSION} trajectories per",
        "# submission at a fixed stride, then filtered for credentials and machine-specific paths,",
        "# filtered again for anything that installs software or reaches the network, deduplicated",
        f"# ordinally and sorted. {count} distinct commands.",
        "#",
        "# The commands are data, not instructions: nothing here is executed except through the",
        "# classification and differential tests, which run only the owned, non-mutating ones.",
        "",
    ]


def main():
    commands = set()
    for submission in SUBMISSIONS:
        commands.update(mine(submission))

    ordered = sorted(commands)
    CORPUS_PATH.parent.mkdir(parents=True, exist_ok=True)
    CORPUS_PATH.write_text("\n".join([*header(len(ordered)), *ordered, ""]), encoding="utf-8")

    size = CORPUS_PATH.stat().st_size
    print(f"wrote {len(ordered)} commands to {CORPUS_PATH} ({size // 1024} KB)", file=sys.stderr)


if __name__ == "__main__":
    main()
