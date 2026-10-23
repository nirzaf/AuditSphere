#!/usr/bin/env python3
"""Observe, dispatch and wait for exact-candidate release verification (RUN-003).

Commands (JSON on stdout, never interactive):
  observe  --repo R --run-id N [--expected-sha S] [--output FILE]
  dispatch --repo R --candidate S [--workflow release-verification.yml] [--state FILE]
  wait     --state FILE [--poll-seconds 60] [--deadline-seconds N]

Exit codes: 0 PASS for the expected source; 1 FAIL/CANCELLED/TIMED_OUT; 3 resolved source differs
from the expected candidate (never reported as a pass); 11 QUEUED/RUNNING (WAITING_CI);
12 CI_STALLED or UNKNOWN; 2 usage or tooling error.

The resolved source comes from the run's `release-verification-source` artifact when present, else
from the "Resolved commit" line of the Resolve job log. Workflow head SHA is recorded separately and
never treated as the tested source.

Dispatch --state updates an existing valid autonomous checkpoint whose currentSha matches the
candidate; it is not an output filename. A post-dispatch checkpoint failure retains the run receipt.
"""

from __future__ import annotations

import argparse
import datetime as dt
import io
import json
import re
import secrets
import subprocess
import sys
import time
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import completion_state as cs  # noqa: E402

WORKFLOW = "release-verification.yml"
STALL_ALLOWANCE = dt.timedelta(minutes=15)
HEAD_IS_NOW = re.compile(r"HEAD is now at ([0-9a-f]{7,40})")
FULL_SHA = re.compile(r"\b[0-9a-f]{40}\b")
EXIT = {"PASS": 0, "FAIL": 1, "CANCELLED": 1, "TIMED_OUT": 1, "QUEUED": 11, "RUNNING": 11, "CI_STALLED": 12, "UNKNOWN": 12}


class GhError(Exception):
    pass


def gh(*args: str, raw: bool = False):
    run = subprocess.run(["gh", *args], capture_output=True)
    if run.returncode != 0:
        raise GhError(f"gh {' '.join(args[:3])} failed: {run.stderr.decode(errors='replace').strip()[:500]}")
    return run.stdout if raw else json.loads(run.stdout or b"null")


def now() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def parse_time(value: str | None) -> dt.datetime | None:
    if not value or value.startswith("0001-"):
        return None
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00"))


def job_timeouts(workflow_path: Path) -> dict[str, int]:
    """Map job display names (prefix before a matrix suffix) to timeout-minutes from the workflow file."""
    timeouts: dict[str, int] = {}
    if not workflow_path.exists():
        return timeouts
    current_name = None
    for line in workflow_path.read_text(encoding="utf-8").splitlines():
        name = re.match(r"^    name: (.+)$", line)
        if name:
            current_name = name.group(1).strip().split("${{")[0].strip()
            continue
        timeout = re.match(r"^    timeout-minutes: (\d+)$", line)
        if timeout and current_name:
            timeouts[current_name] = int(timeout.group(1))
    return timeouts


def timeout_for(job_name: str, timeouts: dict[str, int]) -> int | None:
    for prefix, minutes in timeouts.items():
        if prefix and job_name.startswith(prefix.rstrip(" ·").strip()):
            return minutes
    return None


def resolved_source(repo: str, run_id: int, jobs: list[dict]) -> tuple[str | None, str]:
    artifacts = gh("api", f"repos/{repo}/actions/runs/{run_id}/artifacts?per_page=100").get("artifacts", [])
    for artifact in artifacts:
        if artifact.get("name") == "release-verification-source" and not artifact.get("expired"):
            blob = gh("api", f"repos/{repo}/actions/artifacts/{artifact['id']}/zip", raw=True)
            with zipfile.ZipFile(io.BytesIO(blob)) as archive:
                data = json.loads(archive.read("source.json"))
            if data.get("runId") == run_id and cs.SHA.match(data.get("resolvedCommit", "")):
                return data["resolvedCommit"], "artifact:release-verification-source"
    for job in jobs:
        if job["name"].startswith("Resolve") and job.get("conclusion") == "success":
            try:
                log = gh("api", "--allow-escape-sequences", f"repos/{repo}/actions/jobs/{job['id']}/logs", raw=True)
            except GhError:
                return None, "unresolved:log-unavailable"
            text = log.decode(errors="replace")
            short = HEAD_IS_NOW.search(text)
            if short:
                full = [sha for sha in set(FULL_SHA.findall(text)) if sha.startswith(short.group(1))]
                if len(full) == 1:
                    return full[0], "log:resolve-ref-checkout"
    return None, "unresolved"


def classify(run: dict, jobs: list[dict], timeouts: dict[str, int], at: dt.datetime) -> str:
    if run["status"] != "completed":
        for job in jobs:
            started = parse_time(job.get("started_at"))
            limit = timeout_for(job["name"], timeouts)
            if job["status"] == "in_progress" and started and limit and at - started > dt.timedelta(minutes=limit) + STALL_ALLOWANCE:
                return "CI_STALLED"
        return "QUEUED" if run["status"] in ("queued", "requested", "waiting", "pending") else "RUNNING"
    conclusion = run.get("conclusion")
    if conclusion == "success":
        if not jobs or any(j.get("conclusion") not in ("success", "skipped") for j in jobs):
            return "FAIL"
        return "PASS"
    return {"failure": "FAIL", "cancelled": "CANCELLED", "timed_out": "TIMED_OUT"}.get(conclusion, "UNKNOWN")


def observe(repo: str, run_id: int, expected_sha: str | None, workflow_path: Path) -> tuple[dict, int]:
    run = gh("api", f"repos/{repo}/actions/runs/{run_id}")
    jobs = gh("api", "--paginate", f"repos/{repo}/actions/runs/{run_id}/jobs?per_page=100", "--jq", ".jobs[]", raw=True)
    jobs = [json.loads(line) for line in jobs.decode().splitlines() if line.strip()]
    at = now()
    status = classify(run, jobs, job_timeouts(workflow_path), at)
    # A green workflow or matching source artifact alone is not the full release gate.
    # The canonical aggregate owns the unfiltered suites and their mandatory evidence checks.
    release_contract_verified = run.get("path") == f".github/workflows/{WORKFLOW}" and any(
        job["name"] == "Verify · Full release contract" and job["status"] == "completed" and
        job.get("conclusion") == "success" for job in jobs
    )
    if status == "PASS" and not release_contract_verified:
        status = "UNKNOWN"
    resolved, source_basis = resolved_source(repo, run_id, jobs)
    evidence = {
        "schemaVersion": 1, "repository": repo, "runId": run_id, "attempt": run.get("run_attempt"),
        "workflow": run.get("path"), "event": run.get("event"), "displayTitle": run.get("display_title"),
        "workflowHeadSha": run.get("head_sha"), "resolvedSha": resolved, "resolvedSourceBasis": source_basis,
        "expectedSha": expected_sha, "status": status, "observedAtUtc": at.isoformat(timespec="seconds"),
        "releaseContractVerified": release_contract_verified,
        "jobs": [{"name": j["name"], "status": j["status"], "conclusion": j.get("conclusion"),
                  "startedAt": j.get("started_at"), "completedAt": j.get("completed_at")} for j in jobs],
    }
    code = EXIT[status]
    if expected_sha:
        if resolved is None:
            evidence["sourceMatch"] = "UNRESOLVED"
            if status == "PASS":
                code = 3
        elif resolved != expected_sha:
            evidence["sourceMatch"] = "MISMATCH"
            code = 3
        else:
            evidence["sourceMatch"] = "MATCH"
    evidence["fullPassForExpected"] = bool(status == "PASS" and expected_sha and resolved == expected_sha)
    return evidence, code


def write_state_ci(state_path: Path, evidence: dict) -> None:
    with cs.single_writer(state_path):
        state = cs.load_json(state_path)
        state["ci"] = {
            "runId": evidence["runId"], "attempt": evidence.get("attempt"), "requestedRef": evidence.get("requestedRef"),
            "workflowHeadSha": evidence.get("workflowHeadSha"), "resolvedSha": evidence.get("resolvedSha"),
            "status": evidence["status"] if evidence.get("sourceMatch") != "MISMATCH" else "UNKNOWN",
            "observedAtUtc": evidence.get("observedAtUtc"), "nextPollAtUtc": evidence.get("nextPollAtUtc"),
        }
        state["updatedAtUtc"] = now().isoformat(timespec="seconds")
        state["nextAction"] = cs.select_next(state)["nextAction"]
        errors = cs.check_state(state)
        if errors:
            raise cs.StateError("refusing to write invalid state: " + "; ".join(errors))
        cs.atomic_write_json(state_path, state)


def dispatch(repo: str, candidate: str, workflow: str, state_path: Path | None) -> tuple[dict, int]:
    if not cs.SHA.match(candidate):
        raise GhError("--candidate must be a full 40-character commit SHA")
    if state_path:
        # Refuse an unusable checkpoint before creating a remote run.
        with cs.single_writer(state_path):
            state = cs.load_json(state_path)
            errors = cs.check_state(state, expected_repository=repo)
            if errors:
                raise cs.StateError("invalid dispatch state: " + "; ".join(errors))
            if state["currentSha"] != candidate:
                raise cs.StateError("dispatch candidate differs from state currentSha")
    correlation = f"as-{candidate[:12]}-{secrets.token_hex(4)}"
    started = now() - dt.timedelta(seconds=5)
    # gh may print the created run URL. Keep the automation stdout contract JSON-only.
    subprocess.run(["gh", "workflow", "run", workflow, "-R", repo, "--ref", "master",
                    "-f", f"ref={candidate}", "-f", f"correlation={correlation}"], check=True, stdout=subprocess.PIPE)
    run = None
    for _ in range(30):
        time.sleep(4)
        runs = gh("api", f"repos/{repo}/actions/workflows/{workflow}/runs?event=workflow_dispatch&per_page=20")["workflow_runs"]
        matches = [r for r in runs if correlation in (r.get("display_title") or "") and parse_time(r["created_at"]) >= started]
        if len(matches) == 1:
            run = matches[0]
            break
        if len(matches) > 1:
            raise GhError(f"correlation {correlation} matched {len(matches)} runs")
    if run is None:
        raise GhError(f"dispatched, but no run with correlation {correlation} appeared; not guessing the newest run")
    evidence = {"schemaVersion": 1, "repository": repo, "runId": run["id"], "attempt": run.get("run_attempt"),
                "requestedRef": candidate, "correlation": correlation, "workflowHeadSha": run.get("head_sha"),
                "resolvedSha": None, "status": "QUEUED", "observedAtUtc": now().isoformat(timespec="seconds")}
    if state_path:
        try:
            write_state_ci(state_path, evidence)
        except (cs.StateError, OSError) as exc:
            # The remote effect already happened: retain its identity even if the checkpoint fails.
            evidence.update(stateUpdate="FAILED", stateError=str(exc),
                            recovery="Observe this runId; do not dispatch again solely to repair the local checkpoint.")
            return evidence, 2
    return evidence, EXIT["QUEUED"]


def wait(state_path: Path, poll: int, deadline: int, workflow_path: Path) -> tuple[dict, int]:
    end = time.monotonic() + deadline
    while True:
        state = cs.load_json(state_path)
        ci = state.get("ci") or {}
        if not ci.get("runId"):
            raise GhError("state has no CI run to wait for")
        evidence, code = observe(state["repository"], ci["runId"], state["currentSha"], workflow_path)
        evidence["requestedRef"] = ci.get("requestedRef")
        if code == 11 and time.monotonic() + poll < end:
            write_state_ci(state_path, evidence)
            time.sleep(poll)
            continue
        if code == 11:
            evidence["nextPollAtUtc"] = (now() + dt.timedelta(seconds=poll)).isoformat(timespec="seconds")
        write_state_ci(state_path, evidence)
        return evidence, code


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    o = sub.add_parser("observe")
    o.add_argument("--repo", required=True)
    o.add_argument("--run-id", type=int, required=True)
    o.add_argument("--expected-sha")
    o.add_argument("--output", type=Path)
    d = sub.add_parser("dispatch")
    d.add_argument("--repo", required=True)
    d.add_argument("--candidate", required=True)
    d.add_argument("--workflow", default=WORKFLOW)
    d.add_argument("--state", type=Path, help="Existing valid autonomous checkpoint matching the candidate (not an output path)")
    w = sub.add_parser("wait")
    w.add_argument("--state", type=Path, required=True)
    w.add_argument("--poll-seconds", type=int, default=60)
    w.add_argument("--deadline-seconds", type=int, default=600)
    for p in (o, w):
        p.add_argument("--workflow-file", type=Path, default=Path(".github/workflows") / WORKFLOW)
    args = parser.parse_args(argv)
    try:
        if args.command == "observe":
            evidence, code = observe(args.repo, args.run_id, args.expected_sha, args.workflow_file)
            if args.output:
                cs.atomic_write_json(args.output, evidence)
        elif args.command == "dispatch":
            evidence, code = dispatch(args.repo, args.candidate, args.workflow, args.state)
        else:
            evidence, code = wait(args.state, max(args.poll_seconds, 30), args.deadline_seconds, args.workflow_file)
    except (GhError, cs.StateError, subprocess.CalledProcessError) as exc:
        print(json.dumps({"error": str(exc)}))
        return 2
    print(json.dumps(evidence, indent=2))
    return code


if __name__ == "__main__":
    sys.exit(main())
