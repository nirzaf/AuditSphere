"""RUN-002 acceptance tests for the autonomous-completion state (standard library only)."""

from __future__ import annotations

import copy
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

AGENT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(AGENT))
import completion_state as cs  # noqa: E402

SHA = "e5c24072d24b5de1e7d12d13d8fc0b1192323a48"


def task(**overrides):
    base = {
        "title": "t", "category": "Testing", "severity": "medium",
        "implementationDependencies": [], "acceptanceDependencies": [],
        "implementation": "NOT_ASSESSED", "localVerification": "NOT_RUN", "browserVerification": "NOT_RUN",
        "ciVerification": "NOT_DISPATCHED", "acceptance": "PENDING", "scheduling": "READY",
        "evidencePaths": [], "notes": "",
    }
    base.update(overrides)
    return base


def verified(**overrides):
    return task(implementation="PRESENT", localVerification="PASS", scheduling="ENGINEERING_VERIFIED",
                evidencePaths=["evidence/ok.json"], **overrides)


def state(tasks, **overrides):
    base = {"schemaVersion": 1, "repository": cs.REPOSITORY, "baselineSha": SHA, "currentSha": SHA,
            "runId": "run-test", "activeTaskId": None, "updatedAtUtc": "2026-10-22T18:00:00+00:00",
            "nextAction": "NEXT_READY_TASK", "ci": {"status": "NOT_DISPATCHED"}, "tasks": tasks, "externalGates": []}
    base.update(overrides)
    return base


def result(**overrides):
    base = {"schemaVersion": 1, "repository": cs.REPOSITORY, "taskId": "RUN-004", "candidateSha": SHA,
            "implementationStatus": "PRESENT", "verificationStatus": "PASS", "nextAction": "NEXT_READY_TASK",
            "evidencePaths": ["evidence/run-004.json"], "remainingTaskIds": [], "externalGates": []}
    base.update(overrides)
    return base


class SelectionTests(unittest.TestCase):
    def test_pending_ci_selects_poll_never_completion(self):
        s = state({"RUN-003": task(implementation="PRESENT", localVerification="PASS", scheduling="WAITING_CI",
                                   evidencePaths=["e.json"])}, ci={"status": "RUNNING"})
        self.assertEqual(cs.check_state(s), [])
        self.assertEqual(cs.select_next(s)["nextAction"], "POLL_CI")

    def test_pending_ci_prefers_independent_ready_work(self):
        s = state({"RUN-003": task(scheduling="WAITING_CI"), "WU-007": task()}, ci={"status": "RUNNING"})
        self.assertEqual(cs.select_next(s), {"taskId": "WU-007", "nextAction": "IMPLEMENT"})

    def test_out_of_catalog_prerequisite_parks_task_without_looping(self):
        s = state({"WU-005": task(scheduling="BLOCKED_DEPENDENCY", blockedBy=["R2R:T007"]), "WU-006": task()})
        self.assertEqual(cs.check_state(s), [])
        self.assertEqual(cs.select_next(s), {"taskId": "WU-006", "nextAction": "IMPLEMENT"})
        self.assertFalse(cs.engineering_complete(s))

    def test_professional_blocker_parks_only_its_acceptance_leaf(self):
        tasks = {
            "WU-012": task(implementation="PRESENT", localVerification="PASS", scheduling="BLOCKED_EXTERNAL",
                           acceptance="BLOCKED_PROFESSIONAL", acceptanceDependencies=["GATE-PROFESSIONAL-METHOD"]),
            "WU-026": task(implementationDependencies=[], acceptanceDependencies=["WU-012"]),
            "WU-011": task(implementationDependencies=["WU-012"]),
        }
        s = state(tasks)
        self.assertEqual(cs.check_state(s), [])
        ready = cs.ready_tasks(s)
        self.assertIn("WU-026", ready)  # acceptance dependency never blocks implementation
        self.assertIn("WU-011", ready)  # dependency implemented and locally verified, acceptance still blocked
        self.assertEqual(cs.select_next(s)["nextAction"], "IMPLEMENT")

    def test_repair_is_selected_before_new_work(self):
        s = state({"WU-007": task(), "WU-008": task(scheduling="REPAIR_REQUIRED")})
        self.assertEqual(cs.select_next(s), {"taskId": "WU-008", "nextAction": "REPAIR"})

    def test_only_external_gates_left_reports_waiting_external(self):
        s = state({"WU-018": task(implementation="PRESENT", localVerification="PASS", scheduling="BLOCKED_EXTERNAL")})
        self.assertEqual(cs.select_next(s)["nextAction"], "WAITING_EXTERNAL")

    def test_engineering_completion_requires_exact_source_ci_pass(self):
        s = state({"RUN-001": verified()}, ci={"status": "PASS", "resolvedSha": "0" * 40})
        self.assertNotEqual(cs.select_next(s)["nextAction"], "VERIFY_ENGINEERING_COMPLETION")
        s["ci"]["resolvedSha"] = SHA
        self.assertEqual(cs.select_next(s)["nextAction"], "VERIFY_ENGINEERING_COMPLETION")


class StateIntegrityTests(unittest.TestCase):
    def test_foreign_repository_fails(self):
        self.assertTrue(any("foreign" in e for e in cs.check_state(state({"RUN-001": task()}, repository="evil/fork"))))

    def test_cyclic_dependencies_fail(self):
        s = state({"WU-001": task(implementationDependencies=["WU-002"]), "WU-002": task(implementationDependencies=["WU-001"])})
        self.assertTrue(any("cycle" in e for e in cs.check_state(s)))

    def test_unknown_dependency_and_unknown_field_fail(self):
        s = state({"WU-001": task(implementationDependencies=["WU-099"])})
        self.assertTrue(any("unknown" in e for e in cs.check_state(s)))
        bad = state({"WU-001": task()})
        bad["tasks"]["WU-001"]["secret"] = "x"
        self.assertTrue(any("unknown property" in e for e in cs.check_state(bad)))

    def test_engineering_verified_without_evidence_fails(self):
        s = state({"WU-001": verified()})
        s["tasks"]["WU-001"]["evidencePaths"] = []
        self.assertTrue(any("requires evidence" in e for e in cs.check_state(s)))

    def test_corrupt_json_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp, "state.json")
            path.write_text("{not json", encoding="utf-8")
            with self.assertRaises(cs.StateError):
                cs.load_json(path)

    def test_concurrent_writer_fails_safely(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp, "state.json")
            with cs.single_writer(path):
                with self.assertRaises(cs.StateError):
                    with cs.single_writer(path):
                        pass
            with cs.single_writer(path):  # released lock can be reacquired
                pass

    def test_stale_lock_from_dead_process_is_reclaimed(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp, "state.json")
            Path(f"{path}.lock").write_text(json.dumps({"pid": 999999, "host": os.uname().nodename}), encoding="utf-8")
            with cs.single_writer(path):
                pass

    def test_atomic_write_leaves_no_temporary_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp, "state.json")
            cs.atomic_write_json(path, {"a": 1})
            self.assertEqual(json.loads(path.read_text()), {"a": 1})
            self.assertEqual([p.name for p in Path(tmp).iterdir()], ["state.json"])


class ResultTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        (self.root / "evidence").mkdir()
        (self.root / "evidence" / "run-004.json").write_text(json.dumps({"testsExecuted": 6}), encoding="utf-8")

    def tearDown(self):
        self.tmp.cleanup()

    def test_valid_result_passes(self):
        self.assertEqual(cs.check_result(result(), self.root), [])

    def test_result_without_required_evidence_is_rejected(self):
        self.assertTrue(cs.check_result(result(evidencePaths=[]), self.root))
        self.assertTrue(cs.check_result(result(evidencePaths=["evidence/missing.json"]), self.root))

    def test_path_traversal_and_absolute_paths_are_rejected(self):
        self.assertTrue(cs.check_result(result(evidencePaths=["../etc/passwd"]), self.root))
        self.assertTrue(cs.check_result(result(evidencePaths=["/etc/passwd"]), self.root))

    def test_zero_tests_cannot_support_pass(self):
        (self.root / "evidence" / "empty.json").write_text(json.dumps({"testsExecuted": 0}), encoding="utf-8")
        self.assertTrue(cs.check_result(result(evidencePaths=["evidence/empty.json"]), self.root))

    def test_schema_violations_are_rejected(self):
        self.assertTrue(cs.check_result(result(repository="other/repo"), self.root))
        self.assertTrue(cs.check_result(result(candidateSha="master"), self.root))
        self.assertTrue(cs.check_result(result(nextAction="DONE"), self.root))

    def test_cli_exits_nonzero_for_invalid_result(self):
        bad = self.root / "bad.json"
        bad.write_text(json.dumps(result(evidencePaths=[])), encoding="utf-8")
        run = subprocess.run([sys.executable, str(AGENT / "verify-completion-state.py"), "--result", str(bad),
                              "--evidence-root", str(self.root)], capture_output=True, text=True)
        self.assertEqual(run.returncode, 1)


class SeedingTests(unittest.TestCase):
    def test_spec_seeding_splits_professional_dependencies(self):
        spec = ("## RUN-001: Env\n**[Category]:** Testing\n**Implementation dependencies:** None\n"
                "## WU-012: Methods\n**Execution phase:** B · **Dependencies:** WU-011\n"
                "## WU-011: Presentation\n**Execution phase:** B · **Dependencies:** WU-012\n")
        units = cs.parse_spec_units(spec)
        tasks = cs.seed_tasks(units)
        self.assertEqual(tasks["WU-011"]["implementationDependencies"], [])
        self.assertEqual(tasks["WU-011"]["acceptanceDependencies"], ["WU-012"])
        self.assertEqual(tasks["WU-012"]["implementationDependencies"], ["WU-011"])
        self.assertEqual(cs.check_state(state(tasks)), [])


if __name__ == "__main__":
    unittest.main()
