#!/usr/bin/env python3
"""Create and verify a source-bound manifest for Hostinger GHCR images."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


SCHEMA_VERSION = 1
WORKFLOW = ".github/workflows/hostinger-acceptance-images.yml"
PLATFORM = "linux/amd64"
IMAGE_NAMES = {
    "api": "auditsphere-acceptance-api",
    "worker": "auditsphere-acceptance-worker",
    "migrations": "auditsphere-acceptance-migrations",
}
_SHA_RE = re.compile(r"[0-9a-f]{40}\Z")
_DIGEST_RE = re.compile(r"sha256:[0-9a-f]{64}\Z")
_REPOSITORY_RE = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")


def _validate_identity(repository: str, source_sha: str, run_id: int, attempt: int) -> str:
    if _REPOSITORY_RE.fullmatch(repository) is None:
        raise ValueError("repository must be an owner/name pair")
    if _SHA_RE.fullmatch(source_sha) is None:
        raise ValueError("source SHA must be a full lowercase Git commit SHA")
    if isinstance(run_id, bool) or run_id < 1:
        raise ValueError("run ID must be a positive integer")
    if isinstance(attempt, bool) or attempt < 1:
        raise ValueError("run attempt must be a positive integer")
    owner = repository.split("/", maxsplit=1)[0].lower()
    return f"ghcr.io/{owner}"


def _object_without_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise ValueError(f"duplicate JSON field: {key}")
        value[key] = item
    return value


def _read_json(path: Path, expected_sha256: str | None = None) -> dict[str, Any]:
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"expected a regular JSON file: {path}")
    try:
        if expected_sha256 is None:
            data = path.read_text(encoding="utf-8")
        else:
            with path.open("rb") as stream:
                raw = stream.read(1024 * 1024 + 1)
            if len(raw) > 1024 * 1024 or hashlib.sha256(raw).hexdigest() != expected_sha256:
                raise ValueError("image manifest artifact digest or size mismatch")
            data = raw.decode("utf-8")
        value = json.loads(data, object_pairs_hook=_object_without_duplicate_keys)
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"cannot read JSON file {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise ValueError(f"JSON file must contain an object: {path}")
    return value


def _write_json(path: Path, value: dict[str, Any]) -> None:
    if path.exists() or path.is_symlink():
        raise ValueError(f"refusing to overwrite existing file: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def create_image_record(
    *,
    metadata: dict[str, Any],
    image_key: str,
    repository: str,
    source_sha: str,
    run_id: int,
    attempt: int,
) -> dict[str, Any]:
    """Bind Buildx's pushed digest to this workflow's exact source and image identity."""

    registry = _validate_identity(repository, source_sha, run_id, attempt)
    if image_key not in IMAGE_NAMES:
        raise ValueError(f"unsupported image key: {image_key}")

    digest = metadata.get("containerimage.digest")
    if not isinstance(digest, str) or _DIGEST_RE.fullmatch(digest) is None:
        raise ValueError("Buildx metadata must contain a sha256 containerimage.digest")
    descriptor = metadata.get("containerimage.descriptor")
    if descriptor is not None and not isinstance(descriptor, dict):
        raise ValueError("Buildx containerimage.descriptor must be an object")
    if isinstance(descriptor, dict) and descriptor.get("digest") not in (None, digest):
        raise ValueError("Buildx descriptor digest does not match containerimage.digest")

    image_name = IMAGE_NAMES[image_key]
    tagged_reference = f"{registry}/{image_name}:{source_sha}"
    return {
        "schemaVersion": SCHEMA_VERSION,
        "repository": repository,
        "workflow": WORKFLOW,
        "workflowRunId": run_id,
        "workflowRunAttempt": attempt,
        "sourceSha": source_sha,
        "platform": PLATFORM,
        "imageKey": image_key,
        "imageName": image_name,
        "manifestDigest": digest,
        "taggedReference": tagged_reference,
        "immutableReference": f"{tagged_reference}@{digest}",
    }


def _validate_image_record(
    record: dict[str, Any],
    *,
    image_key: str,
    repository: str,
    source_sha: str,
    run_id: int,
    attempt: int,
) -> dict[str, Any]:
    registry = _validate_identity(repository, source_sha, run_id, attempt)
    record_attempt = record.get("workflowRunAttempt")
    if isinstance(record_attempt, bool) or not isinstance(record_attempt, int) or not 1 <= record_attempt <= attempt:
        raise ValueError(f"{image_key} record has an invalid or future workflow attempt")
    digest = record.get("manifestDigest")
    if not isinstance(digest, str) or _DIGEST_RE.fullmatch(digest) is None:
        raise ValueError(f"{image_key} record has an invalid manifest digest")

    image_name = IMAGE_NAMES[image_key]
    tagged_reference = f"{registry}/{image_name}:{source_sha}"
    expected = {
        "schemaVersion": SCHEMA_VERSION,
        "repository": repository,
        "workflow": WORKFLOW,
        "workflowRunId": run_id,
        "workflowRunAttempt": record_attempt,
        "sourceSha": source_sha,
        "platform": PLATFORM,
        "imageKey": image_key,
        "imageName": image_name,
        "taggedReference": tagged_reference,
        "immutableReference": f"{tagged_reference}@{digest}",
    }
    for field, expected_value in expected.items():
        actual = record.get(field)
        if type(actual) is not type(expected_value) or actual != expected_value:
            raise ValueError(f"{image_key} record mismatch for {field}")
    return {
        "imageName": image_name,
        "platform": PLATFORM,
        "manifestDigest": digest,
        "taggedReference": tagged_reference,
        "immutableReference": f"{tagged_reference}@{digest}",
    }


def assemble_manifest(
    *,
    records_dir: Path,
    repository: str,
    source_sha: str,
    run_id: int,
    attempt: int,
) -> dict[str, Any]:
    """Require one valid pushed-image record for every required image."""

    _validate_identity(repository, source_sha, run_id, attempt)
    if records_dir.is_symlink() or not records_dir.is_dir():
        raise ValueError("image record directory must be a real directory")
    expected_files = {f"{key}.json" for key in IMAGE_NAMES}
    actual_entries = list(records_dir.iterdir())
    if {entry.name for entry in actual_entries} != expected_files or any(
        entry.is_symlink() or not entry.is_file() for entry in actual_entries
    ):
        raise ValueError(
            "image record directory must contain only api.json, worker.json, and migrations.json"
        )

    images: dict[str, dict[str, Any]] = {}
    for image_key in IMAGE_NAMES:
        record = _read_json(records_dir / f"{image_key}.json")
        _validate_image_record(
            record,
            image_key=image_key,
            repository=repository,
            source_sha=source_sha,
            run_id=run_id,
            attempt=attempt,
        )
        images[image_key] = record

    return {
        "schemaVersion": SCHEMA_VERSION,
        "repository": repository,
        "workflow": WORKFLOW,
        "workflowRunId": run_id,
        "workflowRunAttempt": attempt,
        "sourceSha": source_sha,
        "platform": PLATFORM,
        "images": images,
    }


def verify_manifest(
    *,
    manifest: dict[str, Any],
    repository: str,
    source_sha: str,
    run_id: int,
    attempt: int,
) -> dict[str, dict[str, Any]]:
    """Verify source/run identity, complete image set, and every immutable reference."""

    _validate_identity(repository, source_sha, run_id, attempt)
    expected_header = {
        "schemaVersion": SCHEMA_VERSION,
        "repository": repository,
        "workflow": WORKFLOW,
        "workflowRunId": run_id,
        "workflowRunAttempt": attempt,
        "sourceSha": source_sha,
        "platform": PLATFORM,
    }
    for field, expected_value in expected_header.items():
        actual = manifest.get(field)
        if type(actual) is not type(expected_value) or actual != expected_value:
            raise ValueError(f"manifest mismatch for {field}")

    images = manifest.get("images")
    if not isinstance(images, dict) or set(images) != set(IMAGE_NAMES):
        raise ValueError("manifest must contain exactly api, worker, and migrations images")

    verified: dict[str, dict[str, Any]] = {}
    for image_key in IMAGE_NAMES:
        record = images[image_key]
        if not isinstance(record, dict):
            raise ValueError(f"{image_key} image record must be an object")
        verified[image_key] = _validate_image_record(
            record,
            image_key=image_key,
            repository=repository,
            source_sha=source_sha,
            run_id=run_id,
            attempt=attempt,
        )
    return verified


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)

    record = commands.add_parser("record", help="record one Buildx image digest")
    record.add_argument("--metadata-file", type=Path, required=True)
    record.add_argument("--output", type=Path, required=True)
    record.add_argument("--image-key", choices=sorted(IMAGE_NAMES), required=True)
    record.add_argument("--repository", required=True)
    record.add_argument("--source-sha", required=True)
    record.add_argument("--run-id", type=int, required=True)
    record.add_argument("--run-attempt", type=int, required=True)

    assemble = commands.add_parser("assemble", help="assemble the complete image manifest")
    assemble.add_argument("--records-dir", type=Path, required=True)
    assemble.add_argument("--output", type=Path, required=True)
    assemble.add_argument("--repository", required=True)
    assemble.add_argument("--source-sha", required=True)
    assemble.add_argument("--run-id", type=int, required=True)
    assemble.add_argument("--run-attempt", type=int, required=True)

    verify = commands.add_parser("verify", help="verify a source-bound image manifest")
    verify.add_argument("--manifest", type=Path, required=True)
    verify.add_argument("--repository", required=True)
    verify.add_argument("--source-sha", required=True)
    verify.add_argument("--run-id", type=int, required=True)
    verify.add_argument("--run-attempt", type=int, required=True)
    verify.add_argument("--manifest-sha256", help="require the independently recorded artifact SHA-256")
    verify.add_argument("--emit-references", action="store_true", help="emit the verified image references as JSON")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    try:
        if args.command == "record":
            record = create_image_record(
                metadata=_read_json(args.metadata_file),
                image_key=args.image_key,
                repository=args.repository,
                source_sha=args.source_sha,
                run_id=args.run_id,
                attempt=args.run_attempt,
            )
            _write_json(args.output, record)
            print(record["immutableReference"])
        elif args.command == "assemble":
            manifest = assemble_manifest(
                records_dir=args.records_dir,
                repository=args.repository,
                source_sha=args.source_sha,
                run_id=args.run_id,
                attempt=args.run_attempt,
            )
            _write_json(args.output, manifest)
            for image_key, image in manifest["images"].items():
                print(f"{image_key}: {image['immutableReference']}")
        else:
            manifest = _read_json(args.manifest, expected_sha256=args.manifest_sha256)
            images = verify_manifest(
                manifest=manifest,
                repository=args.repository,
                source_sha=args.source_sha,
                run_id=args.run_id,
                attempt=args.run_attempt,
            )
            if args.emit_references:
                print(json.dumps({key: record["immutableReference"] for key, record in images.items()}, sort_keys=True))
            else:
                print(f"Verified {len(images)} immutable images for {args.source_sha}")
    except (OSError, ValueError) as exc:
        print(f"image manifest error: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
