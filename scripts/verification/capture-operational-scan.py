#!/usr/bin/env python3
"""Run one allowlisted WU-038 scanner and emit hash-bound evidence.

This helper never restores/installs dependencies or changes repository/network
configuration. npm audit may make read-only registry requests. Captured stderr is
hashed and discarded; Gitleaks output is redacted by its fixed argv.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import re
import selectors
import shutil
import signal
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.parse import urlsplit

SCRIPT = Path(__file__).resolve()
ROOT = SCRIPT.parents[2]
OUTPUT_DIR = Path("artifacts/completion/wu038-scans")
MAX_VERSION_OUTPUT_BYTES = 256
MAX_GIT_STATUS_BYTES = 4_194_304
MAX_CAPTURE_RUNTIME_SECONDS = 180

VERSION_ARGV = {
    "nuget": ["dotnet", "--version"],
    "npm": ["npm", "--version"],
    "gitleaks": ["gitleaks", "version"],
}

SPEC = importlib.util.spec_from_file_location(
    "operational_evidence_verifier",
    SCRIPT.with_name("verify-operational-evidence.py"),
)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("verifier-unavailable")
VERIFY = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = VERIFY
SPEC.loader.exec_module(VERIFY)


def _utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).strftime("%Y-%m-%dT%H:%M:%SZ")


def _git(*args: str) -> bytes:
    try:
        result = subprocess.run(
            ["git", "-C", str(ROOT), *args],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            timeout=15,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        raise CaptureError("git-observation-unavailable") from None
    if result.returncode != 0 or len(result.stdout) > MAX_GIT_STATUS_BYTES:
        raise CaptureError("git-observation-unavailable")
    return result.stdout


def _repository_identity() -> str:
    remote = _git("config", "--get", "remote.origin.url").decode("utf-8", "replace").strip()
    if remote.startswith("git@"):
        match = re.fullmatch(r"git@github\.com:([^\s]+)", remote, re.IGNORECASE)
        path = match.group(1) if match else ""
    else:
        try:
            parsed = urlsplit(remote)
        except ValueError:
            parsed = None
        path = parsed.path.lstrip("/") if parsed and parsed.hostname == "github.com" else ""
    path = path.removesuffix(".git").strip("/").casefold()
    if path != "nirzaf/auditsphere":
        raise CaptureError("repository-identity-unknown")
    return "nirzaf/AuditSphere"


def _source_observation() -> tuple[str, str]:
    top = _git("rev-parse", "--show-toplevel").decode("utf-8", "replace").strip()
    if not top or Path(top).resolve() != ROOT:
        raise CaptureError("repository-root-unknown")
    commit = _git("rev-parse", "HEAD").decode("ascii", "replace").strip()
    if VERIFY.COMMIT_RE.fullmatch(commit) is None:
        raise CaptureError("source-commit-unknown")
    status = _git("status", "--porcelain=v1", "--untracked-files=all")
    return commit, "CLEAN" if not status else "DIRTY"


def _configuration_paths(scan_id: str) -> dict[str, list[Path]]:
    home = Path.home()
    if scan_id == "nuget":
        machine = [
            Path("/etc/opt/NuGet/Config"),
            Path("/Library/Application Support/NuGet/Config"),
        ]
        user_candidates = [home / ".nuget/NuGet/NuGet.Config", home / ".config/NuGet/NuGet.Config"]
        return {
            "repository": [ROOT / "NuGet.Config"],
            "user": user_candidates,
            "machine": machine,
        }
    if scan_id == "npm":
        user_config = os.environ.get("NPM_CONFIG_USERCONFIG")
        global_config = os.environ.get("NPM_CONFIG_GLOBALCONFIG")
        return {
            "repository": [ROOT / ".npmrc"],
            "project": [ROOT / "src/AuditSphereOps.Ui/.npmrc"],
            "user": [Path(user_config).expanduser()] if user_config else [home / ".npmrc"],
            "global": [Path(global_config).expanduser()] if global_config else [Path("/etc/npmrc")],
        }
    return {
        "repository": [ROOT / ".gitleaks.toml", ROOT / ".gitleaks.yaml"],
        "project": [ROOT / ".gitleaksignore"],
    }


def _config_hash(paths: list[Path]) -> str:
    files: list[Path] = []
    for candidate in paths:
        if candidate.is_symlink():
            raise CaptureError("configuration-path-unsafe")
        if not candidate.exists():
            continue
        if candidate.is_dir():
            children = sorted(candidate.glob("*.config")) + sorted(candidate.glob("*.Config"))
            if any(child.is_symlink() for child in children):
                raise CaptureError("configuration-path-unsafe")
            files.extend(child for child in children if child.is_file())
        elif candidate.is_file():
            files.append(candidate)
        else:
            raise CaptureError("configuration-path-unsafe")
    if not files:
        return "ABSENT"
    digest = hashlib.sha256()
    for index, path in enumerate(sorted(set(files), key=str)):
        size = path.stat().st_size
        if size > VERIFY.MAX_CAPTURE_FILE_BYTES:
            raise CaptureError("configuration-file-too-large")
        digest.update(index.to_bytes(4, "big"))
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(65_536), b""):
                digest.update(chunk)
    return digest.hexdigest()


def _configuration_hashes(scan_id: str) -> dict[str, str]:
    paths = _configuration_paths(scan_id)
    if set(paths) != VERIFY.CAPTURE_CONFIG_KEYS[scan_id]:
        raise CaptureError("configuration-map-invalid")
    return {name: _config_hash(candidates) for name, candidates in paths.items()}


def _run_bounded(argv: list[str], cwd: Path, timeout_seconds: int) -> tuple[int, bytes, str, int]:
    try:
        process = subprocess.Popen(
            argv,
            cwd=cwd,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            start_new_session=(os.name == "posix"),
        )
    except OSError:
        raise CaptureError("tool-unavailable") from None

    def stop_process() -> None:
        if process.poll() is None:
            if os.name == "posix":
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except OSError:
                    process.kill()
            else:
                process.kill()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()

    assert process.stdout is not None and process.stderr is not None
    selector = selectors.DefaultSelector()
    streams = {process.stdout: "stdout", process.stderr: "stderr"}
    for stream in streams:
        os.set_blocking(stream.fileno(), False)
        selector.register(stream, selectors.EVENT_READ)
    stdout = bytearray()
    stderr_digest = hashlib.sha256()
    stderr_bytes = 0
    deadline = time.monotonic() + timeout_seconds
    try:
        while selector.get_map():
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise CaptureError("scan-timeout")
            for key, _ in selector.select(min(remaining, 0.25)):
                stream = key.fileobj
                chunk = os.read(stream.fileno(), 65_536)
                if not chunk:
                    selector.unregister(stream)
                    stream.close()
                    continue
                if streams[stream] == "stdout":
                    if len(stdout) + len(chunk) > VERIFY.MAX_CAPTURE_FILE_BYTES:
                        raise CaptureError("scan-output-too-large")
                    stdout.extend(chunk)
                else:
                    stderr_bytes += len(chunk)
                    if stderr_bytes > VERIFY.MAX_CAPTURE_STDERR_BYTES:
                        raise CaptureError("scan-output-too-large")
                    stderr_digest.update(chunk)
        try:
            exit_code = process.wait(timeout=max(0.0, deadline - time.monotonic()))
        except subprocess.TimeoutExpired:
            raise CaptureError("scan-timeout") from None
        if not 0 <= exit_code <= 255:
            raise CaptureError("scan-exited-by-signal")
        return exit_code, bytes(stdout), stderr_digest.hexdigest(), stderr_bytes
    except CaptureError:
        stop_process()
        raise
    except OSError:
        stop_process()
        raise CaptureError("scan-execution-failed") from None
    finally:
        selector.close()
        for stream in streams:
            if not stream.closed:
                stream.close()
        stop_process()


def _tool_version(scan_id: str, cwd: Path) -> str:
    executable = VERIFY.SCAN_EXECUTABLES[scan_id]
    if shutil.which(executable) is None:
        raise CaptureError("tool-unavailable")
    exit_code, stdout, _, _ = _run_bounded(VERSION_ARGV[scan_id], cwd, 15)
    version = stdout.decode("utf-8", "replace").strip()
    if exit_code != 0 or len(stdout) > MAX_VERSION_OUTPUT_BYTES or not VERIFY._safe_token(version):
        raise CaptureError("tool-version-unknown")
    return version


def _safe_output_directory() -> Path:
    path = ROOT.joinpath(*OUTPUT_DIR.parts)
    cursor = ROOT
    for component in OUTPUT_DIR.parts:
        cursor = cursor / component
        if cursor.is_symlink():
            raise CaptureError("output-path-unsafe")
    path.mkdir(parents=True, exist_ok=True)
    return path


class CaptureError(Exception):
    def __init__(self, code: str):
        super().__init__(code)
        self.code = code


def capture(scan_id: str) -> dict[str, Any]:
    if scan_id not in VERIFY.SCAN_ARGV:
        raise CaptureError("unsupported-scan")
    _repository_identity()
    source_commit, working_tree = _source_observation()
    working_directory = ROOT / VERIFY.SCAN_WORKING_DIRECTORIES[scan_id]
    input_hashes = VERIFY._scan_input_hashes(scan_id, ROOT)
    if input_hashes is None:
        raise CaptureError("scan-inputs-unknown")
    configuration_hashes = _configuration_hashes(scan_id)
    tool_version = _tool_version(scan_id, ROOT)

    started_at = _utc_now()
    exit_code, report_bytes, stderr_sha256, stderr_bytes = _run_bounded(
        VERIFY.SCAN_ARGV[scan_id], working_directory, MAX_CAPTURE_RUNTIME_SECONDS,
    )
    completed_at = _utc_now()

    try:
        report = VERIFY._loads_strict(report_bytes, VERIFY.MAX_CAPTURE_FILE_BYTES)
    except VERIFY.InputError:
        raise CaptureError("scan-report-invalid") from None
    parsed = VERIFY._parse_scan_report(
        scan_id,
        report,
        allow_sparse_nuget=(scan_id == "nuget" and exit_code == 0),
    )
    if parsed is None:
        raise CaptureError("scan-report-unsupported")
    report_findings, report_feeds = parsed

    if _source_observation() != (source_commit, working_tree):
        raise CaptureError("source-changed-during-scan")
    if VERIFY._scan_input_hashes(scan_id, ROOT) != input_hashes:
        raise CaptureError("scan-inputs-changed-during-scan")
    if _configuration_hashes(scan_id) != configuration_hashes:
        raise CaptureError("scan-configuration-changed-during-scan")

    output_directory = _safe_output_directory()
    captured_at = completed_at
    timestamp = captured_at.replace("-", "").replace(":", "")
    stem = f"{scan_id}-{timestamp}-{os.getpid()}"
    report_path = (OUTPUT_DIR / f"{stem}-report.json").as_posix()
    capture_path = (OUTPUT_DIR / f"{stem}-capture.json").as_posix()
    report_sha256 = hashlib.sha256(report_bytes).hexdigest()
    report_reference = {
        "id": f"{scan_id.upper()}-{timestamp}-{os.getpid()}-REPORT",
        "path": report_path,
        "sha256": report_sha256,
        "sourceCommit": source_commit,
    }
    feed_state = {
        "nuget": "IDENTIFIED",
        "npm": "UNKNOWN",
        "gitleaks": "NOT_APPLICABLE",
    }[scan_id]
    feed_identities = report_feeds if scan_id == "nuget" else []
    envelope = {
        "schemaVersion": 1,
        "scanId": scan_id,
        "source": {
            "repository": "nirzaf/AuditSphere",
            "commit": source_commit,
            "workingTree": working_tree,
        },
        "inputHashes": input_hashes,
        "invocation": {
            "argv": VERIFY.SCAN_ARGV[scan_id],
            "workingDirectory": VERIFY.SCAN_WORKING_DIRECTORIES[scan_id],
            "exitCode": exit_code,
            "startedAtUtc": started_at,
            "completedAtUtc": completed_at,
            "stdoutSha256": report_sha256,
            "stderrSha256": stderr_sha256,
            "stderrBytes": stderr_bytes,
        },
        "tool": {"executable": VERIFY.SCAN_EXECUTABLES[scan_id], "version": tool_version},
        "feed": {
            "state": feed_state,
            "identities": feed_identities,
            "captureMethod": VERIFY.CAPTURE_FEED_METHODS[scan_id],
            "configurationHashes": configuration_hashes,
        },
        "report": report_reference,
        "capturedAtUtc": captured_at,
    }
    envelope_bytes = json.dumps(envelope, separators=(",", ":"), sort_keys=True).encode("utf-8")
    with (ROOT / report_path).open("xb") as report_file:
        report_file.write(report_bytes)
    with (ROOT / capture_path).open("xb") as capture_file:
        capture_file.write(envelope_bytes)

    scan_record = {
        "runState": "COMPLETED",
        "failureClass": None,
        "sourceCommit": source_commit,
        "tool": {"name": VERIFY.SCAN_TOOL_NAMES[scan_id], "version": tool_version},
        "feed": {"state": feed_state, "identities": feed_identities},
        "capturedAtUtc": captured_at,
        "evidence": {
            "id": f"{scan_id.upper()}-{timestamp}-{os.getpid()}-CAPTURE",
            "path": capture_path,
            "sha256": hashlib.sha256(envelope_bytes).hexdigest(),
            "sourceCommit": source_commit,
        },
        "findings": [
            {"id": finding_id, "classification": classification, "severity": severity, "triage": "UNREVIEWED"}
            for finding_id, (classification, severity) in sorted(report_findings.items())
        ],
    }
    return {
        "status": "CAPTURED",
        "sourceWorkingTree": working_tree,
        "reportSha256": report_sha256,
        "captureSha256": scan_record["evidence"]["sha256"],
        "scanRecord": scan_record,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scan", required=True, choices=sorted(VERIFY.SCAN_ARGV))
    args = parser.parse_args(argv)
    try:
        result = capture(args.scan)
    except CaptureError as error:
        print(json.dumps({"status": "UNKNOWN", "code": error.code}, separators=(",", ":")))
        return 2
    print(json.dumps(result, separators=(",", ":"), sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
