from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "release.yml"


def step_body(workflow: str, title: str) -> str:
    marker = f"      - name: {title}\n"
    start = workflow.index(marker) + len(marker)
    end = workflow.find("      - name: ", start)
    return workflow[start:] if end < 0 else workflow[start:end]


class DeploymentPollingBudgetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")

    def test_migration_polling_reduces_interval_without_shortening_budget(self) -> None:
        body = step_body(self.workflow, "Run the migration job (blocking)")
        self.assertIn('seq 1 180', body)
        self.assertIn('sleep 5', body)
        self.assertEqual(900, 180 * 5)
        self.assertIn('Succeeded)', body)
        self.assertIn('Failed|FailedStarting)', body)

    def test_canary_polling_reduces_interval_without_shortening_budget(self) -> None:
        body = step_body(self.workflow, "Canary smoke checks (read-only)")
        self.assertIn('seq 1 40', body)
        self.assertIn('sleep 5', body)
        self.assertEqual(200, 40 * 5)
        self.assertIn('/health/live', body)
        self.assertIn('/health/ready', body)
        self.assertIn('test "$code" = 200', body)


if __name__ == "__main__":
    unittest.main()
