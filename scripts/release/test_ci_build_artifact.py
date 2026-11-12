"""Focused safety tests for exact-source CI build artifact reuse."""

from __future__ import annotations

import hashlib
import json
import tempfile
import tarfile
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import patch

import ci_build_artifact as build_artifact


SOURCE_SHA = "0123456789abcdef0123456789abcdef01234567"


class CiBuildArtifactTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        base = Path(self.temp.name)
        self.root = base / "repo"
        self.root.mkdir()
        self.project = self.root / "src" / "Example"
        self.project.mkdir(parents=True)
        (self.project / "Example.csproj").write_text("<Project />", encoding="utf-8")
        self.bin_output = self.project / "bin" / "Release" / "net11.0"
        self.obj_output = self.project / "obj" / "Release" / "net11.0"
        self.bin_output.mkdir(parents=True)
        self.obj_output.mkdir(parents=True)
        (self.bin_output / "Example.dll").write_bytes(b"compiled assembly")
        (self.obj_output / "project.assets.json").write_text("{}", encoding="utf-8")
        self.artifact_dir = base / "artifact"
        self.identity = {
            "root": self.root,
            "repository": "nirzaf/AuditSphere",
            "run_id": 101,
            "run_attempt": 2,
            "source_sha": SOURCE_SHA,
            "dotnet_sdk": "11.0.100",
            "runner_image": "ubuntu-24.04",
            "runner_version": "20261101.1",
        }

    def create_artifact(self) -> dict[str, object]:
        return build_artifact.create_build_artifact(
            output_dir=self.artifact_dir,
            **self.identity,
        )

    def verify_artifact(self, **overrides: object) -> dict[str, object]:
        identity = {**self.identity, **overrides}
        identity.pop("output_dir", None)
        return build_artifact.verify_and_extract_build_artifact(
            artifact_dir=self.artifact_dir,
            **identity,
        )

    def test_round_trip_replaces_existing_release_outputs(self) -> None:
        manifest = self.create_artifact()
        self.assertEqual(2, len(manifest["outputDirectories"]))
        self.assertEqual(build_artifact.BUILD_PROPERTIES, manifest["buildProperties"])
        (self.bin_output / "Example.dll").write_bytes(b"stale output")
        (self.obj_output / "project.assets.json").write_text("stale", encoding="utf-8")

        result = self.verify_artifact()

        self.assertTrue(result["restored"])
        self.assertEqual(b"compiled assembly", (self.bin_output / "Example.dll").read_bytes())
        self.assertEqual("{}", (self.obj_output / "project.assets.json").read_text(encoding="utf-8"))

    def test_build_property_mismatch_is_rejected_before_output_restore(self) -> None:
        manifest = self.create_artifact()
        manifest["buildProperties"]["maxCpuCount"] = 1
        self.assertEqual(3, build_artifact.BUILD_PROPERTIES["maxCpuCount"])
        (self.artifact_dir / "source.json").write_text(
            json.dumps(manifest), encoding="utf-8"
        )

        with self.assertRaisesRegex(ValueError, "manifest mismatch for buildProperties"):
            self.verify_artifact()

        self.assertEqual(b"compiled assembly", (self.bin_output / "Example.dll").read_bytes())
        self.assertEqual("{}", (self.obj_output / "project.assets.json").read_text(encoding="utf-8"))

    def test_wrong_source_is_rejected_without_changing_outputs(self) -> None:
        self.create_artifact()
        (self.bin_output / "Example.dll").write_bytes(b"current output")

        with self.assertRaisesRegex(ValueError, "manifest mismatch for sourceSha"):
            self.verify_artifact(source_sha="fedcba9876543210fedcba9876543210fedcba98")

        self.assertEqual(b"current output", (self.bin_output / "Example.dll").read_bytes())

    def test_archive_digest_mismatch_is_rejected(self) -> None:
        self.create_artifact()
        archive = self.artifact_dir / build_artifact.ARCHIVE_NAME
        archive.write_bytes(archive.read_bytes() + b"tampered")

        with self.assertRaisesRegex(ValueError, "archive size mismatch"):
            self.verify_artifact()

    def test_traversal_member_is_rejected_before_extraction(self) -> None:
        manifest = self.create_artifact()
        archive = self.artifact_dir / build_artifact.ARCHIVE_NAME
        with tarfile.open(archive, mode="w:gz", compresslevel=1) as bundle:
            member = tarfile.TarInfo("../escaped.txt")
            member.size = 4
            import io

            bundle.addfile(member, io.BytesIO(b"evil"))
        manifest["archiveBytes"] = archive.stat().st_size
        manifest["archiveSha256"] = hashlib.sha256(archive.read_bytes()).hexdigest()
        (self.artifact_dir / "source.json").write_text(
            json.dumps(manifest), encoding="utf-8"
        )
        outside = self.root.parent / "escaped.txt"

        with self.assertRaisesRegex(ValueError, "unsafe archive path"):
            self.verify_artifact()

        self.assertFalse(outside.exists())

    def test_lookup_selects_only_successful_master_push_for_exact_sha(self) -> None:
        runs = {
            "workflow_runs": [
                {
                    "id": 99,
                    "run_attempt": 1,
                    "run_number": 99,
                    "head_sha": SOURCE_SHA,
                    "head_branch": "master",
                    "event": "pull_request",
                    "status": "completed",
                    "conclusion": "success",
                    "path": ".github/workflows/ci.yml",
                },
                {
                    "id": 101,
                    "run_attempt": 2,
                    "run_number": 101,
                    "head_sha": SOURCE_SHA,
                    "head_branch": "master",
                    "event": "push",
                    "status": "completed",
                    "conclusion": "success",
                    "path": ".github/workflows/ci.yml",
                },
            ]
        }
        artifacts = {
            "artifacts": [
                {
                    "id": 7,
                    "name": "auditsphere-release-build-101-2",
                    "expired": False,
                    "size_in_bytes": 123,
                }
            ]
        }

        def fake_api(_api_url: str, _token: str, path: str) -> dict[str, object]:
            return artifacts if "/artifacts?" in path else runs

        with patch.object(build_artifact, "_github_json", side_effect=fake_api):
            result = build_artifact.find_fast_build_artifact(
                api_url="https://api.github.com",
                token="test-token",
                repository="nirzaf/AuditSphere",
                branch="master",
                source_sha=SOURCE_SHA,
            )

        self.assertTrue(result["available"])
        self.assertEqual(101, result["runId"])
        self.assertEqual(2, result["runAttempt"])

    def test_lookup_accepts_successful_manual_master_ci_artifact_for_exact_sha(self) -> None:
        runs_by_event = {
            "push": {"workflow_runs": []},
            "workflow_dispatch": {
                "workflow_runs": [
                    {
                        "id": 202,
                        "run_attempt": 1,
                        "run_number": 202,
                        "head_sha": SOURCE_SHA,
                        "head_branch": "master",
                        "event": "workflow_dispatch",
                        "status": "completed",
                        "conclusion": "success",
                        "path": ".github/workflows/ci.yml",
                    },
                    {
                        "id": 203,
                        "run_attempt": 1,
                        "run_number": 203,
                        "head_sha": SOURCE_SHA,
                        "head_branch": "feature/untrusted",
                        "event": "workflow_dispatch",
                        "status": "completed",
                        "conclusion": "success",
                        "path": ".github/workflows/ci.yml",
                    },
                ]
            },
        }
        artifacts = {
            "artifacts": [
                {
                    "id": 9,
                    "name": "auditsphere-release-build-202-1",
                    "expired": False,
                    "size_in_bytes": 456,
                }
            ]
        }

        def fake_api(_api_url: str, _token: str, path: str) -> dict[str, object]:
            if "/artifacts?" in path:
                return artifacts
            event = "workflow_dispatch" if "event=workflow_dispatch" in path else "push"
            return runs_by_event[event]

        with patch.object(build_artifact, "_github_json", side_effect=fake_api):
            result = build_artifact.find_fast_build_artifact(
                api_url="https://api.github.com",
                token="test-token",
                repository="nirzaf/AuditSphere",
                branch="master",
                source_sha=SOURCE_SHA,
            )

        self.assertTrue(result["available"])
        self.assertEqual(202, result["runId"])
        self.assertEqual("exact-source successful master CI artifact found", result["reason"])

    def test_lookup_falls_back_when_actions_read_is_unavailable(self) -> None:
        error = urllib.error.HTTPError("https://api.github.com", 403, "forbidden", {}, None)
        with patch.object(build_artifact, "_github_json", side_effect=error):
            result = build_artifact.find_fast_build_artifact(
                api_url="https://api.github.com",
                token="test-token",
                repository="nirzaf/AuditSphere",
                branch="master",
                source_sha=SOURCE_SHA,
            )

        self.assertFalse(result["available"])
        self.assertEqual("GitHub Actions API unavailable (HTTP 403)", result["reason"])

    def test_angular_lookup_skips_docs_only_run_and_selects_exact_master_artifact(self) -> None:
        runs_by_event = {
            "push": {
                "workflow_runs": [
                    {
                        "id": 303,
                        "run_attempt": 1,
                        "run_number": 303,
                        "head_sha": SOURCE_SHA,
                        "head_branch": "master",
                        "event": "push",
                        "status": "completed",
                        "conclusion": "success",
                        "path": ".github/workflows/ci.yml",
                    },
                    {
                        "id": 302,
                        "run_attempt": 1,
                        "run_number": 302,
                        "head_sha": SOURCE_SHA,
                        "head_branch": "master",
                        "event": "push",
                        "status": "completed",
                        "conclusion": "success",
                        "path": ".github/workflows/ci.yml",
                    },
                    {
                        "id": 301,
                        "run_attempt": 1,
                        "run_number": 301,
                        "head_sha": SOURCE_SHA,
                        "head_branch": "feature/untrusted",
                        "event": "push",
                        "status": "completed",
                        "conclusion": "success",
                        "path": ".github/workflows/ci.yml",
                    },
                ]
            },
            "workflow_dispatch": {"workflow_runs": []},
        }
        artifacts_by_run = {
            303: {"artifacts": []},  # A successful docs-only run has no Angular bundle.
            302: {
                "artifacts": [
                    {"id": 9002, "name": "angular-ui-browser", "expired": False, "size_in_bytes": 1234}
                ]
            },
        }

        def fake_api(_api_url: str, _token: str, path: str) -> dict[str, object]:
            if "/artifacts?" in path:
                run_id = int(path.split("/runs/")[1].split("/")[0])
                return artifacts_by_run[run_id]
            event = "workflow_dispatch" if "event=workflow_dispatch" in path else "push"
            return runs_by_event[event]

        with patch.object(build_artifact, "_github_json", side_effect=fake_api):
            result = build_artifact.find_angular_bundle_artifact(
                api_url="https://api.github.com",
                token="test-token",
                repository="nirzaf/AuditSphere",
                branch="master",
                source_sha=SOURCE_SHA,
            )

        self.assertTrue(result["available"])
        self.assertEqual(302, result["runId"])
        self.assertEqual(1, result["runAttempt"])
        self.assertEqual(9002, result["artifactId"])
        self.assertEqual(SOURCE_SHA, result["sourceSha"])

    def test_angular_lookup_falls_back_for_expired_or_missing_artifact(self) -> None:
        runs = {
            "workflow_runs": [
                {
                    "id": 401,
                    "run_attempt": 1,
                    "run_number": 401,
                    "head_sha": SOURCE_SHA,
                    "head_branch": "master",
                    "event": "push",
                    "status": "completed",
                    "conclusion": "success",
                    "path": ".github/workflows/ci.yml",
                }
            ]
        }
        artifacts = {
            "artifacts": [
                {"id": 1, "name": "angular-ui-browser", "expired": True, "size_in_bytes": 400},
                {"id": 2, "name": "angular-ui-browser", "expired": False, "size_in_bytes": 0},
            ]
        }

        def fake_api(_api_url: str, _token: str, path: str) -> dict[str, object]:
            return artifacts if "/artifacts?" in path else runs

        with patch.object(build_artifact, "_github_json", side_effect=fake_api):
            result = build_artifact.find_angular_bundle_artifact(
                api_url="https://api.github.com",
                token="test-token",
                repository="nirzaf/AuditSphere",
                branch="master",
                source_sha=SOURCE_SHA,
            )

        self.assertFalse(result["available"])
        self.assertEqual("no matching retained exact-source Angular artifact", result["reason"])

    def test_prepare_angular_context_copies_bundle_and_removes_placeholder(self) -> None:
        artifact = Path(self.temp.name) / "angular-artifact"
        artifact.mkdir()
        (artifact / "index.html").write_text("<html>tested exact-source bundle</html>", encoding="utf-8")
        (artifact / "main.js").write_text("bundle", encoding="utf-8")
        assets = artifact / "assets"
        assets.mkdir()
        (assets / "logo.svg").write_text("<svg />", encoding="utf-8")
        release_assets = self.root / "release-assets" / "angular"
        release_assets.mkdir(parents=True)
        (release_assets / ".keep").touch()

        result = build_artifact.prepare_angular_docker_context(
            artifact_dir=artifact,
            context_dir=self.root,
        )

        self.assertTrue(result["available"])
        self.assertEqual(3, result["fileCount"])
        self.assertFalse((release_assets / ".keep").exists())
        self.assertEqual("bundle", (release_assets / "main.js").read_text(encoding="utf-8"))
        self.assertEqual("<svg />", (release_assets / "assets" / "logo.svg").read_text(encoding="utf-8"))

    def test_prepare_angular_context_keeps_fallback_when_bundle_is_missing(self) -> None:
        artifact = Path(self.temp.name) / "missing-angular-artifact"
        release_assets = self.root / "release-assets" / "angular"
        release_assets.mkdir(parents=True)
        placeholder = release_assets / ".keep"
        placeholder.touch()

        result = build_artifact.prepare_angular_docker_context(
            artifact_dir=artifact,
            context_dir=self.root,
        )

        self.assertFalse(result["available"])
        self.assertTrue(placeholder.is_file())

    def test_prepare_angular_context_rejects_symlinked_release_assets_parent(self) -> None:
        artifact = Path(self.temp.name) / "angular-artifact"
        artifact.mkdir()
        (artifact / "index.html").write_text("<html />", encoding="utf-8")
        real_assets = self.root / "actual-release-assets"
        real_assets.mkdir()
        (self.root / "release-assets").symlink_to(real_assets, target_is_directory=True)

        with self.assertRaisesRegex(ValueError, "cannot be symlinks"):
            build_artifact.prepare_angular_docker_context(
                artifact_dir=artifact,
                context_dir=self.root,
            )

    def test_prepare_angular_context_falls_back_for_symlinked_artifact_root(self) -> None:
        artifact = Path(self.temp.name) / "angular-artifact"
        artifact.mkdir()
        (artifact / "index.html").write_text("<html />", encoding="utf-8")
        artifact_link = Path(self.temp.name) / "angular-artifact-link"
        artifact_link.symlink_to(artifact, target_is_directory=True)

        result = build_artifact.prepare_angular_docker_context(
            artifact_dir=artifact_link,
            context_dir=self.root,
        )

        self.assertFalse(result["available"])
        self.assertEqual("downloaded Angular bundle root cannot be a symlink", result["reason"])


if __name__ == "__main__":
    unittest.main()
