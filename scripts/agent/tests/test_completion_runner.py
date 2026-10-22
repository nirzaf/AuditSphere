"""RUN-008 control-flow tests for complete.py with a fake runner (no Codex, no network)."""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

AGENT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(AGENT))
import completion_state as cs  # noqa: E402

COMPLETE = AGENT / "complete.py"


def task(**overrides):
    base = {"title": "t", "category": "Testing", "severity": "medium", "implementationDependencies": [],
            "acceptanceDependencies": [], "implementation": "NOT_ASSESSED", "localVerification": "NOT_RUN",
            "browserVerification": "NOT_RUN", "ciVerification": "NOT_DISPATCHED", "acceptance": "PENDING",
            "scheduling": "READY", "evidencePaths": [], "notes": ""}
    base.update(overrides)
    return base


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.repo = Path(self.tmp.name, "repo")
        self.repo.mkdir()
        git = lambda *a: subprocess.run(["git", "-C", str(self.repo), *a], check=True, capture_output=True)
        git("init", "-q")
        git("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-q", "--allow-empty", "-m", "init")
        self.head = subprocess.run(["git", "-C", str(self.repo), "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
        self.state_path = self.repo / ".local/autonomous/state.json"
        self.calls = Path(self.tmp.name, "calls.jsonl")

    def tearDown(self):
        self.tmp.cleanup()

    def write_state(self, tasks, **extra):
        state = {"schemaVersion": 1, "repository": cs.REPOSITORY, "baselineSha": self.head, "currentSha": self.head,
                 "runId": "run-test", "activeTaskId": None, "updatedAtUtc": "2026-10-22T18:00:00+00:00",
                 "nextAction": "NEXT_READY_TASK", "ci": {"status": "NOT_DISPATCHED"},
                 "budgets": {"maxSessionSeconds": 60, "maxSessionsPerDay": 5, "noProgressLimit": 3,
                             "sessionsToday": 0, "budgetDay": "2026-10-22", "consecutiveNoProgress": 0},
                 "session": {"id": None, "taskId": None, "startedAtUtc": None, "status": "IDLE"},
                 "tasks": tasks, "externalGates": []}
        state.update(extra)
        self.assertEqual(cs.check_state(state), [])
        cs.atomic_write_json(self.state_path, state)

    def runner(self, body: str) -> Path:
        path = Path(self.tmp.name, "runner.py")
        path.write_text("#!" + sys.executable + "\n" + textwrap.dedent(f"""
            import json, sys, pathlib
            args = sys.argv[1:]
            task = args[args.index('--task') + 1]
            out = pathlib.Path(args[args.index('--output') + 1])
            resume = args[args.index('--resume') + 1] if '--resume' in args else None
            with open({str(self.calls)!r}, 'a') as f:
                f.write(json.dumps({{'task': task, 'resume': resume}}) + '\\n')
            HEAD = {self.head!r}
            def result(**kw):
                base = dict(schemaVersion=1, repository='nirzaf/AuditSphere', taskId=task, candidateSha=HEAD,
                            implementationStatus='PRESENT', verificationStatus='PASS', nextAction='NEXT_READY_TASK',
                            evidencePaths=[], remainingTaskIds=[], externalGates=[])
                base.update(kw)
                out.write_text(json.dumps(base))
        """) + textwrap.dedent(body))
        os.chmod(path, 0o755)
        return path

    def run_cli(self, *args):
        proc = subprocess.run([sys.executable, str(COMPLETE), *args, "--repo", str(self.repo), "--state", str(self.state_path)],
                              capture_output=True, text=True)
        return proc.returncode, json.loads(proc.stdout.strip().splitlines()[-1])

    def state(self):
        return json.loads(self.state_path.read_text())

    def test_model_exit_zero_with_running_result_continues_instead_of_completing(self):
        self.write_state({"RUN-003": task(), "WU-007": task()})
        runner = self.runner("result(verificationStatus='RUNNING', nextAction='POLL_CI')\nprint(json.dumps({'thread_id':'s-1'}))\n")
        code, out = self.run_cli("run", "--runner", str(runner))
        self.assertNotEqual(code, 0)
        self.assertEqual(self.state()["tasks"]["RUN-003"]["scheduling"], "WAITING_CI")
        code, out = self.run_cli("next")
        self.assertEqual(out, {"taskId": "WU-007", "nextAction": "IMPLEMENT"})

    def test_crash_after_dispatch_resumes_the_same_session_identity(self):
        self.write_state({"RUN-004": task()})
        crash = self.runner("print(json.dumps({'thread_id':'session-42'}))\nsys.exit(9)\n")
        code, out = self.run_cli("run", "--runner", str(crash))
        self.assertEqual(code, 10)
        self.assertEqual(self.state()["session"]["status"], "INTERRUPTED")
        evidence = self.repo / ".local/autonomous/ev.json"
        evidence.write_text(json.dumps({"testsExecuted": 6}))
        ok = self.runner("result(evidencePaths=['ev.json'])\n")
        code, out = self.run_cli("resume", "--runner", str(ok))
        calls = [json.loads(l) for l in self.calls.read_text().splitlines()]
        self.assertEqual(calls[-1], {"task": "RUN-004", "resume": "session-42"})
        self.assertEqual(self.state()["tasks"]["RUN-004"]["scheduling"], "ENGINEERING_VERIFIED")

    def test_process_killed_mid_session_is_reconciled_not_replayed_fresh(self):
        self.write_state({"RUN-004": task()}, session={"id": "session-7", "taskId": "RUN-004",
                                                         "startedAtUtc": "2026-10-22T18:00:00+00:00", "status": "RUNNING"})
        runner = self.runner("sys.exit(9)\n")
        self.run_cli("run", "--runner", str(runner))
        calls = [json.loads(l) for l in self.calls.read_text().splitlines()]
        self.assertEqual(calls[-1]["resume"], "session-7")

    def test_unknown_source_cannot_complete(self):
        self.write_state({"RUN-004": task()})
        runner = self.runner("result(candidateSha='0'*40)\n")
        code, out = self.run_cli("run", "--runner", str(runner))
        self.assertEqual(code, 20)
        self.assertNotEqual(self.state()["tasks"]["RUN-004"]["scheduling"], "ENGINEERING_VERIFIED")

    def test_unsupported_output_schema_cannot_complete(self):
        self.write_state({"RUN-004": task()})
        runner = self.runner("out.write_text(json.dumps({'status': 'done'}))\n")
        code, out = self.run_cli("run", "--runner", str(runner))
        self.assertEqual(code, 20)

    def test_corrupt_state_fails_safely(self):
        self.state_path.parent.mkdir(parents=True, exist_ok=True)
        self.state_path.write_text("{broken")
        code, out = self.run_cli("status")
        self.assertEqual(code, 20)

    def test_exhausted_budget_checkpoints_and_resumes_next_day(self):
        self.write_state({"RUN-004": task()}, budgets={"maxSessionSeconds": 60, "maxSessionsPerDay": 1, "noProgressLimit": 3,
                                                        "sessionsToday": 1, "budgetDay": __import__("datetime").date.today().isoformat(), "consecutiveNoProgress": 0})
        runner = self.runner("result()\n")
        code, out = self.run_cli("run", "--runner", str(runner))
        self.assertEqual(code, 14)
        self.assertEqual(self.state()["nextAction"], "RESUME_REQUIRED")
        state = self.state()
        state["budgets"]["budgetDay"] = "2000-01-01"  # a new budget window rolls the counter
        cs.atomic_write_json(self.state_path, state)
        evidence = self.repo / ".local/autonomous/ev.json"
        evidence.write_text(json.dumps({"testsExecuted": 1}))
        runner = self.runner("result(evidencePaths=['ev.json'])\n")
        code, out = self.run_cli("run", "--runner", str(runner))
        self.assertEqual(self.state()["tasks"]["RUN-004"]["scheduling"], "ENGINEERING_VERIFIED")

    def test_verify_requires_exact_source_ci(self):
        verified = task(implementation="PRESENT", localVerification="PASS", scheduling="ENGINEERING_VERIFIED", evidencePaths=["e"])
        self.write_state({"RUN-001": verified})
        code, out = self.run_cli("verify")
        self.assertNotEqual(code, 0)
        self.write_state({"RUN-001": verified}, ci={"status": "PASS", "resolvedSha": self.head})
        code, out = self.run_cli("verify")
        self.assertEqual((code, out["verdict"]), (0, "ENGINEERING_COMPLETE"))

    def test_missing_codex_is_a_runner_prerequisite_exit(self):
        self.write_state({"RUN-004": task()})
        env = dict(os.environ, PATH="/nonexistent")
        proc = subprocess.run([sys.executable, str(COMPLETE), "run", "--repo", str(self.repo), "--state", str(self.state_path)],
                              capture_output=True, text=True, env=env)
        self.assertEqual(proc.returncode, 13)


if __name__ == "__main__":
    unittest.main()
