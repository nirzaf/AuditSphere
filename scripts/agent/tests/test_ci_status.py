"""RUN-003 acceptance tests for ci-status.py using saved fixtures (no network, no gh)."""

from __future__ import annotations

import contextlib
import datetime as dt
import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

AGENT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(AGENT))
spec = importlib.util.spec_from_file_location("ci_status", AGENT / "ci-status.py")
ci = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci)

CANDIDATE = "e5c24072d24b5de1e7d12d13d8fc0b1192323a48"
OTHER = "48816d27761d71fa71581c514789844afba02d9b"
NOW = dt.datetime(2026, 10, 22, 18, 30, tzinfo=dt.timezone.utc)
WORKFLOW = """jobs:
  resolve-ref:
    name: Resolve · Requested ref
    timeout-minutes: 5
  dotnet-tests:
    name: Test · ${{ matrix.name }}
    timeout-minutes: 180
"""


def job(name, status="completed", conclusion="success", started="2026-10-22T16:58:08Z", job_id=1):
    return {"id": job_id, "name": name, "status": status, "conclusion": conclusion, "started_at": started,
            "completed_at": None if status != "completed" else "2026-10-22T17:10:00Z"}


class FakeGh:
    def __init__(self, run, jobs, source=None):
        self.run, self.jobs, self.source = run, jobs, source

    def __call__(self, *args, raw=False):
        path = next(a for a in args if a.startswith("repos/"))
        if path.endswith("/artifacts?per_page=100"):
            return {"artifacts": [{"id": 9, "name": "release-verification-source", "expired": False}] if self.source else []}
        if "/artifacts/9/zip" in path:
            import io, zipfile
            buffer = io.BytesIO()
            with zipfile.ZipFile(buffer, "w") as archive:
                archive.writestr("source.json", json.dumps({"runId": self.run["id"], "resolvedCommit": self.source}))
            return buffer.getvalue()
        if "/jobs?per_page=100" in path:
            return "\n".join(json.dumps(j) for j in self.jobs).encode()
        if "/jobs/" in path and path.endswith("/logs"):
            return f"HEAD is now at {OTHER[:7]}\n{OTHER}\n".encode()
        return self.run


class CiStatusTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.workflow = Path(self.tmp.name, "wf.yml")
        self.workflow.write_text(WORKFLOW, encoding="utf-8")
        self._gh, self._now = ci.gh, ci.now
        ci.now = lambda: NOW

    def tearDown(self):
        ci.gh, ci.now = self._gh, self._now
        self.tmp.cleanup()

    def observe(self, run, jobs, source):
        ci.gh = FakeGh(run, jobs, source)
        return ci.observe("nirzaf/AuditSphere", run["id"], CANDIDATE, self.workflow)

    def test_dispatch_keeps_stdout_json_when_gh_prints_a_run_url(self):
        run = {"id": 42, "run_attempt": 1, "head_sha": CANDIDATE,
               "display_title": f"release as-{CANDIDATE[:12]}-fixed", "created_at": NOW.isoformat()}

        def noisy_dispatch(*args, **kwargs):
            if not kwargs.get("capture_output") and kwargs.get("stdout") not in (ci.subprocess.PIPE, ci.subprocess.DEVNULL):
                print("https://github.com/nirzaf/AuditSphere/actions/runs/42")
            return ci.subprocess.CompletedProcess(args[0], 0, stdout=b"run URL\n", stderr=b"")

        output = io.StringIO()
        with patch.object(ci.subprocess, "run", side_effect=noisy_dispatch) as invoked, \
                patch.object(ci, "gh", return_value={"workflow_runs": [run]}), \
                patch.object(ci.secrets, "token_hex", return_value="fixed"), \
                patch.object(ci.time, "sleep"), contextlib.redirect_stdout(output):
            code = ci.main(["dispatch", "--repo", "nirzaf/AuditSphere", "--candidate", CANDIDATE])
        evidence = json.loads(output.getvalue())
        self.assertEqual((code, evidence["runId"], evidence["requestedRef"], evidence["status"]),
                         (11, 42, CANDIDATE, "QUEUED"))
        self.assertIsNone(evidence["resolvedSha"])
        invoked.assert_called_once()

    def test_running_api_and_e2e_waits_and_continues(self):
        run = {"id": 7, "status": "in_progress", "conclusion": None, "head_sha": OTHER}
        evidence, code = self.observe(run, [job("Resolve · Requested ref"), job("Test · .NET API", "in_progress", None)], CANDIDATE)
        self.assertEqual((evidence["status"], code), ("RUNNING", 11))
        self.assertFalse(evidence["fullPassForExpected"])

    def test_wrong_candidate_exits_nonzero_and_is_never_a_pass(self):
        run = {"id": 7, "status": "completed", "conclusion": "success", "head_sha": OTHER}
        evidence, code = self.observe(run, [job("Resolve · Requested ref"), job("Test · .NET API")], OTHER)
        self.assertEqual(code, 3)
        self.assertEqual(evidence["sourceMatch"], "MISMATCH")
        self.assertFalse(evidence["fullPassForExpected"])

    def test_resolved_source_from_log_when_artifact_missing(self):
        run = {"id": 7, "status": "in_progress", "conclusion": None, "head_sha": OTHER}
        evidence, code = self.observe(run, [job("Resolve · Requested ref"), job("Test · .NET API", "in_progress", None)], None)
        self.assertEqual(evidence["resolvedSha"], OTHER)
        self.assertEqual(code, 3)

    def test_failed_cancelled_timed_out_prevent_full_pass(self):
        for conclusion, expected in (("failure", "FAIL"), ("cancelled", "CANCELLED"), ("timed_out", "TIMED_OUT")):
            run = {"id": 7, "status": "completed", "conclusion": conclusion, "head_sha": CANDIDATE}
            evidence, code = self.observe(run, [job("Resolve · Requested ref"), job("Test · .NET API", conclusion=conclusion)], CANDIDATE)
            self.assertEqual((evidence["status"], code), (expected, 1))
            self.assertFalse(evidence["fullPassForExpected"])

    def test_success_with_a_failed_job_is_not_a_pass(self):
        run = {"id": 7, "status": "completed", "conclusion": "success", "head_sha": CANDIDATE}
        evidence, _ = self.observe(run, [job("Resolve · Requested ref"), job("Test · .NET API", conclusion="failure")], CANDIDATE)
        self.assertEqual(evidence["status"], "FAIL")

    def test_exact_candidate_with_all_jobs_successful_passes(self):
        run = {"id": 7, "status": "completed", "conclusion": "success", "head_sha": OTHER}
        evidence, code = self.observe(run, [job("Resolve · Requested ref"), job("Test · .NET API"), job("Test · .NET Playwright E2E")], CANDIDATE)
        self.assertEqual((evidence["status"], code, evidence["fullPassForExpected"]), ("PASS", 0, True))

    def test_job_beyond_timeout_is_stalled_not_passing(self):
        run = {"id": 7, "status": "in_progress", "conclusion": None, "head_sha": CANDIDATE}
        stalled = job("Test · .NET API", "in_progress", None, started="2026-10-22T14:00:00Z")
        evidence, code = self.observe(run, [job("Resolve · Requested ref"), stalled], CANDIDATE)
        self.assertEqual((evidence["status"], code), ("CI_STALLED", 12))


if __name__ == "__main__":
    unittest.main()
