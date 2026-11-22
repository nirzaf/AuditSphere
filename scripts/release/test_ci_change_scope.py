from __future__ import annotations

import unittest

import ci_change_scope


class CiChangeScopeTests(unittest.TestCase):
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
