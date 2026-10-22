#!/usr/bin/env python3
"""Bounded, resumable supervisor for the autonomous-completion assignment (RUN-008).

Developer tooling only: one modifying agent process at a time, standard library, no service,
database or broker. The model's text is advisory; transitions come from the validated result
file, the working tree and exact-source CI evidence.

  complete.py init    --repo . --spec SPEC --state STATE
  complete.py next    --repo . --state STATE
  complete.py run     --repo . --state STATE [--runner codex|PATH] [--profile auditsphere-completion]
  complete.py resume  --repo . --state STATE [--runner ...]
  complete.py status  --repo . --state STATE
  complete.py verify  --repo . --spec SPEC --state STATE

Exit codes (spec 2.5): 0 engineering completion function passed; 10 ready/repair work remains;
11 required CI queued/running; 12 only external prerequisites remain; 13 runner prerequisite
unavailable; 14 budget or no-progress bound reached; 20 corrupt state, wrong source or violated invariant.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import completion_state as cs  # noqa: E402

PROMPT = ("Read AGENTS.md and the completion checkpoint. Execute only task {task}; follow "
          "docs/architecture/auditsphere-autonomous-completion-remediation-spec.md. Write the result JSON "
          "required by scripts/agent/completion-result.schema.json.")
VERIFICATION_TO_SCHEDULING = {"PASS": "ENGINEERING_VERIFIED", "FAIL": "REPAIR_REQUIRED", "RUNNING": "WAITING_CI",
                              "BLOCKED_ENVIRONMENT": "BLOCKED_ENVIRONMENT", "NOT_RUN": "READY"}


def now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds")


def git_head(repo: Path) -> str:
    return subprocess.run(["git", "-C", str(repo), "rev-parse", "HEAD"], check=True, capture_output=True, text=True).stdout.strip()


def load_state(repo: Path, state_path: Path) -> dict:
    state = cs.load_json(state_path)
    errors = cs.check_state(state)
    if errors:
        raise cs.StateError("; ".join(errors))
    return state


def save(state_path: Path, state: dict) -> None:
    state["updatedAtUtc"] = now()
    errors = cs.check_state(state)
    if errors:
        raise cs.StateError("refusing to persist invalid state: " + "; ".join(errors))
    cs.atomic_write_json(state_path, state)


def exit_for(state: dict) -> int:
    action = cs.select_next(state)["nextAction"]
    if action == "VERIFY_ENGINEERING_COMPLETION":
        return 0
    return {"POLL_CI": 11, "WAITING_EXTERNAL": 12}.get(action, 10)


def roll_budget(state: dict) -> None:
    budgets = state.setdefault("budgets", {})
    today = dt.date.today().isoformat()
    if budgets.get("budgetDay") != today:
        budgets["budgetDay"] = today
        budgets["sessionsToday"] = 0


def budget_exhausted(state: dict) -> str | None:
    b = state.get("budgets", {})
    if b.get("sessionsToday", 0) >= b.get("maxSessionsPerDay", 12):
        return "daily session budget reached"
    if b.get("consecutiveNoProgress", 0) >= b.get("noProgressLimit", 3):
        return "no-progress bound reached"
    return None


def runner_argv(runner: str, profile: str, repo: Path, task_id: str, result_path: Path, session_id: str | None) -> list[str]:
    if runner == "codex":
        base = ["codex", "exec"]
        if session_id:
            return base + ["resume", session_id, PROMPT.format(task=task_id)]
        return base + ["--profile", profile, "--json", "-C", str(repo),
                       "--output-schema", str(repo / "scripts/agent/completion-result.schema.json"),
                       "--output-last-message", str(result_path), PROMPT.format(task=task_id)]
    argv = [runner, "--task", task_id, "--output", str(result_path)]
    return argv + (["--resume", session_id] if session_id else [])


def session_id_from_events(stdout: str) -> str | None:
    for line in stdout.splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        for key in ("thread_id", "session_id"):
            if isinstance(event, dict) and isinstance(event.get(key), str):
                return event[key]
    return None


def apply_result(state: dict, task_id: str, result: dict) -> bool:
    """Apply a validated result. Returns True when the task made observable progress."""
    task = state["tasks"][task_id]
    before = (task["implementation"], task["localVerification"], task["scheduling"], tuple(task["evidencePaths"]))
    task["implementation"] = result["implementationStatus"]
    verification = result["verificationStatus"]
    task["localVerification"] = verification if verification != "RUNNING" else task["localVerification"]
    scheduling = VERIFICATION_TO_SCHEDULING[verification]
    if scheduling == "ENGINEERING_VERIFIED" and task["implementation"] != "PRESENT":
        scheduling = "READY"
    if scheduling == "REPAIR_REQUIRED":
        task["repairAttempts"] = task.get("repairAttempts", 0) + 1
        if task["repairAttempts"] >= 3:
            scheduling = "BLOCKED_DEPENDENCY"
            task["notes"] = (task["notes"] + " Escalated after three repair attempts without new evidence.").strip()[:2000]
    task["scheduling"] = scheduling
    task["evidencePaths"] = sorted(set(task["evidencePaths"]) | set(result["evidencePaths"]))
    known = {g["id"] for g in state["externalGates"]}
    state["externalGates"] += [g for g in result["externalGates"] if g["id"] not in known]
    after = (task["implementation"], task["localVerification"], task["scheduling"], tuple(task["evidencePaths"]))
    return before != after


def cmd_run(args, resume_only: bool = False) -> int:
    repo, state_path = args.repo.resolve(), args.state
    if args.runner == "codex" and not shutil.which("codex"):
        print(json.dumps({"exit": 13, "reason": "codex CLI not installed on this runner"}))
        return 13
    with cs.single_writer(state_path):
        state = load_state(repo, state_path)
        head = git_head(repo)
        roll_budget(state)
        session = state.setdefault("session", {"id": None, "taskId": None, "startedAtUtc": None, "status": "IDLE"})
        if session.get("status") == "RUNNING":
            session["status"] = "INTERRUPTED"  # previous process died after dispatch: reconcile, never replay blindly
        reason = budget_exhausted(state)
        if reason:
            state["nextAction"] = "RESUME_REQUIRED"
            save(state_path, state)
            print(json.dumps({"exit": 14, "reason": reason}))
            return 14
        if session.get("status") == "INTERRUPTED" and session.get("taskId"):
            task_id, resume_id = session["taskId"], session.get("id")
        elif resume_only:
            if exit_for(state) == 0:
                return cmd_verify(args)
            print(json.dumps({"exit": exit_for(state), "reason": "no interrupted session"}))
            return exit_for(state)
        else:
            choice = cs.select_next(state)
            task_id, resume_id = choice["taskId"], None
            if task_id is None or choice["nextAction"] not in ("IMPLEMENT", "REPAIR", "RUN_FOCUSED_CHECK"):
                state["nextAction"] = choice["nextAction"]
                save(state_path, state)
                code = exit_for(state)
                if code == 0:
                    return cmd_verify(args)
                print(json.dumps({"exit": code, "nextAction": choice["nextAction"]}))
                return code
        evidence_root = state_path.parent
        result_path = evidence_root / "results" / f"{task_id}.json"
        result_path.parent.mkdir(parents=True, exist_ok=True)
        result_path.unlink(missing_ok=True)
        state["activeTaskId"] = task_id
        state["currentSha"] = head
        session.update({"taskId": task_id, "status": "RUNNING", "startedAtUtc": now(), "id": resume_id})
        state["budgets"]["sessionsToday"] = state["budgets"].get("sessionsToday", 0) + 1
        save(state_path, state)  # persisted before dispatch so a crash is recoverable

    argv = runner_argv(args.runner, args.profile, repo, task_id, result_path, resume_id)
    try:
        proc = subprocess.run(argv, cwd=repo, capture_output=True, text=True,
                              timeout=state["budgets"].get("maxSessionSeconds", 5400))
        stdout, returncode = proc.stdout, proc.returncode
    except subprocess.TimeoutExpired as exc:
        stdout, returncode = (exc.stdout or b"").decode(errors="replace") if isinstance(exc.stdout, bytes) else (exc.stdout or ""), -1
    (evidence_root / "events").mkdir(exist_ok=True)
    (evidence_root / "events" / f"{task_id}-{dt.datetime.now(dt.timezone.utc):%Y%m%dT%H%M%S}.jsonl").write_text(stdout, encoding="utf-8")

    with cs.single_writer(state_path):
        state = load_state(repo, state_path)
        session = state["session"]
        session["id"] = session_id_from_events(stdout) or session.get("id")
        if returncode != 0 or not result_path.exists():
            session["status"] = "INTERRUPTED"
            state["budgets"]["consecutiveNoProgress"] = state["budgets"].get("consecutiveNoProgress", 0) + 1
            state["nextAction"] = "RESUME_REQUIRED"
            save(state_path, state)
            code = 14 if budget_exhausted(state) else 10
            print(json.dumps({"exit": code, "taskId": task_id, "reason": "runner interrupted or produced no result",
                              "sessionId": session["id"]}))
            return code
        captured = {}
        try:
            result = cs.load_json(result_path)
        except cs.StateError as exc:
            result, errors = None, [str(exc)]
        else:
            errors = cs.check_result(result, evidence_root, captured)
        if not errors and result["taskId"] != task_id:
            errors.append(f"result is for {result['taskId']}, not the dispatched {task_id}")
        if not errors and result["candidateSha"] != git_head(repo):
            errors.append("result candidateSha does not match the working tree HEAD")
        if errors:
            session["status"] = "INTERRUPTED"
            state["nextAction"] = "RESUME_REQUIRED"
            save(state_path, state)
            print(json.dumps({"exit": 20, "taskId": task_id, "errors": errors}))
            return 20
        # Retain the hashes of the exact bytes validated above, never model-supplied digests.
        progressed = apply_result(state, task_id, result)
        state["tasks"][task_id].setdefault("evidenceDigests", {}).update(captured)
        state["budgets"]["consecutiveNoProgress"] = 0 if progressed else state["budgets"].get("consecutiveNoProgress", 0) + 1
        state["currentSha"] = result["candidateSha"]
        session.update({"status": "COMPLETED", "taskId": None, "id": None})
        state["activeTaskId"] = None
        state["nextAction"] = cs.select_next(state)["nextAction"]
        save(state_path, state)
        code = exit_for(state)
        if code == 0:
            return cmd_verify(args)
        print(json.dumps({"exit": code, "taskId": task_id, "nextAction": state["nextAction"]}))
        return code


def cmd_verify(args) -> int:
    state = load_state(args.repo, args.state)
    if state["currentSha"] != git_head(args.repo):
        print(json.dumps({"exit": 20, "reason": "state currentSha differs from working tree HEAD"}))
        return 20
    evidence_errors = cs.check_retained_evidence(state, args.state.parent)
    if evidence_errors:
        print(json.dumps({"exit": 20, "errors": evidence_errors}))
        return 20
    if cs.engineering_complete(state):
        gates = [g["id"] for g in state["externalGates"]] or sorted(
            {d for t in state["tasks"].values() for d in t["acceptanceDependencies"] if not cs.TASK_ID.match(d)})
        verdict = "ENGINEERING_COMPLETE_WITH_EXTERNAL_GATES" if gates else "ENGINEERING_COMPLETE"
        print(json.dumps({"exit": 0, "verdict": verdict, "externalGates": gates, "candidateSha": state["currentSha"]}))
        return 0
    code = exit_for(state)
    print(json.dumps({"exit": code if code else 10, "verdict": "NOT_COMPLETE", **cs.select_next(state)}))
    return code if code else 10


def cmd_status(args) -> int:
    state = load_state(args.repo, args.state)
    counts: dict[str, int] = {}
    for task in state["tasks"].values():
        counts[task["scheduling"]] = counts.get(task["scheduling"], 0) + 1
    print(json.dumps({"currentSha": state["currentSha"], "ci": state.get("ci"), "scheduling": counts,
                      "session": state.get("session"), **cs.select_next(state)}, indent=2))
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["init", "next", "run", "resume", "status", "verify"])
    parser.add_argument("--repo", type=Path, default=Path("."))
    parser.add_argument("--spec", type=Path)
    parser.add_argument("--state", type=Path, default=Path(".local/autonomous/state.json"))
    parser.add_argument("--runner", default="codex", help="'codex' or a path to a compatible runner executable")
    parser.add_argument("--profile", default="auditsphere-completion")
    args = parser.parse_args(argv)
    try:
        if args.command == "init":
            if not args.spec:
                parser.error("init requires --spec")
            verifier = Path(__file__).resolve().parent / "verify-completion-state.py"
            return subprocess.run([sys.executable, str(verifier), "init", "--state", str(args.state), "--spec", str(args.spec)]).returncode
        if args.command == "next":
            print(json.dumps(cs.select_next(load_state(args.repo, args.state))))
            return 0
        if args.command == "status":
            return cmd_status(args)
        if args.command == "verify":
            return cmd_verify(args)
        return cmd_run(args, resume_only=args.command == "resume")
    except cs.StateError as exc:
        print(json.dumps({"exit": 20, "error": str(exc)}))
        return 20


if __name__ == "__main__":
    sys.exit(main())
