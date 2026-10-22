#!/usr/bin/env python3
"""Fail unless TRX test results prove a non-vacuous, fully passing run (WU-001).

Usage: assert-test-results.py RESULTS.trx [MORE.trx ...] [--allow-skipped NAME_SUBSTRING ...]

Each file must contain a ResultSummary/Counters element with executed > 0, failed == 0, error == 0,
timeout == 0, aborted == 0 and no notExecuted/inconclusive cases, unless every such case is matched
by --allow-skipped. A green console line is never accepted as evidence. Exit 0 pass, 1 fail, 2 usage.
"""

from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
FAILING_COUNTERS = ("failed", "error", "timeout", "aborted")
NOT_RUN_OUTCOMES = {"NotExecuted", "Inconclusive", "Pending", "Disconnected", "Warning"}


def check(path: Path, allow_skipped: list[str]) -> dict:
    result = {"path": str(path), "ok": False, "problems": []}
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as exc:
        result["problems"].append(f"unreadable TRX: {exc}")
        return result
    counters = root.find(f"{NS}ResultSummary/{NS}Counters")
    if counters is None:
        result["problems"].append("missing ResultSummary/Counters")
        return result
    values = {key: int(value) for key, value in counters.attrib.items() if value.isdigit()}
    result["counters"] = values
    if values.get("executed", 0) <= 0:
        result["problems"].append("no tests executed (vacuous evidence)")
    for key in FAILING_COUNTERS:
        if values.get(key, 0):
            result["problems"].append(f"{key}={values[key]}")
    names = {test.get("id"): test.get("name", "") for test in root.iter(f"{NS}UnitTest")}
    for outcome in root.iter(f"{NS}UnitTestResult"):
        state = outcome.get("outcome", "")
        name = outcome.get("testName") or names.get(outcome.get("testId"), "?")
        if state == "Failed":
            result["problems"].append(f"failed: {name}")
        elif state in NOT_RUN_OUTCOMES and not any(token in name for token in allow_skipped):
            result["problems"].append(f"{state.lower()} (mandatory): {name}")
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
