"""Tests that the PostgreSQL 18-to-19 inventory stays read-only and fail-closed."""

import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
PREFLIGHT = REPOSITORY_ROOT / "scripts/db/postgresql_major_upgrade_preflight.py"

BASE_REPORT = {
    "observedAtUtc": "2026-11-23T00:00:00Z",
    "source": {
        "serverVersionNum": 180006,
        "serverVersion": "18.6",
        "currentDatabase": "auditsphere",
        "currentUser": "auditsphere_preflight",
        "transactionReadOnly": True,
    },
    "catalog": {
        "databaseCount": 3,
        "currentDatabaseEncoding": "UTF8",
        "currentDatabaseCollation": "C.UTF-8",
        "currentDatabaseCtype": "C.UTF-8",
        "invalidIdentifierNames": [],
        "tablespaces": ["pg_default", "pg_global"],
        "currentDatabaseExtensions": [{"name": "plpgsql", "version": "1.0", "credential": "nested-secret"}],
        "btreeGistInetCidrIndexes": [],
        "elevatedRoles": [{
            "name": "auditsphere_admin",
            "superuser": True,
            "createRole": True,
            "createDatabase": True,
            "replication": False,
            "bypassRowSecurity": False,
            "password": "nested-secret",
        }],
    },
    "serverSettings": {
        "standardConformingStrings": "on",
        "ioWorkersSettingPresent": True,
        "maxLocksPerTransaction": {"value": "64", "source": "configuration file"},
    },
    # Unknown future fields must not leak through the output allowlist.
    "password": "never-print-this",
}


class PostgreSqlMajorUpgradePreflightTests(unittest.TestCase):
    def make_fake_psql(self, directory: Path, *, output: str = "", exit_code: int = 0, stderr: str = "") -> Path:
        executable = directory / "psql"
        executable.write_text(
            "#!" + sys.executable + "\n"
            "import json, os, sys\n"
            "from pathlib import Path\n"
            "Path(os.environ['PREFLIGHT_ARGS_FILE']).write_text(json.dumps(sys.argv[1:]))\n"
            "sys.stdout.write(os.environ.get('PREFLIGHT_STDOUT', ''))\n"
            "sys.stderr.write(os.environ.get('PREFLIGHT_STDERR', ''))\n"
            "raise SystemExit(int(os.environ.get('PREFLIGHT_EXIT_CODE', '0')))\n"
        )
        executable.chmod(0o755)
        return executable

    def run_preflight(self, directory: Path, *, env_overrides: dict[str, str] | None = None):
        env = os.environ.copy()
        env.update({
            "PATH": str(directory),
            "PGSERVICE": "audit-preflight",
            "PREFLIGHT_ARGS_FILE": str(directory / "args.json"),
            "PREFLIGHT_STDOUT": json.dumps(BASE_REPORT),
            "PREFLIGHT_STDERR": "",
            "PREFLIGHT_EXIT_CODE": "0",
        })
        if env_overrides:
            env.update(env_overrides)
        return subprocess.run(
            [sys.executable, str(PREFLIGHT)],
            check=False,
            capture_output=True,
            text=True,
            env=env,
        )

    def test_uses_named_service_and_only_a_read_only_inventory_transaction(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            self.make_fake_psql(directory)

            result = self.run_preflight(directory)

            self.assertEqual(0, result.returncode, result.stderr)
            report = json.loads(result.stdout)
            self.assertEqual("REVIEW_REQUIRED", report["preflightStatus"])
            self.assertFalse(report["cutoverQualified"])
            self.assertEqual(0, report["mutationsPerformed"])
            self.assertTrue(report["source"]["transactionReadOnly"])
            self.assertNotIn("never-print-this", result.stdout)
            self.assertNotIn("nested-secret", result.stdout)

            args = json.loads((directory / "args.json").read_text())
            self.assertIn("--no-password", args)
            self.assertIn("service=audit-preflight", args)
            sql = args[args.index("--command") + 1]
            self.assertRegex(sql, r"(?is)^\s*BEGIN READ ONLY;")
            self.assertIn("SET LOCAL statement_timeout", sql)
            self.assertRegex(sql, r"(?is)COMMIT;\s*$")
            self.assertIsNone(re.search(r"(?im)^\s*(INSERT|UPDATE|DELETE|CREATE|DROP|ALTER|TRUNCATE|COPY|CALL|DO)\b", sql))

    def test_password_environment_variable_is_rejected_without_echoing_it(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            self.make_fake_psql(directory)

            result = self.run_preflight(directory, env_overrides={"PGPASSWORD": "sensitive-test-value"})

            self.assertEqual(2, result.returncode)
            self.assertIn("PGPASSFILE", result.stderr)
            self.assertNotIn("sensitive-test-value", result.stderr)
            self.assertFalse((directory / "args.json").exists())

    def test_invalid_service_name_is_rejected_before_invoking_psql(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            self.make_fake_psql(directory)

            result = self.run_preflight(directory, env_overrides={"PGSERVICE": "valid;drop"})

            self.assertEqual(2, result.returncode)
            self.assertIn("non-secret libpq service name", result.stderr)
            self.assertFalse((directory / "args.json").exists())

    def test_incompatible_names_and_index_candidates_are_reported_without_automatic_repair(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            report = json.loads(json.dumps(BASE_REPORT))
            report["catalog"]["invalidIdentifierNames"] = [{"kind": "role", "name": "legacy\nrole"}]
            report["catalog"]["btreeGistInetCidrIndexes"] = [{"schema": "public", "table": "example", "index": "idx", "operatorClass": "inet_ops"}]
            self.make_fake_psql(directory)

            result = self.run_preflight(directory, env_overrides={"PREFLIGHT_STDOUT": json.dumps(report)})

            self.assertEqual(0, result.returncode, result.stderr)
            parsed = json.loads(result.stdout)
            self.assertEqual("BLOCKED_PG19_IDENTIFIER_COMPATIBILITY", parsed["preflightStatus"])
            self.assertFalse(parsed["cutoverQualified"])
            self.assertEqual(0, parsed["mutationsPerformed"])
            self.assertEqual("legacy\nrole", parsed["catalog"]["invalidIdentifierNames"][0]["name"])

    def test_non_18_source_is_blocked_and_future_fields_are_redacted(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            report = json.loads(json.dumps(BASE_REPORT))
            report["source"]["serverVersionNum"] = 170006
            report["source"]["password"] = "nested-secret"
            self.make_fake_psql(directory)

            result = self.run_preflight(directory, env_overrides={"PREFLIGHT_STDOUT": json.dumps(report)})

            self.assertEqual(0, result.returncode, result.stderr)
            parsed = json.loads(result.stdout)
            self.assertEqual("BLOCKED_UNEXPECTED_SOURCE_MAJOR", parsed["preflightStatus"])
            self.assertNotIn("nested-secret", result.stdout)

    def test_btree_gist_inet_cidr_index_is_review_required_not_cutover_qualified(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            report = json.loads(json.dumps(BASE_REPORT))
            report["catalog"]["btreeGistInetCidrIndexes"] = [{
                "schema": "public",
                "table": "network_ranges",
                "index": "network_ranges_address_gist_idx",
                "operatorClass": "inet_ops",
            }]
            self.make_fake_psql(directory)

            result = self.run_preflight(directory, env_overrides={"PREFLIGHT_STDOUT": json.dumps(report)})

            self.assertEqual(0, result.returncode, result.stderr)
            parsed = json.loads(result.stdout)
            self.assertEqual("REVIEW_BTREE_GIST_INET_CIDR_INDEXES", parsed["preflightStatus"])
            self.assertFalse(parsed["cutoverQualified"])
            self.assertEqual(0, parsed["mutationsPerformed"])
            self.assertEqual("network_ranges_address_gist_idx", parsed["catalog"]["btreeGistInetCidrIndexes"][0]["index"])

    def test_psql_failure_suppresses_provider_output(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            self.make_fake_psql(directory)

            result = self.run_preflight(directory, env_overrides={
                "PREFLIGHT_EXIT_CODE": "2",
                "PREFLIGHT_STDERR": "connection failed with password=provider-secret",
            })

            self.assertEqual(1, result.returncode)
            self.assertIn("connection details and provider output were suppressed", result.stderr)
            self.assertNotIn("provider-secret", result.stderr)


if __name__ == "__main__":
    unittest.main()
