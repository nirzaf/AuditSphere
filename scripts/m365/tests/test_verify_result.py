"""WU-017 checks for verify-result.py using synthetic envelopes."""

from __future__ import annotations

import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("verify_result", HERE / "verify-result.py")
vr = importlib.util.module_from_spec(spec)
spec.loader.exec_module(vr)

PLANNED = {"schemaVersion": 1, "repositoryCommit": "0" * 40, "mode": "Plan", "status": "PLANNED",
           "tenantId": "00000000-0000-4000-8000-000000000001", "planSha256": "a" * 64,
           "mutationsAttempted": 0, "mutationsConfirmed": 0, "networkCalls": 0, "changes": [], "checks": [],
           "blockedPrerequisites": [], "redactionsApplied": True}


class VerifyResultTests(unittest.TestCase):
    def run_cli(self, envelope, *args):
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as handle:
            json.dump(envelope, handle)
        return vr.main([handle.name, *args])

    def test_planned_offline_envelope_is_accepted(self):
        self.assertEqual(self.run_cli(PLANNED, "--expect", "PLANNED", "--plan-sha256", "a" * 64), 0)

    def test_planned_with_network_or_mutation_is_rejected(self):
        for field in ("networkCalls", "mutationsAttempted"):
            bad = dict(PLANNED, **{field: 1})
            self.assertEqual(self.run_cli(bad, "--expect", "PLANNED"), 1)

    def test_verified_requires_passing_mandatory_checks(self):
        verified = dict(PLANNED, status="VERIFIED", mode="ReadOnly")
        self.assertEqual(self.run_cli(verified, "--expect", "VERIFIED"), 1)
        verified["checks"] = [{"id": "c", "state": "SKIPPED"}]
        self.assertEqual(self.run_cli(verified, "--expect", "VERIFIED"), 1)
        verified["checks"] = [{"id": "c", "state": "PASS"}]
        self.assertEqual(self.run_cli(verified, "--expect", "VERIFIED"), 0)

    def test_unknown_status_digest_mismatch_and_secret_fields_are_rejected(self):
        self.assertEqual(self.run_cli(dict(PLANNED, status="DONE"), "--expect", "PLANNED"), 1)
        self.assertEqual(self.run_cli(PLANNED, "--expect", "PLANNED", "--plan-sha256", "b" * 64), 1)
        leaked = copy.deepcopy(PLANNED)
        leaked["checks"] = [{"id": "c", "state": "PASS", "observed": {"access_token": "x"}}]
        self.assertEqual(self.run_cli(leaked, "--expect", "PLANNED"), 1)

    def test_malformed_evidence_is_rejected_without_crashing(self):
        verified = dict(PLANNED, status="VERIFIED", mode="ReadOnly", checks=[{"id": "c", "state": "PASS"}])
        cases = [dict(verified, **{name: value})
                 for name, value in (("checks", None), ("checks", "PASS"), ("checks", [None]),
                                     ("checks", [{"state": "PASS"}]), ("checks", [{"id": " ", "state": "PASS"}]),
                                     ("changes", {}), ("blockedPrerequisites", ""), ("schemaVersion", True),
                                     ("status", []), ("mutationsConfirmed", 1),
                                     ("mutationsAttempted", 1), ("mode", "Unknown"))]
        for envelope in cases:
            with self.subTest(envelope=envelope):
                self.assertEqual(self.run_cli(envelope, "--expect", "VERIFIED"), 1)

    def test_string_counters_are_not_coerced(self):
        self.assertEqual(self.run_cli(dict(PLANNED, networkCalls="0"), "--expect", "PLANNED"), 1)


if __name__ == "__main__":
    unittest.main()
