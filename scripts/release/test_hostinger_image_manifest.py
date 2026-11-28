from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from scripts.release import hostinger_image_manifest as image_manifest


SOURCE_SHA = "d18608288e53edd3dad1cf31be465a5be1de4891"
REPOSITORY = "nirzaf/AuditSphere"
RUN_ID = 45074572028
ATTEMPT = 1
DIGESTS = {
    "api": "sha256:" + "1" * 64,
    "worker": "sha256:" + "2" * 64,
    "migrations": "sha256:" + "3" * 64,
}


class HostingerImageManifestTests(unittest.TestCase):
    def create_records(self, directory: Path) -> None:
        directory.mkdir(parents=True, exist_ok=True)
        for image_key, digest in DIGESTS.items():
            record = image_manifest.create_image_record(
                metadata={"containerimage.digest": digest},
                image_key=image_key,
                repository=REPOSITORY,
                source_sha=SOURCE_SHA,
                run_id=RUN_ID,
                attempt=ATTEMPT,
            )
            (directory / f"{image_key}.json").write_text(
                json.dumps(record), encoding="utf-8"
            )

    def test_record_binds_buildx_digest_to_source_and_tag(self) -> None:
        record = image_manifest.create_image_record(
            metadata={
                "containerimage.digest": DIGESTS["api"],
                "containerimage.descriptor": {"digest": DIGESTS["api"]},
            },
            image_key="api",
            repository=REPOSITORY,
            source_sha=SOURCE_SHA,
            run_id=RUN_ID,
            attempt=ATTEMPT,
        )

        self.assertEqual(SOURCE_SHA, record["sourceSha"])
        self.assertEqual("linux/amd64", record["platform"])
        self.assertEqual(
            f"ghcr.io/nirzaf/auditsphere-acceptance-api:{SOURCE_SHA}@{DIGESTS['api']}",
            record["immutableReference"],
        )

    def test_record_rejects_invalid_or_conflicting_digest(self) -> None:
        bad_metadata = (
            {},
            {"containerimage.digest": "sha256:bad"},
            {
                "containerimage.digest": DIGESTS["api"],
                "containerimage.descriptor": {"digest": DIGESTS["worker"]},
            },
        )
        for metadata in bad_metadata:
            with self.subTest(metadata=metadata), self.assertRaises(ValueError):
                image_manifest.create_image_record(
                    metadata=metadata,
                    image_key="api",
                    repository=REPOSITORY,
                    source_sha=SOURCE_SHA,
                    run_id=RUN_ID,
                    attempt=ATTEMPT,
                )

    def test_assemble_and_verify_require_all_exact_source_images(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            records_dir = Path(temporary) / "records"
            self.create_records(records_dir)
            manifest = image_manifest.assemble_manifest(
                records_dir=records_dir,
                repository=REPOSITORY,
                source_sha=SOURCE_SHA,
                run_id=RUN_ID,
                attempt=ATTEMPT,
            )

            verified = image_manifest.verify_manifest(
                manifest=manifest,
                repository=REPOSITORY,
                source_sha=SOURCE_SHA,
                run_id=RUN_ID,
                attempt=ATTEMPT,
            )
            self.assertEqual(set(DIGESTS), set(verified))
            self.assertEqual(DIGESTS["migrations"], verified["migrations"]["manifestDigest"])

            with self.assertRaisesRegex(ValueError, "sourceSha"):
                image_manifest.verify_manifest(
                    manifest=manifest,
                    repository=REPOSITORY,
                    source_sha="fedcba9876543210fedcba9876543210fedcba98",
                    run_id=RUN_ID,
                    attempt=ATTEMPT,
                )

    def test_assemble_rejects_missing_or_cross_run_records(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            records_dir = Path(temporary) / "records"
            self.create_records(records_dir)
            (records_dir / "worker.json").unlink()

            with self.assertRaisesRegex(ValueError, "only api.json"):
                image_manifest.assemble_manifest(
                    records_dir=records_dir,
                    repository=REPOSITORY,
                    source_sha=SOURCE_SHA,
                    run_id=RUN_ID,
                    attempt=ATTEMPT,
                )

            self.create_records(records_dir)
            worker_path = records_dir / "worker.json"
            worker = json.loads(worker_path.read_text(encoding="utf-8"))
            worker["workflowRunId"] += 1
            worker_path.write_text(json.dumps(worker), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "workflowRunId"):
                image_manifest.assemble_manifest(
                    records_dir=records_dir,
                    repository=REPOSITORY,
                    source_sha=SOURCE_SHA,
                    run_id=RUN_ID,
                    attempt=ATTEMPT,
                )

    def test_reference_export_binds_the_artifact_bytes_and_rejects_ambiguous_json(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            records_dir = root / "records"
            self.create_records(records_dir)
            manifest = image_manifest.assemble_manifest(records_dir=records_dir, repository=REPOSITORY,
                source_sha=SOURCE_SHA, run_id=RUN_ID, attempt=ATTEMPT)
            path = root / "manifest.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")
            expected = hashlib.sha256(path.read_bytes()).hexdigest()
            command = [sys.executable, image_manifest.__file__, "verify", "--manifest", str(path),
                "--repository", REPOSITORY, "--source-sha", SOURCE_SHA, "--run-id", str(RUN_ID),
                "--run-attempt", str(ATTEMPT), "--emit-references"]
            verified = subprocess.run(command + ["--manifest-sha256", expected], capture_output=True, text=True)
            self.assertEqual(0, verified.returncode, verified.stderr)
            self.assertEqual({key: image["immutableReference"] for key, image in manifest["images"].items()},
                             json.loads(verified.stdout))

            text = path.read_text(encoding="utf-8")
            path.write_text('{"schemaVersion": 1, ' + text[1:], encoding="utf-8")
            changed = subprocess.run(command + ["--manifest-sha256", expected], capture_output=True, text=True)
            self.assertEqual(2, changed.returncode)
            self.assertIn("artifact digest", changed.stderr)
            actual = hashlib.sha256(path.read_bytes()).hexdigest()
            ambiguous = subprocess.run(command + ["--manifest-sha256", actual], capture_output=True, text=True)
            self.assertEqual(2, ambiguous.returncode)
            self.assertIn("duplicate JSON field", ambiguous.stderr)

if __name__ == "__main__":
    unittest.main()
