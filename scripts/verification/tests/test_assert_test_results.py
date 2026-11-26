"""WU-001 checks for assert-test-results.py using synthetic TRX documents."""

from __future__ import annotations

import html
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
  <ResultSummary outcome="{summary_outcome}"><Counters {counters} /></ResultSummary>
</TestRun>"""


def result(name, outcome, *, test_id=None, execution_id=None):
    escaped_name = html.escape(name, quote=True)
    test_id = html.escape(test_id if test_id is not None else f"test-{name}", quote=True)
    execution_id = html.escape(execution_id if execution_id is not None else f"execution-{name}", quote=True)
    escaped_outcome = html.escape(outcome, quote=True)
    return (f'<UnitTestResult testName="{escaped_name}" testId="{test_id}" '
            f'executionId="{execution_id}" outcome="{escaped_outcome}" />')


class AssertTestResultsTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.tmp.cleanup()

    def trx(self, results, executed, passed, failed=0, skipped=0, body=None,
            counter_overrides=None, summary_outcome="Completed"):
        path = Path(self.tmp.name, f"r{len(list(Path(self.tmp.name).iterdir()))}.trx")
        if body is not None:
            path.write_text(body)
            return path
        counters = {
            "total": executed + skipped,
            "executed": executed,
            "passed": passed,
            "failed": failed,
            "error": 0,
            "timeout": 0,
            "aborted": 0,
        }
        if skipped:
            counters["notExecuted"] = skipped
        for key, value in (counter_overrides or {}).items():
            if value is None:
                counters.pop(key, None)
            else:
                counters[key] = value
        counter_xml = " ".join(f'{key}="{value}"' for key, value in counters.items())
        path.write_text(TRX.format(results="".join(results), counters=counter_xml,
                                   summary_outcome=summary_outcome))
        return path

    def test_passing_run_is_accepted(self):
        self.assertEqual(art.main([str(self.trx([result("A", "Passed")], 1, 1))]), 0)

    def test_zero_executed_and_missing_results_are_vacuous(self):
        evidence = art.check(self.trx([], 0, 0), [])
        self.assertFalse(evidence["ok"])
        self.assertTrue(any("no UnitTestResult" in item for item in evidence["problems"]))

    def test_failure_is_rejected(self):
        self.assertEqual(art.main([str(self.trx([result("A", "Failed")], 1, 0, failed=1))]), 1)

    def test_mandatory_skip_is_rejected_unless_allowed(self):
        path = self.trx([result("A", "Passed"), result("Live.B", "NotExecuted")], 1, 1, skipped=1)
        self.assertEqual(art.main([str(path)]), 1)
        self.assertEqual(art.main([str(path), "--allow-skipped", "Live."]), 0)

    def test_summary_counts_must_match_enumerated_results(self):
        path = self.trx(
            [result("Only row", "Passed")], 1, 1,
            counter_overrides={"total": "2", "notExecuted": "1"})
        evidence = art.check(path, [])
        self.assertFalse(evidence["ok"])
        self.assertTrue(any("total=2 does not match enumerated count 1" in item for item in evidence["problems"]))
        self.assertTrue(any("notExecuted=1 does not match enumerated count 0" in item for item in evidence["problems"]))

    def test_required_and_present_optional_counters_are_strict(self):
        row = result("A", "Passed")
        missing = art.check(self.trx([row], 1, 1, counter_overrides={"passed": None}), [])
        malformed = art.check(self.trx([row], 1, 1, counter_overrides={"executed": "1.0"}), [])
        negative = art.check(self.trx([row], 1, 1, counter_overrides={"notExecuted": "-1"}), [])
        self.assertFalse(missing["ok"])
        self.assertFalse(malformed["ok"])
        self.assertFalse(negative["ok"])

    def test_optional_outcome_counter_is_required_when_outcome_is_enumerated(self):
        rows = [result("A", "Passed"), result("Named skip", "NotExecuted")]
        evidence = art.check(self.trx(rows, 1, 1, skipped=1,
                                      counter_overrides={"notExecuted": None}), ["Named skip"])
        self.assertFalse(evidence["ok"])
        self.assertTrue(any("missing counter notExecuted" in item for item in evidence["problems"]))

    def test_malformed_duplicate_and_unknown_result_cohort(self):
        cases = [
            ("empty testId", [result("A", "Passed", test_id="")]),
            ("empty executionId", [result("A", "Passed", execution_id="  ")]),
            ("duplicate testId", [result("A", "Passed", test_id="same"),
                                  result("B", "Passed", test_id="same")]),
            ("duplicate executionId", [result("A", "Passed", execution_id="same"),
                                        result("B", "Passed", execution_id="same")]),
            ("missing outcome", [result("A", "")]),
            ("unknown outcome", [result("A", "Flaky")]),
        ]
        for label, rows in cases:
            with self.subTest(label=label):
                evidence = art.check(self.trx(rows, len(rows), len(rows)), [])
                self.assertFalse(evidence["ok"])

    def test_nonpassing_dispositions_cannot_be_allowed_as_skips(self):
        row = result("Live.B", "PassedButRunAborted")
        evidence = art.check(self.trx([row], 1, 0), ["Live."])
        self.assertFalse(evidence["ok"])

    def test_incomplete_summary_is_rejected(self):
        evidence = art.check(self.trx([result("A", "Passed")], 1, 1,
                                       summary_outcome="InProgress"), [])
        self.assertFalse(evidence["ok"])

    def test_missing_counters_or_invalid_xml_is_rejected(self):
        self.assertEqual(art.main([str(self.trx([], 0, 0, body="<TestRun/>"))]), 1)
        self.assertEqual(art.main([str(self.trx([], 0, 0, body="not xml"))]), 1)


if __name__ == "__main__":
    unittest.main()
