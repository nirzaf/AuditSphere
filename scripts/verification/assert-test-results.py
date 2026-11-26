#!/usr/bin/env python3
"""Fail unless TRX test results prove a non-vacuous, fully passing run (WU-001).

Usage: assert-test-results.py RESULTS.trx [MORE.trx ...] [--allow-skipped NAME_SUBSTRING ...]

Each file must contain a completed ResultSummary whose required counters agree with
the enumerated UnitTestResult rows. Every row needs unique test and execution IDs.
Failed, error, timeout, aborted, unknown, and other non-passing outcomes always fail.
Named, fully enumerated skip outcomes require an explicit --allow-skipped match.
Exit 0 pass, 1 fail, 2 usage.
"""

from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
FAILING_COUNTERS = ("failed", "error", "timeout", "aborted")
REQUIRED_COUNTERS = ("total", "executed", "passed", *FAILING_COUNTERS)
OUTCOME_COUNTERS = {
    "Passed": "passed",
    "Failed": "failed",
    "Error": "error",
    "Timeout": "timeout",
    "Aborted": "aborted",
    "Inconclusive": "inconclusive",
    "PassedButRunAborted": "passedButRunAborted",
    "NotRunnable": "notRunnable",
    "NotExecuted": "notExecuted",
    "Disconnected": "disconnected",
    "Warning": "warning",
    "Completed": "completed",
    "InProgress": "inProgress",
    "Pending": "pending",
}
FAILING_OUTCOMES = {"Failed", "Error", "Timeout", "Aborted"}
NOT_RUN_OUTCOMES = {"NotExecuted", "NotRunnable", "Inconclusive", "Pending", "Disconnected", "Warning"}
NEVER_EXECUTED_OUTCOMES = {"NotExecuted", "NotRunnable", "Pending", "Disconnected", "Warning"}


def _parse_counter(key: str, raw: str) -> int:
    if not raw.isascii() or not raw.isdigit():
        raise ValueError(f"counter {key} must be a nonnegative integer")
    try:
        return int(raw)
    except ValueError as exc:
        raise ValueError(f"counter {key} is outside the supported integer range") from exc


def check(path: Path, allow_skipped: list[str]) -> dict:
    result = {"path": str(path), "ok": False, "problems": []}
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as exc:
        result["problems"].append(f"unreadable TRX: {exc}")
        return result

    summaries = root.findall(f"{NS}ResultSummary")
    if len(summaries) != 1:
        result["problems"].append("expected exactly one ResultSummary")
        return result
    summary = summaries[0]
    if summary.get("outcome") != "Completed":
        result["problems"].append(f"result summary is not Completed: {summary.get('outcome')!r}")

    counter_nodes = summary.findall(f"{NS}Counters")
    if len(counter_nodes) != 1:
        result["problems"].append("expected exactly one ResultSummary/Counters")
        return result
    counter_node = counter_nodes[0]
    values: dict[str, int] = {}
    for key, raw in counter_node.attrib.items():
        try:
            values[key] = _parse_counter(key, raw)
        except ValueError as exc:
            result["problems"].append(str(exc))
    result["counters"] = values
    for key in REQUIRED_COUNTERS:
        if key not in counter_node.attrib:
            result["problems"].append(f"missing required counter {key}")

    rows = list(root.iter(f"{NS}UnitTestResult"))
    if not rows:
        result["problems"].append("no UnitTestResult rows enumerated")

    names = {test.get("id"): test.get("name", "") for test in root.iter(f"{NS}UnitTest") if test.get("id")}
    test_ids: set[str] = set()
    execution_ids: set[str] = set()
    outcome_counts: Counter[str] = Counter()
    executed = 0
    for row in rows:
        test_id = row.get("testId")
        execution_id = row.get("executionId")
        if not test_id or not test_id.strip():
            result["problems"].append("UnitTestResult is missing testId")
        elif test_id in test_ids:
            result["problems"].append("duplicate UnitTestResult testId")
        else:
            test_ids.add(test_id)
        if not execution_id or not execution_id.strip():
            result["problems"].append("UnitTestResult is missing executionId")
        elif execution_id in execution_ids:
            result["problems"].append("duplicate UnitTestResult executionId")
        else:
            execution_ids.add(execution_id)

        state = row.get("outcome")
        name = row.get("testName") or names.get(test_id, "?")
        counter = OUTCOME_COUNTERS.get(state or "")
        if counter is None:
            result["problems"].append(f"unknown or missing result outcome: {state!r} ({name})")
            continue
        outcome_counts[counter] += 1
        if state not in NEVER_EXECUTED_OUTCOMES:
            executed += 1
        if state in FAILING_OUTCOMES:
            result["problems"].append(f"{state.lower()}: {name}")
        elif state in NOT_RUN_OUTCOMES:
            if not any(token and token in name for token in allow_skipped):
                result["problems"].append(f"{state.lower()} (mandatory): {name}")
        elif state != "Passed":
            result["problems"].append(f"non-passing outcome {state}: {name}")

    expected = {"total": len(rows), "executed": executed}
    expected.update({counter: outcome_counts[counter] for counter in OUTCOME_COUNTERS.values()})
    for key, actual in expected.items():
        if key not in values:
            if key not in REQUIRED_COUNTERS and actual == 0:
                continue
            result["problems"].append(f"missing counter {key} for {actual} enumerated result(s)")
        elif values[key] != actual:
            result["problems"].append(f"{key}={values[key]} does not match enumerated count {actual}")
    known_counters = set(OUTCOME_COUNTERS.values()) | {"total", "executed"}
    for key in values.keys() - known_counters:
        if values[key] != 0:
            result["problems"].append(f"unsupported counter {key}={values[key]}")

    if values.get("executed", 0) <= 0:
        result["problems"].append("no tests executed (vacuous evidence)")
    result["ok"] = not result["problems"]
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("trx", nargs="+", type=Path)
    parser.add_argument("--allow-skipped", nargs="*", default=[])
    args = parser.parse_args(argv)
    results = [check(path, args.allow_skipped) for path in args.trx]
    print(json.dumps({"ok": all(r["ok"] for r in results), "results": results}, indent=2))
    return 0 if all(r["ok"] for r in results) else 1


if __name__ == "__main__":
    sys.exit(main())
