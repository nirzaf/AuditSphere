#!/usr/bin/env python3
"""Validate an AuditSphere M365 result envelope (spec appendix M.4, WU-017). Standard library only.

Usage: verify-result.py RESULT.json --expect STATUS [--plan-sha256 HEX] [--tenant GUID]

Rejects unknown statuses, unsafe field names, mismatched digests, VERIFIED without passing mandatory checks
and, for PLANNED, any network call or mutation. Exit 0 valid, 1 invalid, 2 usage.
"""

from __future__ import annotations

import argparse
import json
import re
import sys

STATUSES = {"PLANNED", "VERIFIED", "APPLIED_AND_VERIFIED", "DRIFT", "BLOCKED_BOOTSTRAP", "BLOCKED_APPROVAL",
            "BLOCKED_EXTERNAL", "INVALID_OR_UNSAFE", "PROVIDER_FAILURE", "RESULT_UNCERTAIN", "PREREQUISITE_MISSING",
            "INCOMPLETE_EVIDENCE"}
REQUIRED = ("schemaVersion", "mode", "status", "mutationsAttempted", "mutationsConfirmed", "networkCalls", "changes",
            "checks", "blockedPrerequisites", "redactionsApplied")
UNSAFE = re.compile(r"(?i)(secret|password|passwd|access_?token|refresh_?token|private_?key|pfx|client_?assertion)")
SHA256 = re.compile(r"^[0-9a-f]{64}$")


def unsafe_keys(node, path="$"):
    if isinstance(node, dict):
        for key, value in node.items():
            if UNSAFE.search(key):
                yield f"{path}.{key}"
            yield from unsafe_keys(value, f"{path}.{key}")
    elif isinstance(node, list):
        for index, value in enumerate(node):
            yield from unsafe_keys(value, f"{path}[{index}]")


def problems(result: dict, expect: str, plan_sha256: str | None, tenant: str | None) -> list[str]:
    found = [f"missing field: {key}" for key in REQUIRED if key not in result]
    if found:
        return found
    if result["schemaVersion"] != 1:
        found.append("schemaVersion must be 1")
    status = result["status"]
    if status not in STATUSES:
        found.append(f"unknown status: {status!r}")
    if status != expect:
        found.append(f"status {status!r} != expected {expect!r}")
    if result["redactionsApplied"] is not True:
        found.append("redactionsApplied must be true")
    for name in ("mutationsAttempted", "mutationsConfirmed", "networkCalls"):
        if type(result[name]) is not int or result[name] < 0:
            found.append(f"{name} must be a non-negative integer")
    found += [f"unsafe field name: {path}" for path in unsafe_keys(result)]
    if plan_sha256 and result.get("planSha256") != plan_sha256:
        found.append("planSha256 does not match the expected plan digest")
    if tenant and str(result.get("tenantId", "")).lower() != tenant.lower():
        found.append("tenantId does not match")
    if status in ("PLANNED", "VERIFIED", "APPLIED_AND_VERIFIED") and not SHA256.match(str(result.get("planSha256") or "")):
        found.append("a successful result must carry the plan digest")
    if expect == "PLANNED" and (result["networkCalls"] != 0 or result["mutationsAttempted"] != 0 or result["mutationsConfirmed"] != 0):
        found.append("PLANNED requires zero network calls and zero mutations")
    if expect in ("VERIFIED", "APPLIED_AND_VERIFIED"):
        checks = result["checks"]
        if not checks:
            found.append("VERIFIED requires at least one mandatory check")
        not_passed = [c.get("id") for c in checks if c.get("state") != "PASS"]
        if not_passed:
            found.append(f"VERIFIED with non-passing checks: {not_passed}")
        if result["blockedPrerequisites"]:
            found.append("VERIFIED with blocked prerequisites")
    return found


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("result")
    parser.add_argument("--expect", required=True, choices=sorted(STATUSES))
    parser.add_argument("--plan-sha256")
    parser.add_argument("--tenant")
    args = parser.parse_args(argv)
    try:
        with open(args.result, encoding="utf-8") as handle:
            result = json.load(handle)
    except (OSError, json.JSONDecodeError) as exc:
        print(json.dumps({"valid": False, "problems": [f"unreadable result: {exc}"]}))
        return 1
    if not isinstance(result, dict):
        print(json.dumps({"valid": False, "problems": ["result must be a JSON object"]}))
        return 1
    found = problems(result, args.expect, args.plan_sha256, args.tenant)
    print(json.dumps({"valid": not found, "status": result.get("status"), "problems": found}, indent=2))
    return 0 if not found else 1


if __name__ == "__main__":
    sys.exit(main())
