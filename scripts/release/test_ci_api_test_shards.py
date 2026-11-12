"""Pure protocol checks. Never starts an API/testhost or touches PostgreSQL."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, Mock
import xml.etree.ElementTree as ET

import api_test_shards as shards


def discoveries():
    xunit, vstest = [], []
    for i in range(8):
        x = dict(id=f"x{i}", collection=f"collection{i // 2}", className=f"C{i // 2}", method="Theory",
                 display=f"C{i // 2}.Theory(value: {i})", kind="Xunit.Sdk.XunitTestCase", skip=None)
        v = dict(id=f"v{i}", xunitId=x["id"], className=x["className"], method=x["method"], display=x["display"],
                 fqn=x["className"] + "." + x["method"])
        xunit.append(x)
        vstest.append(v)
    return xunit, vstest


class ProtocolTests(unittest.TestCase):
    def test_exact_rows_and_fixture_groups_deterministic(self):
        x, v = discoveries()
        rows = shards.inventory(x, v)
        for row in x:
            row["collection"] += "-another-process"
        self.assertEqual(rows, shards.inventory(list(reversed(x)), list(reversed(v))))
        assignments = shards.partition(rows)
        self.assertEqual(sorted(i for a in assignments for i in a), sorted(r["id"] for r in rows))
        for collection in {r["collection"] for r in rows}:
            ids = {r["id"] for r in rows if r["collection"] == collection}
            self.assertEqual(sum(ids.issubset(a) for a in map(set, assignments)), 1)

    def test_fail_closed_discovery_variants(self):
        mutations = [lambda x, v: x.clear(), lambda x, v: v.pop(), lambda x, v: x.append(x[0]),
                     lambda x, v: v.append(v[0]), lambda x, v: x[0].update(kind="Xunit.Sdk.XunitTheoryTestCase"),
                     lambda x, v: x[0].update(skip="skip"), lambda x, v: v[0].update(display="wrong"),
                     lambda x, v: v[0].update(xunitId="unknown"), lambda x, v: x[0].update(collection="")]
        for mutate in mutations:
            with self.subTest(mutation=mutate):
                x, v = discoveries()
                mutate(x, v)
                with self.assertRaises(ValueError):
                    shards.inventory(x, v)

    def test_oversized_collection_never_split(self):
        with self.assertRaises(ValueError):
            shards.partition([{"id": str(i), "collection": "one"} for i in range(shards.MAX_ROWS + 1)])
        rows = [{"id": str(i), "collection": str(i // 7)} for i in range(35)]
        assignments = shards.partition(rows)
        self.assertEqual([len(s) for s in assignments], [21, 14])
        self.assertEqual([len(s) for s in shards.partition([{"id": str(i), "collection": str(i)} for i in range(5)])], [4, 1])
        with self.assertRaises(ValueError):
            shards.partition([])

    def test_trx_and_runner_reconciliation_fail_closed(self):
        rows = shards.inventory(*discoveries())[:2]
        ids = [r["id"] for r in rows]
        namespace = shards.NS["t"]
        def element(parent, tag, **attrs):
            return ET.SubElement(parent, "{" + namespace + "}" + tag, attrs)
        root = ET.Element("{" + namespace + "}TestRun")
        results = element(root, "Results")
        definitions = element(root, "TestDefinitions")
        entries = element(root, "TestEntries")
        for row in rows:
            element(results, "UnitTestResult", testId=row["id"], testName=row["display"], outcome="Passed", executionId=row["id"])
            definition = element(definitions, "UnitTest", id=row["id"], name=row["display"])
            element(definition, "Execution", id=row["id"])
            element(definition, "TestMethod", className=row["className"], name=row["method"])
            element(entries, "TestEntry", testId=row["id"], executionId=row["id"])
        summary = element(root, "ResultSummary", outcome="Completed")
        element(summary, "Counters", total="2", executed="2", passed="2", failed="0", notExecuted="0")
        events = dict(complete=True, errors=[], total=2, results=[dict(id=i, outcome="Passed") for i in ids])
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "results.trx"
            def check(tree, event):
                ET.ElementTree(tree).write(path)
                path.with_name("events.json").write_text(json.dumps(event))
                shards.reconcile(path, ids, rows)
            check(root, events)
            mutations = [lambda r, e: e.update(complete=False), lambda r, e: e["errors"].append("crash"),
                         lambda r, e: e["results"].pop(), lambda r, e: e["results"][0].update(outcome="Skipped"),
                         lambda r, e: r[0][0].set("outcome", "Failed"), lambda r, e: r[0].remove(r[0][0]),
                         lambda r, e: r[0].append(copy.deepcopy(r[0][0])), lambda r, e: r[0][0].set("testId", "unexpected"),
                         lambda r, e: r[1][0][1].set("name", "wrong"), lambda r, e: r[3].set("outcome", "Aborted"),
                         lambda r, e: r[3][0].set("notExecuted", "1"), lambda r, e: r[1].remove(r[1][0]),
                         lambda r, e: r[0][0].attrib.pop("executionId"), lambda r, e: r[0][0].set("executionId", "v1"),
                         lambda r, e: r[2].remove(r[2][0]), lambda r, e: r.append(copy.deepcopy(r[3])),
                         lambda r, e: r[3].append(copy.deepcopy(r[3][0])), lambda r, e: e.update(complete="true"),
                         lambda r, e: e.update(total=True)]
            for mutate in mutations:
                with self.subTest(mutation=mutate):
                    tree, event = copy.deepcopy(root), copy.deepcopy(events)
                    mutate(tree, event)
                    with self.assertRaises(ValueError):
                        check(tree, event)

    def test_owned_cleanup_on_success_timeout_rss_and_signal(self):
        for branch in ("success", "timeout", "rss", "signal", "proc-error", "drain-error"):
            with self.subTest(branch=branch), tempfile.TemporaryDirectory() as folder:
                process = Mock(pid=1234, returncode=0)
                process.poll.side_effect = [0] if branch == "success" else [None, 0]
                state = [(0, []), (0, [])]
                if branch == "rss":
                    state = [(shards.RSS_LIMIT + 1, [1234]), (0, [])]
                if branch == "signal":
                    state = [InterruptedError("Signal 15"), (0, [])]
                if branch == "proc-error":
                    state = [PermissionError("proc"), (0, [])]
                if branch == "drain-error":
                    state = [(0, []), PermissionError("drain")]
                with patch.object(shards.Path, "exists", return_value=True), patch.object(shards.subprocess, "Popen", return_value=process), \
                     patch.object(shards, "group_state", side_effect=state), patch.object(shards.os, "killpg") as kill, \
                     patch.object(shards.time, "sleep"), patch.object(shards.signal, "signal"):
                    log = Path(folder) / "command.log"
                    if branch == "success":
                        shards.bounded(["fake"], log, 1, {})
                    else:
                        with self.assertRaises((ValueError, InterruptedError, PermissionError)):
                            shards.bounded(["fake"], log, -1 if branch == "timeout" else 1, {})
                    kill.assert_called_once_with(1234, shards.signal.SIGKILL)
                    evidence = json.loads(log.with_suffix(".exit.json").read_text())
                    self.assertEqual(evidence["groupDrained"], branch != "drain-error")


if __name__ == "__main__":
    unittest.main()
