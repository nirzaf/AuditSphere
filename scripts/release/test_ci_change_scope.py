from __future__ import annotations

import unittest

import ci_change_scope


class CiChangeScopeTests(unittest.TestCase):
    def test_pg19_preflight_paths_use_the_database_script_gate(self) -> None:
        paths = [
            "scripts/db/postgresql_major_upgrade_preflight.py",
            "scripts/db/tests/test_postgresql_major_upgrade_preflight.py",
            "docs/operations/auditsphere-operations-runbook-postgresql-major-upgrade.md",
            "docs/execution/status.json",
        ]
        self.assertEqual("pg19-preflight-only", ci_change_scope.classify_paths(paths))
        self.assertFalse(ci_change_scope.requires_application_checks(paths))

    def test_each_pg19_preflight_file_uses_the_database_script_gate(self) -> None:
        for path in ci_change_scope.PG19_PREFLIGHT_PATHS:
            with self.subTest(path=path):
                self.assertEqual("pg19-preflight-only", ci_change_scope.classify_paths([path]))

    def test_missing_pg19_preflight_file_fails_closed(self) -> None:
        self.assertEqual(
            "application-paths",
            ci_change_scope.classify_paths(
                ["scripts/db/tests/test_postgresql_major_upgrade_preflight.py"],
                preflight_files_present=False,
            ),
        )

    def test_pg19_preflight_mixed_with_application_or_workflow_path_runs_full_checks(self) -> None:
        for path in (
            "src/AuditSphereOps.Api/Program.cs",
            ".github/workflows/ci.yml",
            "infra/hostinger-acceptance/compose.yaml",
            "scripts/db/migrations/20261123-example.sql",
        ):
            with self.subTest(path=path):
                self.assertEqual(
                    "application-paths",
                    ci_change_scope.classify_paths(
                        ["scripts/db/postgresql_major_upgrade_preflight.py", path]
                    ),
                )
                self.assertTrue(
                    ci_change_scope.requires_application_checks(
                        ["scripts/db/postgresql_major_upgrade_preflight.py", path]
                    )
                )

    def test_nonempty_markdown_docs_diff_can_skip_application_builds(self) -> None:
        self.assertFalse(
            ci_change_scope.requires_application_checks(
                ["docs/architecture/guide.md", "docs/execution/auditsphere-execution-current-slice.md"]
            )
        )

    def test_execution_status_ledger_can_accompany_docs_markdown(self) -> None:
        self.assertFalse(ci_change_scope.requires_application_checks(["docs/execution/status.json"]))
        self.assertFalse(
            ci_change_scope.requires_application_checks(
                ["docs/execution/status.json", "docs/execution/auditsphere-execution-current-slice.md"]
            )
        )

    def test_hostinger_acceptance_markdown_can_skip_application_builds(self) -> None:
        self.assertFalse(
            ci_change_scope.requires_application_checks(
                [
                    "infra/hostinger-acceptance/auditsphere-deployment-hostinger-acceptance-current.md",
                    "docs/execution/auditsphere-execution-current-slice.md",
                ]
            )
        )

    def test_hostinger_compose_changes_still_run_application_checks(self) -> None:
        for path in (
            "infra/hostinger-acceptance/compose.yaml",
            "infra/hostinger-acceptance/postgres-entrypoint-guard.sh",
        ):
            with self.subTest(path=path):
                self.assertTrue(
                    ci_change_scope.requires_application_checks(
                        [
                            "infra/hostinger-acceptance/auditsphere-deployment-hostinger-acceptance-current.md",
                            path,
                        ]
                    )
                )

    def test_unapproved_infrastructure_markdown_remains_full(self) -> None:
        self.assertTrue(
            ci_change_scope.requires_application_checks(
                ["infra/azure/operations.md"]
            )
        )

    def test_empty_diff_fails_closed(self) -> None:
        self.assertTrue(ci_change_scope.requires_application_checks([]))

    def test_any_non_markdown_or_non_docs_path_runs_application_checks(self) -> None:
        for path in (
            "src/AuditSphereOps.Api/Program.cs",
            ".github/workflows/ci.yml",
            "README.md",
            "docs/contracts/openapi.yaml",
            "docs/execution/arbitrary-state.json",
            "docs/architecture/adr/record.mdx",
        ):
            with self.subTest(path=path):
                self.assertTrue(ci_change_scope.requires_application_checks(["docs/guide.md", path]))

    def test_unsafe_or_ambiguous_paths_fail_closed(self) -> None:
        for path in ("docs/../src/Program.cs", "/docs/guide.md", "docs\\guide.md"):
            with self.subTest(path=path):
                self.assertTrue(ci_change_scope.requires_application_checks([path]))


if __name__ == "__main__":
    unittest.main()
