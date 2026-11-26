"""RUN-008 control-flow tests for complete.py with a fake runner (no Codex, no network)."""

from __future__ import annotations

import json
import contextlib
import io
import os
import signal
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest
from unittest import mock
from pathlib import Path

AGENT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(AGENT))
import completion_state as cs  # noqa: E402
import complete as complete_mod  # noqa: E402

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
        return self.run_cli_state(self.state_path, *args)

    def run_cli_state(self, state_path, *args):
        proc = subprocess.run([sys.executable, str(COMPLETE), *args, "--repo", str(self.repo), "--state", str(state_path)],
                              capture_output=True, text=True)
        return proc.returncode, json.loads(proc.stdout.strip().splitlines()[-1])

    def state(self):
        return json.loads(self.state_path.read_text())

    def write_owner(self, task_id="RUN-004", session_id="session-7", *, phase="RUNNING", needs=True,
                    supervisor_pid=None, runner_pid=None, process_group_id=None, repo_path=None,
                    state_path=None, **extra):
        dead_supervisor = subprocess.Popen([sys.executable, "-c", "pass"], start_new_session=True)
        dead_supervisor.wait()
        dead_runner = subprocess.Popen([sys.executable, "-c", "pass"], start_new_session=True)
        dead_runner.wait()
        owner = {"schemaVersion": 1, "repoPath": str(repo_path or self.repo.resolve()),
                 "originatingStatePath": str(state_path or self.state_path.resolve()), "taskId": task_id,
                 "sessionId": session_id, "host": __import__("socket").gethostname(),
                 "supervisorPid": supervisor_pid or dead_supervisor.pid,
                 "runnerPid": runner_pid if runner_pid is not None else (dead_runner.pid if phase != "DISPATCHING" else None),
                 "processGroupId": (process_group_id if process_group_id is not None
                                    else (dead_runner.pid if phase != "DISPATCHING" else None)),
                 "phase": phase, "needsReconciliation": needs,
                 "createdAtUtc": "2026-10-22T18:00:00.000000Z", "updatedAtUtc": "2026-10-22T18:00:00.000000Z"}
        owner.update(extra)
        cs.save_runner_owner(self.repo, owner)
        return owner

    def write_running_state(self, task_id="RUN-004", session_id="session-7"):
        self.write_state({task_id: task()}, activeTaskId=task_id,
                         session={"id": session_id, "taskId": task_id,
                                  "startedAtUtc": "2026-10-22T18:00:00+00:00", "status": "RUNNING"})

    def wait_until(self, predicate, timeout=5):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if predicate():
                return True
            time.sleep(0.025)
        return bool(predicate())

    def test_model_exit_zero_with_running_result_continues_instead_of_completing(self):
        self.write_state({"RUN-003": task(), "WU-007": task()})
        runner = self.runner("result(verificationStatus='RUNNING', nextAction='POLL_CI')\n"
                             "print(json.dumps({'thread_id':'s-1'}))\n"
                             "print('runner diagnostic', file=sys.stderr)\n")
        code, out = self.run_cli("run", "--runner", str(runner))
        self.assertNotEqual(code, 0)
        self.assertEqual(self.state()["tasks"]["RUN-003"]["scheduling"], "WAITING_CI")
        owner = cs.load_runner_owner(self.repo)
        stderr_path = complete_mod._owner_stderr_path(owner)
        self.assertIn("runner diagnostic", stderr_path.read_text(encoding="utf-8"))
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
        self.write_running_state()
        self.write_owner()
        runner = self.runner("sys.exit(9)\n")
        self.run_cli("run", "--runner", str(runner))
        calls = [json.loads(l) for l in self.calls.read_text().splitlines()]
        self.assertEqual(calls[-1]["resume"], "session-7")

    def test_legacy_running_checkpoint_without_owner_is_blocked_without_dispatch(self):
        self.write_running_state()
        before = self.state_path.read_bytes()
        code, out = self.run_cli("run", "--runner", str(self.runner("sys.exit(9)\n")))
        self.assertEqual(code, 20)
        self.assertIn("no trustworthy runner owner", out["error"])
        self.assertEqual(self.state_path.read_bytes(), before)
        self.assertFalse(self.calls.exists())

    def test_corrupt_and_foreign_owner_are_blocked_without_state_mutation(self):
        for kind in ("malformed", "corrupt", "foreign-repo", "foreign-host"):
            with self.subTest(kind=kind):
                self.state_path.unlink(missing_ok=True)
                self.write_state({"RUN-004": task()})
                before = self.state_path.read_bytes()
                owner_path = cs.runner_owner_path(self.repo)
                owner_path.parent.mkdir(parents=True, exist_ok=True)
                if kind == "malformed":
                    owner_path.write_text('{broken', encoding="utf-8")
                elif kind == "corrupt":
                    owner_path.write_text('{"schemaVersion":1,"extra":true}', encoding="utf-8")
                else:
                    foreign = self.write_owner(phase="STOPPED", needs=False)
                    if kind == "foreign-repo":
                        foreign["repoPath"] = str(self.repo.parent / "foreign")
                    else:
                        foreign["host"] = "other-host"
                    cs.atomic_write_json(owner_path, foreign)
                code, out = self.run_cli("run", "--runner", str(self.runner("sys.exit(9)\n")))
                self.assertEqual(code, 20)
                self.assertEqual(self.state_path.read_bytes(), before)
                self.assertFalse(self.calls.exists())

    def test_dispatching_without_pid_blocks_recovery(self):
        self.write_running_state()
        owner = self.write_owner(phase="DISPATCHING", needs=True)
        before = self.state_path.read_bytes()
        owner_bytes = cs.runner_owner_path(self.repo).read_bytes()
        code, out = self.run_cli("run", "--runner", str(self.runner("sys.exit(9)\n")))
        self.assertEqual(code, 20)
        self.assertIn("DISPATCHING", out["error"])
        self.assertEqual(self.state_path.read_bytes(), before)
        self.assertEqual(cs.runner_owner_path(self.repo).read_bytes(), owner_bytes)
        self.assertFalse(self.calls.exists())

    def test_unknown_session_and_wrong_originating_state_block_dead_owner_recovery(self):
        self.write_running_state(session_id=None)
        self.write_owner(session_id=None)
        before = self.state_path.read_bytes()
        code, out = self.run_cli("run", "--runner", str(self.runner("sys.exit(9)\n")))
        self.assertEqual(code, 20)
        self.assertIn("session identity are unknown", out["error"])
        self.assertEqual(self.state_path.read_bytes(), before)
        self.assertFalse(self.calls.exists())

        alternate = self.repo / ".local/autonomous/alternate.json"
        alternate.write_bytes(before)
        code, out = self.run_cli_state(alternate, "run", "--runner", str(self.runner("sys.exit(9)\n")))
        self.assertEqual(code, 20)
        self.assertIn(str(self.state_path), out["error"])
        self.assertEqual(self.state_path.read_bytes(), before)
        self.assertFalse(self.calls.exists())

    def test_concurrent_same_and_alternate_state_runs_are_excluded(self):
        self.write_state({"RUN-004": task()})
        runner = self.runner("print(json.dumps({'thread_id':'session-live'}), flush=True)\nimport time; time.sleep(60)\n")
        first = subprocess.Popen([sys.executable, str(COMPLETE), "run", "--runner", str(runner),
                                  "--repo", str(self.repo), "--state", str(self.state_path)],
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            self.assertTrue(self.wait_until(lambda: self.calls.exists() and
                                            cs.load_runner_owner(self.repo) is not None and
                                            cs.load_runner_owner(self.repo)["phase"] == "RUNNING"))
            before = self.state_path.read_bytes()
            code, out = self.run_cli("run", "--runner", str(runner))
            self.assertEqual(code, 20)
            self.assertIn("repository lock", out["error"])
            alternate = self.repo / ".local/autonomous/alternate.json"
            alternate.write_bytes(self.state_path.read_bytes())
            code, out = self.run_cli_state(alternate, "run", "--runner", str(runner))
            self.assertEqual(code, 20)
            self.assertIn("repository lock", out["error"])
            self.assertEqual(self.state_path.read_bytes(), before)
            self.assertEqual(len(self.calls.read_text().splitlines()), 1)
        finally:
            if first.poll() is None:
                first.kill()  # kill only the supervisor; the recorded runner group remains alive.
                first.wait(timeout=5)
            first.stdout.close()
            first.stderr.close()
        owner = cs.load_runner_owner(self.repo)
        self.assertEqual(cs.process_state(owner["runnerPid"]), "ALIVE")
        before = self.state_path.read_bytes()
        for target in (self.state_path, self.repo / ".local/autonomous/alternate.json"):
            code, _ = self.run_cli_state(target, "run", "--runner", str(runner))
            self.assertEqual(code, 20)
        self.assertEqual(self.state_path.read_bytes(), before)
        self.assertEqual(len(self.calls.read_text().splitlines()), 1)
        with contextlib.suppress(ProcessLookupError):
            os.killpg(owner["processGroupId"], signal.SIGKILL)

    def test_dead_runner_leader_with_live_descendant_blocks_both_state_paths(self):
        self.write_state({"RUN-004": task()})
        child_pid_path = Path(self.tmp.name, "child.pid")
        body = f"""
            import subprocess, time, pathlib, json
            child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])
            pathlib.Path({str(child_pid_path)!r}).write_text(str(child.pid))
            print(json.dumps({{'thread_id':'session-descendant'}}), flush=True)
            time.sleep(60)
        """
        runner = self.runner(body)
        supervisor = subprocess.Popen([sys.executable, str(COMPLETE), "run", "--runner", str(runner),
                                       "--repo", str(self.repo), "--state", str(self.state_path)],
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        owner = None
        try:
            self.assertTrue(self.wait_until(lambda: child_pid_path.exists() and
                                            cs.load_runner_owner(self.repo) is not None and
                                            cs.load_runner_owner(self.repo)["phase"] == "RUNNING"))
            owner = cs.load_runner_owner(self.repo)
            os.kill(owner["runnerPid"], signal.SIGKILL)
            self.assertTrue(self.wait_until(lambda: cs.process_state(owner["runnerPid"]) == "DEAD"))
            supervisor.kill()
            supervisor.wait(timeout=5)
            child_pid = int(child_pid_path.read_text())
            self.assertEqual(cs.process_state(child_pid), "ALIVE")
            calls_before = self.calls.read_text().splitlines()
            for target in (self.state_path, self.repo / ".local/autonomous/alternate.json"):
                code, _ = self.run_cli_state(target, "run", "--runner", str(runner))
                self.assertEqual(code, 20)
            self.assertEqual(self.calls.read_text().splitlines(), calls_before)
        finally:
            if supervisor.poll() is None:
                supervisor.kill()
                supervisor.wait(timeout=5)
            supervisor.stdout.close()
            supervisor.stderr.close()
            if owner:
                with contextlib.suppress(ProcessLookupError):
                    os.killpg(owner["processGroupId"], signal.SIGKILL)

    def test_stopped_recovery_preserves_session_and_applies_exact_retained_result(self):
        self.write_running_state()
        owner = self.write_owner()
        result_path, _ = complete_mod._owner_paths(owner)
        result_path.parent.mkdir(parents=True, exist_ok=True)
        evidence = self.repo / ".local/autonomous/ev.json"
        evidence.parent.mkdir(parents=True, exist_ok=True)
        evidence.write_text(json.dumps({"testsExecuted": 4}), encoding="utf-8")
        result_path.write_text(json.dumps({"schemaVersion": 1, "repository": cs.REPOSITORY,
                                           "taskId": "RUN-004", "candidateSha": self.head,
                                           "implementationStatus": "PRESENT", "verificationStatus": "PASS",
                                           "nextAction": "NEXT_READY_TASK", "evidencePaths": ["ev.json"],
                                           "remainingTaskIds": [], "externalGates": []}), encoding="utf-8")
        retained = result_path.read_bytes()
        code, out = self.run_cli("run", "--runner", str(self.runner("sys.exit(99)\n")))
        self.assertEqual(self.state()["tasks"]["RUN-004"]["scheduling"], "ENGINEERING_VERIFIED")
        self.assertEqual(result_path.read_bytes(), retained)
        self.assertEqual(cs.load_runner_owner(self.repo)["needsReconciliation"], False)
        self.assertFalse(self.calls.exists())

    def test_timeout_stops_owned_group_and_keeps_resumable_session(self):
        self.write_state({"RUN-004": task()})
        runner = self.runner("sys.exit(0)\n")
        args = type("Args", (), {"repo": self.repo, "state": self.state_path,
                                  "runner": str(runner), "profile": "test-profile"})()
        real_runner = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"],
                                       start_new_session=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)

        class TimedOutProcess:
            pid = real_runner.pid
            returncode = None

            def __init__(self):
                self.calls = 0

            def communicate(self, timeout=None):
                self.calls += 1
                if self.calls == 1:
                    raise subprocess.TimeoutExpired("fake-runner", timeout, output=b"", stderr=b"")
                return b'{"thread_id":"session-timeout"}\n', b""

            def wait(self, timeout=None):
                self.returncode = real_runner.wait(timeout=timeout)
                return self.returncode

        with mock.patch.object(complete_mod, "_dispatch_process", return_value=TimedOutProcess()), \
                contextlib.redirect_stdout(io.StringIO()):
            code = complete_mod.cmd_run(args)
        real_runner.stdout.close()
        real_runner.stderr.close()
        owner = cs.load_runner_owner(self.repo)
        self.assertEqual(code, 10)
        self.assertEqual(owner["phase"], "STOPPED")
        self.assertFalse(owner["needsReconciliation"])
        self.assertEqual(cs.process_group_state(owner["processGroupId"]), "DEAD")
        self.assertEqual(self.state()["session"]["id"], "session-timeout")
        self.assertEqual(self.state()["session"]["status"], "INTERRUPTED")
        _, event_path = complete_mod._owner_paths(owner)
        self.assertIn("session-timeout", event_path.read_text(encoding="utf-8"))

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
        evidence = self.state_path.parent / "e"
        evidence.write_text("verified command output")
        verified["evidenceDigests"] = cs.evidence_digests(self.state_path.parent, ["e"])
        self.write_state({"RUN-001": verified}, ci={"status": "PASS", "resolvedSha": self.head})
        code, out = self.run_cli("verify")
        self.assertEqual((code, out["verdict"]), (0, "ENGINEERING_COMPLETE"))
        # Same source and green CI cannot hide replaced, deleted or unrecorded evidence.
        evidence.write_text("different command output")
        for command in ("verify", "run", "resume"):
            with self.subTest(command=command):
                code, out = self.run_cli(command, "--runner", str(self.runner("sys.exit(99)")))
                self.assertEqual(code, 20)
        evidence.unlink()
        self.assertEqual(self.run_cli("verify")[0], 20)
        evidence.write_text("verified command output")
        verified.pop("evidenceDigests")
        self.write_state({"RUN-001": verified}, ci={"status": "PASS", "resolvedSha": self.head})
        self.assertEqual(self.run_cli("verify")[0], 20)

    def test_missing_codex_is_a_runner_prerequisite_exit(self):
        self.write_state({"RUN-004": task()})
        env = dict(os.environ, PATH="/nonexistent")
        proc = subprocess.run([sys.executable, str(COMPLETE), "run", "--repo", str(self.repo), "--state", str(self.state_path)],
                              capture_output=True, text=True, env=env)
        self.assertEqual(proc.returncode, 13)

    def test_missing_flock_support_is_runner_prerequisite_exit_without_state_change(self):
        self.write_state({"RUN-004": task()})
        before = self.state_path.read_bytes()
        args = type("Args", (), {"repo": self.repo, "state": self.state_path,
                                  "runner": str(self.runner("sys.exit(99)\n")), "profile": "test-profile"})()
        with mock.patch.object(cs, "fcntl", None), contextlib.redirect_stdout(io.StringIO()):
            code = complete_mod.cmd_run(args)
        self.assertEqual(code, 13)
        self.assertEqual(self.state_path.read_bytes(), before)
        self.assertFalse(cs.runner_owner_path(self.repo).exists())


if __name__ == "__main__":
    unittest.main()
