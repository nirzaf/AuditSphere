"""Browser shard coverage protocol tests; no browser or database is started."""
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
import xml.etree.ElementTree as ET

from api_test_shards import NS, digest, inventory, write
from e2e_test_shards import aggregate, assignments, load, SHARD_COUNT
from test_ci_api_test_shards import discoveries


class BrowserCoverageTests(unittest.TestCase):
    def test_balanced_stable_partition_does_not_split_collections(self):
        rows = inventory(*discoveries())
        shards = assignments(rows)
        self.assertEqual([2] * SHARD_COUNT, [len(shard) for shard in shards])
        self.assertEqual(shards, assignments(list(reversed(rows))))
        self.assertEqual(sorted(r["id"] for r in rows), sorted(i for s in shards for i in s))
        for collection in {r["collection"] for r in rows}:
            ids = {r["id"] for r in rows if r["collection"] == collection}
            self.assertEqual(1, sum(ids <= set(s) for s in shards))
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            assignments(rows[:2])

    def fixture(self, root):
        xunit, vstest = discoveries()
        rows = inventory(xunit, vstest)
        ns = "{" + NS["t"] + "}"
        for i, ids in enumerate(assignments(rows)):
            folder = root / f"browser-e2e-shard-{i}"
            folder.mkdir()
            write(folder / "xunit.json", xunit)
            write(folder / "vstest.json", vstest)
            write(folder / "ids.json", ids)
            write(folder / "events.json", dict(complete=True, total=len(ids), errors=[],
                                               results=[dict(id=id_, outcome="Passed") for id_ in ids]))
            tree = ET.Element(ns + "TestRun")
            results, definitions, entries, summary = (ET.SubElement(tree, ns + tag) for tag in
                                                       ("Results", "TestDefinitions", "TestEntries", "ResultSummary"))
            summary.set("outcome", "Completed")
            ET.SubElement(summary, ns + "Counters", {key: str(len(ids)) for key in ("total", "executed", "passed")})
            for row in (row for row in rows if row["id"] in ids):
                id_ = row["id"]
                ET.SubElement(results, ns + "UnitTestResult", testId=id_, testName=row["display"],
                              outcome="Passed", executionId=id_)
                definition = ET.SubElement(definitions, ns + "UnitTest", id=id_, name=row["display"])
                ET.SubElement(definition, ns + "Execution", id=id_)
                ET.SubElement(definition, ns + "TestMethod", className=row["className"], name=row["method"])
                ET.SubElement(entries, ns + "TestEntry", testId=id_, executionId=id_)
            ET.ElementTree(tree).write(folder / "results.trx")
            write(folder / "receipt.json", dict(source="a"*40, repository="owner/repo", runId=10,
                  attempt=1, shard=i, assemblySha256="b"*64, trxSha256=digest(folder/"results.trx"),
                  eventsSha256=digest(folder/"events.json")))

    def test_complete_aggregate_passes_and_partial_rerun_can_use_same_run_evidence(self):
        with TemporaryDirectory() as folder:
            root = Path(folder)
            src = root / "input"
            src.mkdir()
            self.fixture(src)
            output = root / "verified"
            aggregate(src, output, "a"*40, "owner/repo", 10, 2)
            self.assertEqual(4, len(list(output.glob("*.trx"))))
            self.assertEqual(8, load(output / "coverage.json")["passed"])

    def test_aggregate_refuses_missing_failed_repeated_or_mismatched_evidence(self):
        for fault in ("missing", "failure", "wrong assignment", "wrong source", "wrong binary", "wrong digest"):
            with self.subTest(fault=fault), TemporaryDirectory() as folder:
                root = Path(folder)
                src = root / "input"
                src.mkdir()
                self.fixture(src)
                shard = src / "browser-e2e-shard-1"
                receipt = load(shard / "receipt.json")
                if fault == "missing":
                    shard.rename(src / "old-shard")
                elif fault == "failure":
                    events = load(shard / "events.json")
                    events["results"][0]["outcome"] = "Failed"
                    write(shard / "events.json", events)
                    receipt["eventsSha256"] = digest(shard / "events.json")
                elif fault == "wrong assignment":
                    write(shard / "ids.json", load(src / "browser-e2e-shard-0" / "ids.json"))
                elif fault == "wrong source":
                    receipt["source"] = "c"*40
                elif fault == "wrong binary":
                    receipt["assemblySha256"] = "c"*64
                elif fault == "wrong digest":
                    receipt["trxSha256"] = "c"*64
                if shard.exists():
                    write(shard / "receipt.json", receipt)
                with self.assertRaises(ValueError):
                    aggregate(src, root / "verified", "a"*40, "owner/repo", 10, 1)
                self.assertFalse((root / "verified").exists())
