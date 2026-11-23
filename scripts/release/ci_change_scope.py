#!/usr/bin/env python3
"""Select fast CI paths while keeping every unapproved or ambiguous diff fail-closed."""

from __future__ import annotations

import os
import subprocess
from pathlib import Path, PurePosixPath


PG19_PREFLIGHT_PATHS = frozenset(
    {
        "scripts/db/postgresql_major_upgrade_preflight.py",
        "scripts/db/tests/test_postgresql_major_upgrade_preflight.py",
    }
)


def classify_paths(paths: list[str], *, preflight_files_present: bool = True) -> str:
    """Classify only verified documentation or PG19-preflight-only diffs as narrow."""
    if not paths:
        return "application-paths"

    has_pg19_preflight = False
    for raw_path in paths:
        path = PurePosixPath(raw_path)
        if path.is_absolute() or ".." in path.parts or "\\" in raw_path:
            return "application-paths"
        if raw_path == "docs/execution/status.json":
            continue
        if raw_path in PG19_PREFLIGHT_PATHS:
            has_pg19_preflight = True
            continue
        docs_markdown = path.suffix.lower() == ".md" and (
            raw_path.startswith("docs/") or path.parts[:2] == ("infra", "hostinger-acceptance")
        )
        if not docs_markdown:
            return "application-paths"
    if has_pg19_preflight:
        return "pg19-preflight-only" if preflight_files_present else "application-paths"
    return "docs-content-only"


def requires_application_checks(paths: list[str]) -> bool:
    """Keep application builds for all changes outside the explicitly narrow paths."""
    return classify_paths(paths) == "application-paths"


def _set_output(full_build: bool, reason: str) -> None:
    output_path = os.environ.get("GITHUB_OUTPUT")
    if not output_path:
        raise RuntimeError("GITHUB_OUTPUT is required")
    with open(output_path, "a", encoding="utf-8") as output:
        output.write(f"full_build={'true' if full_build else 'false'}\n")
        output.write(f"classification={reason}\n")


def main() -> int:
    event_name = os.environ.get("EVENT_NAME", "")
    ref_name = os.environ.get("REF_NAME", "")
    before = os.environ.get("BEFORE_SHA", "")
    head = os.environ.get("HEAD_SHA", "")

    # PRs and operator-dispatched checks always run the full application gate.
    if event_name != "push" or ref_name != "refs/heads/master":
        _set_output(True, "full-event")
        print("Running full application checks for this event.")
        return 0

    if len(before) != 40 or set(before) == {"0"} or len(head) != 40:
        _set_output(True, "missing-or-invalid-sha")
        print("Could not establish a valid push range; running full application checks.")
        return 0

    try:
        exists = subprocess.run(
            ["git", "cat-file", "-e", f"{before}^{{commit}}"],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        ).returncode == 0
        if not exists:
            fetch = subprocess.run(
                ["git", "fetch", "--no-tags", "--depth=1", "origin", before],
                check=False,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            if fetch.returncode != 0:
                raise RuntimeError("push base commit could not be fetched")

        result = subprocess.run(
            ["git", "diff", "--no-renames", "--name-only", "-z", before, head],
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        paths = [item.decode("utf-8", errors="strict") for item in result.stdout.split(b"\0") if item]
    except (OSError, subprocess.SubprocessError, UnicodeDecodeError, RuntimeError) as error:
        _set_output(True, "diff-unavailable")
        print(f"Could not classify the push diff ({error}); running full application checks.")
        return 0

    preflight_files_present = all(path.is_file() for path in map(Path, PG19_PREFLIGHT_PATHS))
    reason = classify_paths(paths, preflight_files_present=preflight_files_present)
    full_build = reason == "application-paths"
    _set_output(full_build, reason)
    print(f"CI classification: {reason}; changed paths: {len(paths)}.")
    if not paths:
        print("An empty diff fails closed to full application checks.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
