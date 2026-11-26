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
import copy
import contextlib
import datetime as dt
import json
import os
import re
import signal
import shutil
import socket
import stat
import subprocess
import sys
import time
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


def owner_now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="microseconds").replace("+00:00", "Z")


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


def canonical_repo_and_state(args) -> tuple[Path, Path]:
    candidate_repo = args.repo.resolve()
    try:
        root = subprocess.run(["git", "-C", str(candidate_repo), "rev-parse", "--show-toplevel"],
                              check=True, capture_output=True, text=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError) as exc:
        raise cs.StateError(f"{candidate_repo}: cannot resolve the canonical Git repository root") from exc
    repo = Path(root).resolve(strict=True)
    state_path = args.state if args.state.is_absolute() else repo / args.state
    return repo, state_path.resolve(strict=False)


def _owner_identity(repo: Path, state_path: Path, task_id: str, session_id: str | None) -> dict:
    created = owner_now()
    owner = {"schemaVersion": 1, "repoPath": str(repo), "originatingStatePath": str(state_path),
             "taskId": task_id, "sessionId": session_id, "host": socket.gethostname(),
             "supervisorPid": os.getpid(), "runnerPid": None, "processGroupId": None,
             "phase": "DISPATCHING", "needsReconciliation": True,
             "createdAtUtc": created, "updatedAtUtc": created}
    errors = cs.validate_runner_owner(owner, repo=repo)
    if errors:
        raise cs.StateError("cannot create runner owner: " + "; ".join(errors))
    return owner


def _owner_paths(owner: dict) -> tuple[Path, Path]:
    created = re.sub(r"[^0-9TZ]", "", owner["createdAtUtc"])
    token = f"{created}-{owner['supervisorPid']}"
    root = Path(owner["originatingStatePath"]).parent
    return (root / "results" / f"{owner['taskId']}-{token}.json",
            root / "events" / f"{owner['taskId']}-{token}.jsonl")


def _owner_stderr_path(owner: dict) -> Path:
    _, event_path = _owner_paths(owner)
    return event_path.with_suffix(".stderr.txt")


def _save_owner(repo: Path, owner: dict, **changes) -> None:
    owner.update(changes)
    owner["updatedAtUtc"] = owner_now()
    cs.save_runner_owner(repo, owner)


def _write_event(path: Path, stdout: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    flags = os.O_CREAT | os.O_EXCL | os.O_WRONLY | getattr(os, "O_NOFOLLOW", 0)
    try:
        fd = os.open(path, flags, 0o600)
    except FileExistsError as exc:
        raise cs.StateError(f"{path}: refusing to replace retained runner event evidence") from exc
    try:
        payload = stdout.encode("utf-8", errors="replace")
        with os.fdopen(fd, "wb") as handle:
            handle.write(payload)
            handle.flush()
            os.fsync(handle.fileno())
    except BaseException:
        raise


def _event_session_id(path: Path) -> str | None:
    try:
        info = path.lstat()
    except FileNotFoundError:
        return None
    if not stat.S_ISREG(info.st_mode) or info.st_size > cs.MAX_EVIDENCE_BYTES:
        return None
    try:
        return session_id_from_events(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError):
        return None


def _load_exact_result(path: Path, evidence_root: Path, task_id: str, current_sha: str):
    try:
        info = path.lstat()
    except FileNotFoundError:
        return None, None, ["result file is missing"]
    if not stat.S_ISREG(info.st_mode):
        return None, None, ["result path is not a regular file"]
    captured = {}
    try:
        result = cs.load_json(path)
    except (cs.StateError, OSError) as exc:
        return None, None, [str(exc)]
    errors = cs.check_result(result, evidence_root, captured)
    if not errors and result["taskId"] != task_id:
        errors.append(f"result is for {result['taskId']}, not the dispatched {task_id}")
    if not errors and result["candidateSha"] != current_sha:
        errors.append("result candidateSha does not match the current working tree HEAD")
    return result, captured, errors


def _result_matches_completed_checkpoint(state: dict, owner: dict, result: dict, captured: dict,
                                         effective_session: str | None) -> bool:
    task_id = owner["taskId"]
    session = state.get("session", {})
    task = state.get("tasks", {}).get(task_id, {})
    expected_local = result["verificationStatus"]
    if expected_local == "RUNNING":
        expected_local = None  # apply_result deliberately retains the prior local verification.
    expected_scheduling = VERIFICATION_TO_SCHEDULING[result["verificationStatus"]]
    if expected_scheduling == "ENGINEERING_VERIFIED" and result["implementationStatus"] != "PRESENT":
        expected_scheduling = "READY"
    if result["verificationStatus"] == "FAIL":
        expected_scheduling = "BLOCKED_DEPENDENCY" if task.get("repairAttempts", 0) >= 3 else "REPAIR_REQUIRED"
    if not (state.get("activeTaskId") is None and session.get("status") == "COMPLETED"
            and session.get("taskId") == task_id and session.get("id") == effective_session
            and state.get("currentSha") == result["candidateSha"]
            and task.get("implementation") == result["implementationStatus"]
            and task.get("scheduling") == expected_scheduling
            and {gate["id"] for gate in result["externalGates"]}.issubset(
                {gate["id"] for gate in state.get("externalGates", [])})
            and set(result["evidencePaths"]).issubset(set(task.get("evidencePaths", [])))):
        return False
    if expected_local is not None and task.get("localVerification") != expected_local:
        return False
    digests = task.get("evidenceDigests", {})
    return all(digests.get(path) == digest for path, digest in captured.items())


def _mark_interrupted(state: dict, task_id: str, session_id: str | None) -> None:
    session = state["session"]
    session.update({"id": session_id, "taskId": task_id, "status": "INTERRUPTED"})
    state["activeTaskId"] = task_id
    state["budgets"]["consecutiveNoProgress"] = state["budgets"].get("consecutiveNoProgress", 0) + 1
    state["nextAction"] = "RESUME_REQUIRED"


def _apply_exact_result(state: dict, owner: dict, result: dict, captured: dict, session_id: str | None) -> None:
    task_id = owner["taskId"]
    progressed = apply_result(state, task_id, result)
    state["tasks"][task_id].setdefault("evidenceDigests", {}).update(captured)
    state["budgets"]["consecutiveNoProgress"] = 0 if progressed else state["budgets"].get("consecutiveNoProgress", 0) + 1
    state["currentSha"] = result["candidateSha"]
    # Retain this completed identity as a crash-recovery marker until the owner record is terminal.
    state["session"].update({"status": "COMPLETED", "taskId": task_id, "id": session_id})
    state["activeTaskId"] = None
    state["nextAction"] = cs.select_next(state)["nextAction"]


def _owner_processes_stopped(owner: dict) -> bool:
    checks = {"supervisor": cs.process_state(owner["supervisorPid"]),
              "runner": cs.process_state(owner["runnerPid"]),
              "processGroup": cs.process_group_state(owner["processGroupId"])}
    if any(state != "DEAD" for state in checks.values()):
        raise cs.StateError("runner owner still has live or uninspectable processes: " + json.dumps(checks))
    return True


def reconcile_owner(repo: Path, state_path: Path, owner: dict) -> None:
    """Reconcile only the dead, exact owner invocation; never infer dispatch from a checkpoint."""
    original = Path(owner["originatingStatePath"])
    if original != state_path:
        raise cs.StateError(f"unreconciled runner belongs to {original}; use that exact state path")
    if owner["host"] != socket.gethostname():
        raise cs.StateError(f"unreconciled runner belongs to foreign host {owner['host']}")
    if owner["phase"] == "DISPATCHING":
        raise cs.StateError("runner dispatch outcome is unknown (DISPATCHING has no durable process identity)")
    if owner["runnerPid"] is None or owner["processGroupId"] is None:
        raise cs.StateError("runner owner has no complete process identity; outcome is unknown")
    _owner_processes_stopped(owner)

    result_path, event_path = _owner_paths(owner)
    event_session = _event_session_id(event_path)
    if owner.get("sessionId") and event_session and owner["sessionId"] != event_session:
        raise cs.StateError("runner event session identity conflicts with its durable owner")
    effective_session = owner.get("sessionId") or event_session
    task_id = owner["taskId"]
    with cs.single_writer(state_path):
        state = load_state(repo, state_path)
        head = git_head(repo)
        session = state.get("session", {})
        if (task_id not in state["tasks"] or session.get("taskId") != task_id
                or (session.get("status") != "COMPLETED" and state.get("activeTaskId") != task_id)):
            raise cs.StateError("runner owner and originating checkpoint task identity disagree")
        if session.get("status") not in ("RUNNING", "INTERRUPTED", "COMPLETED"):
            raise cs.StateError("runner owner and originating checkpoint status disagree")
        if session.get("id") and effective_session and session["id"] not in (effective_session, owner.get("sessionId")):
            raise cs.StateError("runner session identity conflicts with its originating checkpoint")

        result, captured, result_errors = _load_exact_result(result_path, state_path.parent, task_id, head)
        if result is not None and result_errors == []:
            if session.get("status") == "COMPLETED":
                if not _result_matches_completed_checkpoint(state, owner, result, captured, effective_session):
                    raise cs.StateError("completed checkpoint does not match the exact retained runner result")
            else:
                _apply_exact_result(state, owner, result, captured, effective_session)
                save(state_path, state)
        elif any("current working tree HEAD" in error for error in result_errors):
            raise cs.StateError("retained runner result is for a different source revision; inspect before resuming")
        elif session.get("status") == "COMPLETED":
            raise cs.StateError("completed checkpoint has no valid exact runner result")
        else:
            if not effective_session:
                raise cs.StateError("runner outcome and session identity are unknown; refusing fresh dispatch")
            if session.get("status") == "INTERRUPTED":
                if session.get("id") != effective_session:
                    raise cs.StateError("interrupted checkpoint does not match the proven runner session")
            else:
                _mark_interrupted(state, task_id, effective_session)
            save(state_path, state)

    _save_owner(repo, owner, phase="STOPPED", needsReconciliation=False,
                sessionId=effective_session)


def _terminate_group(proc: subprocess.Popen, process_group_id: int) -> bool:
    if process_group_id != proc.pid or process_group_id <= 1:
        return False

    def wait_gone(seconds: float) -> str:
        deadline = time.monotonic() + seconds
        while True:
            state = cs.process_group_state(process_group_id)
            if state != "ALIVE":
                return state
            if time.monotonic() >= deadline:
                return state
            time.sleep(0.05)

    state = cs.process_group_state(process_group_id)
    if state == "DEAD":
        with contextlib.suppress(subprocess.TimeoutExpired):
            proc.wait(timeout=0.1)
        return cs.process_group_state(process_group_id) == "DEAD"
    if state != "ALIVE":
        return False
    try:
        os.killpg(process_group_id, signal.SIGTERM)
    except ProcessLookupError:
        pass
    except OSError:
        return False
    state = wait_gone(2.0)
    if state == "ALIVE":
        try:
            os.killpg(process_group_id, signal.SIGKILL)
        except ProcessLookupError:
            pass
        except OSError:
            return False
        state = wait_gone(2.0)
    try:
        proc.wait(timeout=0.2)
    except subprocess.TimeoutExpired:
        with contextlib.suppress(OSError):
            os.killpg(process_group_id, signal.SIGKILL)
        try:
            proc.wait(timeout=1.0)
        except subprocess.TimeoutExpired:
            return False
    return state == "DEAD" and cs.process_group_state(process_group_id) == "DEAD"


def _dispatch_process(argv: list[str], repo: Path) -> subprocess.Popen:
    return subprocess.Popen(argv, cwd=repo, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            text=False, start_new_session=True)


def _run_locked(args, repo: Path, state_path: Path, resume_only: bool) -> int:
    verify_args = copy.copy(args)
    verify_args.repo, verify_args.state = repo, state_path
    owner = cs.load_runner_owner(repo)
    if owner and owner["host"] != socket.gethostname():
        raise cs.StateError(f"runner owner belongs to foreign host {owner['host']}")
    if owner and owner["needsReconciliation"]:
        reconcile_owner(repo, state_path, owner)
        owner = cs.load_runner_owner(repo)

    with cs.single_writer(state_path):
        state = load_state(repo, state_path)
        session = state.setdefault("session", {"id": None, "taskId": None, "startedAtUtc": None, "status": "IDLE"})
        if session.get("status") in ("RUNNING", "INTERRUPTED"):
            if owner is None:
                raise cs.StateError("legacy nonterminal session has no trustworthy runner owner; inspect before resuming")
            if (owner["originatingStatePath"] != str(state_path) or owner["taskId"] != session.get("taskId")
                    or owner.get("sessionId") != session.get("id") or owner["phase"] != "STOPPED"
                    or owner["needsReconciliation"] or session.get("status") == "RUNNING"):
                raise cs.StateError("nonterminal checkpoint does not match a reconciled runner owner")

        head = git_head(repo)
        roll_budget(state)
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
                return cmd_verify(verify_args)
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
                    return cmd_verify(verify_args)
                print(json.dumps({"exit": code, "nextAction": choice["nextAction"]}))
                return code

        owner = _owner_identity(repo, state_path, task_id, resume_id)
        result_path, event_path = _owner_paths(owner)
        if result_path.exists() or event_path.exists() or _owner_stderr_path(owner).exists():
            # Avoid reusing output from any prior invocation, even if its checkpoint is damaged.
            created = dt.datetime.fromisoformat(owner["createdAtUtc"].replace("Z", "+00:00"))
            for _ in range(1000):
                created += dt.timedelta(microseconds=1)
                owner["createdAtUtc"] = created.isoformat(timespec="microseconds").replace("+00:00", "Z")
                owner["updatedAtUtc"] = owner["createdAtUtc"]
                result_path, event_path = _owner_paths(owner)
                if not result_path.exists() and not event_path.exists() and not _owner_stderr_path(owner).exists():
                    break
            else:
                raise cs.StateError("could not allocate a unique runner result identity")
        result_path.parent.mkdir(parents=True, exist_ok=True)
        cs.save_runner_owner(repo, owner)  # durable DISPATCHING fence before the checkpoint or Popen.
        state["activeTaskId"] = task_id
        state["currentSha"] = head
        session.update({"taskId": task_id, "status": "RUNNING", "startedAtUtc": now(), "id": resume_id})
        state["budgets"]["sessionsToday"] = state["budgets"].get("sessionsToday", 0) + 1
        save(state_path, state)

    argv = runner_argv(args.runner, args.profile, repo, task_id, result_path, resume_id)
    try:
        proc = _dispatch_process(argv, repo)
    except OSError as exc:
        # Popen can fail before returning an identity. DISPATCHING remains fail-closed because
        # an interrupted supervisor cannot distinguish this from a crash in the spawn boundary.
        print(json.dumps({"exit": 20, "taskId": task_id, "reason": f"runner dispatch outcome is unknown: {exc}"}))
        return 20

    try:
        _save_owner(repo, owner, phase="RUNNING", runnerPid=proc.pid, processGroupId=proc.pid)
    except (cs.StateError, OSError) as exc:
        stopped = _terminate_group(proc, proc.pid)
        print(json.dumps({"exit": 20, "taskId": task_id, "reason": str(exc), "processGroupStopped": stopped}))
        return 20

    max_seconds = state["budgets"].get("maxSessionSeconds", 5400)
    timed_out = False
    output_error = None
    try:
        stdout_bytes, stderr_bytes = proc.communicate(timeout=max_seconds)
    except subprocess.TimeoutExpired:
        timed_out = True
        stdout_bytes, stderr_bytes = b"", b""
    except BaseException as exc:
        output_error = f"runner wait failed: {exc}"
        stdout_bytes, stderr_bytes = b"", b""

    stopped = _terminate_group(proc, proc.pid)
    if not stopped:
        print(json.dumps({"exit": 20, "taskId": task_id,
                          "reason": "runner process group could not be proven stopped; reconciliation is required"}))
        return 20
    if timed_out or output_error:
        try:
            stdout_bytes, stderr_bytes = proc.communicate(timeout=2.0)
        except (subprocess.TimeoutExpired, OSError):
            output_error = output_error or "timed-out runner output could not be collected"
    stdout = (stdout_bytes or b"").decode(errors="replace")
    event_error = None
    try:
        _write_event(event_path, stdout)
        _write_event(_owner_stderr_path(owner), (stderr_bytes or b"").decode(errors="replace"))
    except (OSError, cs.StateError) as exc:
        event_error = f"could not retain runner event output: {exc}"
    actual_session = session_id_from_events(stdout) or owner.get("sessionId")
    if event_error or output_error:
        print(json.dumps({"exit": 20, "taskId": task_id, "reason": event_error or output_error}))
        return 20

    _save_owner(repo, owner, phase="STOPPED", needsReconciliation=True, sessionId=actual_session)
    returncode = -1 if timed_out else proc.returncode
    with cs.single_writer(state_path):
        current = load_state(repo, state_path)
        current_session = current.get("session", {})
        if (current.get("activeTaskId") != task_id or current_session.get("status") != "RUNNING"
                or current_session.get("taskId") != task_id):
            print(json.dumps({"exit": 20, "taskId": task_id,
                              "reason": "originating checkpoint changed while the runner was active"}))
            return 20
        effective_session = actual_session
        if returncode != 0 or not result_path.exists():
            if not effective_session:
                print(json.dumps({"exit": 20, "taskId": task_id,
                                  "reason": "runner stopped without a result or known session identity"}))
                return 20
            _mark_interrupted(current, task_id, effective_session)
            save(state_path, current)
            _save_owner(repo, owner, phase="STOPPED", needsReconciliation=False,
                        sessionId=effective_session)
            code = 14 if budget_exhausted(current) else 10
            print(json.dumps({"exit": code, "taskId": task_id,
                              "reason": "runner interrupted or produced no result", "sessionId": effective_session}))
            return code

        result, captured, errors = _load_exact_result(result_path, state_path.parent, task_id, git_head(repo))
        if errors:
            if any("current working tree HEAD" in error for error in errors):
                print(json.dumps({"exit": 20, "taskId": task_id, "errors": errors}))
                return 20
            if not effective_session:
                print(json.dumps({"exit": 20, "taskId": task_id,
                                  "errors": errors + ["runner session identity is unknown"]}))
                return 20
            _mark_interrupted(current, task_id, effective_session)
            save(state_path, current)
            _save_owner(repo, owner, phase="STOPPED", needsReconciliation=False,
                        sessionId=effective_session)
            print(json.dumps({"exit": 20, "taskId": task_id, "errors": errors}))
            return 20

        _apply_exact_result(current, owner, result, captured, effective_session)
        save(state_path, current)
        _save_owner(repo, owner, phase="STOPPED", needsReconciliation=False,
                    sessionId=effective_session)
        code = exit_for(current)
        if code == 0:
            return cmd_verify(verify_args)
        print(json.dumps({"exit": code, "taskId": task_id, "nextAction": current["nextAction"]}))
        return code


def cmd_run(args, resume_only: bool = False) -> int:
    if args.runner == "codex" and not shutil.which("codex"):
        print(json.dumps({"exit": 13, "reason": "codex CLI not installed on this runner"}))
        return 13
    repo, state_path = canonical_repo_and_state(args)
    try:
        with cs.modifying_runner_lock(repo):
            return _run_locked(args, repo, state_path, resume_only)
    except cs.RunnerUnavailable as exc:
        print(json.dumps({"exit": 13, "reason": str(exc)}))
        return 13
    except OSError as exc:
        print(json.dumps({"exit": 20, "error": f"runner state or evidence I/O failed: {exc}"}))
        return 20


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
