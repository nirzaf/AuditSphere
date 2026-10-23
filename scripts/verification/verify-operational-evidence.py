#!/usr/bin/env python3
"""Validate WU-038 operational evidence without running scans or recovery actions.

The verifier reads the supplied JSON, safely rooted evidence files and local Git
identity/status. It never runs scanners or recovery actions, authenticates an
approval, or turns missing evidence into a pass. Exit 0 means a complete record, not acceptance.
Exit codes: 0 complete record, 1 invalid input, 2 incomplete record.

Collectors may provide measurement evidence as strict JSON with exactly these
top-level fields: schemaVersion=1, measurementType, sourceCommit, tool{name,
version}, measuredAtUtc, and metrics. measurementType is one of databaseCapacity,
queries, exports, uploadChunks, or concurrentWorkers; metrics contains exactly
these typed fields respectively: databaseCapacity(maxSessions:integer,
maxTransactionSeconds:number,sampleCount:integer), queries(p95Milliseconds:number,
commandCount:integer,peakMemoryBytes:integer,sampleCount:integer),
exports(p95Milliseconds:number,exportCount:integer,peakMemoryBytes:integer,
sampleCount:integer), uploadChunks(p95Milliseconds:number,chunkBytes:integer,
peakMemoryBytes:integer,sampleCount:integer), and concurrentWorkers(
workerCount:integer,p95Milliseconds:number,peakMemoryBytes:integer,
sampleCount:integer). sourceCommit is 40 lowercase hexadecimal characters;
tool fields are safe tokens; measuredAtUtc uses YYYY-MM-DDTHH:MM:SSZ. Values and
provenance must match the manifest exactly. Duplicate keys, extra fields,
unsupported shapes, or zero sample/count values are not observed evidence.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path, PurePosixPath
from typing import Any
from urllib.parse import urlsplit

MAX_DOCUMENT_BYTES = 1_048_576
MAX_EVIDENCE_BYTES = 20_971_520
MAX_NUMERIC_VALUE = 9_223_372_036_854_775_807
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
ID_RE = re.compile(r"^[A-Z0-9][A-Z0-9._-]{2,95}$")
TOKEN_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._+-]{0,95}$")
HOST_RE = re.compile(
    r"^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(?:\.(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?))*$"
)
LOCALE_RE = re.compile(r"^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$")

SCAN_TOOL_NAMES = {
    "nuget": "dotnet list solution package --vulnerable --include-transitive --format json --no-restore",
    "npm": "npm audit --json",
    "gitleaks": "gitleaks git --redact=100 --report-format json",
}
FINDING_IDENTITY_RULES = {
    "npm": "NPM-SHA256(package-name-casefold + NUL + advisory-source)",
    "nuget": "NUGET-SHA256(package-id-casefold + NUL + resolved-version + NUL + advisory-url)",
    "gitleaks": "GITLEAKS-SHA256(fingerprint)",
}
SCAN_FAILURES = {
    "SOURCE_UNAVAILABLE",
    "TOOL_UNAVAILABLE",
    "FEED_UNAVAILABLE",
    "EXECUTION_FAILED",
    "UNKNOWN_FAILURE",
}
SCAN_FINDING_CLASSES = {"VULNERABILITY", "SECRET", "POLICY", "OTHER"}
FINDING_SEVERITIES = {"CRITICAL", "HIGH", "MODERATE", "LOW", "INFO", "UNKNOWN"}
FINDING_TRIAGE = {"UNREVIEWED", "REVIEW_REQUIRED", "REMEDIATION_REQUIRED"}

TARGET_UNITS = {
    "rpoSeconds": "seconds",
    "rtoSeconds": "seconds",
    "databaseMaxSessions": "sessions",
    "databaseMaxTransactionSeconds": "seconds",
    "queryP95Milliseconds": "milliseconds",
    "exportP95Milliseconds": "milliseconds",
    "uploadChunkP95Milliseconds": "milliseconds",
    "minimumConcurrentWorkers": "workers",
    "retentionDays": "days",
    "custodyModel": "identifier",
    "supportedLocales": "locale-tags",
    "supportedBrowsers": "browser-identifiers",
    "accessibilityTarget": "standard-identifier",
    "telemetryDestinationIds": "destination-identifiers",
}
TARGET_INTEGER = {
    "databaseMaxSessions",
    "minimumConcurrentWorkers",
    "retentionDays",
}
TARGET_LIST = {"supportedLocales", "supportedBrowsers", "telemetryDestinationIds"}
TARGET_TOKEN = {"custodyModel", "accessibilityTarget"}

MEASUREMENT_METRICS = {
    "databaseCapacity": {
        "maxSessions": "integer",
        "maxTransactionSeconds": "number",
        "sampleCount": "integer",
    },
    "queries": {
        "p95Milliseconds": "number",
        "commandCount": "integer",
        "peakMemoryBytes": "integer",
        "sampleCount": "integer",
    },
    "exports": {
        "p95Milliseconds": "number",
        "exportCount": "integer",
        "peakMemoryBytes": "integer",
        "sampleCount": "integer",
    },
    "uploadChunks": {
        "p95Milliseconds": "number",
        "chunkBytes": "integer",
        "peakMemoryBytes": "integer",
        "sampleCount": "integer",
    },
    "concurrentWorkers": {
        "workerCount": "integer",
        "p95Milliseconds": "number",
        "peakMemoryBytes": "integer",
        "sampleCount": "integer",
    },
}
MEASUREMENT_TO_TARGET = {
    "databaseCapacity": {
        "maxSessions": "databaseMaxSessions",
        "maxTransactionSeconds": "databaseMaxTransactionSeconds",
    },
    "queries": {"p95Milliseconds": "queryP95Milliseconds"},
    "exports": {"p95Milliseconds": "exportP95Milliseconds"},
    "uploadChunks": {"p95Milliseconds": "uploadChunkP95Milliseconds"},
    "concurrentWorkers": {"workerCount": "minimumConcurrentWorkers"},
}
POSITIVE_INTEGER_METRICS = {
    ("databaseCapacity", "sampleCount"),
    ("queries", "commandCount"),
    ("queries", "sampleCount"),
    ("queries", "peakMemoryBytes"),
    ("exports", "exportCount"),
    ("exports", "sampleCount"),
    ("exports", "peakMemoryBytes"),
    ("uploadChunks", "chunkBytes"),
    ("uploadChunks", "sampleCount"),
    ("uploadChunks", "peakMemoryBytes"),
    ("concurrentWorkers", "workerCount"),
    ("concurrentWorkers", "sampleCount"),
    ("concurrentWorkers", "peakMemoryBytes"),
}
PRODUCTION_EVIDENCE_FIELDS = (
    "databaseRestore",
    "releaseArchiveStoreRestore",
    "selectedSharePointCopyRestore",
    "independentCheckpointStore",
    "encryptionKeyCustody",
    "rollbackRehearsal",
    "alerts",
    "independentReview",
)


class InputError(Exception):
    """An input failure with a safe, stable code and no echoed input values."""

    def __init__(self, code: str):
        super().__init__(code)
        self.code = code


def _pairs_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise InputError("duplicate-json-key")
        result[key] = value
    return result


def _reject_nonfinite(value: str) -> None:
    raise InputError("non-finite-json-number")


def _loads_strict(data: bytes, max_bytes: int = MAX_DOCUMENT_BYTES) -> Any:
    if len(data) > max_bytes:
        raise InputError("json-document-too-large")
    try:
        text = data.decode("utf-8")
        return json.loads(
            text,
            object_pairs_hook=_pairs_without_duplicates,
            parse_constant=_reject_nonfinite,
        )
    except InputError:
        raise
    except (UnicodeDecodeError, ValueError, RecursionError):
        raise InputError("invalid-json") from None


def load_document(path: Path) -> Any:
    try:
        if path.stat().st_size > MAX_DOCUMENT_BYTES:
            raise InputError("json-document-too-large")
        data = path.read_bytes()
    except InputError:
        raise
    except OSError:
        raise InputError("input-unreadable") from None
    return _loads_strict(data)


def _object(value: Any, keys: set[str], errors: set[str], code: str) -> bool:
    if type(value) is not dict or set(value) != keys:
        errors.add(code)
        return False
    return True


def _known_map(value: Any, keys: set[str], errors: set[str], code: str) -> bool:
    if type(value) is not dict or not set(value).issubset(keys):
        errors.add(code)
        return False
    return True


def _safe_token(value: Any) -> bool:
    return type(value) is str and TOKEN_RE.fullmatch(value) is not None


def _utc_timestamp(value: Any) -> bool:
    if type(value) is not str or not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", value):
        return False
    try:
        datetime.fromisoformat(value[:-1] + "+00:00")
        return True
    except ValueError:
        return False


def _number(value: Any, integer: bool = False) -> bool:
    if integer:
        return type(value) is int and 0 <= value <= MAX_NUMERIC_VALUE
    if type(value) is int:
        return 0 <= value <= MAX_NUMERIC_VALUE
    return type(value) is float and math.isfinite(value) and value >= 0


def _valid_target_value(name: str, value: Any) -> bool:
    if value is None:
        return True
    if name in TARGET_INTEGER:
        return type(value) is int and value > 0
    if name in TARGET_LIST:
        if type(value) is not list or not value or len(value) > 64:
            return False
        if any(type(item) is not str for item in value):
            return False
        if len(set(value)) != len(value):
            return False
        if name == "supportedLocales":
            return all(LOCALE_RE.fullmatch(item) is not None for item in value)
        return all(_safe_token(item) for item in value)
    if name in TARGET_TOKEN:
        return _safe_token(value)
    if name == "rpoSeconds":
        return _number(value)
    return _number(value) and value > 0


def _safe_evidence_path(raw_path: Any) -> bool:
    if type(raw_path) is not str or not raw_path or "\\" in raw_path or "\x00" in raw_path:
        return False
    parts = raw_path.split("/")
    if any(part in ("", ".", "..") for part in parts):
        return False
    allowed_root = raw_path.startswith("artifacts/completion/") or raw_path.startswith("docs/evidence/")
    return allowed_root and raw_path.lower().endswith(".json")


def _validate_reference_shape(reference: Any, errors: set[str]) -> bool:
    if type(reference) is not dict or set(reference) != {"id", "path", "sha256", "sourceCommit"}:
        errors.add("invalid-evidence-reference")
        return False
    if not _safe_token(reference["id"]) or not ID_RE.fullmatch(reference["id"]):
        errors.add("invalid-evidence-reference")
        return False
    if not _safe_evidence_path(reference["path"]):
        errors.add("unsafe-evidence-path")
        return False
    if type(reference["sha256"]) is not str or SHA256_RE.fullmatch(reference["sha256"]) is None:
        errors.add("invalid-evidence-reference")
        return False
    if reference["sourceCommit"] is not None and (
        type(reference["sourceCommit"]) is not str
        or COMMIT_RE.fullmatch(reference["sourceCommit"]) is None
    ):
        errors.add("invalid-evidence-reference")
        return False
    return True


def _is_beneath(path: Path, parent: Path) -> bool:
    try:
        path.relative_to(parent)
        return True
    except ValueError:
        return False


def _resolve_evidence_file(
    root: Path,
    raw_path: str,
    errors: set[str],
    blockers: set[str],
) -> Path | None:
    try:
        canonical_root = root.resolve(strict=True)
        allowed_roots = (
            canonical_root / "artifacts" / "completion",
            canonical_root / "docs" / "evidence",
        )
        if any(not _is_beneath(allowed, canonical_root) for allowed in allowed_roots):
            errors.add("unsafe-evidence-path")
            return None

        candidate = canonical_root.joinpath(*PurePosixPath(raw_path).parts)
        if not any(_is_beneath(candidate, allowed) for allowed in allowed_roots):
            errors.add("unsafe-evidence-path")
            return None

        # Check every root-relative component before resolving or opening the file.
        cursor = canonical_root
        for component in PurePosixPath(raw_path).parts:
            cursor = cursor / component
            if cursor.is_symlink():
                errors.add("unsafe-evidence-path")
                return None

        resolved = candidate.resolve(strict=True)
        if (
            not _is_beneath(resolved, canonical_root)
            or not any(_is_beneath(resolved, allowed) for allowed in allowed_roots)
        ):
            errors.add("unsafe-evidence-path")
            return None
        return resolved
    except (OSError, RuntimeError):
        blockers.add("EVIDENCE_ARTIFACT_UNAVAILABLE")
        return None


def _verify_reference(
    reference: Any,
    root: Path,
    errors: set[str],
    blockers: set[str],
    cache: dict[tuple[str, str], bool],
) -> bool:
    if reference is None:
        blockers.add("EVIDENCE_REFERENCE_MISSING")
        return False
    if not _validate_reference_shape(reference, errors):
        return False
    key = (reference["path"], reference["sha256"])
    if key in cache:
        return cache[key]

    resolved = _resolve_evidence_file(root, reference["path"], errors, blockers)
    if resolved is None:
        cache[key] = False
        return False
    try:
        if not resolved.is_file() or resolved.stat().st_size > MAX_EVIDENCE_BYTES:
            blockers.add("EVIDENCE_ARTIFACT_UNAVAILABLE")
            cache[key] = False
            return False
        digest = hashlib.sha256()
        total = 0
        with resolved.open("rb") as handle:
            while True:
                chunk = handle.read(65_536)
                if not chunk:
                    break
                total += len(chunk)
                if total > MAX_EVIDENCE_BYTES:
                    blockers.add("EVIDENCE_ARTIFACT_UNAVAILABLE")
                    cache[key] = False
                    return False
                digest.update(chunk)
        matched = digest.hexdigest() == reference["sha256"]
        if not matched:
            blockers.add("EVIDENCE_IDENTITY_UNKNOWN")
        cache[key] = matched
        return matched
    except (OSError, RuntimeError):
        blockers.add("EVIDENCE_ARTIFACT_UNAVAILABLE")
        cache[key] = False
        return False


def _read_report_reference(
    reference: Any,
    root: Path,
    scan_id: str | None,
    errors: set[str],
    blockers: set[str],
) -> tuple[bool, Any]:
    if reference is None:
        blockers.add("EVIDENCE_REFERENCE_MISSING")
        if scan_id is not None:
            blockers.add(f"SCAN_EVIDENCE_UNKNOWN_{scan_id.upper()}")
        return False, None
    if not _validate_reference_shape(reference, errors):
        return False, None
    resolved = _resolve_evidence_file(root, reference["path"], errors, blockers)
    if resolved is None:
        return False, None
    try:
        if not resolved.is_file() or resolved.stat().st_size > MAX_EVIDENCE_BYTES:
            blockers.add("EVIDENCE_ARTIFACT_UNAVAILABLE")
            return False, None
        with resolved.open("rb") as handle:
            raw = handle.read(MAX_EVIDENCE_BYTES + 1)
        if len(raw) > MAX_EVIDENCE_BYTES:
            blockers.add("EVIDENCE_ARTIFACT_UNAVAILABLE")
            return False, None
        if hashlib.sha256(raw).hexdigest() != reference["sha256"]:
            blockers.add("EVIDENCE_IDENTITY_UNKNOWN")
            return False, None
    except (OSError, RuntimeError):
        blockers.add("EVIDENCE_ARTIFACT_UNAVAILABLE")
        return False, None

    try:
        return True, _loads_strict(raw, MAX_EVIDENCE_BYTES)
    except InputError:
        if scan_id is not None:
            blockers.add(f"SCAN_REPORT_UNPARSEABLE_{scan_id.upper()}")
        return False, None


def _hashed_finding_id(prefix: str, identity: str) -> str:
    digest = hashlib.sha256(identity.encode("utf-8")).hexdigest().upper()
    return f"{prefix}-{digest}"


def _npm_report(report: Any) -> tuple[dict[str, tuple[str, str]], list[str] | None] | None:
    if type(report) is not dict or type(report.get("auditReportVersion")) is not int:
        return None
    if report["auditReportVersion"] not in {2, 3}:
        return None
    metadata = report.get("metadata")
    vulnerabilities = report.get("vulnerabilities")
    if type(metadata) is not dict or type(vulnerabilities) is not dict:
        return None
    summary = metadata.get("vulnerabilities")
    summary_keys = {"total", "critical", "high", "moderate", "low", "info"}
    if type(summary) is not dict or not summary_keys.issubset(summary):
        return None
    if any(type(summary[key]) is not int or summary[key] < 0 for key in summary_keys):
        return None

    severity_map = {
        "critical": "CRITICAL",
        "high": "HIGH",
        "moderate": "MODERATE",
        "low": "LOW",
        "info": "INFO",
    }
    package_counts = {severity: 0 for severity in severity_map}
    findings: dict[str, tuple[str, str]] = {}
    names: set[str] = set()
    for package_name, details in vulnerabilities.items():
        if (
            type(package_name) is not str
            or not package_name
            or len(package_name) > 512
            or type(details) is not dict
        ):
            return None
        normalized_name = package_name.casefold()
        if normalized_name in names:
            return None
        names.add(normalized_name)
        severity = details.get("severity")
        via = details.get("via")
        if (
            type(details.get("name")) is not str
            or details["name"].casefold() != normalized_name
            or type(severity) is not str
            or severity not in severity_map
            or type(via) is not list
            or not via
        ):
            return None
        advisories = [item for item in via if type(item) is dict]
        if not advisories:
            return None
        for item in via:
            if type(item) not in {str, dict}:
                return None
        for advisory in advisories:
            advisory_severity = advisory.get("severity")
            advisory_source = advisory.get("source")
            if (
                type(advisory_severity) is not str
                or advisory_severity not in severity_map
                or type(advisory_source) not in {int, str}
                or (type(advisory_source) is int and advisory_source <= 0)
                or (type(advisory_source) is str and (not advisory_source or len(advisory_source) > 256))
            ):
                return None
            identity = f"{normalized_name}\0{advisory_source}"
            finding_id = _hashed_finding_id("NPM", identity)
            normalized = ("VULNERABILITY", severity_map[advisory_severity])
            previous = findings.get(finding_id)
            if previous is not None and previous != normalized:
                return None
            findings[finding_id] = normalized
        package_counts[severity] += 1

    expected_counts = {severity: summary[severity] for severity in severity_map}
    if len(vulnerabilities) != summary["total"] or package_counts != expected_counts:
        return None
    return findings, None


def _feed_host(value: Any) -> str | None:
    if type(value) is not str or not value or len(value) > 2048:
        return None
    try:
        parsed = urlsplit(value)
        host = parsed.hostname
        if parsed.scheme.lower() != "https" or host is None or parsed.username or parsed.password:
            return None
        if parsed.fragment or parsed.query or HOST_RE.fullmatch(host) is None:
            return None
        return host.lower()
    except ValueError:
        return None


def _nuget_report(report: Any) -> tuple[dict[str, tuple[str, str]], list[str] | None] | None:
    if type(report) is not dict or type(report.get("version")) is not int or report["version"] != 1:
        return None
    parameters = report.get("parameters")
    sources = report.get("sources")
    projects = report.get("projects")
    if (
        type(parameters) is not str
        or "--vulnerable" not in parameters.split()
        or "--include-transitive" not in parameters.split()
        or type(sources) is not list
        or not sources
        or type(projects) is not list
        or not projects
    ):
        return None
    hosts = [_feed_host(source) for source in sources]
    if any(host is None for host in hosts) or len(set(hosts)) != len(hosts):
        return None

    severity_map = {
        "critical": "CRITICAL",
        "high": "HIGH",
        "moderate": "MODERATE",
        "medium": "MODERATE",
        "low": "LOW",
    }
    findings: dict[str, tuple[str, str]] = {}
    for project in projects:
        frameworks = project.get("frameworks") if type(project) is dict else None
        if type(project) is not dict or type(project.get("path")) is not str or not project["path"]:
            return None
        if type(frameworks) is not list or not frameworks:
            return None
        for framework in frameworks:
            if (
                type(framework) is not dict
                or type(framework.get("framework")) is not str
                or not framework["framework"]
                or type(framework.get("topLevelPackages")) is not list
                or type(framework.get("transitivePackages")) is not list
            ):
                return None
            for group in ("topLevelPackages", "transitivePackages"):
                for package in framework[group]:
                    if (
                        type(package) is not dict
                        or type(package.get("id")) is not str
                        or not package["id"]
                        or type(package.get("resolvedVersion")) is not str
                        or not package["resolvedVersion"]
                        or type(package.get("vulnerabilities")) is not list
                    ):
                        return None
                    for vulnerability in package["vulnerabilities"]:
                        if type(vulnerability) is not dict:
                            return None
                        severity = vulnerability.get("severity")
                        advisory = vulnerability.get("advisoryurl")
                        if (
                            type(severity) is not str
                            or severity.casefold() not in severity_map
                            or type(advisory) is not str
                            or not advisory
                        ):
                            return None
                        canonical = "\0".join((package["id"].casefold(), package["resolvedVersion"], advisory))
                        finding_id = _hashed_finding_id("NUGET", canonical)
                        normalized = ("VULNERABILITY", severity_map[severity.casefold()])
                        previous = findings.get(finding_id)
                        if previous is not None and previous != normalized:
                            return None
                        findings[finding_id] = normalized
    return findings, sorted(hosts)


def _gitleaks_report(report: Any) -> tuple[dict[str, tuple[str, str]], list[str] | None] | None:
    if type(report) is not list:
        return None
    findings: dict[str, tuple[str, str]] = {}
    for item in report:
        if (
            type(item) is not dict
            or type(item.get("Fingerprint")) is not str
            or not item["Fingerprint"]
            or len(item["Fingerprint"]) > 1024
            or type(item.get("RuleID")) is not str
            or not item["RuleID"]
            or type(item.get("Commit")) is not str
            or COMMIT_RE.fullmatch(item["Commit"]) is None
        ):
            return None
        finding_id = _hashed_finding_id("GITLEAKS", item["Fingerprint"])
        if finding_id in findings:
            return None
        findings[finding_id] = ("SECRET", "UNKNOWN")
    return findings, []


def _parse_scan_report(
    scan_id: str, report: Any,
) -> tuple[dict[str, tuple[str, str]], list[str] | None] | None:
    if scan_id == "npm":
        return _npm_report(report)
    if scan_id == "nuget":
        return _nuget_report(report)
    if scan_id == "gitleaks":
        return _gitleaks_report(report)
    return None


def _observe_git_source(root: Path) -> dict[str, Any]:
    environment = os.environ.copy()
    for variable in tuple(environment):
        if variable.startswith("GIT_"):
            environment.pop(variable, None)
    environment.update({
        "GIT_OPTIONAL_LOCKS": "0",
        "GIT_TERMINAL_PROMPT": "0",
        "GIT_CONFIG_NOSYSTEM": "1",
        "GIT_NO_REPLACE_OBJECTS": "1",
    })

    def run(*arguments: str) -> str | None:
        try:
            result = subprocess.run(
                ["git", "--no-optional-locks", "-C", str(root), *arguments],
                check=False,
                stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=5,
                env=environment,
            )
        except (OSError, subprocess.TimeoutExpired):
            return None
        return result.stdout.strip() if result.returncode == 0 else None

    top_level = run("rev-parse", "--show-toplevel")
    root_matches = False
    if top_level is not None:
        try:
            root_matches = Path(top_level).resolve() == root.resolve()
        except OSError:
            root_matches = False
    commit = run("rev-parse", "--verify", "HEAD") if root_matches else None
    if commit is not None and COMMIT_RE.fullmatch(commit) is None:
        commit = None
    status = run("status", "--porcelain=v1", "--untracked-files=all") if root_matches else None
    working_tree = "UNKNOWN" if status is None else ("CLEAN" if not status else "DIRTY")
    return {"rootMatches": root_matches, "commit": commit, "workingTree": working_tree}


def _validate_source(source: Any, errors: set[str]) -> tuple[str | None, bool, bool]:
    if not _object(source, {"repository", "commit", "workingTree"}, errors, "invalid-source-shape"):
        return None, False, False
    repository_valid = True
    if source["repository"] != "nirzaf/AuditSphere":
        errors.add("invalid-source-identity")
        repository_valid = False
    commit = source["commit"]
    if commit is not None and (type(commit) is not str or COMMIT_RE.fullmatch(commit) is None):
        errors.add("invalid-source-identity")
        repository_valid = False
    worktree = source["workingTree"]
    if type(worktree) is not str or worktree not in {"CLEAN", "DIRTY", "UNKNOWN"}:
        errors.add("invalid-source-identity")
        repository_valid = False
    if commit is None and worktree != "UNKNOWN":
        errors.add("invalid-source-identity")
        repository_valid = False
    return (
        commit if type(commit) is str else None,
        type(commit) is str and worktree == "CLEAN",
        repository_valid,
    )


def _validate_finding(finding: Any, errors: set[str]) -> bool:
    if not _object(finding, {"id", "classification", "severity", "triage"}, errors, "invalid-finding-shape"):
        return False
    if not _safe_token(finding["id"]) or not ID_RE.fullmatch(finding["id"]):
        errors.add("invalid-finding-identity")
    if type(finding["classification"]) is not str or finding["classification"] not in SCAN_FINDING_CLASSES:
        errors.add("invalid-finding-classification")
    if type(finding["severity"]) is not str or finding["severity"] not in FINDING_SEVERITIES:
        errors.add("invalid-finding-severity")
    if type(finding["triage"]) is not str or finding["triage"] not in FINDING_TRIAGE:
        errors.add("invalid-finding-triage")
    return True


def _scan_status(
    scan_id: str,
    scan: Any,
    source_commit: str | None,
    source_exact: bool,
    root: Path,
    errors: set[str],
    blockers: set[str],
    cache: dict[tuple[str, str], bool],
) -> str:
    errors_before_scan = errors.copy()
    expected_keys = {
        "runState", "failureClass", "sourceCommit", "tool", "feed",
        "capturedAtUtc", "evidence", "findings",
    }
    if not _object(scan, expected_keys, errors, "invalid-scan-shape"):
        return "UNKNOWN"
    if type(scan["runState"]) is not str or scan["runState"] not in {"NOT_RUN", "FAILED", "COMPLETED"}:
        errors.add("invalid-scan-state")
        return "UNKNOWN"
    if scan["sourceCommit"] is not None and (
        type(scan["sourceCommit"]) is not str or COMMIT_RE.fullmatch(scan["sourceCommit"]) is None
    ):
        errors.add("invalid-scan-source")
        return "UNKNOWN"
    if not _object(scan["tool"], {"name", "version"}, errors, "invalid-tool-shape"):
        return "UNKNOWN"
    tool_name, tool_version = scan["tool"]["name"], scan["tool"]["version"]
    if tool_name is not None and (
        type(tool_name) is not str
        or len(tool_name) > 256
        or re.fullmatch(r"[A-Za-z0-9 ._+=-]+", tool_name) is None
    ):
        errors.add("invalid-tool-identity")
    if tool_version is not None and (type(tool_version) is not str or not _safe_token(tool_version)):
        errors.add("invalid-tool-identity")
    if not _object(scan["feed"], {"state", "identities"}, errors, "invalid-feed-shape"):
        return "UNKNOWN"
    feed = scan["feed"]
    if type(feed["state"]) is not str or feed["state"] not in {"UNKNOWN", "IDENTIFIED", "NOT_APPLICABLE"}:
        errors.add("invalid-feed-state")
    if type(feed["identities"]) is not list or any(
        type(host) is not str or HOST_RE.fullmatch(host) is None for host in feed["identities"]
    ):
        errors.add("invalid-feed-identity")
    elif len(set(host.lower() for host in feed["identities"])) != len(feed["identities"]):
        errors.add("duplicate-feed-identity")

    run_state = scan["runState"]
    if run_state == "NOT_RUN":
        if scan["failureClass"] is not None or scan["findings"] is not None:
            errors.add("invalid-not-run-scan")
        blockers.add(f"SCAN_NOT_RUN_{scan_id.upper()}")
        return "UNKNOWN"
    if run_state == "FAILED":
        if (
            type(scan["failureClass"]) is not str
            or scan["failureClass"] not in SCAN_FAILURES
            or scan["findings"] is not None
        ):
            errors.add("invalid-failed-scan")
        if scan["evidence"] is not None:
            _verify_reference(scan["evidence"], root, errors, blockers, cache)
        blockers.add(f"SCAN_FAILED_UNKNOWN_{scan_id.upper()}")
        return "UNKNOWN"
    if scan["failureClass"] is not None:
        errors.add("invalid-completed-scan")

    findings = scan["findings"]
    declared_findings: dict[str, tuple[str, str]] | None = None
    if findings is not None:
        if type(findings) is not list:
            errors.add("invalid-findings-list")
            findings = None
        else:
            finding_ids: set[str] = set()
            declared_findings = {}
            for finding in findings:
                if _validate_finding(finding, errors):
                    finding_id = finding.get("id")
                    if type(finding_id) is str and finding_id in finding_ids:
                        errors.add("duplicate-finding-identity")
                    elif type(finding_id) is str:
                        finding_ids.add(finding_id)
                    expected_class = "SECRET" if scan_id == "gitleaks" else "VULNERABILITY"
                    if finding.get("classification") != expected_class:
                        errors.add("invalid-finding-classification")
                    if (
                        type(finding_id) is str
                        and type(finding.get("classification")) is str
                        and type(finding.get("severity")) is str
                    ):
                        declared_findings[finding_id] = (finding["classification"], finding["severity"])
    captured_exact = _utc_timestamp(scan["capturedAtUtc"])
    if scan["capturedAtUtc"] is not None and not captured_exact:
        errors.add("invalid-scan-timestamp")
    evidence_matches, report = _read_report_reference(
        scan["evidence"], root, scan_id, errors, blockers,
    )
    if not evidence_matches:
        blockers.add(f"SCAN_EVIDENCE_UNKNOWN_{scan_id.upper()}")
        return "UNKNOWN"
    parsed_report = _parse_scan_report(scan_id, report)
    if parsed_report is None:
        blockers.add(f"SCAN_REPORT_PARSER_UNAVAILABLE_{scan_id.upper()}")
        return "UNKNOWN"
    report_findings, report_feeds = parsed_report
    if declared_findings is None:
        blockers.add(f"SCAN_RESULT_INCOMPLETE_{scan_id.upper()}")
        return "UNKNOWN"
    if report_findings != declared_findings:
        errors.add("scan-report-manifest-mismatch")
        blockers.add(f"SCAN_REPORT_MANIFEST_MISMATCH_{scan_id.upper()}")
        return "UNKNOWN"

    source_matches = (
        source_exact
        and source_commit is not None
        and scan["sourceCommit"] == source_commit
        and type(scan["evidence"]) is dict
        and scan["evidence"].get("sourceCommit") == source_commit
    )
    tool_exact = (
        tool_name == SCAN_TOOL_NAMES[scan_id]
        and type(tool_version) is str
        and _safe_token(tool_version)
    )
    if scan_id == "npm":
        # npm audit JSON does not record which registry supplied the response.
        feed_exact = False
    elif scan_id == "nuget":
        identities = feed["identities"]
        declared_hosts = (
            sorted(host.lower() for host in identities)
            if type(identities) is list
            and all(type(host) is str and HOST_RE.fullmatch(host) is not None for host in identities)
            else []
        )
        feed_exact = (
            feed["state"] == "IDENTIFIED"
            and bool(declared_hosts)
            and report_feeds is not None
            and declared_hosts == report_feeds
        )
    else:
        feed_exact = (
            feed["state"] == "NOT_APPLICABLE"
            and not feed["identities"]
            and report_feeds == []
        )
    if not source_matches:
        blockers.add(f"SCAN_SOURCE_UNKNOWN_{scan_id.upper()}")
    if not tool_exact:
        blockers.add(f"SCAN_TOOL_UNKNOWN_{scan_id.upper()}")
    if not feed_exact:
        blockers.add(f"SCAN_FEED_UNKNOWN_{scan_id.upper()}")
    if not captured_exact or findings is None:
        blockers.add(f"SCAN_RESULT_INCOMPLETE_{scan_id.upper()}")
    if not (
        source_matches and tool_exact and feed_exact
        and captured_exact and findings is not None
    ):
        return "UNKNOWN"
    if errors != errors_before_scan:
        return "UNKNOWN"
    if report_findings:
        blockers.add(f"SCAN_FINDINGS_REVIEW_REQUIRED_{scan_id.upper()}")
        return "FINDINGS_REVIEW_REQUIRED"
    return "NO_FINDINGS"


def _validate_target_value(name: str, value: Any, errors: set[str]) -> bool:
    if _valid_target_value(name, value):
        return True
    errors.add("invalid-target-value")
    return False


def _target_status(
    name: str,
    target: Any,
    root: Path,
    errors: set[str],
    blockers: set[str],
    cache: dict[tuple[str, str], bool],
) -> tuple[str, Any]:
    if not _object(target, {"value", "unit", "approvalEvidence"}, errors, "invalid-target-shape"):
        return "INCOMPLETE", None
    if target["unit"] != TARGET_UNITS[name]:
        errors.add("invalid-target-unit")
    _validate_target_value(name, target["value"], errors)
    value_present = target["value"] is not None and target["value"] != []
    if target["approvalEvidence"] is None:
        blockers.add(f"TARGET_APPROVAL_EVIDENCE_MISSING_{name.upper()}")
        reference_present = False
    else:
        reference_present = _verify_reference(target["approvalEvidence"], root, errors, blockers, cache)
    if not value_present:
        blockers.add(f"TARGET_VALUE_MISSING_{name.upper()}")
    if not value_present or not reference_present:
        return "INCOMPLETE", target["value"]
    return "DECLARED_UNVERIFIED", target["value"]


def _measurement_status(
    name: str,
    measurement: Any,
    source_commit: str | None,
    source_exact: bool,
    root: Path,
    errors: set[str],
    blockers: set[str],
    cache: dict[tuple[str, str], bool],
) -> tuple[str, dict[str, Any]]:
    metrics = MEASUREMENT_METRICS[name]
    expected = set(metrics) | {"sourceCommit", "tool", "measuredAtUtc", "evidence"}
    if not _object(measurement, expected, errors, "invalid-measurement-shape"):
        return "INCOMPLETE", {}
    values: dict[str, Any] = {}
    missing = False
    values_valid = True
    for metric, kind in metrics.items():
        value = measurement[metric]
        values[metric] = value
        if value is None:
            missing = True
        elif not _number(value, integer=(kind == "integer")) or (
            (name, metric) in POSITIVE_INTEGER_METRICS and value == 0
        ):
            errors.add("invalid-measurement-value")
            values_valid = False
    if not _object(measurement["tool"], {"name", "version"}, errors, "invalid-measurement-tool"):
        tool_exact = False
    else:
        tool = measurement["tool"]
        tool_exact = all(_safe_token(tool[key]) for key in ("name", "version"))
        if (tool["name"] is not None and not _safe_token(tool["name"])) or (
            tool["version"] is not None and not _safe_token(tool["version"])
        ):
            errors.add("invalid-measurement-tool")
    captured = measurement["measuredAtUtc"]
    captured_exact = captured is not None and _utc_timestamp(captured)
    if captured is not None and not captured_exact:
        errors.add("invalid-measurement-timestamp")
    source_matches = (
        source_exact
        and source_commit is not None
        and measurement["sourceCommit"] == source_commit
        and type(measurement["evidence"]) is dict
        and measurement["evidence"].get("sourceCommit") == source_commit
    )
    if measurement["sourceCommit"] is not None and (
        type(measurement["sourceCommit"]) is not str
        or COMMIT_RE.fullmatch(measurement["sourceCommit"]) is None
    ):
        errors.add("invalid-measurement-source")
    evidence_matches = _verify_reference(measurement["evidence"], root, errors, blockers, cache)
    if missing:
        blockers.add(f"MEASUREMENT_MISSING_{name.upper()}")
        return "INCOMPLETE", values
    if not values_valid:
        blockers.add(f"MEASUREMENT_CONTENT_UNKNOWN_{name.upper()}")
        return "UNKNOWN", values
    if not source_matches:
        blockers.add(f"MEASUREMENT_SOURCE_UNKNOWN_{name.upper()}")
    if not tool_exact or not captured_exact:
        blockers.add(f"MEASUREMENT_PROVENANCE_UNKNOWN_{name.upper()}")
    if not evidence_matches:
        blockers.add(f"MEASUREMENT_EVIDENCE_UNKNOWN_{name.upper()}")
    if not (source_matches and tool_exact and captured_exact and evidence_matches):
        return "UNKNOWN", values
    report_matches, report = _read_report_reference(
        measurement["evidence"], root, None, errors, blockers,
    )
    if not report_matches or not _measurement_report_matches(name, measurement, values, report):
        blockers.add(f"MEASUREMENT_CONTENT_UNKNOWN_{name.upper()}")
        return "UNKNOWN", values
    return "OBSERVED", values


def _measurement_report_matches(
    name: str,
    measurement: dict[str, Any],
    values: dict[str, Any],
    report: Any,
) -> bool:
    expected_keys = {
        "schemaVersion", "measurementType", "sourceCommit", "tool", "measuredAtUtc", "metrics",
    }
    if type(report) is not dict or set(report) != expected_keys:
        return False
    if (
        type(report["schemaVersion"]) is not int
        or report["schemaVersion"] != 1
        or report["measurementType"] != name
        or type(report["sourceCommit"]) is not str
        or COMMIT_RE.fullmatch(report["sourceCommit"]) is None
        or report["sourceCommit"] != measurement["sourceCommit"]
        or not _object_matches(report["tool"], {"name", "version"})
        or not all(_safe_token(report["tool"][key]) for key in ("name", "version"))
        or not _exact_json_match(report["tool"], measurement["tool"])
        or not _utc_timestamp(report["measuredAtUtc"])
        or report["measuredAtUtc"] != measurement["measuredAtUtc"]
        or type(report["metrics"]) is not dict
        or set(report["metrics"]) != set(MEASUREMENT_METRICS[name])
    ):
        return False
    for metric, kind in MEASUREMENT_METRICS[name].items():
        value = report["metrics"][metric]
        if not _number(value, integer=(kind == "integer")) or (
            (name, metric) in POSITIVE_INTEGER_METRICS and value == 0
        ):
            return False
    return _exact_json_match(report["metrics"], values)


def _object_matches(value: Any, keys: set[str]) -> bool:
    return type(value) is dict and set(value) == keys


def _exact_json_match(actual: Any, expected: Any) -> bool:
    if type(actual) is not type(expected):
        return False
    if type(actual) is dict:
        return actual.keys() == expected.keys() and all(
            _exact_json_match(actual[key], expected[key]) for key in actual
        )
    if type(actual) is list:
        return len(actual) == len(expected) and all(
            _exact_json_match(left, right) for left, right in zip(actual, expected)
        )
    return actual == expected


def _validate_local_restore(
    local: Any,
    root: Path,
    errors: set[str],
    blockers: set[str],
    cache: dict[tuple[str, str], bool],
) -> str:
    if not _object(local, {"scope", "recordedAtUtc", "evidence"}, errors, "invalid-local-restore-shape"):
        return "UNKNOWN"
    if local["scope"] != "LOCAL_LOOPBACK" or not _utc_timestamp(local["recordedAtUtc"]):
        errors.add("invalid-local-restore-scope")
        return "UNKNOWN"
    reference = local["evidence"]
    if not _validate_reference_shape(reference, errors):
        return "UNKNOWN"
    if reference["path"] != "docs/evidence/restore-drill-latest.json":
        errors.add("invalid-local-restore-identity")
        return "UNKNOWN"
    if not _verify_reference(reference, root, errors, blockers, cache):
        blockers.add("LOCAL_RESTORE_EVIDENCE_UNKNOWN")
        return "UNKNOWN"
    try:
        observed = load_document(root / "docs/evidence/restore-drill-latest.json")
    except InputError:
        blockers.add("LOCAL_RESTORE_EVIDENCE_UNKNOWN")
        return "UNKNOWN"
    required = {
        "status", "recordedAt", "databaseHost", "expectedMigrationCount", "restoredMigrationCount",
        "expectedLatestMigration", "restoredLatestMigration", "checkpointDatabaseReconciliation",
        "accountingGroupManifestReconciliation", "releaseDeliveryDuplicateKeys",
        "releaseDeliveryDuplicateCheck", "externalCheckpointStoreVerification",
        "custodiallySeparateStorage", "productionRpoRto",
    }
    if type(observed) is not dict or not required.issubset(observed):
        blockers.add("LOCAL_RESTORE_EVIDENCE_UNKNOWN")
        return "UNKNOWN"
    valid = (
        observed.get("status") == "PASS"
        and observed.get("recordedAt") == local["recordedAtUtc"]
        and observed.get("databaseHost") == "127.0.0.1"
        and type(observed.get("expectedMigrationCount")) is int
        and type(observed.get("restoredMigrationCount")) is int
        and observed.get("expectedMigrationCount") == observed.get("restoredMigrationCount")
        and type(observed.get("expectedLatestMigration")) is str
        and observed.get("expectedLatestMigration") == observed.get("restoredLatestMigration")
        and observed.get("checkpointDatabaseReconciliation") == "PASS"
        and observed.get("accountingGroupManifestReconciliation") == "PASS"
        and type(observed.get("releaseDeliveryDuplicateKeys")) is int
        and observed.get("releaseDeliveryDuplicateKeys") == 0
        and observed.get("releaseDeliveryDuplicateCheck") == "PASS"
        and observed.get("externalCheckpointStoreVerification") == "NOT_RUN"
        and observed.get("custodiallySeparateStorage") is False
        and observed.get("productionRpoRto") == "NOT_RUN"
    )
    if not valid:
        blockers.add("LOCAL_RESTORE_EVIDENCE_UNKNOWN")
        return "UNKNOWN"
    return "LOCAL_ONLY"


def evaluate(document: Any, repository_root: Path) -> dict[str, Any]:
    errors: set[str] = set()
    blockers: set[str] = set()
    cache: dict[tuple[str, str], bool] = {}
    top_keys = {
        "schemaVersion", "recordId", "policy", "findingIdentityRules", "source",
        "securityScans", "targets", "measurements", "recovery",
    }
    if not _object(document, top_keys, errors, "invalid-document-shape"):
        return _report("INVALID", errors, blockers)
    if type(document["schemaVersion"]) is not int or document["schemaVersion"] != 1:
        errors.add("unsupported-schema-version")
    if document["recordId"] != "WU038-OPERATIONAL-EVIDENCE-V1":
        errors.add("invalid-record-identity")
    if document["findingIdentityRules"] != FINDING_IDENTITY_RULES:
        errors.add("unsupported-finding-identity-rules")
    policy = {
        "sourceToolFeedFailure": "UNKNOWN",
        "scanReportUnavailable": "UNKNOWN",
        "measurementEvidenceUnavailable": "UNKNOWN",
        "missingTargetOrMeasurement": "INCOMPLETE",
        "approvalEvidence": "REFERENCE_ONLY_NOT_AUTHENTICATED",
        "localRestore": "LOCAL_ONLY",
        "operationalAcceptance": "NOT_ASSESSED",
    }
    if document["policy"] != policy:
        errors.add("unsupported-evidence-policy")

    source_commit, declared_clean, source_shape_valid = _validate_source(document["source"], errors)
    git_source = _observe_git_source(repository_root)
    source_exact = (
        source_shape_valid
        and source_commit is not None
        and declared_clean
        and git_source["rootMatches"] is True
        and git_source["commit"] == source_commit
        and git_source["workingTree"] == "CLEAN"
    )
    source_state = "EXACT" if source_exact else (
        "DECLARED_UNVERIFIED" if source_shape_valid and source_commit is not None else "UNKNOWN"
    )
    if not source_exact:
        if source_commit is None or git_source["workingTree"] == "UNKNOWN":
            blockers.add("SOURCE_IDENTITY_UNKNOWN")
        elif not git_source["rootMatches"]:
            blockers.add("SOURCE_REPOSITORY_UNKNOWN")
        elif git_source["commit"] != source_commit:
            blockers.add("SOURCE_HEAD_MISMATCH")
        elif git_source["workingTree"] == "DIRTY":
            blockers.add("SOURCE_WORKTREE_DIRTY")
        else:
            blockers.add("SOURCE_CLEANNESS_UNVERIFIED")

    scans = document["securityScans"]
    scan_states: dict[str, str] = {}
    if not _known_map(scans, set(SCAN_TOOL_NAMES), errors, "invalid-scan-set"):
        scans = {}
    for scan_id in SCAN_TOOL_NAMES:
        if scan_id not in scans:
            scan_states[scan_id] = "UNKNOWN"
            blockers.add(f"SCAN_NOT_RECORDED_{scan_id.upper()}")
        else:
            scan_states[scan_id] = _scan_status(
                scan_id, scans[scan_id], source_commit, source_exact,
                repository_root, errors, blockers, cache,
            )

    targets = document["targets"]
    target_states: dict[str, str] = {}
    target_values: dict[str, Any] = {}
    if not _known_map(targets, set(TARGET_UNITS), errors, "invalid-target-set"):
        targets = {}
    for name in TARGET_UNITS:
        if name not in targets:
            target_states[name] = "INCOMPLETE"
            target_values[name] = None
            blockers.add(f"TARGET_MISSING_{name.upper()}")
        else:
            target_states[name], target_values[name] = _target_status(
                name, targets[name], repository_root, errors, blockers, cache,
            )

    measurements = document["measurements"]
    measurement_states: dict[str, str] = {}
    measurement_values: dict[str, dict[str, Any]] = {}
    if not _known_map(measurements, set(MEASUREMENT_METRICS), errors, "invalid-measurement-set"):
        measurements = {}
    for name in MEASUREMENT_METRICS:
        if name not in measurements:
            measurement_states[name] = "INCOMPLETE"
            measurement_values[name] = {}
            blockers.add(f"MEASUREMENT_MISSING_{name.upper()}")
        else:
            measurement_states[name], measurement_values[name] = _measurement_status(
                name, measurements[name], source_commit, source_exact,
                repository_root, errors, blockers, cache,
            )
    for measure_name, mappings in MEASUREMENT_TO_TARGET.items():
        if measurement_states.get(measure_name) != "OBSERVED":
            continue
        for metric, target_name in mappings.items():
            observed = measurement_values[measure_name].get(metric)
            target = target_values.get(target_name)
            if target is None:
                continue
            if measure_name == "concurrentWorkers":
                exceeded = observed < target
            else:
                exceeded = observed > target
            if exceeded:
                blockers.add(f"MEASUREMENT_OUTSIDE_TARGET_{measure_name.upper()}")
                measurement_states[measure_name] = "OUTSIDE_TARGET"

    recovery = document["recovery"]
    if not _object(recovery, {"localRestoreDrill", "productionRecovery"}, errors, "invalid-recovery-shape"):
        recovery = {}
    local_state = _validate_local_restore(
        recovery.get("localRestoreDrill"), repository_root, errors, blockers, cache,
    ) if "localRestoreDrill" in recovery else "UNKNOWN"
    if local_state == "UNKNOWN":
        blockers.add("LOCAL_RESTORE_EVIDENCE_UNKNOWN")
    production_state = _validate_production_recovery(
        recovery.get("productionRecovery"), source_commit, source_exact,
        repository_root, errors, blockers, cache,
    ) if "productionRecovery" in recovery else "INCOMPLETE"
    if production_state == "INCOMPLETE":
        blockers.add("PRODUCTION_RECOVERY_INCOMPLETE")
    production = recovery.get("productionRecovery", {})
    for observed_name, target_name in (
        ("observedRpoSeconds", "rpoSeconds"),
        ("observedRtoSeconds", "rtoSeconds"),
    ):
        observed = production.get(observed_name) if type(production) is dict else None
        target = target_values.get(target_name)
        if observed is not None and target is not None and _number(observed) and observed > target:
            blockers.add(f"RECOVERY_OUTSIDE_TARGET_{observed_name.upper()}")

    if errors:
        status = "INVALID"
    elif blockers:
        status = "INCOMPLETE"
    else:
        status = "EVIDENCE_RECORD_COMPLETE"
    return {
        "schemaVersion": 1,
        "status": status,
        "sourceIdentity": source_state,
        "sourceObservation": {
            "repositoryRootMatches": git_source["rootMatches"],
            "commit": git_source["commit"],
            "workingTree": git_source["workingTree"],
        },
        "securityScans": scan_states,
        "targets": target_states,
        "measurements": measurement_states,
        "recovery": {
            "localRestore": local_state,
            "production": production_state,
        },
        "approvalReferences": "HASHED_REFERENCE_ONLY_NOT_AUTHENTICATED",
        "operationalAcceptance": "NOT_ASSESSED",
        "errorCodes": sorted(errors),
        "blockerCodes": sorted(blockers),
    }


def _validate_production_recovery(
    production: Any,
    source_commit: str | None,
    source_exact: bool,
    root: Path,
    errors: set[str],
    blockers: set[str],
    cache: dict[tuple[str, str], bool],
) -> str:
    keys = {"observedRpoSeconds", "observedRtoSeconds", *PRODUCTION_EVIDENCE_FIELDS}
    if not _object(production, keys, errors, "invalid-production-recovery-shape"):
        return "INCOMPLETE"
    incomplete = False
    for metric in ("observedRpoSeconds", "observedRtoSeconds"):
        value = production[metric]
        if value is None:
            incomplete = True
        elif not _number(value) or (metric == "observedRtoSeconds" and value == 0):
            errors.add("invalid-production-recovery-value")
    for field in PRODUCTION_EVIDENCE_FIELDS:
        reference = production[field]
        if reference is None:
            incomplete = True
        else:
            reference_matches = _verify_reference(reference, root, errors, blockers, cache)
            if not (
                reference_matches
                and source_exact
                and source_commit is not None
                and type(reference) is dict
                and reference.get("sourceCommit") == source_commit
            ):
                blockers.add("PRODUCTION_RECOVERY_SOURCE_UNKNOWN")
                incomplete = True
    if incomplete:
        blockers.add("PRODUCTION_RECOVERY_INCOMPLETE")
        return "INCOMPLETE"
    return "EVIDENCE_REFERENCED_NOT_ACCEPTED"


def _report(status: str, errors: set[str], blockers: set[str]) -> dict[str, Any]:
    return {
        "schemaVersion": 1,
        "status": status,
        "sourceIdentity": "UNKNOWN",
        "sourceObservation": {
            "repositoryRootMatches": False,
            "commit": None,
            "workingTree": "UNKNOWN",
        },
        "securityScans": {},
        "targets": {},
        "measurements": {},
        "recovery": {"localRestore": "UNKNOWN", "production": "INCOMPLETE"},
        "approvalReferences": "HASHED_REFERENCE_ONLY_NOT_AUTHENTICATED",
        "operationalAcceptance": "NOT_ASSESSED",
        "errorCodes": sorted(errors),
        "blockerCodes": sorted(blockers),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence_json", type=Path)
    args = parser.parse_args(argv)
    try:
        document = load_document(args.evidence_json)
    except InputError as exc:
        print(json.dumps(_report("INVALID", {exc.code}, set()), sort_keys=True))
        return 1
    except OSError:
        print(json.dumps(_report("INVALID", {"input-unreadable"}, set()), sort_keys=True))
        return 1

    result = evaluate(document, Path(__file__).resolve().parents[2])
    print(json.dumps(result, indent=2, sort_keys=True))
    if result["status"] == "INVALID":
        return 1
    if result["status"] == "INCOMPLETE":
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
