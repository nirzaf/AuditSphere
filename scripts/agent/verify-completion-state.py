#!/usr/bin/env python3
"""Validate and seed the autonomous-completion run state (RUN-002).

Usage:
  verify-completion-state.py --state .local/autonomous/state.json
  verify-completion-state.py --result last-result.json --evidence-root .local/autonomous
  verify-completion-state.py init --state .local/autonomous/state.json \
      --spec docs/architecture/auditsphere-autonomous-completion-remediation-spec.md
  verify-completion-state.py select --state .local/autonomous/state.json

Exit codes: 0 valid; 1 invalid state/result; 2 usage or environment error.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import completion_state as cs  # noqa: E402

# Acceptance-only gates known from the specification (section 8 / 8.1). They never block implementation.
EXTERNAL_ACCEPTANCE = {
    "WU-012": ["GATE-PROFESSIONAL-METHOD"],
    "WU-014": ["GATE-D1-TERMINAL-POLICY"],
    **{f"WU-0{n}": ["GATE-M365-LIVE-TARGET"] for n in range(18, 26)},
    "WU-037": ["GATE-HUMAN-AT"],
    "WU-038": ["GATE-OPERATIONS-TARGETS"],
    "WU-040": ["GATE-PRODUCTION-AUTHORIZATION"],
}


def now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds")


def git(*args: str) -> str:
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True).stdout.strip()


def cmd_validate_state(state_path: Path) -> int:
    try:
        state = cs.load_json(state_path)
        errors = cs.check_state(state)
    except cs.StateError as exc:
        print(f"invalid: {exc}", file=sys.stderr)
        return 1
    if errors:
        print("invalid state:\n  " + "\n  ".join(errors), file=sys.stderr)
        return 1
    print(json.dumps({"valid": True, "tasks": len(state["tasks"]), **cs.select_next(state)}))
    return 0


def cmd_validate_result(result_path: Path, evidence_root: Path) -> int:
    try:
        errors = cs.check_result(cs.load_json(result_path), evidence_root)
    except cs.StateError as exc:
        print(f"invalid: {exc}", file=sys.stderr)
        return 1
    if errors:
        print("invalid result:\n  " + "\n  ".join(errors), file=sys.stderr)
        return 1
    print(json.dumps({"valid": True}))
    return 0


def cmd_init(state_path: Path, spec_path: Path, force: bool) -> int:
    if state_path.exists() and not force:
        print(f"{state_path} exists; use --force to reseed", file=sys.stderr)
        return 2
    units = cs.parse_spec_units(spec_path.read_text(encoding="utf-8"))
    if not units:
        print("no RUN/WU units found in the specification", file=sys.stderr)
        return 2
    head = git("rev-parse", "HEAD")
    baseline = cs.SHA.search(spec_path.read_text(encoding="utf-8").split("**Reviewed HEAD:**", 1)[-1][:200])
    state = {
        "schemaVersion": 1, "repository": cs.REPOSITORY,
        "baselineSha": baseline.group(0) if baseline else head, "currentSha": head,
        "runId": f"run-{dt.datetime.now(dt.timezone.utc):%Y%m%dT%H%M%SZ}",
        "activeTaskId": None, "worktreeFingerprint": git("rev-parse", "HEAD^{tree}"),
        "updatedAtUtc": now(), "nextAction": "NEXT_READY_TASK",
        "ci": {"status": "NOT_DISPATCHED"},
        "budgets": {"maxSessionSeconds": 5400, "maxSessionsPerDay": 12, "noProgressLimit": 3,
                    "sessionsToday": 0, "budgetDay": dt.date.today().isoformat(), "consecutiveNoProgress": 0},
        "session": {"id": None, "taskId": None, "startedAtUtc": None, "status": "IDLE"},
        "tasks": cs.seed_tasks(units, EXTERNAL_ACCEPTANCE),
        "externalGates": [],
    }
    errors = cs.check_state(state)
    if errors:
        print("seeded state invalid:\n  " + "\n  ".join(errors), file=sys.stderr)
        return 1
    state["nextAction"] = cs.select_next(state)["nextAction"]
    with cs.single_writer(state_path):
        cs.atomic_write_json(state_path, state)
    print(json.dumps({"seeded": len(state["tasks"]), "state": str(state_path)}))
    return 0


def cmd_select(state_path: Path) -> int:
    state = cs.load_json(state_path)
    errors = cs.check_state(state)
    if errors:
        print("invalid state:\n  " + "\n  ".join(errors), file=sys.stderr)
        return 1
    print(json.dumps(cs.select_next(state)))
    return 0


def main(argv: list[str] | None = None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    if argv and argv[0] in ("init", "select"):
        sub = argparse.ArgumentParser(prog=f"verify-completion-state.py {argv[0]}")
        sub.add_argument("--state", type=Path, required=True)
        if argv[0] == "init":
            sub.add_argument("--spec", type=Path, required=True)
            sub.add_argument("--force", action="store_true")
        args = sub.parse_args(argv[1:])
        try:
            return cmd_init(args.state, args.spec, args.force) if argv[0] == "init" else cmd_select(args.state)
        except cs.StateError as exc:
            print(f"error: {exc}", file=sys.stderr)
            return 1
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--state", type=Path)
    parser.add_argument("--result", type=Path)
    parser.add_argument("--evidence-root", type=Path, default=Path(".local/autonomous"))
    args = parser.parse_args(argv)
    if not args.state and not args.result:
        parser.print_usage(sys.stderr)
        return 2
    code = 0
    if args.state:
        code |= cmd_validate_state(args.state)
    if args.result:
        code |= cmd_validate_result(args.result, args.evidence_root)
    return code


if __name__ == "__main__":
    sys.exit(main())
