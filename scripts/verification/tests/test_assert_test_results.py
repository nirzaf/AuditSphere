"""WU-001 checks for assert-test-results.py using synthetic TRX documents."""

from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("assert_test_results", HERE / "assert-test-results.py")
art = importlib.util.module_from_spec(spec)
spec.loader.exec_module(art)

TRX = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>{results}</Results>
  <ResultSummary outcome="Completed"><Counters total="{total}" executed="{executed}" passed="{passed}" failed="{failed}" error="0" timeout="0" aborted="0" notExecuted="{skipped}" /></ResultSummary>
</TestRun>"""


def result(name, outcome):
    return f'<UnitTestResult testName="{name}" outcome="{outcome}" />'


class AssertTestResultsTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.tmp.cleanup()

    def trx(self, results, executed, passed, failed=0, skipped=0, body=None):
        path = Path(self.tmp.name, f"r{len(list(Path(self.tmp.name).iterdir()))}.trx")
        path.write_text(body or TRX.format(results="".join(results), total=executed + skipped, executed=executed,
                                           passed=passed, failed=failed, skipped=skipped))
        return path

    def test_passing_run_is_accepted(self):
        self.assertEqual(art.main([str(self.trx([result("A", "Passed")], 1, 1))]), 0)

    def test_zero_executed_is_vacuous(self):
        self.assertEqual(art.main([str(self.trx([], 0, 0))]), 1)

    def test_failure_is_rejected(self):
        self.assertEqual(art.main([str(self.trx([result("A", "Failed")], 1, 0, failed=1))]), 1)

    def test_mandatory_skip_is_rejected_unless_allowed(self):
        path = self.trx([result("A", "Passed"), result("Live.B", "NotExecuted")], 1, 1, skipped=1)
        self.assertEqual(art.main([str(path)]), 1)
        self.assertEqual(art.main([str(path), "--allow-skipped", "Live."]), 0)

    def test_missing_counters_or_invalid_xml_is_rejected(self):
        self.assertEqual(art.main([str(self.trx([], 0, 0, body="<TestRun/>"))]), 1)
        self.assertEqual(art.main([str(self.trx([], 0, 0, body="not xml"))]), 1)


if __name__ == "__main__":
    unittest.main()
