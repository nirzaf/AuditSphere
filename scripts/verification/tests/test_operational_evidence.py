"""Focused tests for the offline WU-038 operational evidence verifier."""

from __future__ import annotations

import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "scripts" / "verification" / "verify-operational-evidence.py"
CONTRACT = ROOT / "docs" / "spec" / "completion-operational-evidence.json"
SPEC = importlib.util.spec_from_file_location("verify_operational_evidence", SCRIPT)
verify = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(verify)


class OperationalEvidenceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.document = verify.load_document(CONTRACT)

    def _write_scan_capture(
        self,
        root: Path,
        scan_id: str,
        commit: str,
        report: object,
        version: str,
        feeds: list[str],
        exit_code: int,
    ) -> dict[str, object]:
        captured = "2026-10-23T00:00:00Z"
        captured_feeds = [] if scan_id == "npm" else feeds
        captured_feed_state = {
            "nuget": "IDENTIFIED",
            "npm": "UNKNOWN",
            "gitleaks": "NOT_APPLICABLE",
        }[scan_id]
        directory = root / "artifacts" / "completion"
        directory.mkdir(parents=True, exist_ok=True)
        report_path = f"artifacts/completion/{scan_id}-report.json"
        report_bytes = json.dumps(report, separators=(",", ":")).encode()
        (root / report_path).write_bytes(report_bytes)
        report_reference = {
            "id": f"{scan_id.upper()}-REPORT-001",
            "path": report_path,
            "sha256": hashlib.sha256(report_bytes).hexdigest(),
            "sourceCommit": commit,
        }
        config_keys = verify.CAPTURE_CONFIG_KEYS[scan_id]
        config_hashes = {key: "ABSENT" for key in config_keys}
        if scan_id == "nuget":
            config_hashes["repository"] = hashlib.sha256((root / "NuGet.Config").read_bytes()).hexdigest()
        if scan_id == "npm":
            project_npmrc = root / "src/AuditSphereOps.Ui/.npmrc"
            if project_npmrc.is_file():
                config_hashes["project"] = hashlib.sha256(project_npmrc.read_bytes()).hexdigest()
        capture = {
            "schemaVersion": 1,
            "scanId": scan_id,
            "source": {"repository": "nirzaf/AuditSphere", "commit": commit, "workingTree": "CLEAN"},
            "inputHashes": verify._scan_input_hashes(scan_id, root),
            "invocation": {
                "argv": verify.SCAN_ARGV[scan_id],
                "workingDirectory": verify.SCAN_WORKING_DIRECTORIES[scan_id],
                "exitCode": exit_code,
                "startedAtUtc": captured,
                "completedAtUtc": captured,
                "stdoutSha256": hashlib.sha256(report_bytes).hexdigest(),
                "stderrSha256": hashlib.sha256(b"").hexdigest(),
                "stderrBytes": 0,
            },
            "tool": {"executable": verify.SCAN_EXECUTABLES[scan_id], "version": version},
            "feed": {
                "state": captured_feed_state,
                "identities": captured_feeds,
                "captureMethod": verify.CAPTURE_FEED_METHODS[scan_id],
                "configurationHashes": config_hashes,
            },
            "report": report_reference,
            "capturedAtUtc": captured,
        }
        capture_bytes = json.dumps(capture, separators=(",", ":")).encode()
        capture_path = f"artifacts/completion/{scan_id}-capture.json"
        (root / capture_path).write_bytes(capture_bytes)
        return {
            "runState": "COMPLETED",
            "failureClass": None,
            "sourceCommit": commit,
            "tool": {"name": verify.SCAN_TOOL_NAMES[scan_id], "version": version},
            "feed": {
                "state": captured_feed_state,
                "identities": captured_feeds,
            },
            "capturedAtUtc": captured,
            "evidence": {
                "id": f"{scan_id.upper()}-CAPTURE-001",
                "path": capture_path,
                "sha256": hashlib.sha256(capture_bytes).hexdigest(),
                "sourceCommit": commit,
            },
            "findings": [],
        }

    def test_current_record_is_incomplete_and_local_restore_stays_local(self) -> None:
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            exit_code = verify.main([str(CONTRACT)])
        result = json.loads(output.getvalue())

        self.assertEqual(2, exit_code)
        self.assertEqual("INCOMPLETE", result["status"])
        self.assertEqual("DECLARED_UNVERIFIED", result["sourceIdentity"])
        self.assertIn(result["sourceObservation"]["workingTree"], {"CLEAN", "DIRTY", "UNKNOWN"})
        self.assertEqual("UNKNOWN", result["securityScans"]["nuget"])
        self.assertEqual("LOCAL_ONLY", result["recovery"]["localRestore"])
        self.assertEqual("INCOMPLETE", result["recovery"]["production"])
        self.assertEqual("HASHED_REFERENCE_ONLY_NOT_AUTHENTICATED", result["approvalReferences"])
        self.assertEqual("NOT_ASSESSED", result["operationalAcceptance"])

    def test_missing_target_and_measurement_are_incomplete(self) -> None:
        document = copy.deepcopy(self.document)
        del document["targets"]["rpoSeconds"]
        del document["measurements"]["queries"]

        result = verify.evaluate(document, ROOT)

        self.assertEqual("INCOMPLETE", result["status"])
        self.assertIn("TARGET_MISSING_RPOSECONDS", result["blockerCodes"])
        self.assertIn("MEASUREMENT_MISSING_QUERIES", result["blockerCodes"])

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            artifact_path = root / "artifacts" / "completion" / "queries.json"
            artifact_path.parent.mkdir(parents=True)
            commit = "e" * 40
            tool = {"name": "collector", "version": "1.0"}
            measured_at = "2026-10-23T00:00:00Z"
            declared_metrics = {
                "p95Milliseconds": 1.0,
                "commandCount": 2,
                "peakMemoryBytes": 512,
                "sampleCount": 5,
            }

            def observe(
                artifact_metrics: dict[str, int | float],
                manifest_metrics: dict[str, int | float],
                extra_fields: dict[str, object] | None = None,
            ):
                report = {
                    "schemaVersion": 1,
                    "measurementType": "queries",
                    "sourceCommit": commit,
                    "tool": tool,
                    "measuredAtUtc": measured_at,
                    "metrics": artifact_metrics,
                }
                if extra_fields:
                    report.update(extra_fields)
                raw = json.dumps(report, separators=(",", ":")).encode()
                artifact_path.write_bytes(raw)
                measurement = {
                    **manifest_metrics,
                    "sourceCommit": commit,
                    "tool": tool,
                    "measuredAtUtc": measured_at,
                    "evidence": {
                        "id": "QUERIES-REPORT-001",
                        "path": "artifacts/completion/queries.json",
                        "sha256": hashlib.sha256(raw).hexdigest(),
                        "sourceCommit": commit,
                    },
                }
                errors: set[str] = set()
                blockers: set[str] = set()
                status, _ = verify._measurement_status(
                    "queries", measurement, commit, True, root, errors, blockers, {},
                )
                return status, errors, blockers, measurement, report

            supported = observe(declared_metrics, declared_metrics)
            self.assertEqual("OBSERVED", supported[0])
            self.assertEqual(set(), supported[1])

            unsupported = observe(declared_metrics, declared_metrics, {"rawOutput": "unparsed"})
            self.assertEqual("UNKNOWN", unsupported[0])
            self.assertIn("MEASUREMENT_CONTENT_UNKNOWN_QUERIES", unsupported[2])

            high_report_metrics = {**declared_metrics, "p95Milliseconds": 900.0}
            mismatched = observe(high_report_metrics, declared_metrics)
            self.assertEqual("UNKNOWN", mismatched[0])
            self.assertIn("MEASUREMENT_CONTENT_UNKNOWN_QUERIES", mismatched[2])

            zero_sample_metrics = {**declared_metrics, "sampleCount": 0}
            zero_sample = observe(zero_sample_metrics, declared_metrics)
            self.assertEqual("UNKNOWN", zero_sample[0])
            self.assertIn("MEASUREMENT_CONTENT_UNKNOWN_QUERIES", zero_sample[2])

            for measurement_name, metric_name in sorted(verify.POSITIVE_INTEGER_METRICS):
                with self.subTest(measurement=measurement_name, metric=metric_name):
                    metrics = {
                        key: (1 if kind == "integer" else 1.0)
                        for key, kind in verify.MEASUREMENT_METRICS[measurement_name].items()
                    }
                    metrics[metric_name] = 0
                    measurement = {
                        "sourceCommit": commit,
                        "tool": tool,
                        "measuredAtUtc": measured_at,
                    }
                    report = {
                        "schemaVersion": 1,
                        "measurementType": measurement_name,
                        **measurement,
                        "metrics": metrics,
                    }
                    self.assertFalse(verify._measurement_report_matches(
                        measurement_name, measurement, metrics, report,
                    ))

    def test_scan_failures_and_missing_report_cannot_become_no_findings(self) -> None:
        document = copy.deepcopy(self.document)
        failed = document["securityScans"]["nuget"]
        failed["runState"] = "FAILED"
        failed["failureClass"] = "FEED_UNAVAILABLE"
        result = verify.evaluate(document, ROOT)
        self.assertEqual("UNKNOWN", result["securityScans"]["nuget"])

        scan = document["securityScans"]["npm"]
        scan.update({
            "runState": "COMPLETED",
            "sourceCommit": "a" * 40,
            "tool": {"name": verify.SCAN_TOOL_NAMES["npm"], "version": "10.0.0"},
            "feed": {"state": "IDENTIFIED", "identities": ["registry.npmjs.org"]},
            "capturedAtUtc": "2026-10-23T00:00:00Z",
            "findings": [],
        })
        result = verify.evaluate(document, ROOT)
        self.assertEqual("UNKNOWN", result["securityScans"]["npm"])
        self.assertIn("SCAN_EVIDENCE_UNKNOWN_NPM", result["blockerCodes"])

    def test_self_declared_clean_source_is_not_exact_when_git_observes_dirty_tree(self) -> None:
        document = copy.deepcopy(self.document)
        document["source"].update({"commit": "a" * 40, "workingTree": "CLEAN"})
        observation = {"rootMatches": True, "commit": "a" * 40, "workingTree": "DIRTY"}

        with patch.object(verify, "_observe_git_source", return_value=observation):
            result = verify.evaluate(document, ROOT)

        self.assertEqual("DECLARED_UNVERIFIED", result["sourceIdentity"])
        self.assertIn("SOURCE_WORKTREE_DIRTY", result["blockerCodes"])

    def test_hashed_npm_report_high_finding_cannot_match_empty_manifest_findings(self) -> None:
        document = copy.deepcopy(self.document)
        commit = "b" * 40
        document["source"].update({"commit": commit, "workingTree": "CLEAN"})
        report = {
            "auditReportVersion": 2,
            "metadata": {"vulnerabilities": {
                "total": 1, "critical": 0, "high": 1, "moderate": 0, "low": 0, "info": 0,
            }},
            "vulnerabilities": {"fixture-package": {
                "name": "fixture-package",
                "severity": "high",
                "via": [{"source": 1001, "severity": "high"}],
            }},
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            package_dir = root / "src" / "AuditSphereOps.Ui"
            package_dir.mkdir(parents=True)
            (package_dir / "package.json").write_text("{}", encoding="utf-8")
            (package_dir / "package-lock.json").write_text("{}", encoding="utf-8")
            document["securityScans"]["npm"] = self._write_scan_capture(
                root, "npm", commit, report, "10.0.0", ["registry.npmjs.org"], 1,
            )
            exact_clean = {"rootMatches": True, "commit": commit, "workingTree": "CLEAN"}
            with patch.object(verify, "_observe_git_source", return_value=exact_clean):
                result = verify.evaluate(document, root)

        self.assertEqual("UNKNOWN", result["securityScans"]["npm"])
        self.assertIn("scan-report-manifest-mismatch", result["errorCodes"])
        self.assertIn("SCAN_REPORT_MANIFEST_MISMATCH_NPM", result["blockerCodes"])
        self.assertNotEqual("NO_FINDINGS", result["securityScans"]["npm"])

    def test_nuget_sparse_zero_report_requires_bound_completed_capture(self) -> None:
        commit = "c" * 40
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative in (
                "AuditSphereOps.slnx", "global.json", "Directory.Packages.props",
                "Directory.Build.props", "NuGet.Config", "src/obj/project.assets.json",
                "src/Project.csproj", "src/packages.lock.json",
            ):
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("fixture", encoding="utf-8")
            report = {
                "version": 1,
                "parameters": "--vulnerable --include-transitive",
                "sources": ["https://api.nuget.org/v3/index.json"],
                "projects": [{"path": f"src/Project-{index}.csproj"} for index in range(9)],
            }
            scan = self._write_scan_capture(
                root, "nuget", commit, report, "10.0.300", ["api.nuget.org"], 0,
            )
            errors: set[str] = set()
            blockers: set[str] = set()
            status = verify._scan_status(
                "nuget", scan, commit, True, root, errors, blockers, {},
            )
            self.assertEqual("NO_FINDINGS", status)
            self.assertEqual(set(), errors)

            input_path = root / "src/Project.csproj"
            input_path.write_text("changed", encoding="utf-8")
            blockers = set()
            status = verify._scan_status(
                "nuget", scan, commit, True, root, set(), blockers, {},
            )
            self.assertEqual("UNKNOWN", status)
            self.assertIn("SCAN_INPUTS_UNKNOWN_NUGET", blockers)
            input_path.write_text("fixture", encoding="utf-8")

            envelope_path = root / scan["evidence"]["path"]
            envelope = json.loads(envelope_path.read_text(encoding="utf-8"))
            envelope["invocation"]["exitCode"] = 1
            envelope_bytes = json.dumps(envelope, separators=(",", ":")).encode()
            envelope_path.write_bytes(envelope_bytes)
            scan["evidence"]["sha256"] = hashlib.sha256(envelope_bytes).hexdigest()
            status = verify._scan_status(
                "nuget", scan, commit, True, root, set(), set(), {},
            )
            self.assertEqual("UNKNOWN", status)

            envelope["invocation"]["exitCode"] = 0
            envelope["invocation"]["stdoutSha256"] = "f" * 64
            envelope_bytes = json.dumps(envelope, separators=(",", ":")).encode()
            envelope_path.write_bytes(envelope_bytes)
            scan["evidence"]["sha256"] = hashlib.sha256(envelope_bytes).hexdigest()
            blockers = set()
            status = verify._scan_status(
                "nuget", scan, commit, True, root, set(), blockers, {},
            )
            self.assertEqual("UNKNOWN", status)
            self.assertIn("SCAN_REPORT_HASH_UNKNOWN_NUGET", blockers)

            envelope["invocation"]["stdoutSha256"] = envelope["report"]["sha256"]
            envelope["invocation"]["argv"][-1] = "--restore"
            envelope_bytes = json.dumps(envelope, separators=(",", ":")).encode()
            envelope_path.write_bytes(envelope_bytes)
            scan["evidence"]["sha256"] = hashlib.sha256(envelope_bytes).hexdigest()
            blockers = set()
            status = verify._scan_status(
                "nuget", scan, commit, True, root, set(), blockers, {},
            )
            self.assertEqual("UNKNOWN", status)
            self.assertIn("SCAN_CAPTURE_UNKNOWN_NUGET", blockers)

            envelope["invocation"]["argv"] = verify.SCAN_ARGV["nuget"]
            envelope["source"]["workingTree"] = "DIRTY"
            envelope_bytes = json.dumps(envelope, separators=(",", ":")).encode()
            envelope_path.write_bytes(envelope_bytes)
            scan["evidence"]["sha256"] = hashlib.sha256(envelope_bytes).hexdigest()
            blockers = set()
            status = verify._scan_status(
                "nuget", scan, commit, False, root, set(), blockers, {},
            )
            self.assertEqual("UNKNOWN", status)
            self.assertIn("SCAN_SOURCE_UNKNOWN_NUGET", blockers)

            self.assertIsNone(verify._parse_scan_report("nuget", report))
            self.assertIsNotNone(verify._parse_scan_report(
                "nuget", report, allow_sparse_nuget=True,
            ))

    def test_npm_registry_configuration_does_not_claim_audit_feed_identity(self) -> None:
        commit = "d" * 40
        report = {
            "auditReportVersion": 2,
            "metadata": {"vulnerabilities": {
                "total": 0, "critical": 0, "high": 0, "moderate": 0, "low": 0, "info": 0,
            }},
            "vulnerabilities": {},
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            package_dir = root / "src" / "AuditSphereOps.Ui"
            package_dir.mkdir(parents=True)
            (package_dir / "package.json").write_text("{}", encoding="utf-8")
            (package_dir / "package-lock.json").write_text("{}", encoding="utf-8")
            (package_dir / ".npmrc").write_text("registry=https://registry.npmjs.org/\n", encoding="utf-8")
            scan = self._write_scan_capture(
                root, "npm", commit, report, "11.19.0", ["registry.npmjs.org"], 0,
            )
            self.assertEqual("UNKNOWN", scan["feed"]["state"])
            self.assertEqual([], scan["feed"]["identities"])
            blockers: set[str] = set()
            status = verify._scan_status(
                "npm", scan, commit, True, root, set(), blockers, {},
            )

        self.assertEqual("UNKNOWN", status)
        self.assertIn("SCAN_FEED_UNKNOWN_NPM", blockers)
        self.assertNotIn("SCAN_REPORT_MANIFEST_MISMATCH_NPM", blockers)

    def test_scan_parsers_reject_incomplete_nuget_and_count_redacted_gitleaks_entries(self) -> None:
        self.assertIsNone(verify._parse_scan_report("nuget", {
            "version": 1,
            "parameters": "--vulnerable --include-transitive",
            "sources": ["https://api.nuget.org/v3/index.json"],
            "projects": [{"path": "src/Example.csproj"}],
        }))
        report = [{
            "Fingerprint": "fingerprint-001",
            "RuleID": "generic-api-key",
            "Commit": "c" * 40,
            "Secret": "<redacted>",
        }, {
            "Fingerprint": "fingerprint-002",
            "RuleID": "generic-api-key",
            "Commit": "d" * 40,
            "Secret": "<redacted>",
        }]

        parsed = verify._parse_scan_report("gitleaks", report)

        self.assertIsNotNone(parsed)
        assert parsed is not None
        self.assertEqual(2, len(parsed[0]))
        self.assertTrue(all(finding == ("SECRET", "UNKNOWN") for finding in parsed[0].values()))
        self.assertEqual([], parsed[1])

    def test_duplicate_keys_wrong_types_and_unrecognized_approval_claims_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "duplicate.json"
            path.write_text('{"schemaVersion":1,"schemaVersion":1}', encoding="utf-8")
            with self.assertRaises(verify.InputError) as caught:
                verify.load_document(path)
            self.assertEqual("duplicate-json-key", caught.exception.code)

        wrong_type = copy.deepcopy(self.document)
        wrong_type["targets"]["rpoSeconds"]["value"] = True
        self.assertEqual("INVALID", verify.evaluate(wrong_type, ROOT)["status"])

        zero_rpo = copy.deepcopy(self.document)
        zero_rpo["targets"]["rpoSeconds"]["value"] = 0
        zero_rpo_result = verify.evaluate(zero_rpo, ROOT)
        self.assertNotIn("invalid-target-value", zero_rpo_result["errorCodes"])
        self.assertEqual("INCOMPLETE", zero_rpo_result["targets"]["rpoSeconds"])
        self.assertIn(
            "TARGET_APPROVAL_EVIDENCE_MISSING_RPOSECONDS",
            zero_rpo_result["blockerCodes"],
        )
        self.assertFalse(verify._valid_target_value("rtoSeconds", 0))

        production = {
            "observedRpoSeconds": 0,
            "observedRtoSeconds": 1,
            **{field: None for field in verify.PRODUCTION_EVIDENCE_FIELDS},
        }
        errors: set[str] = set()
        blockers: set[str] = set()
        verify._validate_production_recovery(production, None, False, ROOT, errors, blockers, {})
        self.assertNotIn("invalid-production-recovery-value", errors)
        production["observedRtoSeconds"] = 0
        errors.clear()
        verify._validate_production_recovery(production, None, False, ROOT, errors, blockers, {})
        self.assertIn("invalid-production-recovery-value", errors)

        fabricated = copy.deepcopy(self.document)
        fabricated["targets"]["rpoSeconds"]["approved"] = True
        result = verify.evaluate(fabricated, ROOT)
        self.assertEqual("INVALID", result["status"])
        self.assertNotIn("approved", json.dumps(result).lower())

    def test_secret_fields_and_untrusted_evidence_paths_are_not_echoed(self) -> None:
        secret = copy.deepcopy(self.document)
        secret["apiKey"] = "secret-sentinel-value"
        result = verify.evaluate(secret, ROOT)
        self.assertEqual("INVALID", result["status"])
        self.assertNotIn("secret-sentinel-value", json.dumps(result))

        traversal = copy.deepcopy(self.document)
        traversal["targets"]["rpoSeconds"]["approvalEvidence"] = {
            "id": "APPROVAL-001",
            "path": "docs/evidence/../../.env",
            "sha256": "b" * 64,
            "sourceCommit": None,
        }
        result = verify.evaluate(traversal, ROOT)
        self.assertEqual("INVALID", result["status"])
        self.assertNotIn(".env", json.dumps(result))

        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            root = base / "repo"
            allowed = root / "artifacts" / "completion"
            allowed.mkdir(parents=True)
            outside = base / "outside.json"
            outside.write_text('{"outside":true}', encoding="utf-8")
            (allowed / "outside.json").symlink_to(outside)
            reference = {
                "id": "OUTSIDE-001",
                "path": "artifacts/completion/outside.json",
                "sha256": hashlib.sha256(outside.read_bytes()).hexdigest(),
                "sourceCommit": None,
            }
            opened_outside: list[Path] = []
            original_open = Path.open

            def track_open(path: Path, *args: object, **kwargs: object):
                if path == outside.resolve():
                    opened_outside.append(path)
                return original_open(path, *args, **kwargs)

            with patch.object(verify.Path, "open", new=track_open):
                errors: set[str] = set()
                blockers: set[str] = set()
                self.assertFalse(verify._verify_reference(reference, root, errors, blockers, {}))
                self.assertEqual((False, None), verify._read_report_reference(
                    reference, root, "npm", errors, blockers,
                ))

            self.assertIn("unsafe-evidence-path", errors)
            self.assertEqual([], opened_outside)


if __name__ == "__main__":
    unittest.main()
