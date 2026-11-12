#!/usr/bin/env python3
"""Create, discover, and safely reuse exact-source Release build outputs in GitHub Actions."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import shutil
import tarfile
import tempfile
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path, PurePosixPath
from typing import Any


SCHEMA_VERSION = 1
ARCHIVE_NAME = "release-build.tar.gz"
ARTIFACT_PREFIX = "auditsphere-release-build"
OUTPUT_ROOTS = {"src", "tests", "tools"}
BUILD_PROPERTIES = {
    "maxCpuCount": 3,
    "buildInParallel": True,
    "graphBuild": True,
    "useSharedCompilation": False,
    "runAnalyzers": False,
}
MAX_ARCHIVE_BYTES = 8 * 1024**3
MAX_EXPANDED_BYTES = 10 * 1024**3
MAX_ARCHIVE_MEMBERS = 500_000


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _validate_sha(value: str) -> None:
    if re.fullmatch(r"[0-9a-f]{40}", value) is None:
        raise ValueError("source SHA must be a full lowercase Git commit SHA")


def _relative_parts(path_text: str) -> tuple[str, ...]:
    if not isinstance(path_text, str) or not path_text or "\\" in path_text:
        raise ValueError(f"unsafe archive path: {path_text!r}")
    raw_parts = path_text.split("/")
    if any(part in {"", ".", ".."} for part in raw_parts):
        raise ValueError(f"unsafe archive path: {path_text!r}")
    path = PurePosixPath(path_text)
    if path.is_absolute() or path.parts != tuple(raw_parts):
        raise ValueError(f"unsafe archive path: {path_text!r}")
    return tuple(raw_parts)


def _is_release_output(path_text: str) -> bool:
    try:
        parts = _relative_parts(path_text)
    except ValueError:
        return False
    return (
        len(parts) >= 5
        and parts[0] in OUTPUT_ROOTS
        and parts[-3:] in (("bin", "Release", "net11.0"), ("obj", "Release", "net11.0"))
    )


def _reject_symlink_ancestors(root: Path, path: Path) -> None:
    relative = path.relative_to(root)
    current = root
    for part in relative.parts:
        current = current / part
        if current.is_symlink():
            raise ValueError(f"symlink in build-output path: {current.relative_to(root)}")


def _output_directories(root: Path) -> list[Path]:
    directories: list[Path] = []
    for root_name in sorted(OUTPUT_ROOTS):
        base = root / root_name
        if not base.is_dir() or base.is_symlink():
            continue
        for candidate in base.rglob("net11.0"):
            if (
                candidate.is_dir()
                and candidate.parent.name == "Release"
                and candidate.parent.parent.name in {"bin", "obj"}
            ):
                _reject_symlink_ancestors(root, candidate)
                relative = candidate.relative_to(root).as_posix()
                if not _is_release_output(relative):
                    raise ValueError(f"unexpected build output path: {relative}")
                directories.append(candidate)
    return sorted(set(directories), key=lambda path: path.relative_to(root).as_posix())


def _runner_identity(runner_image: str, runner_version: str) -> dict[str, str]:
    return {
        "runnerImage": runner_image,
        "runnerImageOS": os.environ.get("ImageOS", "unknown"),
        "runnerImageVersion": runner_version or os.environ.get("ImageVersion", "unknown"),
        "operatingSystem": platform.system(),
        "machine": platform.machine(),
    }


def create_build_artifact(
    *,
    root: Path,
    output_dir: Path,
    repository: str,
    run_id: int,
    run_attempt: int,
    source_sha: str,
    dotnet_sdk: str,
    runner_image: str,
    runner_version: str,
) -> dict[str, Any]:
    """Package all Release/net11.0 bin/obj trees and bind them to their exact build identity."""

    root = root.resolve()
    output_dir = output_dir.resolve()
    if output_dir == root or root in output_dir.parents:
        raise ValueError("artifact output directory must be outside the source checkout")
    output_dir.mkdir(parents=True, exist_ok=True)
    if any(output_dir.iterdir()):
        raise ValueError("output directory must be empty")
    _validate_sha(source_sha)
    if run_id < 1 or run_attempt < 1 or not repository or not dotnet_sdk or not runner_image:
        raise ValueError("repository, positive run identity, SDK, and runner image are required")

    directories = _output_directories(root)
    if not directories:
        raise ValueError("no .NET Release/net11.0 bin/obj outputs were found")

    archive = output_dir / ARCHIVE_NAME
    with tarfile.open(archive, mode="w:gz", compresslevel=1, format=tarfile.PAX_FORMAT) as bundle:
        for directory in directories:
            bundle.add(
                directory,
                arcname=directory.relative_to(root).as_posix(),
                recursive=True,
            )
    if archive.stat().st_size > MAX_ARCHIVE_BYTES:
        raise ValueError("compressed build artifact exceeds the safe size limit")

    manifest = {
        "schemaVersion": SCHEMA_VERSION,
        "repository": repository,
        "workflow": ".github/workflows/ci.yml",
        "workflowRunId": run_id,
        "workflowRunAttempt": run_attempt,
        "sourceSha": source_sha,
        "configuration": "Release",
        "targetFramework": "net11.0",
        "dotnetSdk": dotnet_sdk,
        "buildProperties": dict(BUILD_PROPERTIES),
        **_runner_identity(runner_image, runner_version),
        "archiveFile": ARCHIVE_NAME,
        "archiveSha256": _sha256(archive),
        "archiveBytes": archive.stat().st_size,
        "outputDirectories": [
            directory.relative_to(root).as_posix() for directory in directories
        ],
    }
    (output_dir / "source.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    return manifest


def _validate_member_path(path_text: str, output_directories: set[str]) -> PurePosixPath:
    parts = _relative_parts(path_text)
    if parts[0] not in OUTPUT_ROOTS:
        raise ValueError(f"archive path is outside build roots: {path_text}")
    if not any(
        path_text == directory or path_text.startswith(directory + "/")
        for directory in output_directories
    ):
        raise ValueError(f"archive path is outside declared build outputs: {path_text}")
    return PurePosixPath(*parts)


def _remove_path(path: Path) -> None:
    if path.is_symlink() or path.is_file():
        path.unlink()
    elif path.exists():
        shutil.rmtree(path)


def verify_and_extract_build_artifact(
    *,
    root: Path,
    artifact_dir: Path,
    repository: str,
    run_id: int,
    run_attempt: int,
    source_sha: str,
    dotnet_sdk: str,
    runner_image: str,
    runner_version: str,
) -> dict[str, Any]:
    """Verify identity, digest and member paths before transactionally restoring outputs."""

    root = root.resolve()
    artifact_dir = artifact_dir.resolve()
    _validate_sha(source_sha)
    manifest_path = artifact_dir / "source.json"
    archive = artifact_dir / ARCHIVE_NAME
    if manifest_path.is_symlink() or archive.is_symlink():
        raise ValueError("build artifact manifest and archive must be regular files")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if not isinstance(manifest, dict):
        raise ValueError("build artifact manifest must be a JSON object")
    expected = {
        "schemaVersion": SCHEMA_VERSION,
        "repository": repository,
        "workflow": ".github/workflows/ci.yml",
        "workflowRunId": run_id,
        "workflowRunAttempt": run_attempt,
        "sourceSha": source_sha,
        "configuration": "Release",
        "targetFramework": "net11.0",
        "dotnetSdk": dotnet_sdk,
        "buildProperties": dict(BUILD_PROPERTIES),
        **_runner_identity(runner_image, runner_version),
        "archiveFile": ARCHIVE_NAME,
    }
    for field, value in expected.items():
        if manifest.get(field) != value:
            raise ValueError(f"build artifact manifest mismatch for {field}")

    if not archive.is_file():
        raise ValueError("build artifact archive is missing")
    archive_bytes = archive.stat().st_size
    if archive_bytes > MAX_ARCHIVE_BYTES or archive_bytes != manifest.get("archiveBytes"):
        raise ValueError("build artifact archive size mismatch or safe limit exceeded")
    if _sha256(archive) != manifest.get("archiveSha256"):
        raise ValueError("build artifact archive digest mismatch")

    declared = manifest.get("outputDirectories")
    if (
        not isinstance(declared, list)
        or not declared
        or any(not isinstance(item, str) for item in declared)
        or len(set(declared)) != len(declared)
    ):
        raise ValueError("build artifact output directory manifest is missing or duplicated")
    output_directories: set[str] = set()
    for item in declared:
        if not _is_release_output(item):
            raise ValueError(f"unexpected declared build output directory: {item}")
        parsed = _relative_parts(item)
        if len(parsed) < 5:
            raise ValueError(f"invalid build output directory: {item}")
        output_directories.add(item)
    for first in output_directories:
        if any(second.startswith(first + "/") for second in output_directories if second != first):
            raise ValueError("nested declared build output directories are not allowed")

    with tarfile.open(archive, mode="r:gz") as bundle:
        members = bundle.getmembers()
        if not members or len(members) > MAX_ARCHIVE_MEMBERS:
            raise ValueError("build artifact has no members or exceeds the member-count limit")
        seen: set[str] = set()
        expanded_bytes = 0
        for member in members:
            raw_name = member.name[:-1] if member.isdir() and member.name.endswith("/") else member.name
            member_path = _validate_member_path(raw_name, output_directories)
            normalized = member_path.as_posix()
            if normalized in seen:
                raise ValueError(f"duplicate archive member: {normalized}")
            seen.add(normalized)
            if member.isfile():
                expanded_bytes += member.size
            if expanded_bytes > MAX_EXPANDED_BYTES:
                raise ValueError("expanded build artifact exceeds the safe size limit")

            if member.issym():
                link = PurePosixPath(member.linkname)
                if link.is_absolute() or "\\" in member.linkname:
                    raise ValueError(f"unsafe symbolic link in build archive: {member.name}")
                combined_text = os.path.normpath(str(member_path.parent / link))
                combined = PurePosixPath(combined_text)
                if combined.is_absolute() or any(part == ".." for part in combined.parts):
                    raise ValueError(f"symbolic link escapes build outputs: {member.name}")
                if not any(
                    combined_text == directory or combined_text.startswith(directory + "/")
                    for directory in output_directories
                ):
                    raise ValueError(f"symbolic link escapes declared build outputs: {member.name}")
            elif member.islnk():
                target = _validate_member_path(member.linkname, output_directories)
                if not any(
                    target.as_posix() == directory
                    or target.as_posix().startswith(directory + "/")
                    for directory in output_directories
                ):
                    raise ValueError(f"hard link escapes declared build outputs: {member.name}")
            elif not (member.isdir() or member.isfile()):
                raise ValueError(f"unsupported special file in build archive: {member.name}")

        for directory in output_directories:
            if directory not in seen:
                raise ValueError(f"declared build output is absent from archive: {directory}")

        # Stage all entries first. Only after a complete extraction are existing Release/net11.0
        # outputs replaced; any replacement error restores the prior directories before fallback.
        with tempfile.TemporaryDirectory(prefix=".auditsphere-build-restore-", dir=root) as temp_name:
            staging = Path(temp_name)
            bundle.extractall(path=staging, filter="data")
            backups = staging / ".previous"
            installed: list[Path] = []
            saved: list[tuple[Path, Path]] = []
            try:
                for index, directory in enumerate(sorted(output_directories)):
                    relative = PurePosixPath(directory)
                    staged = staging.joinpath(*relative.parts)
                    destination = root.joinpath(*relative.parts)
                    if not staged.is_dir() or staged.is_symlink():
                        raise ValueError(f"declared build output was not restored: {directory}")
                    _reject_symlink_ancestors(root, destination.parent)
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    if destination.is_symlink():
                        raise ValueError(f"refusing to replace symlink build output: {directory}")
                    if destination.exists():
                        backup = backups.joinpath(str(index), *relative.parts)
                        backup.parent.mkdir(parents=True, exist_ok=True)
                        os.replace(destination, backup)
                        saved.append((destination, backup))
                    os.replace(staged, destination)
                    installed.append(destination)
            except Exception:
                for destination in reversed(installed):
                    _remove_path(destination)
                for destination, backup in reversed(saved):
                    if backup.exists() or backup.is_symlink():
                        destination.parent.mkdir(parents=True, exist_ok=True)
                        os.replace(backup, destination)
                raise

    return {
        "sourceSha": source_sha,
        "workflowRunId": run_id,
        "workflowRunAttempt": run_attempt,
        "archiveSha256": manifest["archiveSha256"],
        "archiveBytes": archive_bytes,
        "outputDirectoryCount": len(output_directories),
        "archiveMemberCount": len(members),
        "restored": True,
    }


def _github_json(api_url: str, token: str, path: str) -> dict[str, Any]:
    request = urllib.request.Request(
        api_url.rstrip("/") + path,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    with urllib.request.urlopen(request, timeout=20) as response:
        payload = json.load(response)
    if not isinstance(payload, dict):
        raise ValueError("GitHub API returned an unexpected response")
    return payload


def find_fast_build_artifact(
    *,
    api_url: str,
    token: str,
    repository: str,
    branch: str,
    source_sha: str,
    workflow_id: str = "ci.yml",
) -> dict[str, Any]:
    """Find a successful default-branch CI artifact for the exact resolved source SHA."""

    _validate_sha(source_sha)
    if re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) is None:
        raise ValueError("repository must be in owner/name form")
    if not token:
        return {"available": False, "reason": "actions-read token unavailable"}
    workflow = urllib.parse.quote(workflow_id, safe="")
    repo = urllib.parse.quote(repository, safe="/")
    runs: list[dict[str, Any]] = []
    for event in ("push", "workflow_dispatch"):
        query = urllib.parse.urlencode(
            {"branch": branch, "event": event, "status": "completed", "per_page": "100"}
        )
        try:
            runs_payload = _github_json(
                api_url,
                token,
                f"/repos/{repo}/actions/workflows/{workflow}/runs?{query}",
            )
        except (OSError, urllib.error.URLError, json.JSONDecodeError, ValueError) as exc:
            status = getattr(exc, "code", None)
            return {
                "available": False,
                "reason": f"GitHub Actions API unavailable{f' (HTTP {status})' if status else ''}",
            }
        event_runs = runs_payload.get("workflow_runs", [])
        if not isinstance(event_runs, list):
            return {"available": False, "reason": "GitHub Actions run metadata is malformed"}
        runs.extend(run for run in event_runs if isinstance(run, dict))

    matches = [
        run
        for run in runs
        if isinstance(run, dict)
        and run.get("head_sha") == source_sha
        and run.get("head_branch") == branch
        and run.get("event") in {"push", "workflow_dispatch"}
        and run.get("status") == "completed"
        and run.get("conclusion") == "success"
        and isinstance(run.get("path", ".github/workflows/ci.yml"), str)
        and run.get("path", ".github/workflows/ci.yml").endswith("/ci.yml")
    ]
    matches.sort(key=lambda run: (run.get("run_number", 0), run.get("run_attempt", 0)), reverse=True)
    for run in matches:
        run_id = run.get("id")
        attempt = run.get("run_attempt")
        if not isinstance(run_id, int) or not isinstance(attempt, int) or run_id < 1 or attempt < 1:
            continue
        expected_name = f"{ARTIFACT_PREFIX}-{run_id}-{attempt}"
        try:
            artifacts = _github_json(
                api_url,
                token,
                f"/repos/{repo}/actions/runs/{run_id}/artifacts?per_page=100",
            ).get("artifacts", [])
        except (OSError, urllib.error.URLError, json.JSONDecodeError, ValueError) as exc:
            status = getattr(exc, "code", None)
            return {
                "available": False,
                "reason": f"GitHub Actions artifact metadata unavailable{f' (HTTP {status})' if status else ''}",
            }
        for artifact in artifacts if isinstance(artifacts, list) else []:
            if (
                isinstance(artifact, dict)
                and artifact.get("name") == expected_name
                and artifact.get("expired") is False
                and isinstance(artifact.get("size_in_bytes"), int)
                and artifact["size_in_bytes"] > 0
            ):
                return {
                    "available": True,
                    "reason": "exact-source successful master CI artifact found",
                    "runId": run_id,
                    "runAttempt": attempt,
                    "artifactId": artifact.get("id"),
                    "artifactName": expected_name,
                    "sourceSha": source_sha,
                }
    return {"available": False, "reason": "no matching successful fast-CI artifact"}


def find_angular_bundle_artifact(
    *,
    api_url: str,
    token: str,
    repository: str,
    branch: str,
    source_sha: str,
    workflow_id: str = "ci.yml",
) -> dict[str, Any]:
    """Find a retained Angular bundle from a successful default-branch CI run for this SHA."""

    _validate_sha(source_sha)
    if re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) is None:
        raise ValueError("repository must be in owner/name form")
    if not token:
        return {"available": False, "reason": "actions-read token unavailable"}
    workflow = urllib.parse.quote(workflow_id, safe="")
    repo = urllib.parse.quote(repository, safe="/")
    runs: list[dict[str, Any]] = []
    for event in ("push", "workflow_dispatch"):
        query = urllib.parse.urlencode(
            {"branch": branch, "event": event, "status": "completed", "per_page": "100"}
        )
        try:
            payload = _github_json(
                api_url,
                token,
                f"/repos/{repo}/actions/workflows/{workflow}/runs?{query}",
            )
        except (OSError, urllib.error.URLError, json.JSONDecodeError, ValueError) as exc:
            status = getattr(exc, "code", None)
            return {
                "available": False,
                "reason": f"GitHub Actions API unavailable{f' (HTTP {status})' if status else ''}",
            }
        event_runs = payload.get("workflow_runs", [])
        if not isinstance(event_runs, list):
            return {"available": False, "reason": "GitHub Actions run metadata is malformed"}
        runs.extend(run for run in event_runs if isinstance(run, dict))

    matches = [
        run
        for run in runs
        if run.get("head_sha") == source_sha
        and run.get("head_branch") == branch
        and run.get("event") in {"push", "workflow_dispatch"}
        and run.get("status") == "completed"
        and run.get("conclusion") == "success"
        and isinstance(run.get("path", ".github/workflows/ci.yml"), str)
        and run.get("path", ".github/workflows/ci.yml").endswith("/ci.yml")
    ]
    matches.sort(key=lambda run: (run.get("run_number", 0), run.get("run_attempt", 0)), reverse=True)
    for run in matches:
        run_id = run.get("id")
        attempt = run.get("run_attempt")
        if not isinstance(run_id, int) or run_id < 1 or not isinstance(attempt, int) or attempt < 1:
            continue
        try:
            artifacts = _github_json(
                api_url,
                token,
                f"/repos/{repo}/actions/runs/{run_id}/artifacts?per_page=100",
            ).get("artifacts", [])
        except (OSError, urllib.error.URLError, json.JSONDecodeError, ValueError) as exc:
            status = getattr(exc, "code", None)
            return {
                "available": False,
                "reason": f"GitHub Actions artifact metadata unavailable{f' (HTTP {status})' if status else ''}",
            }
        for artifact in artifacts if isinstance(artifacts, list) else []:
            if (
                isinstance(artifact, dict)
                and artifact.get("name") == "angular-ui-browser"
                and artifact.get("expired") is False
                and isinstance(artifact.get("size_in_bytes"), int)
                and artifact["size_in_bytes"] > 0
                and isinstance(artifact.get("id"), int)
            ):
                return {
                    "available": True,
                    "reason": "exact-source successful default-branch Angular artifact found",
                    "runId": run_id,
                    "runAttempt": attempt,
                    "artifactId": artifact["id"],
                    "artifactName": "angular-ui-browser",
                    "sourceSha": source_sha,
                }
    return {"available": False, "reason": "no matching retained exact-source Angular artifact"}


def prepare_angular_docker_context(*, artifact_dir: Path, context_dir: Path) -> dict[str, Any]:
    """Copy a downloaded CI UI bundle into the API image context, or report fallback required."""

    if artifact_dir.is_symlink():
        return {"available": False, "reason": "downloaded Angular bundle root cannot be a symlink"}
    artifact_dir = artifact_dir.resolve()
    context_dir = context_dir.resolve()
    index = artifact_dir / "index.html"
    if (
        not artifact_dir.is_dir()
        or not index.is_file()
        or index.is_symlink()
        or index.stat().st_size == 0
    ):
        return {"available": False, "reason": "downloaded Angular bundle has no regular index.html"}
    files = [path for path in artifact_dir.rglob("*") if path.is_file()]
    if not files or any(path.is_symlink() for path in artifact_dir.rglob("*")):
        return {"available": False, "reason": "downloaded Angular bundle is empty or contains symlinks"}
    target = context_dir / "release-assets" / "angular"
    if context_dir not in target.parents:
        raise ValueError("Angular bundle target must be within the Docker build context")
    release_assets = context_dir / "release-assets"
    if release_assets.is_symlink() or target.is_symlink():
        raise ValueError("Angular bundle target and its parent cannot be symlinks")
    target.mkdir(parents=True, exist_ok=True)
    for child in target.iterdir():
        if child.name == ".keep":
            child.unlink()
        elif child.is_dir() and not child.is_symlink():
            shutil.rmtree(child)
        else:
            child.unlink()
    for source in files:
        relative = source.relative_to(artifact_dir)
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
    return {
        "available": True,
        "reason": "exact-source CI Angular artifact copied into API image context",
        "fileCount": len(files),
        "indexBytes": index.stat().st_size,
    }


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)

    create = commands.add_parser("create", help="package Release outputs from a successful CI build")
    create.add_argument("--root", type=Path, default=Path.cwd())
    create.add_argument("--output-dir", required=True, type=Path)
    create.add_argument("--repository", required=True)
    create.add_argument("--run-id", required=True, type=int)
    create.add_argument("--run-attempt", required=True, type=int)
    create.add_argument("--source-sha", required=True)
    create.add_argument("--dotnet-sdk", required=True)
    create.add_argument("--runner-image", required=True)
    create.add_argument("--runner-version", default="")

    restore = commands.add_parser("verify-and-extract", help="verify and restore a fast-CI artifact")
    restore.add_argument("--root", type=Path, default=Path.cwd())
    restore.add_argument("--artifact-dir", required=True, type=Path)
    restore.add_argument("--repository", required=True)
    restore.add_argument("--run-id", required=True, type=int)
    restore.add_argument("--run-attempt", required=True, type=int)
    restore.add_argument("--source-sha", required=True)
    restore.add_argument("--dotnet-sdk", required=True)
    restore.add_argument("--runner-image", required=True)
    restore.add_argument("--runner-version", default="")

    find = commands.add_parser("find", help="locate a successful exact-source fast-CI artifact")
    find.add_argument("--api-url", required=True)
    find.add_argument("--repository", required=True)
    find.add_argument("--branch", required=True)
    find.add_argument("--source-sha", required=True)
    find.add_argument("--workflow-id", default="ci.yml")
    find.add_argument("--github-output", type=Path)

    find_angular = commands.add_parser(
        "find-angular-bundle", help="locate a retained exact-source Angular CI artifact"
    )
    find_angular.add_argument("--api-url", required=True)
    find_angular.add_argument("--repository", required=True)
    find_angular.add_argument("--branch", required=True)
    find_angular.add_argument("--source-sha", required=True)
    find_angular.add_argument("--workflow-id", default="ci.yml")
    find_angular.add_argument("--github-output", type=Path)

    prepare_angular = commands.add_parser(
        "prepare-angular-context", help="stage an optional downloaded Angular bundle for API image build"
    )
    prepare_angular.add_argument("--artifact-dir", required=True, type=Path)
    prepare_angular.add_argument("--context-dir", type=Path, default=Path.cwd())
    prepare_angular.add_argument("--github-output", type=Path)
    return parser


def main() -> int:
    args = _parser().parse_args()
    try:
        if args.command == "create":
            result = create_build_artifact(
                root=args.root,
                output_dir=args.output_dir,
                repository=args.repository,
                run_id=args.run_id,
                run_attempt=args.run_attempt,
                source_sha=args.source_sha,
                dotnet_sdk=args.dotnet_sdk,
                runner_image=args.runner_image,
                runner_version=args.runner_version,
            )
        elif args.command == "verify-and-extract":
            result = verify_and_extract_build_artifact(
                root=args.root,
                artifact_dir=args.artifact_dir,
                repository=args.repository,
                run_id=args.run_id,
                run_attempt=args.run_attempt,
                source_sha=args.source_sha,
                dotnet_sdk=args.dotnet_sdk,
                runner_image=args.runner_image,
                runner_version=args.runner_version,
            )
        elif args.command == "find":
            result = find_fast_build_artifact(
                api_url=args.api_url,
                token=os.environ.get("GH_TOKEN", ""),
                repository=args.repository,
                branch=args.branch,
                source_sha=args.source_sha,
                workflow_id=args.workflow_id,
            )
        elif args.command == "find-angular-bundle":
            result = find_angular_bundle_artifact(
                api_url=args.api_url,
                token=os.environ.get("GH_TOKEN", ""),
                repository=args.repository,
                branch=args.branch,
                source_sha=args.source_sha,
                workflow_id=args.workflow_id,
            )
            if args.github_output:
                output = {
                    "available": str(result["available"]).lower(),
                    "run_id": str(result.get("runId", "")),
                    "run_attempt": str(result.get("runAttempt", "")),
                    "artifact_id": str(result.get("artifactId", "")),
                    "artifact_name": result.get("artifactName", ""),
                    "reason": result.get("reason", ""),
                }
                with args.github_output.open("a", encoding="utf-8") as stream:
                    for key, value in output.items():
                        if "\n" in value or "\r" in value:
                            raise ValueError("GitHub output value contains a newline")
                        stream.write(f"{key}={value}\n")
        else:
            result = prepare_angular_docker_context(
                artifact_dir=args.artifact_dir,
                context_dir=args.context_dir,
            )
            if args.github_output:
                with args.github_output.open("a", encoding="utf-8") as stream:
                    stream.write(f"available={str(result['available']).lower()}\n")
        print(json.dumps(result, sort_keys=True))
        return 0
    except (OSError, tarfile.TarError, ValueError, json.JSONDecodeError, urllib.error.URLError) as exc:
        print(f"CI Release build artifact rejected or unavailable: {exc}", file=os.sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
